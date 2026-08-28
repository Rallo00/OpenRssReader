using System.Net;
using System.Text.RegularExpressions;

namespace OpenRssReader.Services;

public sealed record SpeechVoiceOption(string Id, string DisplayName);

public sealed class TextToSpeechService : IDisposable
{
    private const int SpeakAsync = 1;
    private const int PurgeBeforeSpeak = 2;
    private dynamic? _voice;

    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }

    public IReadOnlyList<SpeechVoiceOption> GetInstalledVoices()
    {
        dynamic? voice = null;
        try
        {
            voice = CreateVoice();
            dynamic tokens = voice.GetVoices(string.Empty, string.Empty);
            var voices = new List<SpeechVoiceOption>();

            for (var index = 0; index < Convert.ToInt32(tokens.Count); index++)
            {
                dynamic? token = null;
                try
                {
                    token = tokens.Item(index);
                    var id = (string)token.Id;
                    var description = (string)token.GetDescription();
                    voices.Add(new SpeechVoiceOption(id, description));
                }
                catch
                {
                    // Keep the available voices even if a malformed SAPI token is present.
                }
                finally
                {
                    ReleaseComObject(token);
                }
            }

            ReleaseComObject(tokens);
            return voices;
        }
        catch
        {
            return [];
        }
        finally
        {
            ReleaseComObject(voice);
        }
    }

    public void Speak(string title, string html, int volume, string voiceId)
    {
        Stop();
        EnsureVoice();

        SelectVoice(voiceId);
        _voice!.Volume = Math.Clamp(volume, 0, 100);
        _voice.Speak(BuildText(title, html), SpeakAsync | PurgeBeforeSpeak);
        IsSpeaking = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsSpeaking || IsPaused)
        {
            return;
        }

        _voice?.Pause();
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsSpeaking || !IsPaused)
        {
            return;
        }

        _voice?.Resume();
        IsPaused = false;
    }

    public void SetVolume(int volume)
    {
        if (_voice is not null)
        {
            _voice.Volume = Math.Clamp(volume, 0, 100);
        }
    }

    public void Stop()
    {
        var voice = _voice;
        _voice = null;
        if (voice is not null)
        {
            // A fresh SpVoice instance prevents SAPI from retaining the previous
            // asynchronous queue when the reader moves to another article.
            voice.Speak(string.Empty, PurgeBeforeSpeak);
            ReleaseComObject(voice);
        }

        IsSpeaking = false;
        IsPaused = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private void EnsureVoice()
    {
        if (_voice is not null)
        {
            return;
        }

        _voice = CreateVoice();
    }

    private void SelectVoice(string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return;
        }

        dynamic? tokens = null;
        try
        {
            tokens = _voice!.GetVoices(string.Empty, string.Empty);
            for (var index = 0; index < Convert.ToInt32(tokens.Count); index++)
            {
                dynamic? token = null;
                try
                {
                    token = tokens.Item(index);
                    if (string.Equals((string)token.Id, voiceId, StringComparison.OrdinalIgnoreCase))
                    {
                        _voice.Voice = token;
                        return;
                    }
                }
                finally
                {
                    ReleaseComObject(token);
                }
            }
        }
        finally
        {
            ReleaseComObject(tokens);
        }
    }

    private static dynamic CreateVoice()
    {
        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException("Windows SAPI is not available on this computer.");
        return Activator.CreateInstance(voiceType)
            ?? throw new InvalidOperationException("Windows could not start the selected speech voice.");
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && System.Runtime.InteropServices.Marshal.IsComObject(value))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // Releasing an already detached COM object is safe to ignore.
        }
    }

    private static string BuildText(string title, string html)
    {
        var text = Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? title : $"{title}. {text}";
    }
}
