# Nora AI Command Setup Guide

## Table of Contents

- [Quick Reference: Nora Variables](#quick-reference-nora-variables)
- [Overview](#overview)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Initial Setup](#initial-setup)
- [Configuration Options](#configuration-options)
- [How It Works](#how-it-works)
- [Content Moderation Details](#content-moderation-details)
- [Conversation Context](#conversation-context)
- [Testing the Command](#testing-the-command)
- [Troubleshooting](#troubleshooting)
- [Cost Monitoring](#cost-monitoring)
- [Security Considerations](#security-considerations)
- [Advanced Configuration](#advanced-configuration)
- [Code Architecture](#code-architecture)
- [FAQ](#faq)

## Quick Reference: Nora Variables

All configurable knobs for `!nora`, with direct links to where they're defined.

### Files

| Variable | File | Line(s) | Default | Description |
|----------|------|---------|---------|-------------|
| System prompt | [NoraPrompt.txt](KfChatDotNetBot/Commands/NoraPrompt.txt) | entire file | *(see file)* | Nora's personality, context, and instructions. Edit this file freely — changes are picked up on next invocation without restart. |
| Moods | [NoraMoods.cs:5-17](KfChatDotNetBot/Commands/NoraMoods.cs#L5-L17) | 5-17 | 10 moods | Random mood injected per conversation context |
| Rate limit | [NoraCommand.cs:89-94](KfChatDotNetBot/Commands/NoraCommand.cs#L89-L94) | 89-94 | 1/min per user | `Window`, `MaxInvocations`, `Flags` |
| Input word limit | [NoraCommand.cs:96](KfChatDotNetBot/Commands/NoraCommand.cs#L96) | 96 | 15 words | `MaxWords` constant |
| Input char limit | [NoraCommand.cs:97](KfChatDotNetBot/Commands/NoraCommand.cs#L97) | 97 | 140 chars | `MaxCharacters` constant |
| Execution timeout | [NoraCommand.cs:87](KfChatDotNetBot/Commands/NoraCommand.cs#L87) | 87 | 30 seconds | Max time for the entire command |
| Required permission | [NoraCommand.cs:85](KfChatDotNetBot/Commands/NoraCommand.cs#L85) | 85 | `UserRight.Loser` | Minimum user permission level |
| Max response tokens | [GrokApi.cs:105,122](KfChatDotNetBot/Services/GrokApi.cs#L105) | 105, 122 | 300 tokens | `maxTokens` parameter default |
| Temperature | [GrokApi.cs:167](KfChatDotNetBot/Services/GrokApi.cs#L167) | 167 | 0.7 | Creativity/consistency balance |
| Compaction summary tokens | [ConversationContextManager.cs:127](KfChatDotNetBot/Services/ConversationContextManager.cs#L127) | 127 | 150 tokens | Max tokens for context summary during compaction |

### Database Settings (via `BuiltIn.cs`)

These are changed at runtime via admin commands or direct DB updates — no rebuild needed.

| Setting Key | BuiltIn.cs Field | Line | Default | Description |
|-------------|-----------------|------|---------|-------------|
| `Grok.Nora.Model` | [GrokNoraModel](KfChatDotNetBot/Settings/BuiltIn.cs#L482-L483) | 482-483 | `grok-4-1-fast-reasoning` | Which Grok model to use |
| `Grok.Nora.ContextMode` | [GrokNoraContextMode](KfChatDotNetBot/Settings/BuiltIn.cs#L484-L485) | 484-485 | `perChatter` | `perChatter`, `perRoom`, or `disabled` |
| `Grok.Nora.ContextMaxTokens` | [GrokNoraContextMaxTokens](KfChatDotNetBot/Settings/BuiltIn.cs#L486-L487) | 486-487 | `2400` | Max estimated tokens before context compaction |
| `Grok.Nora.ContextExpiryMinutes` | [GrokNoraContextExpiryMinutes](KfChatDotNetBot/Settings/BuiltIn.cs#L488-L489) | 488-489 | `30` | Minutes of inactivity before context expires |
| `Grok.Nora.UserInfoEnabled` | [GrokNoraUserInfoEnabled](KfChatDotNetBot/Settings/BuiltIn.cs#L490-L491) | 490-491 | `true` | Inject user profile (balance, VIP, etc.) into prompt |
| `Grok.ApiKey` | [GrokApiKey](KfChatDotNetBot/Settings/BuiltIn.cs#L477-L478) | 477-478 | *(empty)* | xAI API key |
| `Grok.Chat.Endpoint` | [GrokChatEndpoint](KfChatDotNetBot/Settings/BuiltIn.cs#L479-L481) | 479-481 | `https://api.x.ai/v1/chat/completions` | API endpoint |

## Overview

The `!nora` command allows chat users to interact with Grok AI through the KfChatDotNet bot. All messages are automatically moderated through OpenAI's Moderation API to filter out illegal content while allowing profanity and general offensive language.

## Features

- **AI Responses**: Powered by Grok (xAI) with customizable personality
- **Content Moderation**: OpenAI Moderation API blocks illegal content (bomb-making, drug manufacturing, CSAM)
- **Rate Limiting**: 1 use per minute per user to prevent spam and control API costs
- **Input Limits**: 15 words max, 140 characters max
- **Permission Level**: Available to all users (UserRight.Loser)
- **Response Format**: `**Nora to @username:** [AI response]`
- **Hot-reloadable prompt**: Edit `NoraPrompt.txt` and changes take effect on the next invocation

## Prerequisites

### 1. OpenAI API Key
- **Purpose**: Content moderation (free tier available)
- **Get it**: https://platform.openai.com/api-keys
- **Cost**: Moderation API is free, but has rate limits
- **Documentation**: https://platform.openai.com/docs/api-reference/moderations

### 2. Grok API Key (xAI)
- **Purpose**: AI chat completions
- **Get it**: https://console.x.ai/
- **Cost**: ~$5 per 1M input tokens for grok-4-1-fast-reasoning model
- **Documentation**: https://docs.x.ai/api
- **Pricing**: https://docs.x.ai/developers/models

## Initial Setup

### Step 1: Run the Bot Once
On first startup after deploying the code, the bot will automatically create the new settings in the database with default values.

```bash
dotnet run --project KfChatDotNetBot
```

The following settings will be created in `db.sqlite`:
- `OpenAi.ApiKey` (null by default)
- `OpenAi.Moderation.Endpoint` (defaults to `https://api.openai.com/v1/moderations`)
- `Grok.ApiKey` (null by default)
- `Grok.Chat.Endpoint` (defaults to `https://api.x.ai/v1/chat/completions`)
- `Grok.Nora.Model` (defaults to `grok-4-1-fast-reasoning`)
- `Grok.Nora.ContextMode` (defaults to `perChatter` — options: `perChatter`, `perRoom`, `disabled`)
- `Grok.Nora.ContextMaxTokens` (defaults to `2400`)
- `Grok.Nora.ContextExpiryMinutes` (defaults to `30`)

### Step 2: Configure API Keys

You need to set the API keys in the database. You can do this either:

**Option A: Using Admin Commands** (if available in your bot):
```
!admin setting set OpenAi.ApiKey <your-openai-api-key>
!admin setting set Grok.ApiKey <your-grok-api-key>
```

**Option B: Direct Database Update**:
```bash
# Open the SQLite database
sqlite3 db.sqlite

# Set OpenAI API key
UPDATE Settings SET Value = 'sk-proj-...' WHERE Key = 'OpenAi.ApiKey';

# Set Grok API key
UPDATE Settings SET Value = 'xai-...' WHERE Key = 'Grok.ApiKey';

# Exit sqlite
.exit
```

**Option C: Using .NET EF Core**:
```csharp
await SettingsProvider.SetValueAsync(BuiltIn.Keys.OpenAiApiKey, "sk-proj-...");
await SettingsProvider.SetValueAsync(BuiltIn.Keys.GrokApiKey, "xai-...");
```

### Step 3: Edit Nora's Prompt

Edit [`KfChatDotNetBot/Commands/NoraPrompt.txt`](KfChatDotNetBot/Commands/NoraPrompt.txt) with Nora's personality and context. This file is read at runtime and hot-reloads when modified — no restart needed.

### Step 4: Restart the Bot
```bash
dotnet run --project KfChatDotNetBot
```

The `!nora` command is now ready to use!

## Configuration Options

### Customize Nora's Personality

Edit [`KfChatDotNetBot/Commands/NoraPrompt.txt`](KfChatDotNetBot/Commands/NoraPrompt.txt) directly. The file is re-read whenever it changes on disk — no restart or rebuild required. You can write as much context as you want.

### Change the AI Model

If you want to use a different Grok model:

```sql
UPDATE Settings
SET Value = 'grok-2-latest'
WHERE Key = 'Grok.Nora.Model';
```

Available models: `grok-4-1-fast-reasoning`, `grok-2-latest` (check xAI docs for current models)

### Use a Proxy

If you need to route requests through a proxy (applies to both APIs):

```sql
UPDATE Settings
SET Value = 'http://your-proxy:8080'
WHERE Key = 'Proxy';
```

## How It Works

```
User Input: !nora what is 2+2
     |
[1] Input Validation
    - Check word count (<=15 words)
    - Check character count (<=140 chars)
     |
[2] Content Moderation (OpenAI)
    - Send to OpenAI Moderation API
    - Check for illegal content:
      X Block: illicit activities, self-harm instructions, CSAM
      OK Allow: profanity, harassment, hate speech
     |
[3] Load Prompt
    - Read NoraPrompt.txt (cached, hot-reloads on file change)
    - Append user info if enabled
    - Append random mood
     |
[4] AI Response (Grok)
    - Send system prompt + conversation context to Grok
    - Max 300 tokens response
     |
[5] Format & Send
    - Format: "Nora to @username: [response]"
    - Truncate if needed (1023 byte limit)
    - Post to chat
```

## Content Moderation Details

### What Gets Blocked (Illegal Content)
The command blocks content flagged with these OpenAI moderation categories:
- **illicit**: Instructions for illegal activities (bomb-making, drug manufacturing, hacking, etc.)
- **illicit/violent**: Violent illegal activities
- **self-harm/instructions**: Detailed self-harm methods
- **sexual/minors**: Any content involving minors

### What Gets Allowed (Offensive But Not Illegal)
These are flagged but NOT blocked:
- **harassment**: General insults, harassment
- **hate**: Hate speech
- **sexual**: Adult sexual content
- **violence**: General violence descriptions
- **violence/graphic**: Graphic violence

This design philosophy allows the command to be used in edgy chat environments while still blocking truly dangerous content.

## Conversation Context

The `!nora` command supports persistent conversation context so Nora remembers previous messages within a session.

### Context Modes

Set the context mode via the `Grok.Nora.ContextMode` setting:

| Mode | Behavior |
|------|----------|
| `perChatter` (default) | Each user gets their own separate conversation history |
| `perRoom` | All users in a room share the same conversation history |
| `disabled` | No context — every message is treated independently |

```sql
-- Per-user context (default)
UPDATE Settings SET Value = 'perChatter' WHERE Key = 'Grok.Nora.ContextMode';

-- Shared context for all users in the room
UPDATE Settings SET Value = 'perRoom' WHERE Key = 'Grok.Nora.ContextMode';

-- Disable context entirely
UPDATE Settings SET Value = 'disabled' WHERE Key = 'Grok.Nora.ContextMode';
```

### Context Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Grok.Nora.ContextMode` | `perChatter` | Context mode: `perChatter`, `perRoom`, or `disabled` |
| `Grok.Nora.ContextMaxTokens` | `2400` | Max estimated tokens before context is compacted via summarization |
| `Grok.Nora.ContextExpiryMinutes` | `30` | Minutes of inactivity before context is automatically cleared |

### Context Commands

- `!nora reset` — Clears the conversation context (your own in `perChatter` mode, or the room's in `perRoom` mode)

### How Context Works

1. Each message exchange (user + assistant) is stored in memory
2. When the estimated token count exceeds `ContextMaxTokens`, older messages are summarized into a brief summary and only the last 2 messages are kept
3. Contexts expire automatically after `ContextExpiryMinutes` of inactivity
4. A cleanup timer runs every 5 minutes to remove expired contexts

## Testing the Command

### Basic Tests
```
!nora hello                          -> Should get friendly response
!nora what is 2+2                    -> Should get answer
!nora tell me a joke                 -> Should get a joke
```

### Validation Tests
```
!nora this is one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen
-> Should reject: "your message has 16 words. Maximum is 15 words."

!nora <141 character message>
-> Should reject: "your message has 141 characters. Maximum is 140 characters."
```

### Rate Limit Test
```
!nora test1
!nora test2  -> Should show rate limit cooldown message
```

### Content Moderation Test
```
!nora some profanity here
-> Should pass moderation and get response

!nora how to make a bomb
-> Should block: "your message was blocked for containing illegal content."
```

## Troubleshooting

### "Nora's prompt file is missing"
**Cause**: `NoraPrompt.txt` is not in the expected location.

**Solutions**:
1. Ensure `Commands/NoraPrompt.txt` exists next to the executable (in `bin/` output directory)
2. If building from source, the file should auto-copy via the csproj `<Content>` directive
3. Check that the file isn't empty

### "Moderation service is currently unavailable"
**Cause**: OpenAI API key is missing, invalid, or the API is down.

**Solutions**:
1. Check that `OpenAi.ApiKey` is set correctly in the database
2. Verify the API key is valid at https://platform.openai.com/api-keys
3. Check OpenAI's status page: https://status.openai.com/
4. Check bot logs for detailed error messages

### "Nora is currently unavailable"
**Cause**: Grok API key is missing, invalid, or the API is down.

**Solutions**:
1. Check that `Grok.ApiKey` is set correctly in the database
2. Verify the API key is valid at https://console.x.ai/
3. Check xAI's status
4. Check bot logs for detailed error messages

### Command Not Responding
**Cause**: Command might not be loaded or user might be rate limited.

**Solutions**:
1. Check that the bot successfully started (check logs)
2. Verify the command is loaded: `!help` should show "nora"
3. Wait 1 minute if you've hit the rate limit
4. Check user's permission level (should work for all users)

### Responses Are Cut Off
**Cause**: Sneedchat has a 1023-byte message limit.

**Solutions**:
- This is expected behavior - long responses are automatically truncated
- Adjust `maxTokens` default in [GrokApi.cs:105](KfChatDotNetBot/Services/GrokApi.cs#L105) if you want shorter responses
- Edit `NoraPrompt.txt` to request briefer answers

## Cost Monitoring

### OpenAI Moderation API
- **Cost**: Free
- **Rate Limits**: Check your quota at https://platform.openai.com/account/limits
- **Usage**: View at https://platform.openai.com/usage

### Grok API (xAI)
- **Cost**: ~$5 per 1M input tokens (varies by model)
- **Rate Limits**: Set at 1 use/user/minute in the bot
- **Usage**: Monitor at https://console.x.ai/
- **Budget**: Consider setting usage alerts in xAI console

### Example Cost Calculation
```
Average message: 10 words (~15 tokens input)
Average response: 50 tokens output
Cost per interaction: ~$0.0003 (assuming $5/1M tokens)

At 1 request/user/minute:
- 100 users: ~100 requests/hour max = ~$0.03/hour
- 1000 users: ~1000 requests/hour max = ~$0.30/hour

Rate limiting prevents runaway costs!
```

## Security Considerations

### API Key Storage
- API keys are stored in `db.sqlite` with the `IsSecret` flag
- They are not displayed in logs when the `IsSecret` flag is set
- Keep `db.sqlite` secure and don't commit it to version control

### Content Logging
The bot logs:
- All moderation results (flagged categories)
- Blocked illegal content attempts with usernames
- Allowed-but-flagged content (profanity) for monitoring

Check logs at: `logs/` directory (configured in `NLog.config`)

### Rate Limiting
- 1 use per minute per user prevents spam
- No global rate limit by default (all users can use simultaneously)
- Consider adding `RateLimitFlags.Global` if abuse is a concern

## Advanced Configuration

### Add Global Rate Limit
Edit [NoraCommand.cs:89-94](KfChatDotNetBot/Commands/NoraCommand.cs#L89-L94):

```csharp
public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
{
    Window = TimeSpan.FromMinutes(1),
    MaxInvocations = 1,
    Flags = RateLimitFlags.Global  // Add this flag
};
```

This limits ALL users combined to 1 request per minute.

### Increase Response Length
Edit [GrokApi.cs:105](KfChatDotNetBot/Services/GrokApi.cs#L105):

```csharp
max_tokens = 500  // Increase from 300 to 500
```

Note: Longer responses may get truncated by Sneedchat's 1023-byte limit.

### Change Input Limits
Edit [NoraCommand.cs:96-97](KfChatDotNetBot/Commands/NoraCommand.cs#L96-L97):

```csharp
private const int MaxWords = 20;       // Increase from 15 to 20
private const int MaxCharacters = 200; // Increase from 140 to 200
```

### Require Higher Permissions
Edit [NoraCommand.cs:85](KfChatDotNetBot/Commands/NoraCommand.cs#L85):

```csharp
public UserRight RequiredRight => UserRight.TrueAndHonest;  // Mods only
```

## Code Architecture

### Files
- **[KfChatDotNetBot/Commands/NoraPrompt.txt](KfChatDotNetBot/Commands/NoraPrompt.txt)** - System prompt (hot-reloadable)
- **[KfChatDotNetBot/Commands/NoraCommand.cs](KfChatDotNetBot/Commands/NoraCommand.cs)** - Main command logic
- **[KfChatDotNetBot/Commands/NoraMoods.cs](KfChatDotNetBot/Commands/NoraMoods.cs)** - Random mood modifiers
- **[KfChatDotNetBot/Services/OpenAiModeration.cs](KfChatDotNetBot/Services/OpenAiModeration.cs)** - OpenAI moderation API integration
- **[KfChatDotNetBot/Services/GrokApi.cs](KfChatDotNetBot/Services/GrokApi.cs)** - Grok API integration
- **[KfChatDotNetBot/Services/ConversationContextManager.cs](KfChatDotNetBot/Services/ConversationContextManager.cs)** - Conversation context and compaction
- **[KfChatDotNetBot/Settings/BuiltIn.cs](KfChatDotNetBot/Settings/BuiltIn.cs)** - DB setting keys

### Auto-Discovery
The command is automatically discovered via reflection in [BotCommands.cs](KfChatDotNetBot/Services/BotCommands.cs) on bot startup. No manual registration needed!

## FAQ

**Q: Can I use a different AI provider instead of Grok?**
A: Yes! The code is modular. Create a new service similar to `GrokApi.cs` and update `NoraCommand.cs` to use it.

**Q: Why is moderation required?**
A: To prevent the bot from being used to generate illegal or dangerous content that could create liability.

**Q: Can I disable moderation?**
A: Not recommended, but technically you could modify `NoraCommand.cs` to skip the moderation step. This is strongly discouraged.

**Q: What if OpenAI moderation is down?**
A: The command fails-safe and blocks all messages when moderation is unavailable, preventing unmoderated content from reaching the AI.

**Q: Can I increase the rate limit?**
A: Yes, edit the `RateLimitOptions` in `NoraCommand.cs`. Be aware this increases API costs.

**Q: Does this work with the GambaSesh detection system?**
A: Yes! The command respects the bot's existing systems. Responses are sent with `bypassSeshDetect: true` to ensure they're always posted.

**Q: How do I edit Nora's prompt?**
A: Edit `KfChatDotNetBot/Commands/NoraPrompt.txt` (or `Commands/NoraPrompt.txt` in the build output directory). Changes are picked up automatically — no restart needed.
