using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using WeaponSkins.Services;
using WeaponSkins.Shared;
using WeaponSkins.Extensions;
using WeaponSkins.Econ;

namespace WeaponSkins.Database;

public class DatabaseSynchronizeService
{
    private DatabaseService DatabaseService { get; init; }
    private DataService DataService { get; init; }
    private PlayerService PlayerService { get; init; }
    private InventoryService InventoryService { get; init; }
    private EconService EconService { get; init; }
    private ISwiftlyCore Core { get; init; }
    private ILogger<DatabaseSynchronizeService> Logger { get; init; }

    public DatabaseSynchronizeService(
        DatabaseService databaseService, 
        DataService dataService,
        PlayerService playerService,
        InventoryService inventoryService,
        EconService econService,
        ISwiftlyCore core,
        ILogger<DatabaseSynchronizeService> logger)
    {
        DatabaseService = databaseService;
        DataService = dataService;
        PlayerService = playerService;
        InventoryService = inventoryService;
        EconService = econService;
        Core = core;
        Logger = logger;
    }

    public async Task SynchronizeAsync()
    {
        try
        {
            var skins = await DatabaseService.GetAllSkinsAsync();
            skins.ToList().ForEach(skin => DataService.WeaponDataService.StoreSkin(skin));
            var knives = await DatabaseService.GetAllKnifesAsync();
            knives.ToList().ForEach(knife => DataService.KnifeDataService.StoreKnife(knife));
            var gloves = await DatabaseService.GetAllGlovesAsync();
            gloves.ToList().ForEach(glove => DataService.GloveDataService.StoreGlove(glove));
            var agents = await DatabaseService.GetAllAgentsAsync();
            agents.ToList().ForEach(agent => DataService.AgentDataService.SetAgent(agent.SteamID, agent.Team, agent.AgentIndex));
            var musicKits = await DatabaseService.GetAllMusicKitsAsync();
            musicKits.ToList().ForEach(mk => DataService.MusicKitDataService.SetMusicKit(mk.SteamID, mk.MusicKitIndex));
        }
        catch (Exception ex)
        {
            throw new Exception("Database synchronization failed", ex);
        }
    }

    public void Synchronize()
    {
        SynchronizeAsync();
    }

    public async Task ApplyPlayerSkinsFromDBAsync(ulong steamID)
    {
        try
        {
            Logger.LogInformation("Applying skins from database for player {SteamID}", steamID);

            // Get all player data directly from database
            var skins = await DatabaseService.GetSkinsAsync(steamID);
            var knives = await DatabaseService.GetKnifesAsync(steamID);
            var agents = await DatabaseService.GetAgentsAsync(steamID);
            var musicKits = await DatabaseService.GetMusicKitsAsync(steamID);

            // Apply immediately on main thread - query fresh gloves data
            var freshGloves = await DatabaseService.GetGlovesAsync(steamID);
            await ApplyAllPlayerSkinsImmediate(steamID, skins, knives, freshGloves, agents, musicKits);

            Logger.LogInformation("Successfully applied skins from database for player {SteamID}", steamID);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply skins from database for player {SteamID}", steamID);
            throw;
        }
    }

