using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OpenRssReader.Services;

public sealed record KokoroVoiceOption(string Id, string DisplayName);

/// <summary>
/// Installs Kokoro's model and bundled voices next to the executable, then
/// performs synthesis locally. Network access is only used for the one-time
/// official ONNX model download.
/// </summary>
public sealed class KokoroTextToSpeechService : IDisposable
{
    private const string ModelFileName = "kokoro.onnx";
    private const string ModelDownloadUrl = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx";
    private readonly string _rootDirectory;
    private readonly string _bundledVoicesDirectory;
    private readonly SemaphoreSlim _installationLock = new(1, 1);
    private KokoroTTS? _engine;

    public KokoroTextToSpeechService() : this(AppContext.BaseDirectory)
    {
    }

    internal KokoroTextToSpeechService(string applicationDirectory)
    {
        _rootDirectory = Path.Combine(applicationDirectory, "kokoro");
        _bundledVoicesDirectory = Path.Combine(applicationDirectory, "voices");
    }

    public string ModelPath => Path.Combine(_rootDirectory, ModelFileName);
    public string VoicesDirectory => Path.Combine(_rootDirectory, "voices");
    public bool IsInstalled => File.Exists(ModelPath) && Directory.Exists(VoicesDirectory) &&
        Directory.EnumerateFiles(VoicesDirectory, "*.npy", SearchOption.TopDirectoryOnly).Any();
    public bool IsSpeaking { get; private set; }

    public IReadOnlyList<KokoroVoiceOption> GetSupportedVoices(string language)
    {
        var sourceDirectory = Directory.Exists(_bundledVoicesDirectory)
            ? _bundledVoicesDirectory
            : VoicesDirectory;

        if (!Directory.Exists(sourceDirectory))
        {
            return [];
        }

        // The nested voices-zh set requires a different model. This installer
        // uses the multilingual v1.0 model and its matching top-level voices.
        return Directory.EnumerateFiles(sourceDirectory, "*.npy", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)
            .Where(name => MatchesLanguage(name, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => GetLanguage(name), StringComparer.Ordinal)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new KokoroVoiceOption(name, $"{GetLanguage(name)} - {GetVoiceName(name)}"))
            .ToList();
    }

    public bool IsVoiceInstalled(string voiceId) => IsInstalled &&
        Path.GetFileName(voiceId) == voiceId && File.Exists(Path.Combine(VoicesDirectory, $"{voiceId}.npy"));

    public async Task InstallAsync(string language, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var voices = GetSupportedVoices(language);
        if (voices.Count == 0)
        {
            throw new InvalidOperationException($"Kokoro does not support {language}.");
        }

        await _installationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            await CopyBundledVoicesAsync(voices, cancellationToken);
            await DownloadModelAsync(progress, cancellationToken);
        }
        finally
        {
            _installationLock.Release();
        }
    }

    private async Task DownloadModelAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(ModelPath))
        {
            progress?.Report(1);
            return;
        }

        var temporaryPath = $"{ModelPath}.download";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 128];
                long downloadedBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedBytes += read;
                    if (totalBytes is > 0)
                    {
                        progress?.Report(downloadedBytes / (double)totalBytes.Value);
                    }
                }

                await target.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, ModelPath, true);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task SpeakAsync(string text, string voiceId, double speed = 1.0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!IsVoiceInstalled(voiceId))
        {
            throw new InvalidOperationException("Install Kokoro before using its voices.");
        }

        IsSpeaking = true;
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureEngine();
                var voice = KokoroVoiceManager.GetVoice(voiceId);
                var normalizedSpeed = double.IsFinite(speed) ? Math.Clamp(speed, 0.5, 2.0) : 1.0;
                _engine!.SpeakFast(text, voice, new KokoroTTSPipelineConfig { Speed = (float)normalizedSpeed });
            }, cancellationToken);
        }
        catch
        {
            IsSpeaking = false;
            throw;
        }
    }

    public void Stop()
    {
        _engine?.StopPlayback();
        IsSpeaking = false;
    }

    public void Dispose()
    {
        Stop();
        _engine?.Dispose();
        _engine = null;
    }

    private async Task CopyBundledVoicesAsync(IReadOnlyList<KokoroVoiceOption> voices, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(VoicesDirectory);
        foreach (var voice in voices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(_bundledVoicesDirectory, $"{voice.Id}.npy");
            var destinationPath = Path.Combine(VoicesDirectory, $"{voice.Id}.npy");
            if (File.Exists(destinationPath))
            {
                continue;
            }

            var temporaryPath = $"{destinationPath}.download";
            try
            {
                await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }
                File.Move(temporaryPath, destinationPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private void EnsureEngine()
    {
        KokoroVoiceManager.LoadVoicesFromPath(VoicesDirectory);
        if (_engine is not null)
        {
            return;
        }

        _engine = KokoroTTS.LoadModel(ModelPath);
    }

    private static bool MatchesLanguage(string voiceId, string language)
    {
        var prefixes = language.Trim().ToLowerInvariant() switch
        {
            "english" => "ab",
            "american english" or "english (united states)" => "a",
            "british english" or "english (united kingdom)" => "b",
            "spanish" => "e",
            "french" => "f",
            "hindi" => "h",
            "italian" => "i",
            "japanese" => "j",
            "portuguese" or "brazilian portuguese" => "p",
            "chinese" or "chinese (simplified)" or "chinese (traditional)" or "mandarin chinese" => "z",
            _ => string.Empty
        };
        return voiceId.Length > 3 && voiceId[2] == '_' && prefixes.Contains(voiceId[0]);
    }

    private static string GetLanguage(string voiceId) => voiceId[..Math.Min(2, voiceId.Length)].ToLowerInvariant() switch
    {
        "af" or "am" => "American English",
        "bf" or "bm" => "British English",
        "ef" or "em" => "Spanish",
        "ff" => "French",
        "hf" or "hm" => "Hindi",
        "if" or "im" => "Italian",
        "jf" or "jm" => "Japanese",
        "pf" or "pm" => "Brazilian Portuguese",
        "zf" or "zm" => "Mandarin Chinese",
        _ => "Other"
    };

    private static string GetVoiceName(string voiceId)
    {
        var separator = voiceId.IndexOf('_');
        var name = separator >= 0 ? voiceId[(separator + 1)..] : voiceId;
        return string.Join(' ', name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
