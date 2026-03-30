using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SwiftlyS2.Shared.Players;
using WeaponSkins.Services;
using WeaponSkins.Shared;
using WeaponSkins.Configuration;

namespace WeaponSkins.Database;

public class DatabaseSynchronizeService
{
    private DatabaseService DatabaseService { get; init; }
    private DataService DataService { get; init; }
    private ILogger<DatabaseSynchronizeService> Logger { get; init; }
    private MainConfigModel _mainConfig;

    public DatabaseSynchronizeService(DatabaseService databaseService,
        DataService dataService,
        ILogger<DatabaseSynchronizeService> logger,
        IOptionsMonitor<MainConfigModel> options)
    {
        DatabaseService = databaseService;
        DataService = dataService;
        Logger = logger;
        _mainConfig = options.CurrentValue;
        options.OnChange(newConfig => _mainConfig = newConfig);
    }

    public void Synchronize()
    {
        Task.Run(async () =>
        {
            var skins = await DatabaseService.GetAllSkinsAsync();
            skins.ToList().ForEach(skin => DataService.WeaponDataService.StoreSkin(skin));
            var knives = await DatabaseService.GetAllKnifesAsync();
            knives.ToList().ForEach(knife => DataService.KnifeDataService.StoreKnife(knife));
            var gloves = await DatabaseService.GetAllGlovesAsync();
            gloves.ToList().ForEach(glove => DataService.GloveDataService.StoreGlove(glove));
            var agents = await DatabaseService.GetAllAgentsAsync();
            agents.ToList().ForEach(agent => DataService.AgentDataService.SetAgent(agent.SteamID, agent.Team, agent.AgentIndex));
            // Skip global music kit sync since GetAllMusicKitsAsync doesn't provide team info
            // Team-specific music kits will be synced when individual players join via SynchronizePlayerAsync
            // This prevents overwriting team-specific music kits with both-teams fallback
        });
    }

    public async Task SynchronizePlayerAsync(ulong steamId)
    {
        var debug = _mainConfig.DebugLogging;

        var skins = await DatabaseService.GetSkinsAsync(steamId);
        foreach (var skin in skins)
        {
            DataService.WeaponDataService.StoreSkin(skin);
            if (debug)
                Logger.LogInformation("[Sync] [{SteamId}] DB→Memory: weapon defindex={DefIdx} paint={Paint} wear={Wear} seed={Seed} team={Team}",
                    steamId, skin.DefinitionIndex, skin.Paintkit, skin.PaintkitWear, skin.PaintkitSeed, skin.Team);
        }

        var knives = await DatabaseService.GetKnifesAsync(steamId);
        foreach (var knife in knives)
        {
            DataService.KnifeDataService.StoreKnife(knife);
            if (debug)
                Logger.LogInformation("[Sync] [{SteamId}] DB→Memory: knife defindex={DefIdx} paint={Paint} team={Team}",
                    steamId, knife.DefinitionIndex, knife.Paintkit, knife.Team);
        }

        var gloves = await DatabaseService.GetGlovesAsync(steamId);
        foreach (var glove in gloves)
        {
            DataService.GloveDataService.StoreGlove(glove);
            if (debug)
                Logger.LogInformation("[Sync] [{SteamId}] DB→Memory: glove defindex={DefIdx} paint={Paint} wear={Wear} seed={Seed} team={Team}",
                    steamId, glove.DefinitionIndex, glove.Paintkit, glove.PaintkitWear, glove.PaintkitSeed, glove.Team);
        }

        var agents = await DatabaseService.GetAgentsAsync(steamId);
        foreach (var agent in agents)
        {
            DataService.AgentDataService.SetAgent(agent.SteamID, agent.Team, agent.AgentIndex);
            if (debug)
                Logger.LogInformation("[Sync] [{SteamId}] DB→Memory: agent index={AgentIdx} team={Team}",
                    steamId, agent.AgentIndex, agent.Team);
        }

        var musicKits = await DatabaseService.GetMusicKitsByTeamAsync(steamId);
        foreach (var musicKit in musicKits)
        {
            DataService.MusicKitDataService.SetMusicKit(steamId, musicKit.Team, musicKit.MusicKitIndex);
            if (debug)
                Logger.LogInformation("[Sync] [{SteamId}] DB→Memory: music kit index={MusicKit} team={Team}", steamId, musicKit.MusicKitIndex, musicKit.Team);
        }

        if (debug)
            Logger.LogInformation("[Sync] [{SteamId}] Sync complete: {Skins} skins, {Knives} knives, {Gloves} gloves, {Agents} agents, {MusicKits} music kits",
                steamId, skins.Count(), knives.Count(), gloves.Count(), agents.Count(), musicKits.Count());
    }
}