    private async Task ApplyAllPlayerSkinsImmediate(ulong steamID,
        IEnumerable<WeaponSkinData> skins,
        IEnumerable<KnifeSkinData> knives,
        IEnumerable<GloveData> gloves,
        IEnumerable<(ulong SteamID, Team Team, int AgentIndex)> agents,
        IEnumerable<(ulong SteamID, int MusicKitIndex)> musicKits)
    {
        // Get player instance
        if (!PlayerService.TryGetPlayer(steamID, out var player))
        {
            Logger.LogWarning("Player {SteamID} not found for immediate skin application", steamID);
            return;
        }

        // Apply immediately on main thread
        await Task.Run(() =>
        {
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (!player.IsAlive) return;

                // Apply weapons
                var playerSkins = skins.Where(s => s.Team == player.Controller.Team).ToList();
                if (playerSkins.Any())
                {
                    // Update data service
                    foreach (var skin in playerSkins)
                    {
                        DataService.WeaponDataService.StoreSkin(skin);
                    }
                    
                    // Update inventory service
                    InventoryService.UpdateWeaponSkins(steamID, playerSkins);
                    
                    // Find and regive weapons
                    var weapons = player.PlayerPawn?.WeaponServices?.MyWeapons;
                    if (weapons != null)
                    {
                        foreach (var skin in playerSkins)
                        {
                            foreach (var weapon in weapons)
                            {
                                if (weapon.Value?.AttributeManager?.Item?.ItemDefinitionIndex == skin.DefinitionIndex)
                                {
                                    Core.Scheduler.NextWorldUpdate(() =>
                                    {
                                        player.RegiveWeapon(weapon.Value, skin.DefinitionIndex);
                                    });
                                    break;
                                }
                            }
                        }
                    }
                }

                // Apply knife
                var knifeForTeam = knives.FirstOrDefault(k => k.Team == player.Controller.Team);
                if (knifeForTeam != null)
                {
                    // Update data service
                    DataService.KnifeDataService.StoreKnife(knifeForTeam);
                    
                    // Update inventory service
                    InventoryService.UpdateKnifeSkins(steamID, new List<KnifeSkinData> { knifeForTeam });
                    
                    Core.Scheduler.NextWorldUpdate(() =>
                    {
                        player.RegiveKnife();
                    });
                }

                // Apply agent
                var agentForTeam = agents.FirstOrDefault(a => a.Team == player.Controller.Team);
                if (agentForTeam.SteamID != 0)
                {
                    // Update data service
                    DataService.AgentDataService.SetAgent(steamID, agentForTeam.Team, agentForTeam.AgentIndex);
                }

                // Apply gloves - SAME LOGIC AS AGENTS
                var gloveForTeam = gloves.FirstOrDefault(g => g.Team == player.Controller.Team);
                if (gloveForTeam != null)
                {
                    // Update data service
                    DataService.GloveDataService.StoreGlove(gloveForTeam);
                }

                // Apply music kit
                var musicKit = musicKits.FirstOrDefault();
                if (musicKit.MusicKitIndex > 0)
                {
                    // Update data service
                    DataService.MusicKitDataService.SetMusicKit(steamID, musicKit.MusicKitIndex);
                    
                    // Update inventory service
                    InventoryService.UpdateMusicKit(steamID, musicKit.MusicKitIndex);
                }

                // Apply both agent and gloves together with delay to prevent race conditions
                Core.Scheduler.DelayBySeconds(0.1f, () =>
                {
                    // Apply agent visual update
                    var agent = EconService.Agents.Values.FirstOrDefault(a => a.Index == agentForTeam.AgentIndex);
                    if (agent != null && agentForTeam.SteamID != 0)
                    {
                        if (!player.IsAlive) return;
                        var pawn = player.PlayerPawn!;
                        
                        var current = pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance()
                            .ModelState
                            .ModelName;
                        
                        var refreshModel = GetRefreshModel(current, agent.ModelPath);
                        if (!string.IsNullOrWhiteSpace(refreshModel))
                        {
                            pawn.SetModel(refreshModel);
                            pawn.SetModel(current);
                        }
                        
                        Core.Scheduler.NextWorldUpdate(() =>
                        {
                            if (!player.IsAlive) return;
                            pawn.SetModel(agent.ModelPath);
                        });
                    }

                    // Apply glove visual update with FRESH team data
                    var currentTeam = player.Controller.Team;
                    
                    // Re-query fresh glove data for CURRENT team, not stale team
                    if (DataService.GloveDataService.TryGetGlove(steamID, currentTeam, out var freshGlove))
                    {
                        // Clear inventory cache first to force fresh data
                        InventoryService.ResetGloveSkin(steamID, currentTeam);
                        
                        // Normalize wear value before updating inventory
                        var normalizedGlove = freshGlove.DeepClone();
                        if (normalizedGlove.PaintkitWear < 0.0001f)
                        {
                            normalizedGlove.PaintkitWear = 0.0001f;
                        }
                        
                        // Update inventory right before visual update
                        InventoryService.UpdateGloveSkins(steamID, new List<GloveData> { normalizedGlove });
                        
                        // Use FULL glove application with all attributes like weapons/knives
                        if (player.IsAlive && player.PlayerPawn != null)
                        {
                            var pawn = player.PlayerPawn;
                            
                            // Add a small delay to ensure system is ready, then apply
                            Core.Scheduler.DelayBySeconds(0.05f, () =>
                            {
                                Core.Scheduler.NextWorldUpdate(() =>
                                {
                                    var model = pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance()
                                        .ModelState
                                        .ModelName;
                                    pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
                                    pawn.SetModel(model);

                                    Core.Scheduler.NextWorldUpdate(() =>
                                    {
                                        // Use normalized glove data to prevent visual glitches
                                        var econGloves = pawn.EconGloves;
                                        econGloves.ItemDefinitionIndex = normalizedGlove.DefinitionIndex;
                                        econGloves.NetworkedDynamicAttributes.SetOrAddAttribute("set item texture prefab", normalizedGlove.Paintkit);
                                        econGloves.NetworkedDynamicAttributes.SetOrAddAttribute("set item texture seed", normalizedGlove.PaintkitSeed);
                                        econGloves.NetworkedDynamicAttributes.SetOrAddAttribute("set item texture wear", normalizedGlove.PaintkitWear);
                                        econGloves.AttributeList.SetOrAddAttribute("set item texture prefab", normalizedGlove.Paintkit);
                                        econGloves.AttributeList.SetOrAddAttribute("set item texture seed", normalizedGlove.PaintkitSeed);
                                        econGloves.AttributeList.SetOrAddAttribute("set item texture wear", normalizedGlove.PaintkitWear);
                                        econGloves.Initialized = true;
                                        
                                        // Use simple bodygroup logic (fallback since GetClassnameByDefinitionIndex returns null for gloves)
                                        var bodygroupValue = 1; // Use legacy bodygroup like the original method
                                        pawn.AcceptInput("SetBodygroup", $"default_gloves,{bodygroupValue}");
                                        
                                        // Add a small delay to ensure visual changes take effect
                                        Core.Scheduler.DelayBySeconds(0.1f, () =>
                                        {
                                            // Visual delay completed
                                        });
                                    });
                                });
                            });
                        }
                    }
                    else
                    {
                        // Reset inventory right before visual reset
                        InventoryService.ResetGloveSkin(steamID, currentTeam);
                        
                        // Apply glove reset - no gloves for current team
                        var inventory = InventoryService.Get(steamID);
                        if (inventory != null)
                        {
                            player.RegiveGlove(inventory);
                        }
                    }
                });
            });
        });
    }

    private string GetRefreshModel(string currentModel, string targetModel)
    {
        foreach (var agent in EconService.Agents.Values)
        {
            var candidate = agent.ModelPath;
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (string.Equals(candidate, currentModel, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(candidate, targetModel, StringComparison.OrdinalIgnoreCase)) continue;

            return candidate;
        }

        return string.Empty;
    }
}