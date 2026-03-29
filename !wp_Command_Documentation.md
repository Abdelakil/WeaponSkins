# !wp Command Documentation

## Overview
The `!wp` command is a custom chat command for the WeaponSkins plugin that reads and applies updated skin data directly from the database, bypassing the cache and eliminating the need to wait for the next round.

## Usage
```
!wp
```

## Functionality
- **Direct Database Read**: Queries the database directly for the player's latest skin data
- **Immediate Application**: Applies skins, knives, gloves, agents, and music kits instantly
- **Bypasses Cache**: Does not rely on cached data, ensuring fresh updates
- **Real-time Updates**: Reflects website changes immediately without round restart

## Features
- **Player-specific**: Only applies skins to the player who executed the command
- **Complete Coverage**: Updates weapons, knives, gloves, agents, and music kits
- **Team-aware**: Applies correct skins based on player's current team
- **Error Handling**: Provides clear feedback on success or failure
- **Thread-safe**: Uses proper main thread scheduling for all operations

## Response Messages
- **Success**: "Skins updated immediately from database!"
- **Error**: "Failed to update skins from database. Check console for details."
- **Invalid User**: "This command can only be used by players."
- **Steam ID Error**: "Unable to get your Steam ID."

## Technical Details

### Database Queries
- Queries all player-specific data: `GetSkinsAsync()`, `GetKnifesAsync()`, `GetGlovesAsync()`, `GetAgentsAsync()`, `GetMusicKitsAsync()`
- Bypasses in-memory cache for guaranteed fresh data
- Handles team-specific skin applications

### Application Process
1. **Database Read**: Direct queries for player's latest data
2. **API Updates**: Uses `WeaponSkinAPI` methods to update skin data
3. **Immediate Application**: Regives weapons/knives for instant visual updates
4. **Main Thread Safety**: All UI operations scheduled via `Core.Scheduler.NextWorldUpdate()`

### Supported Items
- **Weapons**: All weapon skins with paintkits, wear, seed, quality, stattrak, nametags
- **Knives**: Knife skins with full customization support
- **Gloves**: Team-specific glove applications
- **Agents**: Character model updates
- **Music Kits**: Audio customization

## Safety Features
- **Exception Handling**: Comprehensive error catching and logging
- **Player Validation**: Ensures command is executed by valid players
- **Thread Safety**: All chat replies and skin applications on main thread
- **Null Safety**: Proper null checking for all player data
- **Performance**: Async operations prevent game lag

## Integration
- **Database Service**: Uses existing `DatabaseService` for queries
- **WeaponSkinAPI**: Leverages established API for skin updates
- **Player Extensions**: Uses extension methods for weapon regiving
- **Logging**: Integrated with existing logging system

## Use Cases
- **Website Updates**: Users change skins on website and want immediate in-game updates
- **Admin Testing**: Quick testing of skin changes without round restarts
- **Bug Fixes**: Resyncing player data after database issues
- **Live Updates**: Real-time skin application during gameplay

## Performance Impact
- **Minimal**: Direct database queries are efficient
- **Async**: Non-blocking operations prevent game lag
- **Player-specific**: Only affects individual players, not server-wide
- **Optimized**: Uses existing API patterns for maximum compatibility
