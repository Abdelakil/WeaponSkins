using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

using WeaponSkins.Configuration;
using WeaponSkins.Database;
using WeaponSkins.Extensions;
using WeaponSkins.Services;
using WeaponSkins.Shared;

namespace WeaponSkins;

public class AutoSyncService : IDisposable
{
    private ISwiftlyCore Core { get; init; }
    private ILogger<AutoSyncService> Logger { get; init; }
    private DatabaseService DatabaseService { get; init; }
    private DatabaseSynchronizeService DatabaseSynchronizeService { get; init; }
    private PlayerService PlayerService { get; init; }
    private WpCommandService WpCommandService { get; init; }
    private InventoryService InventoryService { get; init; }

    private AutoSyncConfig _config;
    private MainConfigModel _mainConfig;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    // Track last-known data hashes per player to detect changes
    private readonly ConcurrentDictionary<ulong, string> _playerDataHashes = new();

    public AutoSyncService(
        ISwiftlyCore core,
        ILogger<AutoSyncService> logger,
        IOptionsMonitor<MainConfigModel> options,
        DatabaseService databaseService,
        DatabaseSynchronizeService databaseSynchronizeService,
        PlayerService playerService,
        WpCommandService wpCommandService,
        InventoryService inventoryService)
    {
        Core = core;
        Logger = logger;
        DatabaseService = databaseService;
        DatabaseSynchronizeService = databaseSynchronizeService;
        PlayerService = playerService;
        WpCommandService = wpCommandService;
        InventoryService = inventoryService;

        _config = options.CurrentValue.AutoSync;
        _mainConfig = options.CurrentValue;

        options.OnChange(newConfig =>
        {
            var wasEnabled = _config.Enabled;
            _config = newConfig.AutoSync;
            _mainConfig = newConfig;

            if (_config.Enabled && !wasEnabled)
                Start();
            else if (!_config.Enabled && wasEnabled)
                Stop();
        });

        // Clean up cached hashes when a player disconnects
        Core.Event.OnClientDisconnected += (@event) =>
        {
            var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
            if (player != null)
                OnPlayerDisconnected(player.SteamID);
        };

        if (_config.Enabled)
        {
            Start();
        }

        Logger.LogInformation("AutoSyncService initialized. Enabled: {Enabled}, interval: {Interval}s",
            _config.Enabled, _config.PollingIntervalSeconds);
    }

