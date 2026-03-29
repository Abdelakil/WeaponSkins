using Microsoft.Extensions.Logging;

using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

using WeaponSkins.Database;

namespace WeaponSkins;

public partial class CommandService
{

    private ISwiftlyCore Core { get; init; }
    private ILogger Logger { get; init; }
    private MenuService MenuService { get; init; }
    private DatabaseSynchronizeService DatabaseSynchronizeService { get; init; }

    public CommandService(ISwiftlyCore core,
        ILogger<CommandService> logger,
        MenuService menuService,
        DatabaseSynchronizeService databaseSynchronizeService)
    {
        Core = core;
        Logger = logger;
        MenuService = menuService;
        DatabaseSynchronizeService = databaseSynchronizeService;

        RegisterCommands();
    }
    public void RegisterCommands()
    {
        Core.Command.RegisterCommand("ws", CommandSkin);
        Core.Command.RegisterCommand("wp", CommandSyncDatabase);
    }

    private void CommandSkin(ICommandContext context)
    {
        if (!context.IsSentByPlayer)
        {
            context.Reply("This command can only be used by players.");
            return;
        }

        MenuService.OpenMainMenu(context.Sender!);
    }

    private void CommandSyncDatabase(ICommandContext context)
    {
        if (!context.IsSentByPlayer)
        {
            context.Reply("This command can only be used by players.");
            return;
        }

        var steamID = context.Sender?.SteamID;
        if (steamID == null)
        {
            context.Reply("Unable to get your Steam ID.");
            return;
        }

        try
        {
            // Apply player's skins directly from database
            Task.Run(async () =>
            {
                try
                {
                    await DatabaseSynchronizeService.ApplyPlayerSkinsFromDBAsync(steamID.Value);
                    
                    // Switch back to main thread for reply
                    Core.Scheduler.NextWorldUpdate(() =>
                    {
                        context.Reply("Skins updated immediately from database!");
                    });
                    
                    Logger.LogInformation("Database sync triggered by player {SteamID}", steamID);
                }
                catch (Exception ex)
                {
                    // Switch back to main thread for error reply
                    Core.Scheduler.NextWorldUpdate(() =>
                    {
                        context.Reply("Failed to update skins from database. Check console for details.");
                    });
                    
                    Logger.LogError(ex, "Failed to sync database via !wp command");
                }
            });
        }
        catch (Exception ex)
        {
            context.Reply("Failed to start database synchronization.");
            Logger.LogError(ex, "Failed to start database sync via !wp command");
        }
    }
}