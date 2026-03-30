using System.Security.Cryptography;
using System.Text;

namespace WeaponSkins.Database;

public partial class DatabaseService
{
    public async Task<long> GetSkinsCountAsync(string steamId)
    {
        return await fsql.Select<SkinModel>()
            .Where(s => s.SteamID == steamId)
            .CountAsync();
    }

    public async Task<long> GetKnivesCountAsync(string steamId)
    {
        return await fsql.Select<KnifeModel>()
            .Where(k => k.SteamID == steamId)
            .CountAsync();
    }

    public async Task<long> GetGlovesCountAsync(string steamId)
    {
        return await fsql.Select<GloveModel>()
            .Where(g => g.SteamID == steamId)
            .CountAsync();
    }

    public async Task<long> GetAgentsCountAsync(string steamId)
    {
        return await fsql.Select<AgentModel>()
            .Where(a => a.SteamID == steamId)
            .CountAsync();
    }

    private static string ComputeHash(StringBuilder sb)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    public async Task<string> GetSkinsChecksumAsync(string steamId)
    {
        var skins = await fsql.Select<SkinModel>()
            .Where(s => s.SteamID == steamId)
            .ToListAsync(s => new { s.Team, s.DefinitionIndex, s.PaintID, s.Wear, s.Seed, s.Stattrak, s.Nametag });

        if (skins.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var s in skins.OrderBy(x => x.Team).ThenBy(x => x.DefinitionIndex))
        {
            // Normalize wear value to prevent floating-point precision issues
            // EXCLUDE StattrakCount from checksum since it increments during gameplay
            var normalizedWear = s.Wear.ToString("F8", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"{s.Team}:{s.DefinitionIndex}:{s.PaintID}:{normalizedWear}:{s.Seed}:{s.Stattrak}:{s.Nametag ?? ""}|");
        }

        return ComputeHash(sb);
    }

    public async Task<string> GetKnivesChecksumAsync(string steamId)
    {
        var knives = await fsql.Select<KnifeModel>()
            .Where(k => k.SteamID == steamId)
            .ToListAsync(k => new { k.Team, k.Knife });

        if (knives.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var k in knives.OrderBy(x => x.Team))
        {
            sb.Append($"{k.Team}:{k.Knife}|");
        }

        // Only include knife definition indices from skins, not all skin data
        // This prevents false positives when skin data changes
        var knifeSkins = await fsql.Select<SkinModel>()
            .Where(s => s.SteamID == steamId && s.DefinitionIndex >= 500 && s.DefinitionIndex < 600)
            .ToListAsync(s => new { s.Team, s.DefinitionIndex });

        foreach (var s in knifeSkins.OrderBy(x => x.Team).ThenBy(x => x.DefinitionIndex))
        {
            sb.Append($"{s.Team}:{s.DefinitionIndex}|");
        }

        return ComputeHash(sb);
    }

    public async Task<string> GetGlovesChecksumAsync(string steamId)
    {
        var gloves = await fsql.Select<GloveModel>()
            .Where(g => g.SteamID == steamId)
            .ToListAsync(g => new { g.Team, g.DefinitionIndex });

        if (gloves.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var g in gloves.OrderBy(x => x.Team))
        {
            sb.Append($"{g.Team}:{g.DefinitionIndex}|");
        }

        // Only include glove definition indices and normalized wear from skins, not all skin data
        // This prevents false positives when skin data changes
        var gloveSkins = await fsql.Select<SkinModel>()
            .Where(s => s.SteamID == steamId && s.DefinitionIndex > 5000)
            .ToListAsync(s => new { s.Team, s.DefinitionIndex, s.Wear });

        foreach (var s in gloveSkins.OrderBy(x => x.Team).ThenBy(x => x.DefinitionIndex))
        {
            // Normalize wear value to prevent floating-point precision issues
            var normalizedWear = s.Wear.ToString("F8", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"{s.Team}:{s.DefinitionIndex}:{normalizedWear}|");
        }

        return ComputeHash(sb);
    }

    public async Task<string> GetAgentsChecksumAsync(string steamId)
    {
        var agents = await fsql.Select<AgentModel>()
            .Where(a => a.SteamID == steamId)
            .ToListAsync(a => new { a.Team, a.AgentIndex });

        if (agents.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var a in agents.OrderBy(x => x.Team))
        {
            sb.Append($"{a.Team}:{a.AgentIndex}|");
        }

        return ComputeHash(sb);
    }

    public async Task<long> GetMusicKitsCountAsync(string steamId)
    {
        return await fsql.Select<MusicKitModel>()
            .Where(m => m.SteamID == steamId)
            .CountAsync();
    }

    public async Task<string> GetMusicKitsChecksumAsync(string steamId)
    {
        var musicKits = await fsql.Select<MusicKitModel>()
            .Where(m => m.SteamID == steamId)
            .ToListAsync(m => new { m.WeaponTeam, m.MusicID });

        if (musicKits.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var mk in musicKits.OrderBy(x => x.WeaponTeam))
        {
            sb.Append($"{mk.WeaponTeam}:{mk.MusicID}|");
        }

        return ComputeHash(sb);
    }
}
