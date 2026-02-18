# 🔒 Security & Privacy Guide

**Important:** If you're contributing to this project and want to maintain anonymity, follow these steps to prevent leaking personal information.

## Table of Contents

- [Anonymize Your Git Identity](#anonymize-your-git-identity)
- [Prevent Path Leaks](#prevent-path-leaks)

## Anonymize Your Git Identity

Configure Git to use a pseudonymous identity for this repository:

```bash
# Navigate to the repository
cd KfChatDotNet

# Set local Git identity (only affects this repo)
git config user.name "YourPseudonym"
git config user.email "pseudonym@example.com"

# Or use GitHub's noreply email
git config user.email "username@users.noreply.github.com"

# Verify your settings
git config user.name
git config user.email
```

> **Note:** Use `git config` (without `--global`) to set identity per-repository only. This won't affect your other projects.

### Check Your Current Identity

Before making commits, verify what identity will be used:

```bash
# Check current configuration
git config --list --show-origin | grep user

# See what will be used for the next commit
git config user.name
git config user.email
```

## Prevent Path Leaks

Your system username and file paths can leak into commits through:

### 1. Exception Stack Traces

Be careful when committing error logs or debugging output that might contain paths like:
- ❌ `/home/john.smith/repos/KfChatDotNet/`
- ❌ `C:\Users\JohnSmith\Documents\KfChatDotNet\`
- ❌ `/Users/john.smith/Projects/KfChatDotNet/`

**What to do:**
- Strip paths from error logs before committing
- Use relative paths in documentation
- Sanitize stack traces to remove usernames

### 2. Absolute Paths in Code

Avoid hardcoding absolute paths. Use relative paths or configuration:

```csharp
// ❌ Bad - leaks username
var logPath = "/home/john.smith/.bot/logs/";
var dataPath = "C:\\Users\\JohnSmith\\AppData\\bot\\";

// ✅ Good - use relative or configurable paths
var logPath = Path.Combine(Environment.CurrentDirectory, "logs");
var dataPath = Path.Combine(AppContext.BaseDirectory, "data");
var configPath = await SettingsProvider.GetValueAsync("Bot.LogPath");
```

### 3. IDE Configuration Files

The `.gitignore` already excludes common IDE files, but verify:

```bash
# Check what's being tracked
git ls-files | grep -E '\.(user|suo|vscode|idea)'

# If you find personal IDE files, remove them
git rm --cached path/to/personal/file
git commit -m "Remove personal IDE files"
```

### 4. Database Files

The database (`db.sqlite`) can contain:
- Your KiwiFarms password (in Settings table)
- Your KiwiFarms cookies
- Potentially identifiable usage patterns

**Never commit `db.sqlite`** - it's already in `.gitignore`.