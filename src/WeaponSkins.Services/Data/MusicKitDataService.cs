using System.Collections.Concurrent;
using SwiftlyS2.Shared.Players;
using WeaponSkins.Shared;

namespace WeaponSkins.Services;

public class MusicKitDataService
{
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<Team, int>> _playerMusicKits = new();

    public bool SetMusicKit(ulong steamId, Team team, int musicKitIndex)
    {
        var playerMusicKits = _playerMusicKits.GetOrAdd(steamId, _ => new());
        var oldMusicKitIndex = playerMusicKits.GetOrAdd(team, 0);
        var changed = oldMusicKitIndex != musicKitIndex;
        if (changed)
        {
            playerMusicKits[team] = musicKitIndex;
        }
        return changed;
    }

    public bool TryGetMusicKit(ulong steamId, Team team, out int musicKitIndex)
    {
        musicKitIndex = 0;
        return _playerMusicKits.TryGetValue(steamId, out var playerMusicKits) &&
               playerMusicKits.TryGetValue(team, out musicKitIndex);
    }

    public bool TryGetMusicKits(ulong steamId, out IEnumerable<(Team team, int musicKitIndex)> musicKits)
    {
        musicKits = null;
        if (_playerMusicKits.TryGetValue(steamId, out var playerMusicKits))
        {
            musicKits = playerMusicKits.Select(kvp => (kvp.Key, kvp.Value));
            return true;
        }
        return false;
    }

    public bool TryGetMusicKits(ulong steamId, out IEnumerable<MusicKitData> musicKits)
    {
        musicKits = null;
        if (_playerMusicKits.TryGetValue(steamId, out var playerMusicKits))
        {
            musicKits = playerMusicKits.Select(kvp => new MusicKitData 
            { 
                SteamID = steamId, 
                Team = kvp.Key, 
                MusicKitIndex = kvp.Value 
            });
            return true;
        }
        return false;
    }

    public bool RemoveMusicKit(ulong steamId, Team team)
    {
        return _playerMusicKits.TryGetValue(steamId, out var playerMusicKits) && 
               playerMusicKits.TryRemove(team, out _);
    }

    public void RemovePlayer(ulong steamId)
    {
        _playerMusicKits.TryRemove(steamId, out _);
    }
}
