using System.Diagnostics;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// Basic stream capture using yt-dlp in a separate window when CaptureAsync() is called
/// </summary>
/// <param name="streamUrl">Streamer URL</param>
/// <param name="ct">Cancellation token</param>
public class StreamCapture(string streamUrl, StreamCaptureMethods captureMethod, CaptureOverridesModel? captureOverrides = null, CancellationToken ct = default)
{
    private readonly Dictionary<string, Setting> _settings = SettingsProvider
        .GetMultipleValuesAsync([BuiltIn.Keys.CaptureYtDlpBinaryPath, BuiltIn.Keys.CaptureYtDlpWorkingDirectory,
            BuiltIn.Keys.CaptureYtDlpCookiesFromBrowser, BuiltIn.Keys.CaptureYtDlpOutputFormat, BuiltIn.Keys.CaptureYtDlpParentTerminal,
            BuiltIn.Keys.CaptureYtDlpScriptPath, BuiltIn.Keys.CaptureYtDlpUserAgent, BuiltIn.Keys.CaptureStreamlinkBinaryPath,
            BuiltIn.Keys.CaptureStreamlinkOutputFormat, BuiltIn.Keys.CaptureStreamlinkRemuxScript, BuiltIn.Keys.CaptureStreamlinkTwitchOptions]).Result;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Initiates the capture of the stream itself by generating the underlying script and executing it
    /// </summary>
    /// <exception cref="UnsupportedOperatingSystemException">Thrown if the operating system is unsupported (i.e. not Windows, Linux or FreeBSD</exception>
    public async Task CaptureAsync()
    {
        var scriptPath = await CreateScriptAsync();

        string pStartInfoFileName;
        string pStartInfoExecuteArgument;
        string pStartInfoExecuteScript;
        if (OperatingSystem.IsWindows())
        {
            pStartInfoFileName = "cmd.exe";
            pStartInfoExecuteArgument = "/C";
            pStartInfoExecuteScript = $"START {scriptPath}";
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            pStartInfoFileName = captureOverrides?.CaptureYtDlpParentTerminal ?? _settings[BuiltIn.Keys.CaptureYtDlpParentTerminal].Value!;
            pStartInfoExecuteArgument = "-x";
            pStartInfoExecuteScript = scriptPath;
        }
        else
        {
            throw new UnsupportedOperatingSystemException();
        }
        
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = pStartInfoFileName,
            ArgumentList = { pStartInfoExecuteArgument, pStartInfoExecuteScript },
            WorkingDirectory = captureOverrides?.CaptureYtDlpWorkingDirectory ?? _settings[BuiltIn.Keys.CaptureYtDlpWorkingDirectory].Value
        });

        if (process == null)
        {
            _logger.Error("Caught a null when trying to launch the capture process. Variables follow");
            _logger.Error($"pStartInfoFileName = {pStartInfoFileName}");
            _logger.Error($"pStartInfoExecuteArgument = {pStartInfoExecuteArgument}");
            _logger.Error($"pStartInfoExecuteScript = {pStartInfoExecuteScript}");
            return;
        }
        
        var task = process.WaitForExitAsync(ct);
        // The process is supposed to exit almost immediately as the actual work is done in a separate terminal.
        // Therefore, any laggards will be killed, as it's not how this is meant to be implemented.
        // The capture should survive the bot being restarted.
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        catch (Exception e)
        {
            _logger.Error("Caught an exception while waiting for the capture process task");
            _logger.Error(e);
            return;
        }

        if (task.IsFaulted)
        {
            _logger.Error("Capture process task faulted");
            _logger.Error(task.Exception);
        }
        _logger.Info($"Script {pStartInfoExecuteScript} launched and yielded to us!");
    }

    /// <summary>
    /// Writes the script needed to perform the capture and returns the path of this script
    /// </summary>
    /// <returns>Absolute path of the script generated</returns>
    /// <exception cref="UnsupportedOperatingSystemException">Thrown if the operating system is unsupported (i.e. not Windows, Linux or FreeBSD</exception>
    private async Task<string> CreateScriptAsync()
    {
        var random = Convert.ToHexString(Guid.NewGuid().ToByteArray()[..4]);
        var scriptPath = Path.Join(_settings[captureOverrides?.CaptureYtDlpScriptPath ?? BuiltIn.Keys.CaptureYtDlpScriptPath].Value,
            $"bot_ytdlp_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{random}.sh");
        if (OperatingSystem.IsWindows())
        {
            Path.ChangeExtension(scriptPath, ".bat");
        }
        _logger.Info($"Generated script path: {scriptPath}");

        string captureLine;
        if (captureMethod == StreamCaptureMethods.YtDlp)
        {
            captureLine = $"{captureOverrides?.CaptureYtDlpBinaryPath ?? _settings[BuiltIn.Keys.CaptureYtDlpBinaryPath].Value} " +
                          $"-o \"{captureOverrides?.CaptureYtDlpOutputFormat ?? _settings[BuiltIn.Keys.CaptureYtDlpOutputFormat].Value}\" " +
                          $"--user-agent \"{captureOverrides?.CaptureYtDlpUserAgent ?? _settings[BuiltIn.Keys.CaptureYtDlpUserAgent].Value}\" " +
                          $"--cookies-from-browser {captureOverrides?.CaptureYtDlpCookiesFromBrowser ?? _settings[BuiltIn.Keys.CaptureYtDlpCookiesFromBrowser].Value} " +
                          $"--write-info-json --wait-for-video 15 --merge-output-format mp4 --verbose {streamUrl}";
        }
        else if (captureMethod == StreamCaptureMethods.Streamlink)
        {
            var twitchOpts = string.Empty;
            if (streamUrl.Contains("twitch.tv"))
            {
                twitchOpts = captureOverrides?.CaptureStreamlinkTwitchOptions ?? _settings[BuiltIn.Keys.CaptureStreamlinkTwitchOptions].Value;
            }
            captureLine = $"{captureOverrides?.CaptureStreamlinkBinaryPath ?? _settings[BuiltIn.Keys.CaptureStreamlinkBinaryPath].Value} {twitchOpts} " +
                          $"--output \"{captureOverrides?.CaptureStreamlinkOutputFormat ?? _settings[BuiltIn.Keys.CaptureStreamlinkOutputFormat].Value}\" " +
                          $"--retry-streams 15 --retry-max 10 {streamUrl} best";
        }
        else
        {
            _logger.Error($"We were given a stream capture method that doesn't exist: {captureMethod}");
            throw new UnsupportedStreamCaptureMethodException();
        }

        var remuxLine = string.Empty;
        if (captureMethod == StreamCaptureMethods.Streamlink)
        {
            remuxLine = captureOverrides?.CaptureStreamlinkRemuxScript ?? _settings[BuiltIn.Keys.CaptureStreamlinkRemuxScript].Value;
        }
        
        string scriptContent;

        if (OperatingSystem.IsWindows())
        {
            // GetPathRoot on Windows returns the top level directory, e.g. "C:\". Assuming the working directory is on another drive such as D:
            // we'll need to swap to that drive letter, so this just trims off the \ to transform it to D: or whatever. UNC paths not supported
            scriptContent = $"{Path.GetPathRoot(captureOverrides?.CaptureYtDlpWorkingDirectory ?? _settings[BuiltIn.Keys.CaptureYtDlpWorkingDirectory].Value)?.TrimEnd('\\')}{Environment.NewLine}" +
                            $"CD {captureOverrides?.CaptureYtDlpWorkingDirectory ?? _settings[BuiltIn.Keys.CaptureYtDlpWorkingDirectory].Value}{Environment.NewLine}" +
                            $"{captureLine}{Environment.NewLine}" +
                            $"{remuxLine}{Environment.NewLine}" +
                            $"PAUSE";
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            scriptContent = $"#!/bin/bash{Environment.NewLine}" +
                            $"cd {captureOverrides?.CaptureYtDlpWorkingDirectory ?? _settings[BuiltIn.Keys.CaptureYtDlpWorkingDirectory].Value}{Environment.NewLine}" +
                            $"{captureLine}{Environment.NewLine}" +
                            $"{remuxLine}{Environment.NewLine}" +
                            $"read -p \"Press enter to exit\"";
        }
        else
        {
            throw new UnsupportedOperatingSystemException();
        }
        
        _logger.Info("Wrote the script, contents follow this message");
        _logger.Info(scriptContent);

        await File.WriteAllTextAsync(scriptPath, scriptContent, ct);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            _logger.Info($"Marked {scriptPath} as executable since we're on Linux or FreeBSD");
        }

        return scriptPath;
    }
}

public class UnsupportedOperatingSystemException : Exception;

public class UnsupportedStreamCaptureMethodException : Exception;

public enum StreamCaptureMethods
{
    YtDlp,
    Streamlink
}