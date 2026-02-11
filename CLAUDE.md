# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

KfChatDotNet is a C# .NET 10.0 chat bot for KiwiFarms (Sneedchat) with extensive third-party integrations. The solution consists of three projects:

- **KfChatDotNetWsClient**: WebSocket client library for KiwiFarms chat communication
- **KickWsClient**: WebSocket client library for Kick platform integration
- **KfChatDotNetBot**: Main bot application with command handling and service integrations

## Build and Development Commands

```bash
# Build the entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Run the bot
dotnet run --project KfChatDotNetBot

# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore
```

## Database Management

The bot uses Entity Framework Core with SQLite (`db.sqlite`).

```bash
# Add a new migration (from solution root)
dotnet ef migrations add <MigrationName> --project KfChatDotNetBot

# Update database to latest migration
dotnet ef database update --project KfChatDotNetBot

# Remove the last migration
dotnet ef migrations remove --project KfChatDotNetBot

# View migration SQL
dotnet ef migrations script --project KfChatDotNetBot
```

Migrations run automatically on startup via `Program.cs`.

## Architecture

### Message Flow

1. **ChatClient** ([KfChatDotNetWsClient/ChatClient.cs](KfChatDotNetWsClient/ChatClient.cs)): WebSocket connection and event emission
   - Connects to KiwiFarms WebSocket endpoint
   - Parses incoming packets and emits typed events
   - Handles reconnection logic and cookie management

2. **ChatBot** ([KfChatDotNetBot/ChatBot.cs](KfChatDotNetBot/ChatBot.cs)): Core bot orchestration
   - Subscribes to ChatClient events
   - Manages sent message tracking and auto-deletion
   - Handles GambaSesh presence detection
   - Coordinates with BotServices for external integrations

3. **BotCommands** ([KfChatDotNetBot/Services/BotCommands.cs](KfChatDotNetBot/Services/BotCommands.cs)): Command routing
   - Uses reflection to discover all ICommand implementations
   - Matches incoming messages against command regex patterns
   - Enforces rate limits, permissions, and timeouts
   - Filters based on user rights and Kasino bans/exclusions

4. **ICommand Implementations** ([KfChatDotNetBot/Commands/](KfChatDotNetBot/Commands/)): Individual command handlers
   - Each command defines regex patterns for matching
   - Commands specify required user rights and rate limits
   - Commands can be marked with attributes (NoPrefixRequired, KasinoCommand, WagerCommand, AllowAdditionalMatches)

### Command System

All commands implement `ICommand` interface:
- `Patterns`: List of regex patterns for command matching
- `HelpText`: Help text shown to users (null to hide from help)
- `RequiredRight`: Minimum user permission level
- `Timeout`: Command execution timeout
- `RateLimitOptions`: Rate limiting configuration
- `RunCommand()`: Async method that executes the command

Command attributes:
- `[NoPrefixRequired]`: Command matches without requiring `!` prefix
- `[AllowAdditionalMatches]`: Continue processing after this command matches
- `[KasinoCommand]`: Requires Kasino to be enabled, enforces bans
- `[WagerCommand]`: Enforces self-exclusions for gambling addicts

### Settings System

Settings are stored in the database (not config files) via `SettingsProvider`:
- `SettingsProvider.GetValueAsync(key)` - Get a single setting
- `SettingsProvider.GetMultipleValuesAsync(keys)` - Get multiple settings efficiently
- `SettingsProvider.SetValueAsync(key, value)` - Update a setting
- Built-in keys defined in `BuiltIn.Keys` static class
- Migration from legacy `config.json` happens automatically on startup

### Service Integrations

**BotServices** ([KfChatDotNetBot/Services/BotServices.cs](KfChatDotNetBot/Services/BotServices.cs)) initializes and manages connections to external services:
- Discord: Message relaying and bot presence
- Twitch: Stream status monitoring and GraphQL API
- Kick: WebSocket connection for stream events
- Gambling sites: Rainbet, Shuffle, Howlgg, Chipsgg, Clashgg, BetBolt, Yeet
- Stream platforms: DLive, PeerTube, Owncast, YouTube
- Kasino: Internal gambling system with rain, mines, limbo, coinflip

Each service is implemented as a separate class in [KfChatDotNetBot/Services/](KfChatDotNetBot/Services/).

### Message Tracking

`ChatBot.SendChatMessage()` and `ChatBot.SendChatMessageAsync()` return a `SentMessageTrackerModel`:
- Tracks message status (WaitingForResponse, ResponseReceived, Lost, NotSending)
- Provides message ID for editing/deletion
- Measures round-trip delay
- Supports auto-deletion after a specified TimeSpan
- Handles message replay after disconnection

### Database Schema

