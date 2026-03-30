using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

using WeaponSkins.Configuration;
using WeaponSkins.Database;
using WeaponSkins.Extensions;
using WeaponSkins.Services;
using WeaponSkins.Shared;

namespace WeaponSkins;

public class WpCommandService
{
    private ISwiftlyCore Core { get; init; }
    private ILogger<WpCommandService> Logger { get; init; }
    private DatabaseSynchronizeService DatabaseSynchronizeService { get; init; }
    private InventoryService InventoryService { get; init; }
    private PlayerService PlayerService { get; init; }
    private DataService DataService { get; init; }
    private WeaponSkinGetterAPI Api { get; init; }
    private Econ.EconService EconService { get; init; }

    private readonly ConcurrentDictionary<ulong, DateTime> _cooldowns = new();
    private readonly ConcurrentDictionary<ulong, Team> _playerTeams = new();

    private WpCommandConfig _config;
    private MainConfigModel _mainConfig;

    public WpCommandService(
        ISwiftlyCore core,
        ILogger<WpCommandService> logger,
        IOptionsMonitor<MainConfigModel> options,
        DatabaseSynchronizeService databaseSynchronizeService,
        InventoryService inventoryService,
        PlayerService playerService,
        DataService dataService,
        WeaponSkinGetterAPI api,
        Econ.EconService econService)
    {
        Core = core;
        Logger = logger;
        DatabaseSynchronizeService = databaseSynchronizeService;
        InventoryService = inventoryService;
        PlayerService = playerService;
        DataService = dataService;
        Api = api;
        EconService = econService;

        _config = options.CurrentValue.WpCommand;
        _mainConfig = options.CurrentValue;

        options.OnChange(newConfig =>
        {
            _config = newConfig.WpCommand;
            _mainConfig = newConfig;
        });

        Core.Event.OnClientDisconnected += (@event) =>
        {
            var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
            if (player == null) return;
            _cooldowns.TryRemove(player.SteamID, out _);
            _playerTeams.TryRemove(player.SteamID, out _);
        };

        // Start team change monitoring using timer
        var teamChangeTimer = new System.Timers.Timer(1000); // 1 second
        teamChangeTimer.Elapsed += (_, _) =>
        {
            try
            {
                var playersToRemove = new List<ulong>();
                
                foreach (var (steamId, _) in _playerTeams)
                {
                    if (PlayerService.TryGetPlayer(steamId, out var player))
                    {
                        var currentTeam = player.Controller.Team;
                        
                        // Check if team changed
                        if (_playerTeams.TryGetValue(steamId, out var lastTeam) && lastTeam != currentTeam)
                        {
                            if (_mainConfig.DebugLogging)
                                Logger.LogInformation("[WP] [{SteamId}] Team changed from {OldTeam} to {NewTeam}, applying cosmetics", steamId, lastTeam, currentTeam);
                            
                            // Update stored team
                            _playerTeams[steamId] = currentTeam;
                            
                            // Apply cosmetics for new team
                            if (player.IsAlive())
                            {
                                Core.Scheduler.NextWorldUpdate(() =>
                                {
                                    ApplyAllCosmetics(player);
                                });
                            }
                        }
                    }
                    else
                    {
                        // Player no longer exists, mark for removal
                        playersToRemove.Add(steamId);
                    }
                }
                
                // Remove disconnected players
                foreach (var steamId in playersToRemove)
                {
                    _playerTeams.TryRemove(steamId, out _);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[WP] Error in team change monitoring");
            }
        };
        teamChangeTimer.Start();

        if (_config.Enabled)
        {
            RegisterCommand();
        }

        Logger.LogInformation("WpCommandService initialized. Command enabled: {Enabled}, name: {Name}, cooldown: {Cooldown}s",
            _config.Enabled, _config.CommandName, _config.CooldownSeconds);
    }

    private void RegisterCommand()
    {
        Core.Command.RegisterCommand(_config.CommandName, OnWpCommand);
    }

    private void OnWpCommand(ICommandContext context)
    {
        if (!context.IsSentByPlayer)
        {
            context.Reply("This command can only be used by players.");
            return;
        }

        if (!_config.Enabled)
        {
            context.Reply($"{_mainConfig.PluginPrefix} This command is currently disabled.");
            return;
        }

        var player = context.Sender!;
        var steamId = player.SteamID;

        // Permission check
        if (!string.IsNullOrEmpty(_config.Permission))
        {
            if (!Core.Permission.PlayerHasPermission(steamId, _config.Permission))
            {
                context.Reply($"{_mainConfig.PluginPrefix} You do not have permission to use this command.");
                return;
            }
        }

        // Cooldown check
        if (_cooldowns.TryGetValue(steamId, out var lastUsed))
        {
            var elapsed = (DateTime.UtcNow - lastUsed).TotalSeconds;
            if (elapsed < _config.CooldownSeconds)
            {
                var remaining = Math.Ceiling(_config.CooldownSeconds - elapsed);
                context.Reply($"{_mainConfig.PluginPrefix} Please wait {remaining}s before using this command again.");
                return;
            }
        }

        _cooldowns[steamId] = DateTime.UtcNow;

        context.Reply($"{_mainConfig.PluginPrefix} Syncing your skins from database...");

        if (_mainConfig.DebugLogging)
            Logger.LogInformation("[WP] Player {SteamId} triggered !{Cmd}", steamId, _config.CommandName);

        Task.Run(async () =>
        {
            try
            {
                await DatabaseSynchronizeService.SynchronizePlayerAsync(steamId);

                Core.Scheduler.NextWorldUpdate(() =>
                {
                    ApplyAllCosmetics(player);
                    context.Reply($"{_mainConfig.PluginPrefix} Your skins have been refreshed!");
                });

                if (_mainConfig.DebugLogging)
                    Logger.LogInformation("[WP] Successfully synced and applied cosmetics for {SteamId}", steamId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[WP] Failed to sync cosmetics for {SteamId}", steamId);
                Core.Scheduler.NextWorldUpdate(() =>
                {
                    context.Reply($"{_mainConfig.PluginPrefix} Failed to sync skins. Please try again later.");
                });
            }
        });
    }

    public void ApplyAllCosmetics(IPlayer player)
    {
        var steamId = player.SteamID;
        var debug = _mainConfig.DebugLogging;

        if (!InventoryService.TryGet(steamId, out var inventory))
        {
            if (debug)
                Logger.LogWarning("[WP] No inventory found for {SteamId}", steamId);
            return;
        }

        // ── Phase 1: Update inventory items (all types) ──
        if (Api.TryGetWeaponSkins(steamId, out var skins))
        {
            foreach (var skin in skins)
            {
                inventory.UpdateWeaponSkin(skin);
                if (debug)
                    Logger.LogInformation("[WP] [{SteamId}] Inventory updated: weapon defindex={DefIdx} paint={Paint} team={Team}",
                        steamId, skin.DefinitionIndex, skin.Paintkit, skin.Team);
            }
        }

        if (Api.TryGetKnifeSkins(steamId, out var knives))
        {
            foreach (var knife in knives)
            {
                inventory.UpdateKnifeSkin(knife);
                if (debug)
                    Logger.LogInformation("[WP] [{SteamId}] Inventory updated: knife defindex={DefIdx} paint={Paint} team={Team}",
                        steamId, knife.DefinitionIndex, knife.Paintkit, knife.Team);
            }
        }

        if (Api.TryGetGloveSkins(steamId, out var gloves))
        {
            foreach (var glove in gloves)
            {
                inventory.UpdateGloveSkin(glove);
                if (debug)
                    Logger.LogInformation("[WP] [{SteamId}] Inventory updated: glove defindex={DefIdx} paint={Paint} team={Team}",
                        steamId, glove.DefinitionIndex, glove.Paintkit, glove.Team);
            }
        }

        // ── If player is not alive, inventory updates are enough (applied on next spawn) ──
        if (!PlayerService.TryGetPlayer(steamId, out var currentPlayer)) return;
        if (!currentPlayer.IsAlive())
        {
            if (debug)
                Logger.LogInformation("[WP] [{SteamId}] Player not alive, inventory updated only (will apply on spawn)", steamId);
            return;
        }

        var team = currentPlayer.Controller.Team;

        // Register player for team change monitoring
        _playerTeams.TryAdd(steamId, team);

        // Get team-specific music kit and update inventory
        if (DataService.MusicKitDataService.TryGetMusicKit(steamId, team, out var musicKitIndex))
        {
            // Use original method to test if it works
            inventory.UpdateMusicKit(musicKitIndex);
            if (debug)
                Logger.LogInformation("[WP] [{SteamId}] Inventory updated: music kit index={MusicKit} team={Team} (original method)", steamId, musicKitIndex, team);
        }
        else if (debug)
        {
            Logger.LogWarning("[WP] [{SteamId}] No music kit found for team={Team}", steamId, team);
        }

        // ── Phase 2: Apply agent FIRST (takes ~2 ticks via SetModel) ──
        if (debug)
            Logger.LogInformation("[WP] [{SteamId}] Phase 2: Applying agent model (team={Team})", steamId, team);
        ApplyPlayerAgent(currentPlayer);

        // ── Phase 3: Regive weapons and knife (next tick, after agent starts) ──
        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (!currentPlayer.IsAlive()) return;
            var pawn = currentPlayer.PlayerPawn!;

            if (Api.TryGetWeaponSkins(steamId, out var weaponSkins))
            {
                foreach (var skin in weaponSkins)
                {
                    if (skin.Team != team) continue;
                    foreach (var weapon in pawn.WeaponServices!.MyWeapons)
                    {
                        if (weapon.Value!.AttributeManager.Item.ItemDefinitionIndex == skin.DefinitionIndex)
                        {
                            var defIdx = skin.DefinitionIndex;
                            var w = weapon.Value;
                            if (debug)
                                Logger.LogInformation("[WP] [{SteamId}] Phase 3: Regiving weapon defindex={DefIdx}", steamId, defIdx);
                            currentPlayer.RegiveWeapon(w, defIdx);
                        }
                    }
                }
            }

            if (Api.TryGetKnifeSkins(steamId, out var knifeSkins))
            {
                foreach (var knife in knifeSkins)
                {
                    if (knife.Team == team)
                    {
                        if (debug)
                            Logger.LogInformation("[WP] [{SteamId}] Phase 3: Regiving knife defindex={DefIdx}", steamId, knife.DefinitionIndex);
                        currentPlayer.RegiveKnife();
                        break;
                    }
                }
            }
        });

        // ── Phase 4: Apply gloves ALWAYS LAST (after agent model is fully settled) ──
        // Agent takes ~2 ticks (refresh + final SetModel). 200ms delay ensures completion.
        // SetModel() resets EconGloves, so gloves MUST come after all model changes.
        Core.Scheduler.DelayBySeconds(0.2f, () =>
        {
            if (!currentPlayer.IsAlive())
            {
                if (debug)
                    Logger.LogInformation("[WP] [{SteamId}] Phase 4: Skipped gloves — player not alive", steamId);
                return;
            }

            if (!InventoryService.TryGet(steamId, out var inv))
            {
                if (debug)
                    Logger.LogWarning("[WP] [{SteamId}] Phase 4: Skipped gloves — no inventory", steamId);
                return;
            }

            if (Api.TryGetGloveSkins(steamId, out var gloveSkins))
            {
                foreach (var glove in gloveSkins)
                {
                    if (glove.Team == team)
                    {
                        if (debug)
                            Logger.LogInformation("[WP] [{SteamId}] Phase 4: Applying gloves defindex={DefIdx} paint={Paint} (200ms after agent)",
                                steamId, glove.DefinitionIndex, glove.Paintkit);
                        
                        // Apply the specific glove data directly instead of reading from inventory loadout
                        // This ensures we use the correct glove data from auto-sync, not stale inventory data
                        currentPlayer.ApplyGloveData(glove);
                        break;
                    }
                }
            }
            else
            {
                if (debug)
                    Logger.LogInformation("[WP] [{SteamId}] Phase 4: No glove data found", steamId);
            }
        });
    }

