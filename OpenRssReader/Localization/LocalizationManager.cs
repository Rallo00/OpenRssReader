using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OpenRssReader.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string DefaultLanguage = "en";
    private Dictionary<string, string> _strings = [];

    private LocalizationManager()
    {
        SetLanguage(DefaultLanguage);
    }

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    public string this[string key] => _strings.TryGetValue(key, out var value) ? value : key;

    public void SetLanguage(string? languageCode)
    {
        var requestedLanguage = string.IsNullOrWhiteSpace(languageCode)
            ? DefaultLanguage
            : languageCode.Trim().ToLowerInvariant();

        var englishStrings = LoadStrings(DefaultLanguage) ?? [];
        var localizedStrings = requestedLanguage == DefaultLanguage
            ? []
            : LoadStrings(requestedLanguage) ?? [];

        _strings = new Dictionary<string, string>(englishStrings, StringComparer.Ordinal);
        foreach (var (key, value) in localizedStrings)
        {
            _strings[key] = value;
        }
        CurrentLanguage = requestedLanguage;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    public string Get(string key, params object[] arguments)
    {
        var value = this[key];
        return arguments.Length == 0
            ? value
            : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    private static Dictionary<string, string>? LoadStrings(string languageCode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", $"{languageCode}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
