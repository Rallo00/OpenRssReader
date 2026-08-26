using System.Net.Http;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenRssReader.Models;
using OpenRssReader.ViewModels;

namespace OpenRssReader.Services;

public sealed class FeedService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HashSet<string> _articleDateLookups = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> TryLoadArticleContentAsync(ArticleItem article)
    {
        if (!article.RequiresArticleContentFetch || !Uri.TryCreate(article.Link, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        article.RequiresArticleContentFetch = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("OpenRssReader/1.0");
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = ExtractArticleContent(await response.Content.ReadAsStringAsync(timeout.Token), uri, article.Title);
            if (content is null)
            {
                return false;
            }

            article.HtmlContent = HtmlRenderer.CreateDocument(article.Title, content, article.Summary);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ArticleItem>> FetchFeedAsync(FeedSubscription subscription)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, subscription.Url);
        request.Headers.UserAgent.ParseAdd("OpenRssReader/1.0");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync();
        if (xml.TrimStart().StartsWith('{'))
        {
            return ParseJsonFeed(subscription, xml);
        }

        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var rootName = document.Root?.Name.LocalName.ToLowerInvariant();

        var articles = rootName switch
        {
            "feed" => ParseAtom(subscription, document),
            _ => ParseRss(subscription, document)
        };

        await ResolveMissingPublicationDatesAsync(articles);
        return articles;
    }

    public async Task<FeedSubscription> DiscoverFeedAsync(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var initialUri) ||
            (initialUri.Scheme != Uri.UriSchemeHttp && initialUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS address.");
        }

        var candidate = initialUri;
        var content = await DownloadAsync(candidate);
        if (!LooksLikeFeed(content))
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            var alternate = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "link" &&
                    element.Attribute("href") is not null &&
                    (element.Attribute("type")?.Value.Contains("rss", StringComparison.OrdinalIgnoreCase) == true ||
                     element.Attribute("type")?.Value.Contains("atom", StringComparison.OrdinalIgnoreCase) == true ||
                     element.Attribute("type")?.Value.Contains("json", StringComparison.OrdinalIgnoreCase) == true));
            if (alternate is null || !Uri.TryCreate(candidate, alternate.Attribute("href")!.Value, out candidate))
            {
                throw new InvalidOperationException("No valid RSS, Atom, or JSON feed was found at this address.");
            }
            content = await DownloadAsync(candidate);
        }

        var name = GetFeedTitle(content, candidate.Host);
        var color = CreateAccentColor(candidate.Host);
        return new FeedSubscription
        {
            Id = CreateStableId("feed", candidate.AbsoluteUri),
            Name = name,
            Url = candidate.AbsoluteUri,
            GroupName = "RSS Feeds",
            AccentHex = color,
            AccentBrush = BrushFactory.CreateBrush(color),
            FaviconUrl = CreateFaviconUrl(candidate)
        };
    }

    private async Task<string> DownloadAsync(Uri address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("OpenRssReader/1.0");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static IReadOnlyList<ArticleItem> ParseRss(FeedSubscription subscription, XDocument document)
    {
        return document.Descendants()
            .Where(x => x.Name.LocalName == "item")
            .Take(50)
            .Select(item => CreateArticle(subscription, item, false))
            .ToList();
    }

    private static IReadOnlyList<ArticleItem> ParseAtom(FeedSubscription subscription, XDocument document)
    {
        return document.Descendants()
            .Where(x => x.Name.LocalName == "entry")
            .Take(50)
            .Select(item => CreateArticle(subscription, item, true))
            .ToList();
    }

    private static IReadOnlyList<ArticleItem> ParseJsonFeed(FeedSubscription subscription, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The address is not a valid JSON Feed.");
        }

        return items.EnumerateArray().Take(50).Select(item =>
        {
            var title = item.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? "Untitled article" : "Untitled article";
            var link = item.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? subscription.Url : subscription.Url;
            var summary = item.TryGetProperty("summary", out var summaryValue) ? summaryValue.GetString() ?? string.Empty : string.Empty;
            var hasFullContent = item.TryGetProperty("content_html", out var contentValue) && !string.IsNullOrWhiteSpace(contentValue.GetString());
            var content = hasFullContent ? contentValue.GetString()! : summary;
            var publishedAt = ParsePublishedAt(
                GetJsonString(item, "date_published"),
                GetJsonString(item, "published"),
                GetJsonString(item, "date"),
                GetJsonString(item, "date_modified"),
                GetJsonString(item, "modified"));
            return new ArticleItem
            {
                Id = CreateStableId(subscription.Id, item.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? link : link),
                FeedId = subscription.Id,
                SourceName = subscription.Name,
                Title = title,
                Summary = HtmlRenderer.ToPlainText(summary),
                HtmlContent = HtmlRenderer.CreateDocument(title, content, summary),
                Link = link,
                PublishedAt = publishedAt ?? DateTimeOffset.Now,
                HasPublicationDate = publishedAt.HasValue,
                Author = subscription.Name,
                ThumbnailLabel = CreateThumbnailLabel(subscription.Name),
                ThumbnailUrl = item.TryGetProperty("image", out var image) && image.TryGetProperty("url", out var imageUrl) ? imageUrl.GetString() ?? string.Empty : HtmlRenderer.ExtractImageUrl(content),
                FaviconUrl = subscription.FaviconUrl,
                ThumbnailBrush = subscription.AccentBrush,
                HeroBrush = BrushFactory.CreateHeroBrush(subscription.AccentHex),
                RequiresArticleContentFetch = NeedsArticleContentFetch(link, summary, hasFullContent)
            };
        }).ToList();
    }

    private static bool LooksLikeFeed(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith("{") || trimmed.StartsWith("<rss", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<feed", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Contains("<rss", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("<feed", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFeedTitle(string content, string fallback)
    {
        if (content.TrimStart().StartsWith('{'))
        {
            using var json = JsonDocument.Parse(content);
            return json.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? fallback : fallback;
        }

        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        return document.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim() ?? fallback;
    }

    private static string CreateAccentColor(string value)
    {
        var colors = new[] { "#4E8CBF", "#C66E8B", "#D18B4F", "#6A9A78", "#7166A9" };
        return colors[Math.Abs(value.GetHashCode(StringComparison.Ordinal)) % colors.Length];
    }

    private static string CreateFaviconUrl(Uri address) => new Uri(address, "/favicon.ico").AbsoluteUri;

    private static ArticleItem CreateArticle(FeedSubscription subscription, XElement item, bool isAtom)
    {
        var link = isAtom
            ? item.Elements().FirstOrDefault(x => x.Name.LocalName == "link" && x.Attribute("href") is not null)?.Attribute("href")?.Value
            : item.Elements().FirstOrDefault(x => x.Name.LocalName == "link")?.Value;
        var title = GetElementValue(item, "title", "Untitled article") ?? "Untitled article";
        var summary = HtmlRenderer.ToPlainText(GetElementValue(item, isAtom ? "summary" : "description", "No summary available."));
        var embeddedContent = GetElementValue(item, "encoded", null) ?? GetElementValue(item, "content", null);
        var hasFullContent = !string.IsNullOrWhiteSpace(embeddedContent);
        var content = embeddedContent ?? $"<p>{System.Net.WebUtility.HtmlEncode(summary)}</p>";
        var author = GetElementValue(item, "creator", null)
            ?? GetElementValue(item, "author", subscription.Name);
        var publishedAt = isAtom
            ? ParsePublishedAt(
                GetElementValue(item, "published", null),
                GetElementValue(item, "issued", null),
                GetElementValue(item, "created", null),
                GetElementValue(item, "updated", null),
                GetElementValue(item, "modified", null))
            : ParsePublishedAt(
                GetElementValue(item, "pubDate", null),
                GetElementValue(item, "date", null),
                GetElementValue(item, "published", null),
                GetElementValue(item, "publishedDate", null),
                GetElementValue(item, "created", null),
                GetElementValue(item, "updated", null));

        return new ArticleItem
        {
            Id = CreateStableId(subscription.Id, link ?? title),
            FeedId = subscription.Id,
            SourceName = subscription.Name,
            Title = title.Trim(),
            Summary = summary.Trim(),
            HtmlContent = HtmlRenderer.CreateDocument(title.Trim(), content, summary.Trim()),
            Link = link ?? subscription.Url,
            PublishedAt = publishedAt ?? DateTimeOffset.Now,
            HasPublicationDate = publishedAt.HasValue,
            Author = HtmlRenderer.ToPlainText(author ?? subscription.Name).Trim(),
            ThumbnailLabel = CreateThumbnailLabel(subscription.Name),
            ThumbnailUrl = HtmlRenderer.ExtractImageUrl(content),
            FaviconUrl = subscription.FaviconUrl,
            ThumbnailBrush = subscription.AccentBrush,
            HeroBrush = BrushFactory.CreateHeroBrush(subscription.AccentHex),
            RequiresArticleContentFetch = NeedsArticleContentFetch(link ?? subscription.Url, summary, hasFullContent)
        };
    }

    private static bool NeedsArticleContentFetch(string link, string summary, bool hasFullContent) =>
        !hasFullContent && HtmlRenderer.ToPlainText(summary).Length < 900 &&
        Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? ExtractArticleContent(string html, Uri baseUri, string title)
    {
        var article = FindBestContentContainer(html);
        if (string.IsNullOrWhiteSpace(article))
        {
            return null;
        }

        article = Regex.Replace(article, @"<!--.*?-->|<(?:script|style|noscript|iframe|nav|footer|aside|form|button)\b[^>]*>.*?</(?:script|style|noscript|iframe|nav|footer|aside|form|button)\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        article = Regex.Replace(article, @"\son\w+\s*=\s*(['""]).*?\1", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        article = CleanSourceSpecificContent(article, baseUri);
        article = RemoveDuplicateTitle(article, title);
        if (HtmlRenderer.ToPlainText(article).Length < 220)
        {
            return null;
        }

        return MakeUrlsAbsolute(article, baseUri);
    }

    private static string RemoveDuplicateTitle(string html, string title)
    {
        var heading = Regex.Match(html, @"<h1\b[^>]*>.*?</h1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return heading.Success && string.Equals(HtmlRenderer.ToPlainText(heading.Value), title.Trim(), StringComparison.OrdinalIgnoreCase)
            ? html.Remove(heading.Index, heading.Length)
            : html;
    }

    private static string CleanSourceSpecificContent(string html, Uri source)
    {
        if (!source.Host.EndsWith("bbc.co.uk", StringComparison.OrdinalIgnoreCase) &&
            !source.Host.EndsWith("bbc.com", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        html = RemoveMatchingElements(html, "div", attributes =>
            attributes.Contains("data-block=\"metadata\"", StringComparison.OrdinalIgnoreCase) ||
            attributes.Contains("data-block='metadata'", StringComparison.OrdinalIgnoreCase) ||
            attributes.Contains("data-block=\"topicList\"", StringComparison.OrdinalIgnoreCase) ||
            attributes.Contains("data-block='topicList'", StringComparison.OrdinalIgnoreCase));
        html = Regex.Replace(html, @"<figcaption\b[^>]*>.*?</figcaption\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var relatedTopics = Regex.Match(html, @"<h2\b[^>]*>\s*Related\s+topics\s*</h2\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return relatedTopics.Success ? html[..relatedTopics.Index] : html;
    }

    private static string RemoveMatchingElements(string html, string elementName, Func<string, bool> acceptsAttributes)
    {
        foreach (var element in FindElements(html, elementName, acceptsAttributes).OrderByDescending(element => element.Length))
        {
            html = html.Replace(element, string.Empty, StringComparison.Ordinal);
        }

        return html;
    }

    private static string? FindBestContentContainer(string html)
    {
        var preferred = FindElements(html, "div", attributes => HasContentHint(attributes))
            .Concat(FindElements(html, "section", attributes => HasContentHint(attributes)))
            .Concat(FindElements(html, "article", attributes => HasContentHint(attributes)))
            .Where(IsVisible)
            .ToList();
        if (preferred.Count > 0)
        {
            return preferred.MaxBy(content => HtmlRenderer.ToPlainText(content).Length);
        }

        var articles = FindElements(html, "article", _ => true).Where(IsVisible).ToList();
        if (articles.Count > 0)
        {
            return articles.MaxBy(content => HtmlRenderer.ToPlainText(content).Length);
        }

        return FindElements(html, "main", _ => true).Where(IsVisible)
            .MaxBy(content => HtmlRenderer.ToPlainText(content).Length);
    }

    private static bool HasContentHint(string attributes) =>
        Regex.IsMatch(attributes, @"\b(?:id|class|itemprop|role)\s*=\s*(['""])[^'""]*(?:article[-_ ]?(?:body|content|main)|news[-_ ]?item|art[-_ ]?main|ar[-_ ]?main|post[-_ ]?content|entry[-_ ]?content|story[-_ ]?body|articleBody)[^'""]*\1", RegexOptions.IgnoreCase);

    private static bool IsVisible(string element)
    {
        var openingTagEnd = element.IndexOf('>');
        var openingTag = openingTagEnd >= 0 ? element[..(openingTagEnd + 1)] : element;
        return !Regex.IsMatch(openingTag, @"<(?:article|div|section|main)\b[^>]*(?:hidden\b|aria-hidden\s*=\s*(['""])true\1|style\s*=\s*(['""])[^'""]*display\s*:\s*none[^'""]*\2)", RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> FindElements(string html, string elementName, Func<string, bool> acceptsAttributes)
    {
        var safeName = Regex.Escape(elementName);
        var startPattern = $@"<{safeName}\b(?<attributes>[^>]*)>";
        foreach (Match start in Regex.Matches(html, startPattern, RegexOptions.IgnoreCase))
        {
            if (!acceptsAttributes(start.Groups["attributes"].Value))
            {
                continue;
            }

            var end = FindElementEnd(html, elementName, start.Index, start.Length);
            if (end > start.Index)
            {
                yield return html[start.Index..end];
            }
        }
    }

    private static int FindElementEnd(string html, string elementName, int startIndex, int startLength)
    {
        var safeName = Regex.Escape(elementName);
        var tagPattern = $@"</?{safeName}\b[^>]*>";
        var depth = 1;
        foreach (Match tag in Regex.Matches(html, tagPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            if (tag.Index <= startIndex)
            {
                continue;
            }

            if (tag.Value.StartsWith("</", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0)
                {
                    return tag.Index + tag.Length;
                }
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                depth++;
            }
        }

        return startIndex + startLength;
    }

    private static string MakeUrlsAbsolute(string html, Uri baseUri) => Regex.Replace(
        html,
        @"(?<prefix>\b(?:src|href|poster)\s*=\s*['""])(?<value>[^'""]+)",
        match =>
        {
            var value = match.Groups["value"].Value;
            return Uri.TryCreate(baseUri, value, out var absolute) && absolute.Scheme is "http" or "https"
                ? match.Groups["prefix"].Value + absolute.AbsoluteUri
                : match.Value;
        },
        RegexOptions.IgnoreCase);

    private static string? GetElementValue(XElement element, string localName, string? fallback)
    {
        return element.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value ?? fallback;
    }

    private static string? GetJsonString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task ResolveMissingPublicationDatesAsync(IReadOnlyList<ArticleItem> articles)
    {
        var pending = articles.Where(article => !article.HasPublicationDate && _articleDateLookups.Add(article.Id)).ToList();
        await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (article, cancellationToken) =>
        {
            var publishedAt = await TryGetPublicationDateFromArticleAsync(article.Link, cancellationToken);
            if (publishedAt.HasValue)
            {
                article.PublishedAt = publishedAt.Value;
                article.HasPublicationDate = true;
            }
        });
    }

    private async Task<DateTimeOffset?> TryGetPublicationDateFromArticleAsync(string address, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("OpenRssReader/1.0");
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ExtractPublicationDate(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ExtractPublicationDate(string html)
    {
        foreach (Match tag in Regex.Matches(html, @"<(?:meta|time)\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase))
        {
            var attributes = tag.Groups["attributes"].Value;
            var key = GetHtmlAttribute(attributes, "property") ?? GetHtmlAttribute(attributes, "name") ?? GetHtmlAttribute(attributes, "itemprop");
            if (key is not ("article:published_time" or "article:published" or "datePublished" or "date" or "publishdate"))
            {
                continue;
            }

            var value = GetHtmlAttribute(attributes, "content") ?? GetHtmlAttribute(attributes, "datetime");
            var publishedAt = ParsePublishedAt(value);
            if (publishedAt.HasValue)
            {
                return publishedAt;
            }
        }

        var jsonDate = Regex.Match(html, @"""datePublished""\s*:\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase);
        var fromJson = ParsePublishedAt(jsonDate.Success ? jsonDate.Groups["value"].Value : null);
        if (fromJson.HasValue)
        {
            return fromJson;
        }

        var italianDate = Regex.Match(html, @"\b(?<day>\d{1,2})\s+(?<month>gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre)\s+(?<year>20\d{2})\s*\|\s*(?<hour>\d{1,2})[\.:](?<minute>\d{2})", RegexOptions.IgnoreCase);
        return italianDate.Success && DateTimeOffset.TryParseExact(
            $"{italianDate.Groups["day"].Value} {italianDate.Groups["month"].Value} {italianDate.Groups["year"].Value} {italianDate.Groups["hour"].Value}:{italianDate.Groups["minute"].Value}",
            "d MMMM yyyy H:mm",
            CultureInfo.GetCultureInfo("it-IT"),
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? GetHtmlAttribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $@"\b{Regex.Escape(name)}\s*=\s*(['""])(?<value>.*?)\1", RegexOptions.IgnoreCase);
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static DateTimeOffset? ParsePublishedAt(params string?[] values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = NormalizePublishedDate(value!);
            if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string NormalizePublishedDate(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"(?<=\d{4}),\s*", " ");
        return normalized
            .Replace("CEST", "+02:00", StringComparison.OrdinalIgnoreCase)
            .Replace("CET", "+01:00", StringComparison.OrdinalIgnoreCase)
            .Replace("EDT", "-04:00", StringComparison.OrdinalIgnoreCase)
            .Replace("EST", "-05:00", StringComparison.OrdinalIgnoreCase)
            .Replace("PDT", "-07:00", StringComparison.OrdinalIgnoreCase)
            .Replace("PST", "-08:00", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStableId(string feedId, string value)
    {
        var bytes = Encoding.UTF8.GetBytes($"{feedId}|{value}");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return hash[..24];
    }

    private static string CreateThumbnailLabel(string sourceName)
    {
        var letters = sourceName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]));
        return string.Concat(letters).PadRight(2, 'R');
    }
}
