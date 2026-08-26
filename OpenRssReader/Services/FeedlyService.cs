using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using OpenRssReader.Models;
using OpenRssReader.ViewModels;

namespace OpenRssReader.Services;

public static class FeedlyService
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("https://cloud.feedly.com/v3/"), Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<string> VerifyAsync(string accessToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "profile", accessToken);
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() ?? "Feedly account" : "Feedly account";
    }

    public static async Task<IReadOnlyList<FeedSubscription>> GetSubscriptionsAsync(string accessToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "subscriptions", accessToken);
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscriptions = new List<FeedSubscription>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var streamId = item.GetProperty("id").GetString() ?? string.Empty;
            var url = streamId.StartsWith("feed/", StringComparison.OrdinalIgnoreCase) ? streamId[5..] : streamId;
            if (!Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
            var name = item.TryGetProperty("title", out var title) ? title.GetString() ?? url : url;
            var group = item.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0 &&
                categories[0].TryGetProperty("label", out var label) ? label.GetString() ?? "RSS Feeds" : "RSS Feeds";
            const string accent = "#4E8CBF";
            subscriptions.Add(new FeedSubscription { Id = streamId, Name = name, Url = url, GroupName = group, AccentHex = accent, AccentBrush = BrushFactory.CreateBrush(accent), FaviconUrl = new Uri(new Uri(url), "/favicon.ico").AbsoluteUri });
        }
        return subscriptions;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        return request;
    }
}