    private string? GetRefreshModel(string currentModel, string targetModel)
    {
        foreach (var agent in EconService.Agents.Values)
        {
            var candidate = agent.ModelPath;
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (string.Equals(candidate, currentModel, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(candidate, targetModel, StringComparison.OrdinalIgnoreCase)) continue;
            return candidate;
        }
        return null;
    }

    private void ApplyPlayerAgent(IPlayer player)
    {
        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (!player.IsAlive()) return;

            var pawn = player.PlayerPawn!;
            var current = pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance()
                .ModelState
                .ModelName;
            DataService.AgentDataService.CaptureDefaultModel(player.SteamID, player.Controller.Team, current);

            if (DataService.AgentDataService.TryGetAgent(player.SteamID, player.Controller.Team, out var agentIndex))
            {
                var agent = EconService.Agents.Values.FirstOrDefault(a => a.Index == agentIndex);
                if (agent != null)
                {
                    var modelPath = agent.ModelPath;
                    var refreshModel = GetRefreshModel(current, modelPath);
                    if (!string.IsNullOrWhiteSpace(refreshModel))
                    {
                        pawn.SetModel(refreshModel);
                        pawn.SetModel(current);
                    }

                    Core.Scheduler.NextWorldUpdate(() =>
                    {
                        if (!player.IsAlive()) return;
                        pawn.SetModel(modelPath);
                    });
                }
            }
            else if (DataService.AgentDataService.TryGetDefaultModel(player.SteamID, player.Controller.Team,
                         out var defaultModel))
            {
                var refreshModel = GetRefreshModel(current, defaultModel);
                if (!string.IsNullOrWhiteSpace(refreshModel))
                {
                    pawn.SetModel(refreshModel);
                    pawn.SetModel(current);
                }

                Core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!player.IsAlive()) return;
                    pawn.SetModel(defaultModel);
                });
            }
        });
    }
}
