using System.Globalization;

namespace OpenRssReader.Localization;

public sealed record LanguageOption(string Code, string NativeName);

public static class LanguageCatalog
{
    private static readonly CultureInfo[] Cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
        .Append(CultureInfo.GetCultureInfo("no"))
        .Where(culture => !string.IsNullOrWhiteSpace(culture.Name))
        .DistinctBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
        .OrderBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly Lazy<IReadOnlyList<LanguageOption>> Languages = new(() => Cultures
        .Select(culture => new LanguageOption(culture.Name, NativeDisplayName(culture)))
        .OrderBy(option => option.NativeName, StringComparer.OrdinalIgnoreCase)
        .ToArray());

    private static readonly Lazy<IReadOnlyDictionary<string, string>> CodesByName = new(BuildNameLookup);

    public static IReadOnlyList<LanguageOption> All => Languages.Value;

    public static string? ResolveCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim().Replace('_', '-');
        var culture = Cultures.FirstOrDefault(culture => string.Equals(culture.Name, value, StringComparison.OrdinalIgnoreCase));
        if (culture is not null)
        {
            return culture.Name;
        }

        if (CodesByName.Value.TryGetValue(value, out var code))
        {
            return code;
        }

        var separator = value.IndexOf('-');
        if (separator > 0 && CodesByName.Value.TryGetValue(value[..separator], out var baseCode))
        {
            return baseCode;
        }

        return null;
    }

    public static LanguageOption? Find(string? value)
    {
        var code = ResolveCode(value);
        if (code is null)
        {
            return null;
        }

        var culture = Cultures.FirstOrDefault(culture => string.Equals(culture.Name, code, StringComparison.OrdinalIgnoreCase))
            ?? Cultures.FirstOrDefault(culture => string.Equals(culture.Name, code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        return culture is null ? null : new LanguageOption(culture.Name, NativeDisplayName(culture));
    }

    public static string GetNativeName(string value) => Find(value)?.NativeName ?? value;

    private static string NativeDisplayName(CultureInfo culture)
    {
        var nativeName = culture.NativeName;
        return culture.TextInfo.ToUpper(nativeName[..1]) + nativeName[1..];
    }

    private static IReadOnlyDictionary<string, string> BuildNameLookup()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Codes take precedence over language names such as "Yi" (ii).
        foreach (var culture in Cultures)
        {
            names.Add(culture.Name, culture.Name);
        }

        foreach (var culture in Cultures)
        {
            // Accept both native and legacy English names in existing settings
            // and backups, while new selections are always stored by code.
            names.TryAdd(culture.EnglishName, culture.Name);
            names.TryAdd(culture.NativeName, culture.Name);
            names.TryAdd(NativeDisplayName(culture), culture.Name);
        }

        names["American English"] = "en-US";
        names["British English"] = "en-GB";
        names["en-US"] = "en-US";
        names["en-GB"] = "en-GB";
        names["English (United States)"] = "en-US";
        names["English (United Kingdom)"] = "en-GB";
        names["Brazilian Portuguese"] = "pt-BR";
        names["pt-BR"] = "pt-BR";
        names["Mandarin Chinese"] = "zh";
        return names;
    }
}
