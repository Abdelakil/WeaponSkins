using SwiftlyS2.Shared.Players;

namespace WeaponSkins.Database;

public partial class DatabaseService
{
    public async Task StoreMusicKitsAsync(IEnumerable<(ulong SteamID, int MusicKitIndex)> musicKits)
    {
        var models = new List<MusicKitModel>();
        foreach (var mk in musicKits)
        {
            models.Add(MusicKitModel.FromDataModel(mk.SteamID, 0, mk.MusicKitIndex));
            models.Add(MusicKitModel.FromDataModel(mk.SteamID, 1, mk.MusicKitIndex));
        }
        
        await fsql.InsertOrUpdate<MusicKitModel>()
            .SetSource(models)
            .ExecuteAffrowsAsync();
    }

    public async Task StoreMusicKitAsync(ulong steamId, Team team, int musicKitIndex)
    {
        var model = MusicKitModel.FromDataModel(steamId, (int)team, musicKitIndex);
        await fsql.InsertOrUpdate<MusicKitModel>()
            .SetSource(model)
            .ExecuteAffrowsAsync();
    }

    public async Task<int?> GetMusicKitAsync(ulong steamId)
    {
        // Return T team music kit as fallback for old interface
        return await GetMusicKitAsync(steamId, Team.T);
    }

    public async Task<int?> GetMusicKitAsync(ulong steamId, Team team)
    {
        var model = await fsql.Select<MusicKitModel>()
            .Where(mk => mk.SteamID == steamId.ToString() && mk.WeaponTeam == (int)team)
            .ToOneAsync();
        return model?.MusicID;
    }

    public async Task<IEnumerable<(ulong SteamID, int MusicKitIndex)>> GetMusicKitsAsync(ulong steamId)
    {
        var models = await fsql.Select<MusicKitModel>()
            .Where(mk => mk.SteamID == steamId.ToString())
            .ToListAsync();
        return models.Select(m => (ulong.Parse(m.SteamID), m.MusicID));
    }

    public async Task<IEnumerable<(Team Team, int MusicKitIndex)>> GetMusicKitsByTeamAsync(ulong steamId)
    {
        var models = await fsql.Select<MusicKitModel>()
            .Where(mk => mk.SteamID == steamId.ToString())
            .ToListAsync();
        return models.Select(m => ((Team)m.WeaponTeam, m.MusicID));
    }

    public async Task<IEnumerable<(ulong SteamID, int MusicKitIndex)>> GetAllMusicKitsAsync()
    {
        var models = await fsql.Select<MusicKitModel>().ToListAsync();
        return models.Select(m => (ulong.Parse(m.SteamID), m.MusicID)).Distinct();
    }

    public async Task RemoveMusicKitAsync(ulong steamId)
    {
        await fsql.Delete<MusicKitModel>()
            .Where(mk => mk.SteamID == steamId.ToString())
            .ExecuteAffrowsAsync();
    }
}
