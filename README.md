# KfChatDotNet

> ## ⚠️ **IMPORTANT: Read This First** ⚠️
>
> **If you're contributing to this project, please read [PRIVACY.md](PRIVACY.md) to learn how to protect your identity.**

---
A C# .NET chat bot for KiwiFarms (Sneedchat) with extensive third-party integrations for stream monitoring, gambling site tracking, and interactive commands.

## Features

- 🤖 **Interactive Chat Bot** - Command-based interactions with regex pattern matching
- 🎰 **Kasino System** - Internal gambling games (mines, limbo, coinflip, blackjack, roulette, and more)
- 📺 **Stream Monitoring** - Track streams across Twitch, Kick, DLive, PeerTube, Owncast, YouTube
- 💬 **Multi-Platform Integration** - Discord relay, Twitch chat, Kick events
- 🎲 **Gambling Tracking** - Monitor bets across Rainbet, Shuffle, Howlgg, Chips.gg, Clash.gg, and more
- 🔄 **Auto-Reconnect** - Robust WebSocket connection with automatic cookie refresh
- 🗃️ **Database-Backed Settings** - SQLite database with EF Core migrations

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher
- A KiwiFarms account
- (Optional) SQLite viewer for database inspection

## Quick Start

### 1. Clone and Build

```bash
git clone <repository-url>
cd KfChatDotNet
dotnet build
```

### 2. Configure Credentials

The bot uses a **database-backed settings system**. On first run, it will create `db.sqlite` with default settings.

#### Option A: Using config.json (Easiest for First Run)

Create `KfChatDotNetBot/config.json`:

```json
{
  "KfUsername": "your_kiwifarms_username",
  "KfPassword": "your_kiwifarms_password",
  "KfChatRoomId": 15,
  "KfDomain": "kiwifarms.st",
  "KfWsEndpoint": "wss://kiwifarms.st:9443/chat.ws",
  "SuppressChatMessages": true
}
```

> **Note:** The bot will automatically migrate `config.json` to the database on first run and rename it to `config.json.migrated`.

#### Option B: Directly Edit Database (After First Run)

Build the project, then run the bot once. This generates tables which can be updated by running `sqlite`:

```bash
# Build
dotnet build

# Run
dotnet run --project KfChatDotNetBot

# Ctr + C to stop the application, then run
sqlite3 db.sqlite
```

```sql
-- Update your credentials
UPDATE Settings SET Value = 'your_username' WHERE Key = 'KiwiFarms.Username';
UPDATE Settings SET Value = 'your_password' WHERE Key = 'KiwiFarms.Password';

-- Enable read-only mode for testing
UPDATE Settings SET Value = 'true' WHERE Key = 'KiwiFarms.SuppressChatMessages';
```

### 3. Run the Bot

```bash
cd KfChatDotNetBot
dotnet run
```

The bot will:
1. ✅ Create/migrate the database
2. ✅ Sync built-in settings
3. ✅ Connect to KiwiFarms chat
4. ✅ Start listening for commands

## Configuration Guide

### Essential Settings

| Setting | Description | Default | Required |
|---------|-------------|---------|----------|
| `KiwiFarms.Username` | Your KF username | - | ✅ Yes |
| `KiwiFarms.Password` | Your KF password | - | ✅ Yes |
| `KiwiFarms.RoomId` | Chat room ID to join | `15` | ✅ Yes |
| `KiwiFarms.SuppressChatMessages` | Read-only mode (no messages sent) | `false` | Recommended for testing |

### Optional Integrations

All external services are **disabled by default** or optional:

| Service | Enable Setting | Auth Required |
|---------|---------------|---------------|
| Discord | `Discord.Token` | Bot token |
| Kick | `Kick.Enabled` | None (public API) |
| Twitch | Auto-enabled | None (public GraphQL) |
| Jackpot | `Jackpot.Enabled` | None |
| Rainbet | `Rainbet.Enabled` | None |
| Shuffle | Auto-enabled | None |
| YouTube | `YouTube.ApiKey` | API key |
| FlareSolverr | `FlareSolverr.ApiUrl` | Service URL |

### Viewing All Settings

```bash
# View non-secret settings
sqlite3 db.sqlite "SELECT Key, Value, Description FROM Settings WHERE IsSecret = 0 LIMIT 20;"

# View secret settings (passwords, tokens)
sqlite3 db.sqlite "SELECT Key, Value FROM Settings WHERE IsSecret = 1;"
```

