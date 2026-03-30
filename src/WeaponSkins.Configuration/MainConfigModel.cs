using Microsoft.Extensions.Configuration;

namespace WeaponSkins.Configuration;

public class MainConfigModel
{
    public string StorageBackend { get; set; } = "inherit";

    public string InventoryUpdateBackend { get; set; } = "hook";

    public bool SyncFromDatabaseWhenPlayerJoin { get; set; } = false;

    public List<string> ItemLanguages { get; set; } = [];

    public ItemPermissionConfig ItemPermissions { get; set; } = new();

    public WpCommandConfig WpCommand { get; set; } = new();

    public AutoSyncConfig AutoSync { get; set; } = new();

    public string PluginPrefix { get; set; } = "[WeaponSkins]";

    public bool DebugLogging { get; set; } = false;
}

public class WpCommandConfig
{
    public bool Enabled { get; set; } = true;

    public string CommandName { get; set; } = "wp";

    public float CooldownSeconds { get; set; } = 5.0f;

    public string Permission { get; set; } = "";
}

public class AutoSyncConfig
{
    public bool Enabled { get; set; } = false;

    public float PollingIntervalSeconds { get; set; } = 30.0f;
}