<div align="center">
  <img src="https://pan.samyyc.dev/s/VYmMXE" />
  <h2><strong>WeaponSkins (Beta)</strong></h2>
  <h3>A powerful swiftlys2 plugin to change player's weapon skins, knifes and gloves.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/samyycX/WeaponSkins/total" alt="Downloads">
  <img src="https://img.shields.io/github/stars/samyycX/WeaponSkins?style=flat&logo=github" alt="Stars">
  <img src="https://img.shields.io/github/license/samyycX/WeaponSkins" alt="License">
</p>

## Features
- MySQL & sqlite database support
- Fully functioning in-game skin menu
- Compatible with CounterStrikeSharp WeaponPaints database
- Long-term stattrak tracking
- Player-based skin name localization
- Completely game-based econ data dumping (no network required)

## Main Configuration
The `config.toml` should have the following structure in `Main` section:
```toml
[Main]
StorageBackend = "inherit"
InventoryUpdateBackend = "hook"
SyncFromDatabaseWhenPlayerJoin = false
ItemLanguages = []
```

#### `StorageBackend`
When set to `inherit`, the plugin use the database configuration from swiftlys2 database configuration.

When set to `sqlite`, the plugin use its own file-based sqlite database.

#### `InventoryUpdateBackend`
Recommended to set to `hook` for now. Another option is `inventory` but its deprecated.

#### `SyncFromDatabaseWhenPlayerJoin`
When set to true, the plugin will automatically synchronize skin data from database when a player join.

The update is asynchronous so it won't introduce lags in theory.

### `ItemLanguages`
When unset or set to empty list, the plugin will load item and skin names in all languages.

When set to a list of language, the **first** language will be used as default fallback language, and only  languages in this list will be loaded and set for player based on their game language.

This configuration helps optimizing your memory usage, reducing 80MB at max.

Check the `Code` column in this table for all available languages: 
[Available language codes](https://swiftlys2.net/docs/development/translations/#language-codes)

## `!wp` Command (Manual Skin Refresh)
Players can type `!wp` in chat to manually synchronize their skins from the database and instantly apply them in-game. This is useful when cosmetics are changed externally (e.g., via a website that modifies the database directly).

```toml
[Main.WpCommand]
Enabled = true
CommandName = "wp"
CooldownSeconds = 5.0
Permission = ""
```

- **`Enabled`** — Enable or disable the `!wp` command.
- **`CommandName`** — The chat command name (without `!`).
- **`CooldownSeconds`** — Minimum seconds between uses per player.
- **`Permission`** — Permission string required to use the command. Leave empty for all players.

### Application Order
When applying cosmetics (on `!wp`, spawn, or auto-sync), the plugin enforces this strict order:
1. **Agent model** (first — `SetModel()` resets body components)
2. **Weapons / Knife** (next tick)
3. **Gloves** (always last, 200ms after agent — because `SetModel()` destroys `EconGloves` state)

## Auto-Sync (Background Database Change Detection)
Automatically detects database changes for online players and applies updates in real-time. Uses efficient checksum-based polling — only triggers sync when actual data changes are detected.

```toml
[Main.AutoSync]
Enabled = false
PollingIntervalSeconds = 30.0
```

- **`Enabled`** — Enable or disable automatic background sync.
- **`PollingIntervalSeconds`** — How often to check for changes (minimum 5s).

## Debug Logging & Plugin Prefix
```toml
[Main]
PluginPrefix = "[WeaponSkins]"
DebugLogging = false
```

- **`PluginPrefix`** — Prefix for all chat messages.
- **`DebugLogging`** — When `true`, logs detailed information about every DB fetch, inventory update, and cosmetic application (weapon/knife/glove/agent/music with defindex, paint, team).

## Item Permissions
Gate entire feature groups with a single permission string in `config.toml`:
```toml
[Main.ItemPermissions]
WeaponSkins = "vip"
KnifeSkins = "vip"
GloveSkins = "vip"
Stickers = "vip"
Keychains = "vip"
Agents = "vip"
```
Leave a value empty or remove it to keep the feature available to everyone. Players without the required permission cannot open the related menus, and any equipped cosmetics of that type are hidden until they regain access.

## Showcase
[Youtube](https://youtu.be/MRa8JIRLysE)
  
## Building

- Open the project in your preferred .NET IDE (e.g., Visual Studio, Rider, VS Code).
- Build the project. The output DLL and resources will be placed in the `build/` directory.
- The publish process will also create a zip file for easy distribution.

## Publishing

- Use the `dotnet publish -c Release` command to build and package your plugin.
- Distribute the generated zip file or the contents of the `build/publish` directory.