### Testing Mode

For safe testing without sending messages to chat:

```sql
UPDATE Settings SET Value = 'true' WHERE Key = 'KiwiFarms.SuppressChatMessages';
```

When enabled, the bot will connect, listen, and process commands but **won't actually send messages**.

## Development

### Project Structure

```
KfChatDotNet/
├── KfChatDotNetWsClient/     # WebSocket client library for KF chat
├── KickWsClient/             # WebSocket client library for Kick
└── KfChatDotNetBot/          # Main bot application
    ├── Commands/             # Bot commands (implement ICommand)
    ├── Services/             # External service integrations
    ├── Models/               # Data models and DTOs
    ├── Migrations/           # EF Core database migrations
    └── Settings/             # Settings system
```

### Common Commands

```bash
# Build the solution
dotnet build

# Run in release mode
dotnet run --project KfChatDotNetBot -c Release

# Clean build artifacts
dotnet clean

# Add a database migration
dotnet ef migrations add MigrationName --project KfChatDotNetBot

# Update database to latest migration
dotnet ef database update --project KfChatDotNetBot

# View migration SQL
dotnet ef migrations script --project KfChatDotNetBot
```

### Adding a New Command

1. Create a class in `KfChatDotNetBot/Commands/` implementing `ICommand`
2. Define regex patterns and command logic
3. The bot auto-discovers commands via reflection on startup

Example:

```csharp
public class HelloCommand : ICommand
{
    public List<Regex> Patterns => [new Regex(@"^hello$", RegexOptions.IgnoreCase)];
    public string? HelpText => "Says hello!";
    public UserRight RequiredRight => UserRight.User;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message,
        UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.SendChatMessage($"Hello, @{message.Author.Username}!");
    }
}
```

### Database Migrations

Migrations run **automatically** on startup. The database schema is managed via Entity Framework Core.

```bash
# Create a new migration
dotnet ef migrations add AddNewFeature --project KfChatDotNetBot

# Migrations are applied automatically when the bot starts
dotnet run --project KfChatDotNetBot
```

## Architecture Overview

### Message Flow

```
KiwiFarms Chat (WebSocket)
          ↓
    ChatClient (WsClient library)
          ↓
    ChatBot (Event handlers)
          ↓
    BotCommands (Pattern matching)
          ↓
    ICommand implementations (Your commands)
```

### Key Components

- **ChatClient** - WebSocket connection to KF, handles reconnection and cookie management
- **ChatBot** - Main bot orchestration, message tracking, GambaSesh detection
- **BotCommands** - Command routing with regex patterns, rate limiting, permissions
- **BotServices** - Manages all external service connections (Discord, Twitch, gambling sites)
- **Money** - Internal Kasino gambling system with balance tracking

### Settings System

Settings are **stored in the database**, not config files:

- All settings have defaults defined in `BuiltIn.cs`
- Settings are cached to reduce database queries
- Secrets are marked and hidden from logs
- Settings sync automatically on startup

For more architectural details, see [CLAUDE.md](CLAUDE.md).

## Logging

Logging is configured via `NLog.config`. The bot logs extensively:

- **Debug**: Packet details, message processing, connection events
- **Info**: User joins/parts, sent messages, state changes
- **Error**: Exceptions, disconnections, failed operations

## Optional Services Setup

### FlareSolverr (for Cloudflare bypass)

```bash
# Using Docker
docker run -d \
  --name=flaresolverr \
  -p 8191:8191 \
  ghcr.io/flaresolverr/flaresolverr:latest

# Update setting
UPDATE Settings SET Value = 'http://localhost:8191/' WHERE Key = 'FlareSolverr.ApiUrl';
```

### Discord Bot

1. Create a bot at [Discord Developer Portal](https://discord.com/developers/applications)
2. Get your bot token
3. Update setting: `UPDATE Settings SET Value = 'your_bot_token' WHERE Key = 'Discord.Token';`

### Redis (for YouTube PubSub)

```bash
# Using Docker
docker run -d --name redis -p 6379:6379 redis:latest

# Update setting
UPDATE Settings SET Value = 'localhost:6379' WHERE Key = 'YouTube.PubSub.RedisConnectionString';
```

## License

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See [LICENSE](LICENSE) for details.