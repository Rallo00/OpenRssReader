using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenRssReader.Services;

public static class FreshRssService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<FreshRssLibrary> GetLibraryAsync(string serverUrl, string username, string password)
    {
        var session = await CreateSessionAsync(serverUrl, username, password);
        using var request = CreateRequest(HttpMethod.Get, $"{session.Endpoint}/reader/api/0/subscription/list?output=json", session.Auth);
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var feeds = document.RootElement.GetProperty("subscriptions").EnumerateArray().Select(item =>
        {
            var category = item.TryGetProperty("categories", out var categories) && categories.GetArrayLength() > 0 ? categories[0].GetProperty("label").GetString() ?? string.Empty : string.Empty;
            return new FreshRssFeed(item.GetProperty("id").GetString() ?? string.Empty, item.GetProperty("title").GetString() ?? string.Empty, item.GetProperty("url").GetString() ?? string.Empty, category);
        }).ToList();
        var groups = feeds.Where(feed => !string.IsNullOrWhiteSpace(feed.GroupId)).Select(feed => new FreshRssGroup(feed.GroupId, feed.GroupId)).Distinct().ToList();
        return new FreshRssLibrary(groups, feeds);
    }

    public static async Task<IReadOnlyList<FreshRssArticleState>> GetArticleStatesAsync(string serverUrl, string username, string password)
    {
        var session = await CreateSessionAsync(serverUrl, username, password);
        var results = new List<FreshRssArticleState>();
        var continuation = string.Empty;

        do
        {
            var continuationParameter = string.IsNullOrWhiteSpace(continuation)
                ? string.Empty
                : $"&c={Uri.EscapeDataString(continuation)}";
            using var request = CreateRequest(HttpMethod.Get, $"{session.Endpoint}/reader/api/0/stream/contents/reading-list?n=10000&output=json{continuationParameter}", session.Auth);
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

            if (document.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var entryId = item.TryGetProperty("id", out var id) ? id.GetString() : null;
                    var link = GetArticleLink(item);
                    if (string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(link))
                    {
                        continue;
                    }

                    var isRead = item.TryGetProperty("categories", out var categories) &&
                        categories.EnumerateArray().Any(category => string.Equals(category.GetString(), "user/-/state/com.google/read", StringComparison.Ordinal));
                    results.Add(new FreshRssArticleState(entryId, link, !isRead));
                }
            }

            continuation = document.RootElement.TryGetProperty("continuation", out var next)
                ? next.GetString() ?? string.Empty
                : string.Empty;
        }
        while (!string.IsNullOrWhiteSpace(continuation) && results.Count < 50000);

        return results;
    }

    public static async Task UpdateReadStatesAsync(string serverUrl, string username, string password, IEnumerable<FreshRssReadUpdate> updates)
    {
        var groupedUpdates = updates
            .Where(update => !string.IsNullOrWhiteSpace(update.EntryId))
            .DistinctBy(update => update.EntryId)
            .GroupBy(update => update.IsUnread)
            .ToList();
        if (groupedUpdates.Count == 0)
        {
            return;
        }

        var session = await CreateSessionAsync(serverUrl, username, password);
        using var tokenRequest = CreateRequest(HttpMethod.Get, $"{session.Endpoint}/reader/api/0/token", session.Auth);
        using var tokenResponse = await Client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadAsStringAsync()).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("FreshRSS did not return an update token.");
        }

        foreach (var group in groupedUpdates)
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new("T", token),
                new("async", "true"),
                new(group.Key ? "r" : "a", "user/-/state/com.google/read")
            };
            form.AddRange(group.Select(update => new KeyValuePair<string, string>("i", update.EntryId)));
            using var request = CreateRequest(HttpMethod.Post, $"{session.Endpoint}/reader/api/0/edit-tag", session.Auth);
            request.Content = new FormUrlEncodedContent(form);
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<FreshRssSession> CreateSessionAsync(string serverUrl, string username, string password)
    {
        var endpoint = NormalizeEndpoint(serverUrl);
        using var login = await Client.PostAsync($"{endpoint}/accounts/ClientLogin", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = username, ["Passwd"] = password }));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadAsStringAsync()).Split('\n').FirstOrDefault(line => line.StartsWith("Auth=", StringComparison.Ordinal))?[5..];
        if (string.IsNullOrWhiteSpace(auth))
        {
            throw new InvalidOperationException("FreshRSS rejected the username or password.");
        }

        return new FreshRssSession(endpoint, auth);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string auth)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("GoogleLogin", $"auth={auth}");
        return request;
    }

    private static string? GetArticleLink(JsonElement item)
    {
        foreach (var propertyName in new[] { "alternate", "canonical" })
        {
            if (!item.TryGetProperty(propertyName, out var links) || links.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var link in links.EnumerateArray())
            {
                if (link.TryGetProperty("href", out var href) && !string.IsNullOrWhiteSpace(href.GetString()))
                {
                    return href.GetString();
                }
            }
        }

        return null;
    }

    private static string NormalizeEndpoint(string url)
    {
        var path = new Uri(url.Trim(), UriKind.Absolute).GetLeftPart(UriPartial.Path).TrimEnd('/');
        return path.EndsWith("/api/greader.php", StringComparison.OrdinalIgnoreCase) ? path : $"{path}/api/greader.php";
    }
}

internal sealed record FreshRssSession(string Endpoint, string Auth);
public sealed record FreshRssGroup(string Id, string Title);
public sealed record FreshRssFeed(string Id, string Title, string Url, string GroupId);
public sealed record FreshRssLibrary(IReadOnlyList<FreshRssGroup> Groups, IReadOnlyList<FreshRssFeed> Feeds);
public sealed record FreshRssArticleState(string EntryId, string Link, bool IsUnread);
public sealed record FreshRssReadUpdate(string EntryId, bool IsUnread);
