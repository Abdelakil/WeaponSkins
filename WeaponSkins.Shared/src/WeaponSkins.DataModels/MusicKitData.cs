using SwiftlyS2.Shared.Players;

namespace WeaponSkins.Shared;

public record MusicKitData
{
    public required ulong SteamID { get; set; }
    public required Team Team { get; init; }
    public required int MusicKitIndex { get; init; }
    
    public MusicKitData DeepClone()
    {
        return new MusicKitData
        {
            SteamID = SteamID,
            Team = Team,
            MusicKitIndex = MusicKitIndex
        };
    }
}
