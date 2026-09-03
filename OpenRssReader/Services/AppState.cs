namespace OpenRssReader.Services;

public sealed class AppState
{
    public DateTimeOffset? LastRefreshAt { get; set; }
    public string FeedlyAccessToken { get; set; } = string.Empty;
    public int ArticleRetentionDays { get; set; } = 30;
    public int AutoRefreshIntervalMinutes { get; set; } = 30;
    public int MarkAsReadDelaySeconds { get; set; } = 3;
    public string ReadingFontFamily { get; set; } = "Segoe UI";
    public string ReadingTitleFontFamily { get; set; } = "Segoe UI";
    public int ReadingFontSize { get; set; } = 18;
    public int ReadingTitleFontSize { get; set; } = 40;
    public string TextToSpeechVoiceId { get; set; } = string.Empty;
    public string TranslationTargetLanguage { get; set; } = "English";
    public string UnreadSortOrder { get; set; } = "Newest first";
    public string GroupBy { get; set; } = "Date";
    public string Appearance { get; set; } = "Light";
    public string ApplicationLanguage { get; set; } = "en";
    public bool DisplaySourceFavicons { get; set; } = true;
    public bool ShowAllArticlesList { get; set; } = true;
    public bool ShowSavedList { get; set; } = true;
    public bool ShowUnreadList { get; set; } = true;
    public List<string> Folders { get; set; } = [];
    public List<FeedState> Feeds { get; set; } = [];
    public List<ArticleState> Articles { get; set; } = [];
}

public sealed class FeedState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#5A8FD8";
    public string FaviconUrl { get; set; } = string.Empty;
}

public sealed class ArticleState
{
    public string Id { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string HtmlContent { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public string Author { get; set; } = string.Empty;
    public string ThumbnailLabel { get; set; } = "RSS";
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#5A8FD8";
    public bool IsFavorite { get; set; }
    public bool IsSaved { get; set; }
    public bool IsUnread { get; set; } = true;
}
