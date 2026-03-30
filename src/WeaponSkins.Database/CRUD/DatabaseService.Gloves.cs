using SwiftlyS2.Shared.Players;

using WeaponSkins.Shared;

namespace WeaponSkins.Database;

public partial class DatabaseService
{

    public async Task StoreGlovesAsync(IEnumerable<GloveData> gloves)
    {
        await fsql.InsertOrUpdate<GloveModel>()
            .SetSource(gloves.Select(glove => GloveModel.FromDataModel(glove)))
            .ExecuteAffrowsAsync();

        await fsql.InsertOrUpdate<SkinModel>()
            .SetSource(gloves.Select(glove => SkinModel.FromGloveDataModel(glove)))
            .ExecuteAffrowsAsync();
    }

    public async Task<GloveData?> GetGloveAsync(ulong steamId,
        Team team)
    {
        var model = await fsql.Select<GloveModel>()
            .Where(glove => glove.SteamID == steamId.ToString() && glove.Team == (short)team)
            .ToOneAsync();

        var data = model.ToDataModel();
        var skinModel = await fsql.Select<SkinModel>()
            .Where(skin => skin.SteamID == steamId.ToString() && skin.Team == (short)team &&
                           skin.DefinitionIndex == data.DefinitionIndex)
            .ToOneAsync();

        if (skinModel != null)
        {
            data.Paintkit = skinModel.PaintID;
            data.PaintkitWear = skinModel.Wear;
            data.PaintkitSeed = skinModel.Seed;
        }

        return data;
    }

    public async Task<IEnumerable<GloveData>> GetGlovesAsync(ulong steamId)
    {
        // Get all entries including duplicates, then handle them
        var results = await fsql.Select<GloveModel, SkinModel>()
            .LeftJoin((glove,
                    skin) =>
                glove.SteamID == skin.SteamID &&
                glove.Team == skin.Team &&
                glove.DefinitionIndex == skin.DefinitionIndex)
            .Where((glove, skin) => glove.SteamID == steamId.ToString())
            .ToListAsync((Glove,
                Skin) => new { Glove, Skin });

        // Group by glove and take only the first skin entry for each glove to handle duplicates
        // This matches the behavior of GetGloveAsync which uses ToOneAsync()
        var groupedResults = results
            .GroupBy(item => new { item.Glove.SteamID, item.Glove.Team, item.Glove.DefinitionIndex })
            .Select(group => group.First());  // Take first entry to match GetGloveAsync behavior

        return groupedResults.Select(item =>
        {
            var data = item.Glove.ToDataModel();

            if (item.Skin != null)
            {
                data.Paintkit = item.Skin.PaintID;
                data.PaintkitWear = item.Skin.Wear;
                data.PaintkitSeed = item.Skin.Seed;
            }

            return data;
        }).ToList();
    }

    public async Task<IEnumerable<GloveData>> GetAllGlovesAsync()
    {
        // Get all entries including duplicates, then handle them
        var results = await fsql.Select<GloveModel, SkinModel>()
            .LeftJoin((glove,
                    skin) =>
                glove.SteamID == skin.SteamID &&
                glove.Team == skin.Team &&
                glove.DefinitionIndex == skin.DefinitionIndex)
            .ToListAsync((Glove,
                Skin) => new { Glove, Skin });

        // Group by glove and take only the first skin entry for each glove to handle duplicates
        // This matches the behavior of GetGloveAsync which uses ToOneAsync()
        var groupedResults = results
            .GroupBy(item => new { item.Glove.SteamID, item.Glove.Team, item.Glove.DefinitionIndex })
            .Select(group => group.First());  // Take first entry to match GetGloveAsync behavior

        return groupedResults.Select(item =>
        {
            var data = item.Glove.ToDataModel();

            if (item.Skin != null)
            {
                data.Paintkit = item.Skin.PaintID;
                data.PaintkitWear = item.Skin.Wear;
                data.PaintkitSeed = item.Skin.Seed;
            }

            return data;
        }).ToList();
    }

    public async Task RemoveGloveAsync(ulong steamId,
        Team team)
    {
        await fsql.Delete<GloveModel>()
            .Where(glove => glove.SteamID == steamId.ToString() && glove.Team == (short)team)
            .ExecuteAffrowsAsync();
    }

    public async Task RemoveGlovesAsync(ulong steamId)
    {
        await fsql.Delete<GloveModel>()
            .Where(glove => glove.SteamID == steamId.ToString())
            .ExecuteAffrowsAsync();
    }
}