using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using OpenRssReader.Localization;

namespace OpenRssReader.Services;

public sealed class TranslationService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<string> TranslateAsync(string content, string targetLanguage, string format, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var request = new TranslationRequest(
            content,
            "auto",
            ResolveLanguageCode(targetLanguage),
            format);

        using var response = await Client.PostAsJsonAsync(
            "https://translate.libregalaxy.org/translate",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Translation service returned {(int)response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<TranslationResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(result?.TranslatedText))
        {
            throw new InvalidOperationException("Translation service returned an empty response.");
        }

        return result.TranslatedText;
    }

    private static string ResolveLanguageCode(string language)
    {
        var option = LanguageCatalog.Find(language);
        return option is null ? "en" : CultureInfo.GetCultureInfo(option.Code).TwoLetterISOLanguageName;
    }

    private sealed record TranslationRequest(string Q, string Source, string Target, string Format);
    private sealed record TranslationResponse(string? TranslatedText);
}
