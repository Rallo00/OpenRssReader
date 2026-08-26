using System.Windows.Media;
using OpenRssReader.Helpers;

namespace OpenRssReader.Models;

public sealed class ArticleItem : ObservableObject
{
    private bool _isFavorite;
    private bool _isSaved;
    private bool _isUnread = true;
    private string _sourceName = string.Empty;
    private DateTimeOffset _publishedAt;
    private bool _hasPublicationDate;
    private string _htmlContent = string.Empty;

    public required string Id { get; init; }
    public required string FeedId { get; init; }
    public required string SourceName
    {
        get => _sourceName;
        set => SetProperty(ref _sourceName, value);
    }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string HtmlContent
    {
        get => _htmlContent;
        set => SetProperty(ref _htmlContent, value);
    }
    public required string Link { get; init; }
    public required DateTimeOffset PublishedAt
    {
        get => _publishedAt;
        set
        {
            if (SetProperty(ref _publishedAt, value))
            {
                OnPropertyChanged(nameof(DisplayTime));
                OnPropertyChanged(nameof(DisplayDay));
                OnPropertyChanged(nameof(DisplayDateLabel));
            }
        }
    }
    public string Author { get; init; } = "Unknown author";
    public string ThumbnailLabel { get; init; } = "RSS";
    public string ThumbnailUrl { get; init; } = string.Empty;
    public string FaviconUrl { get; init; } = string.Empty;
    public required SolidColorBrush ThumbnailBrush { get; init; }
    public required SolidColorBrush HeroBrush { get; init; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool IsSaved
    {
        get => _isSaved;
        set => SetProperty(ref _isSaved, value);
    }

    public bool IsUnread
    {
        get => _isUnread;
        set => SetProperty(ref _isUnread, value);
    }

    public string DisplayTime => PublishedAt.ToLocalTime().ToString("HH:mm");
    public string DisplayDay => PublishedAt.ToLocalTime().ToString("dddd, dd MMMM yyyy").ToUpperInvariant();
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailUrl);
    public string DisplayDateLabel => PublishedAt.ToLocalTime().ToString("dddd, dd MMMM yyyy 'at' HH:mm").ToUpperInvariant();
    public string AuthorLine => $"{Author.ToUpperInvariant()}  {SourceName.ToUpperInvariant()}";
    public string FooterHint => $"Saved locally from {SourceName}. Open the original article for the full experience.";
    public bool HasPublicationDate
    {
        get => _hasPublicationDate;
        set => SetProperty(ref _hasPublicationDate, value);
    }

    // Set for headline-only feeds. The page is fetched only when the article is opened.
    public bool RequiresArticleContentFetch { get; set; }
}