    private void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _pollingTask = PollLoopAsync(_cts.Token);
        Logger.LogInformation("AutoSync polling started with interval {Interval}s", _config.PollingIntervalSeconds);
    }

    private void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _pollingTask = null;
        Logger.LogInformation("AutoSync polling stopped.");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var intervalMs = (int)(_config.PollingIntervalSeconds * 1000);
                await Task.Delay(Math.Max(intervalMs, 5000), ct);

                if (ct.IsCancellationRequested) break;

                await PollOnlinePlayersAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[AutoSync] Error during polling cycle");
            }
        }
    }

    private async Task PollOnlinePlayersAsync(CancellationToken ct)
    {
        // Get all online players from the main thread
        List<(ulong SteamId, IPlayer Player)> onlinePlayers = [];

        // We need to gather online player IDs; the PlayerManager is main-thread only,
        // but PlayerService stores them in a dictionary we can iterate.
        // Collect connected players via Core.PlayerManager on the next world update
        var tcs = new TaskCompletionSource<List<ulong>>();
        Core.Scheduler.NextWorldUpdate(() =>
        {
            var ids = new List<ulong>();
            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                if (player.Controller is { IsValid: true })
                    ids.Add(player.SteamID);
            }
            tcs.TrySetResult(ids);
        });

        List<ulong> steamIds;
        try
        {
            steamIds = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch
        {
            return;
        }

        if (steamIds.Count == 0) return;

        if (_mainConfig.DebugLogging)
            Logger.LogInformation("[AutoSync] Polling {Count} online players for changes", steamIds.Count);

        foreach (var steamId in steamIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var hasChanges = await CheckAndSyncPlayerAsync(steamId);
                if (hasChanges)
                {
                    if (_mainConfig.DebugLogging)
                        Logger.LogInformation("[AutoSync] Changes detected for {SteamId}, applying cosmetics", steamId);

                    Core.Scheduler.DelayBySeconds(2.0f, () => // Longer delay to ensure player is fully connected and team is settled
                    {
                        if (PlayerService.TryGetPlayer(steamId, out var player))
                        {
                            if (_mainConfig.DebugLogging)
                            {
                                Logger.LogInformation("[AutoSync] [{SteamId}] Applying cosmetics for current team={Team}", steamId, player.Controller.Team);
                            }
                            WpCommandService.ApplyAllCosmetics(player);
                            
                            // If player is not alive, retry when they spawn
                            if (!player.IsAlive())
                            {
                                Core.Scheduler.DelayBySeconds(1.0f, () =>
                                {
                                    if (PlayerService.TryGetPlayer(steamId, out var alivePlayer) && alivePlayer.IsAlive())
                                    {
                                        if (_mainConfig.DebugLogging)
                                            Logger.LogInformation("[AutoSync] [{SteamId}] Retrying cosmetics application - player is now alive", steamId);
                                        WpCommandService.ApplyAllCosmetics(alivePlayer);
                                    }
                                });
                            }
                        }
                        else if (_mainConfig.DebugLogging)
                        {
                            Logger.LogWarning("[AutoSync] [{SteamId}] Player not found for cosmetic application", steamId);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[AutoSync] Error checking player {SteamId}", steamId);
            }
        }
    }

    private async Task<bool> CheckAndSyncPlayerAsync(ulong steamId)
    {
        // Build a hash of current DB state for this player
        var newHash = await BuildPlayerDataHashAsync(steamId);
        var oldHash = _playerDataHashes.GetValueOrDefault(steamId, "");

        if (newHash == oldHash)
            return false;

        // Data changed — sync it
        await DatabaseSynchronizeService.SynchronizePlayerAsync(steamId);
        _playerDataHashes[steamId] = newHash;
        return true;
    }

    private async Task<string> BuildPlayerDataHashAsync(ulong steamId)
    {
        // Build a lightweight fingerprint from DB data
        // We concatenate counts and key fields to detect changes without deep comparison
        var steamIdStr = steamId.ToString();

        var skinCount = await DatabaseService.GetSkinsCountAsync(steamIdStr);
        var knifeCount = await DatabaseService.GetKnivesCountAsync(steamIdStr);
        var gloveCount = await DatabaseService.GetGlovesCountAsync(steamIdStr);
        var agentCount = await DatabaseService.GetAgentsCountAsync(steamIdStr);
        var musicKitCount = await DatabaseService.GetMusicKitsCountAsync(steamIdStr);

        // Also get a checksum of actual data to detect attribute changes (paint, wear, etc.)
        var skinChecksum = await DatabaseService.GetSkinsChecksumAsync(steamIdStr);
        var knifeChecksum = await DatabaseService.GetKnivesChecksumAsync(steamIdStr);
        var gloveChecksum = await DatabaseService.GetGlovesChecksumAsync(steamIdStr);
        var agentChecksum = await DatabaseService.GetAgentsChecksumAsync(steamIdStr);
        var musicKitChecksum = await DatabaseService.GetMusicKitsChecksumAsync(steamIdStr);

        return $"{skinCount}:{skinChecksum}|{knifeCount}:{knifeChecksum}|{gloveCount}:{gloveChecksum}|{agentCount}:{agentChecksum}|{musicKitCount}:{musicKitChecksum}";
    }

    public void OnPlayerDisconnected(ulong steamId)
    {
        _playerDataHashes.TryRemove(steamId, out _);
    }

    public void Dispose()
    {
        Stop();
        _playerDataHashes.Clear();
    }
}