The `ApplicationDbContext` manages these entities:
- `Users`: KiwiFarms users with permissions
- `Gamblers`: Kasino users with balance and stats
- `Transactions`: Kasino transaction history
- `Wagers`: Active and historical bets
- `Exclusions`: Self-exclusion periods
- `Perks`: Gambler perks (e.g., reduced house edge)
- `Settings`: Bot configuration
- `Images`: Uploaded image metadata
- `UsersWhoWere`: User activity timestamps (join/part/message)
- Various third-party service data tables (HowlggBets, RainbetBets, etc.)

## Important Patterns

### Async/Await Conventions

The codebase has inconsistent async patterns:
- Some methods expose both sync and async versions (e.g., `SendChatMessage()` and `SendChatMessageAsync()`)
- `Disconnect()` is intentionally synchronous with a separate `DisconnectAsync()` method
- Avoid changing existing sync/async signatures without careful consideration

### GambaSesh Detection

GambaSesh is another bot that the bot avoids conflicting with:
- `ChatBot.GambaSeshPresent` tracks his presence
- Messages are suppressed by default when he's present (unless `bypassSeshDetect=true`)
- Presence is detected via user join events and messages
- `BotServices.TemporarilyBypassGambaSeshForDiscord` exists for special cases

### Length Limits

Sneedchat enforces a 1023-byte message limit:
- `SendChatMessage()` accepts `LengthLimitBehavior` enum: TruncateNicely, TruncateExactly, RefuseToSend, DoNothing
- Use `string.Utf8LengthBytes()` extension method to check length
- Use `string.TruncateBytes(limit)` extension method to truncate safely

### Session Management

The bot handles KiwiFarms authentication via cookies:
- `KfTokenService` manages login and cookie refresh
- On `203` status code or "cannot join room" errors, cookies are refreshed
- Cookies are persisted to the database
- Bot can operate in "guest mode" with no cookies

## Common Tasks

### Adding a New Command

1. Create a new class in `KfChatDotNetBot/Commands/` that implements `ICommand`
2. Define regex patterns in the `Patterns` property
3. Implement `RunCommand()` method with command logic
4. Set `RequiredRight`, `Timeout`, `RateLimitOptions`, and `HelpText`
5. Add attributes if needed: `[NoPrefixRequired]`, `[KasinoCommand]`, etc.
6. The command will be auto-discovered via reflection on next startup

### Adding a New Service Integration

1. Create a new class in `KfChatDotNetBot/Services/`
2. Add initialization method to `BotServices.InitializeServices()`
3. Add corresponding settings keys to `BuiltIn.Keys`
4. If the service needs WebSocket or periodic tasks, follow existing patterns (PeriodicTimer, WebsocketClient)

### Modifying the Database Schema

1. Update the relevant `DbModel` class in `KfChatDotNetBot/Models/DbModels/`
2. Add/update the `DbSet` property in `ApplicationDbContext.cs`
3. Generate a migration: `dotnet ef migrations add <Name> --project KfChatDotNetBot`
4. Migration runs automatically on next bot startup

### Sending Messages to Chat

```csharp
// Synchronous
var tracker = _bot.SendChatMessage("Hello!");

// Asynchronous
var tracker = await _bot.SendChatMessageAsync("Hello!");

// Bypass GambaSesh detection
_bot.SendChatMessage("Important message", bypassSeshDetect: true);

// Auto-delete after 5 seconds
await _bot.SendChatMessageAsync("Temporary message", autoDeleteAfter: TimeSpan.FromSeconds(5));

// Wait for the message to be echoed by the server
if (await _bot.WaitForChatMessageAsync(tracker, TimeSpan.FromSeconds(10)))
{
    // Message was successfully sent, tracker.ChatMessageId is now available
}
```

## Dependencies

Key NuGet packages:
- `Websocket.Client`: WebSocket client used throughout
- `Microsoft.EntityFrameworkCore.Sqlite`: Database ORM
- `NLog`: Logging framework
- `Humanizer.Core`: Human-readable text formatting
- `SixLabors.ImageSharp`: Image manipulation for meme generation
- `StackExchange.Redis`: Redis caching
- `FlareSolverrSharp`: Cloudflare bypass
- `HtmlAgilityPack`: HTML parsing
- `Nerdbank.GitVersioning`: Automatic versioning from git tags

## Testing

There are currently no automated tests in this repository. Manual testing is performed by running the bot.

## Logging

NLog configuration is in `KfChatDotNetBot/NLog.config`. The bot logs extensively:
- Debug: Detailed packet information, message processing steps
- Info: User joins/parts, sent messages, state changes
- Error: Exceptions, disconnections, failed operations

## Code Style Notes

- The codebase uses explicit null checks and nullable reference types
- Extensive use of LINQ for data queries
- Some deliberately provocative comments and language (see Program.cs license header)
- Privacy is explicitly not a concern - data is logged freely
- Performance optimizations include "BUY MORE RAM" philosophy (unlimited message tracking)
