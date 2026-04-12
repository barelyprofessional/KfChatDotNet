using System.Diagnostics;
using System.Net;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// Local OpenAI Whisper integration for voice message transcription.
///
/// Downloads audio from a URL and runs it through a local Whisper binary
/// (openai-whisper CLI) for speech-to-text transcription.
///
/// Requires: pip install openai-whisper (or faster-whisper)
///
/// Configuration:
/// - Whisper.Enabled: Feature toggle (default: false)
/// - Whisper.BinaryPath: Path to the whisper binary (default: whisper)
/// - Whisper.Model: Model size - tiny, base, small, medium, large (default: base)
/// - Proxy: Global proxy setting (optional, used for downloading)
/// </summary>
public static class WhisperTranscription
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Downloads audio from a URL and transcribes it using local Whisper.
    ///
    /// Flow:
    /// 1. Check if feature is enabled
    /// 2. Download audio from URL to a temp file
    /// 3. Run whisper CLI on the temp file with --output_format txt
    /// 4. Read the output text file and return the transcription
    /// 5. Clean up temp files
    ///
    /// Returns null on any failure. All errors are logged via NLog.
    /// </summary>
    public static async Task<string?> TranscribeFromUrlAsync(string url, string fileName, CancellationToken ct = default)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.WhisperEnabled,
            BuiltIn.Keys.WhisperBinaryPath,
            BuiltIn.Keys.WhisperModel,
            BuiltIn.Keys.Proxy
        ]);

        if (!settings[BuiltIn.Keys.WhisperEnabled].ToBoolean())
        {
            Logger.Debug("Whisper transcription is disabled");
            return null;
        }

        var whisperBinary = settings[BuiltIn.Keys.WhisperBinaryPath].Value ?? "whisper";
        var model = settings[BuiltIn.Keys.WhisperModel].Value ?? "base";

        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (settings[BuiltIn.Keys.Proxy].Value != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(settings[BuiltIn.Keys.Proxy].Value);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = ".ogg";
        var tempAudioPath = Path.Combine(tempDir, $"voice{ext}");

        try
        {
            // Download the audio file
            Logger.Info($"Downloading voice message from {url}");
            using var client = new HttpClient(handler);
            var audioBytes = await client.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(tempAudioPath, audioBytes, ct);
            Logger.Info($"Downloaded {audioBytes.Length} bytes to {tempAudioPath}");

            // Run local whisper
            var args = $"\"{tempAudioPath}\" --model {model} --output_format txt --output_dir \"{tempDir}\"";
            Logger.Info($"Running: {whisperBinary} {args}");

            var processInfo = new ProcessStartInfo
            {
                FileName = whisperBinary,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                Logger.Error("Failed to start Whisper process");
                return null;
            }

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                Logger.Error($"Whisper exited with code {process.ExitCode}: {stderr}");
                return null;
            }

            // Whisper outputs a .txt file with the same base name as the input
            var outputPath = Path.Combine(tempDir, "voice.txt");
            if (!File.Exists(outputPath))
            {
                Logger.Error($"Whisper output file not found at {outputPath}");
                return null;
            }

            var transcription = (await File.ReadAllTextAsync(outputPath, ct)).Trim();

            if (string.IsNullOrWhiteSpace(transcription))
            {
                Logger.Warn("Whisper returned empty transcription");
                return null;
            }

            Logger.Info($"Transcription received: {transcription.Length} characters");
            return transcription;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during Whisper transcription");
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to cleanup Whisper temp files");
            }
        }
    }
}
