using System.Net;
using System.Text.RegularExpressions;

namespace OpenRssReader.Services;

public sealed class TextToSpeechService : IDisposable
{
    private const int SpeakAsync = 1;
    private const int PurgeBeforeSpeak = 2;
    private dynamic? _voice;

    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }

    public void Speak(string title, string html, int volume)
    {
        Stop();
        EnsureVoice();

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
            if (System.Runtime.InteropServices.Marshal.IsComObject(voice))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(voice);
            }
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

        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException("Windows SAPI is not available on this computer.");
        _voice = Activator.CreateInstance(voiceType)
            ?? throw new InvalidOperationException("Windows could not start the selected speech voice.");
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
