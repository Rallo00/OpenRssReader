using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Data;
using OpenRssReader.Helpers;
using OpenRssReader.Models;
using OpenRssReader.Services;

namespace OpenRssReader.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> RetiredSampleFeedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "9to5mac", "the-verge", "arstechnica", "guardian-world", "seriouseats"
    };
    private readonly FeedService _feedService = new();
    private readonly StorageService _storageService = new();
    private readonly TextToSpeechService _textToSpeechService = new();
    private readonly TranslationService _translationService = new();
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly List<ArticleItem> _allArticles = [];
    private readonly List<FeedSubscription> _allFeeds = [];
    private readonly List<string> _folders = [];
    private ArticleItem? _selectedArticle;
    private DateTimeOffset? _lastRefreshAt;
    private string _searchText = string.Empty;
    private string _activeSectionTitle = "Unread";
    private string _activeSectionSubtitle = "Loading articles...";
    private bool _showFavoritesOnly;
    private bool _showSavedOnly;
    private bool _showUnreadOnly = true;
    private string _feedlyAccessToken = string.Empty;
    private int _articleRetentionDays = 30;
    private string _readingFontFamily = "Segoe UI";
    private string _readingTitleFontFamily = "Segoe UI";
    private int _readingFontSize = 18;
    private int _readingTitleFontSize = 40;
    private string _textToSpeechVoiceId = string.Empty;
    private string _translationTargetLanguage = "English";
    private string? _translatedTitle;
    private string? _translatedHtml;
    private bool _isTranslating;
    private string? _selectedFeedId;
    private string _unreadSortOrder = "Newest first";
    private string _groupBy = "Date";
    private string _appearance = "Light";
    private bool _displaySourceFavicons = true;
    private bool _showAllArticlesList = true;
    private bool _showSavedList = true;
    private bool _showUnreadList = true;
    private bool _isTextToSpeechActive;
    private bool _isTextToSpeechPaused;
    private int _textToSpeechVolume = 80;
    private int _autoRefreshIntervalMinutes = 30;
    private int _markAsReadDelaySeconds = 3;
    private CancellationTokenSource? _readDelayCancellation;

    public MainViewModel()
    {
        FeedGroups = [];
        VisibleArticles = [];
        ApplyGrouping();
        RefreshAllCommand = new RelayCommand(async () => await RefreshFeedsAsync());
        SelectAllCommand = new RelayCommand(() =>
        {
            _showUnreadOnly = false;
            _showFavoritesOnly = false;
            _showSavedOnly = false;
            _selectedFeedId = null;
            RefreshVisibleArticles();
        });
        SelectUnreadCommand = new RelayCommand(() =>
        {
            _showUnreadOnly = true;
            _showFavoritesOnly = false;
            _showSavedOnly = false;
            _selectedFeedId = null;
            RefreshVisibleArticles();
        });
        ToggleFavoritesFilterCommand = new RelayCommand(() =>
        {
            _showFavoritesOnly = !_showFavoritesOnly;
            if (_showFavoritesOnly)
            {
                _showUnreadOnly = false;
                _showSavedOnly = false;
            }
            RefreshVisibleArticles();
        });
        SelectSavedCommand = new RelayCommand(() =>
        {
            _showSavedOnly = true;
            _showUnreadOnly = false;
            _showFavoritesOnly = false;
            _selectedFeedId = null;
            RefreshVisibleArticles();
        });
        ToggleSelectedFavoriteCommand = new RelayCommand(ToggleSelectedFavorite, () => SelectedArticle is not null);
        ToggleSelectedSavedCommand = new RelayCommand(ToggleSelectedSaved, () => SelectedArticle is not null);
        ToggleSelectedReadCommand = new RelayCommand(ToggleSelectedRead, () => SelectedArticle is not null);
        OpenInBrowserCommand = new RelayCommand(OpenSelectedInBrowser, () => SelectedArticle is not null);
        DecreaseReadingSizeCommand = new RelayCommand(() => _ = AdjustReadingTypographyAsync(-1));
        IncreaseReadingSizeCommand = new RelayCommand(() => _ = AdjustReadingTypographyAsync(1));
        TranslateSelectedArticleCommand = new RelayCommand(async () => await TranslateSelectedArticleAsync(), () => SelectedArticle is not null && !_isTranslating);
        ToggleTextToSpeechCommand = new RelayCommand(ToggleTextToSpeech, () => SelectedArticle is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public ObservableCollection<FeedGroup> FeedGroups { get; }
    public IReadOnlyList<string> FolderNames => _folders.OrderBy(name => name).ToList();
    public ObservableCollection<ArticleItem> VisibleArticles { get; }
    public RelayCommand RefreshAllCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectUnreadCommand { get; }
    public RelayCommand ToggleFavoritesFilterCommand { get; }
    public RelayCommand SelectSavedCommand { get; }
    public RelayCommand ToggleSelectedFavoriteCommand { get; }
    public RelayCommand ToggleSelectedSavedCommand { get; }
    public RelayCommand ToggleSelectedReadCommand { get; }
    public RelayCommand OpenInBrowserCommand { get; }
    public RelayCommand DecreaseReadingSizeCommand { get; }
    public RelayCommand IncreaseReadingSizeCommand { get; }
    public RelayCommand TranslateSelectedArticleCommand { get; }
    public RelayCommand ToggleTextToSpeechCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshVisibleArticles();
            }
        }
    }

    public ArticleItem? SelectedArticle
    {
        get => _selectedArticle;
        set
        {
            if (!SetProperty(ref _selectedArticle, value))
            {
                return;
            }

            CancelReadDelay();
            ScheduleMarkAsRead(_selectedArticle);
            _translatedTitle = null;
            _translatedHtml = null;

            OnPropertyChanged(nameof(HasSelectedArticle));
            OnPropertyChanged(nameof(SelectedArticleHtml));
            OnPropertyChanged(nameof(SelectedArticleDisplayTitle));
            OnPropertyChanged(nameof(TranslationToolTip));
            ToggleSelectedFavoriteCommand.RaiseCanExecuteChanged();
            ToggleSelectedSavedCommand.RaiseCanExecuteChanged();
            ToggleSelectedReadCommand.RaiseCanExecuteChanged();
            _textToSpeechService.Stop();
            SetTextToSpeechState();
            ToggleTextToSpeechCommand.RaiseCanExecuteChanged();
            TranslateSelectedArticleCommand.RaiseCanExecuteChanged();
            if (_selectedArticle is not null)
            {
                _ = LoadArticleContentAsync(_selectedArticle);
            }
            OpenInBrowserCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedArticle => SelectedArticle is not null;
    public string SelectedArticleHtml => HtmlRenderer.ApplyReadingTypography(
        _translatedHtml ?? SelectedArticle?.HtmlContent ?? HtmlRenderer.CreateDocument("Open RSS Reader", "<p>Select an article to start reading.</p>", string.Empty),
        _readingFontFamily,
        _readingTitleFontFamily,
        _readingFontSize,
        _appearance == "Dark");
    public string SelectedArticleDisplayTitle => _translatedTitle ?? SelectedArticle?.Title ?? string.Empty;
    public string LastRefreshLabel => _lastRefreshAt is null ? "Not refreshed yet" : $"Updated on {_lastRefreshAt.Value.ToLocalTime():dd MMM yyyy HH:mm}";
    public string ActiveSectionTitle
    {
        get => _activeSectionTitle;
        private set => SetProperty(ref _activeSectionTitle, value);
    }

    public string ActiveSectionSubtitle
    {
        get => _activeSectionSubtitle;
        private set => SetProperty(ref _activeSectionSubtitle, value);
    }

    public int TotalUnreadCount => _allArticles.Count(article => article.IsUnread);
    public bool HasUnreadItems => TotalUnreadCount > 0;
    public int TotalSavedCount => _allArticles.Count(article => article.IsSaved);
    public bool HasSavedItems => TotalSavedCount > 0;
    public Brush UnreadSectionBackground => _showUnreadOnly ? BrushFactory.CreateBrush("#E7DED3") : BrushFactory.CreateBrush("#EFE8DE");
    public Brush AllArticlesSectionBackground => !_showUnreadOnly && !_showFavoritesOnly ? BrushFactory.CreateBrush("#E7DED3") : BrushFactory.CreateBrush("#EFE8DE");
    public bool IsFavoritesFilterActive => _showFavoritesOnly;
    public bool IsSavedFilterActive => _showSavedOnly;
    public int ArticleRetentionDays => _articleRetentionDays;
    public string ReadingFontFamily => _readingFontFamily;
    public string ReadingTitleFontFamily => _readingTitleFontFamily;
    public int ReadingFontSize => _readingFontSize;
    public int ReadingTitleFontSize => _readingTitleFontSize;
    public string TextToSpeechVoiceId => _textToSpeechVoiceId;
    public string TranslationTargetLanguage => _translationTargetLanguage;
    public string TranslationToolTip => _isTranslating ? "Translating article..." : $"Translate to {_translationTargetLanguage}";
    public bool DisplaySourceFavicons => _displaySourceFavicons;
    public bool ShowAllArticlesList => _showAllArticlesList;
    public bool ShowSavedList => _showSavedList;
    public bool ShowUnreadList => _showUnreadList;
    public string UnreadSortOrder => _unreadSortOrder;
    public string GroupBy => _groupBy;
    public string Appearance => _appearance;
    public int AutoRefreshIntervalMinutes => _autoRefreshIntervalMinutes;
    public int MarkAsReadDelaySeconds => _markAsReadDelaySeconds;
    public bool IsTextToSpeechActive => _isTextToSpeechActive;
    public string TextToSpeechButtonPath => _isTextToSpeechActive && !_isTextToSpeechPaused
        ? "M7 5h4v14H7z M13 5h4v14h-4z"
        : "M8 5v14l11-7z";
    public string TextToSpeechToolTip => _isTextToSpeechActive && !_isTextToSpeechPaused ? "Pause reading" : "Read article aloud";
    public int TextToSpeechVolume
    {
        get => _textToSpeechVolume;
        set
        {
            var normalizedVolume = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _textToSpeechVolume, normalizedVolume))
            {
                _textToSpeechService.SetVolume(normalizedVolume);
            }
        }
    }

    public async Task InitializeAsync()
    {
        var state = await _storageService.LoadAsync();
        if (state is null)
        {
            return;
        }

        LoadState(state);
        CleanupExpiredArticles();
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public async ValueTask DisposeAsync()
    {
        CancelReadDelay();
        _textToSpeechService.Dispose();
        await PersistAsync();
    }

    private void LoadState(AppState state)
    {
        _feedlyAccessToken = state.FeedlyAccessToken;
        _articleRetentionDays = Math.Clamp(state.ArticleRetentionDays <= 0 ? 30 : state.ArticleRetentionDays, 1, 3650);
        _autoRefreshIntervalMinutes = Math.Clamp(state.AutoRefreshIntervalMinutes <= 0 ? 30 : state.AutoRefreshIntervalMinutes, 1, 1440);
        _markAsReadDelaySeconds = Math.Clamp(state.MarkAsReadDelaySeconds <= 0 ? 3 : state.MarkAsReadDelaySeconds, 1, 3600);
        _readingFontFamily = string.IsNullOrWhiteSpace(state.ReadingFontFamily) ? "Segoe UI" : state.ReadingFontFamily;
        _readingTitleFontFamily = string.IsNullOrWhiteSpace(state.ReadingTitleFontFamily) ? _readingFontFamily : state.ReadingTitleFontFamily;
        _readingFontSize = Math.Clamp(state.ReadingFontSize <= 0 ? 18 : state.ReadingFontSize, 12, 32);
        _readingTitleFontSize = Math.Clamp(state.ReadingTitleFontSize <= 0 ? 40 : state.ReadingTitleFontSize, 24, 64);
        _textToSpeechVoiceId = state.TextToSpeechVoiceId ?? string.Empty;
        _translationTargetLanguage = string.IsNullOrWhiteSpace(state.TranslationTargetLanguage) ? "English" : state.TranslationTargetLanguage;
        OnPropertyChanged(nameof(ReadingFontFamily));
        OnPropertyChanged(nameof(ReadingTitleFontFamily));
        OnPropertyChanged(nameof(ReadingFontSize));
        OnPropertyChanged(nameof(ReadingTitleFontSize));
        _unreadSortOrder = state.UnreadSortOrder == "Oldest first" ? "Oldest first" : "Newest first";
        _groupBy = state.GroupBy == "Source" ? "Source" : "Date";
        _appearance = state.Appearance is "Dark" or "System" ? state.Appearance : "Light";
        _displaySourceFavicons = state.DisplaySourceFavicons;
        _showAllArticlesList = state.ShowAllArticlesList;
        _showSavedList = state.ShowSavedList;
        _showUnreadList = state.ShowUnreadList;
        ApplyGrouping();
        _folders.Clear();
        _folders.AddRange((state.Folders ?? [])
            .Select(NormalizeFolderName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        _allFeeds.Clear();
        foreach (var feedState in state.Feeds.Where(feed => !RetiredSampleFeedIds.Contains(feed.Id)))
        {
            _allFeeds.Add(new FeedSubscription
            {
                Id = feedState.Id,
                Name = feedState.Name,
                Url = feedState.Url,
                GroupName = feedState.GroupName,
                AccentHex = feedState.AccentHex,
                AccentBrush = BrushFactory.CreateBrush(feedState.AccentHex),
                FaviconUrl = string.IsNullOrWhiteSpace(feedState.FaviconUrl) ? CreateFaviconUrl(feedState.Url) : feedState.FaviconUrl
            });
        }

        foreach (var folderName in _allFeeds.Select(feed => NormalizeFolderName(feed.GroupName)).Where(name => !string.IsNullOrEmpty(name)))
        {
            if (!_folders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
            {
                _folders.Add(folderName);
            }
        }

        _allArticles.Clear();
        foreach (var articleState in state.Articles.Where(article => !RetiredSampleFeedIds.Contains(article.FeedId)).OrderByDescending(article => article.PublishedAt))
        {
            _allArticles.Add(new ArticleItem
            {
                Id = articleState.Id,
                FeedId = articleState.FeedId,
                SourceName = articleState.SourceName,
                Title = articleState.Title,
                Summary = articleState.Summary,
                HtmlContent = HtmlRenderer.RemoveUnsupportedEmbeds(articleState.HtmlContent),
                Link = articleState.Link,
                PublishedAt = articleState.PublishedAt,
                Author = articleState.Author,
                ThumbnailLabel = articleState.ThumbnailLabel,
                ThumbnailUrl = string.IsNullOrWhiteSpace(articleState.ThumbnailUrl) ? HtmlRenderer.ExtractImageUrl(articleState.HtmlContent) : articleState.ThumbnailUrl,
                FaviconUrl = string.IsNullOrWhiteSpace(articleState.FaviconUrl) ? CreateFaviconUrl(articleState.Link) : articleState.FaviconUrl,
                ThumbnailBrush = BrushFactory.CreateBrush(articleState.AccentHex),
                HeroBrush = BrushFactory.CreateHeroBrush(articleState.AccentHex),
                RequiresArticleContentFetch = IsSummaryOnlyDocument(articleState.HtmlContent, articleState.Summary),
                IsFavorite = articleState.IsFavorite,
                IsSaved = articleState.IsSaved,
                IsUnread = articleState.IsUnread
            });
        }

        _lastRefreshAt = state.LastRefreshAt;
        RebuildFeedGroups();
        RecalculateUnreadCounts();
        OnPropertyChanged(nameof(LastRefreshLabel));
        _ = PersistAsync();
    }

    public async Task RefreshFeedsAsync()
    {
        if (!await _refreshSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            await RefreshAllAsync();
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private async Task RefreshAllAsync()
    {
        var existingById = _allArticles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var feed in _allFeeds)
        {
            try
            {
                var items = await _feedService.FetchFeedAsync(feed);
                foreach (var item in items)
                {
                    if (!existingById.TryGetValue(item.Id, out var existing))
                    {
                        _allArticles.Add(item);
                        existingById[item.Id] = item;
                    }
                    else
                    {
                        if (item.HasPublicationDate)
                        {
                            existing.PublishedAt = item.PublishedAt;
                            existing.HasPublicationDate = true;
                        }

                        existing.RequiresArticleContentFetch |= item.RequiresArticleContentFetch;
                    }
                }
            }
            catch
            {
            }
        }

        _allArticles.Sort((left, right) => right.PublishedAt.CompareTo(left.PublishedAt));
        CleanupExpiredArticles();
        _lastRefreshAt = DateTimeOffset.Now;
        RecalculateUnreadCounts();
        RefreshVisibleArticles();
        OnPropertyChanged(nameof(LastRefreshLabel));
        await PersistAsync();
    }

    public async Task AddFeedAsync(string address, string? displayName = null, string? folderName = null)
    {
        var subscription = await _feedService.DiscoverFeedAsync(address);
        if (_allFeeds.Any(feed => string.Equals(feed.Url, subscription.Url, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("This feed is already in your library.");
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            subscription.Name = displayName.Trim();
        }

        subscription.GroupName = NormalizeFolderName(folderName);

        _allFeeds.Add(subscription);
        RebuildFeedGroups();
        await RefreshAllAsync();
    }

    public async Task UpdateFeedAsync(FeedSubscription feed, string displayName, string address, string? folderName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Enter a display name for the feed.");
        }

        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var feedUri) ||
            (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS feed address.");
        }

        feed.Name = displayName.Trim();
        feed.Url = feedUri.AbsoluteUri;
        feed.GroupName = NormalizeFolderName(folderName);
        feed.FaviconUrl = CreateFaviconUrl(feed.Url);
        foreach (var article in _allArticles.Where(article => article.FeedId == feed.Id))
        {
            article.SourceName = feed.Name;
        }

        RebuildFeedGroups();
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public async Task CreateFolderAsync(string name)
    {
        var normalized = NormalizeFolderName(name);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new InvalidOperationException("Enter a folder name.");
        }

        if (_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder with this name already exists.");
        }

        _folders.Add(normalized);
        RebuildFeedGroups();
        OnPropertyChanged(nameof(FolderNames));
        await PersistAsync();
    }

    public async Task RenameFolderAsync(string currentName, string newName)
    {
        var normalized = NormalizeFolderName(newName);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new InvalidOperationException("Enter a folder name.");
        }

        if (!string.Equals(currentName, normalized, StringComparison.OrdinalIgnoreCase) && _folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder with this name already exists.");
        }

        var index = _folders.FindIndex(name => string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _folders[index] = normalized;
        foreach (var feed in _allFeeds.Where(feed => string.Equals(feed.GroupName, currentName, StringComparison.OrdinalIgnoreCase)))
        {
            feed.GroupName = normalized;
        }

        RebuildFeedGroups();
        OnPropertyChanged(nameof(FolderNames));
        await PersistAsync();
    }

    public async Task DeleteFolderAsync(string name)
    {
        _folders.RemoveAll(folder => string.Equals(folder, name, StringComparison.OrdinalIgnoreCase));
        foreach (var feed in _allFeeds.Where(feed => string.Equals(feed.GroupName, name, StringComparison.OrdinalIgnoreCase)))
        {
            feed.GroupName = string.Empty;
        }

        RebuildFeedGroups();
        OnPropertyChanged(nameof(FolderNames));
        await PersistAsync();
    }

    public async Task DeleteFeedAsync(FeedSubscription feed)
    {
        _allFeeds.Remove(feed);
        _allArticles.RemoveAll(article => article.FeedId == feed.Id);
        RebuildFeedGroups();
        RecalculateUnreadCounts();
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public async Task MarkFeedAsReadAsync(FeedSubscription feed)
    {
        var changed = false;
        foreach (var article in _allArticles.Where(article => article.FeedId == feed.Id && article.IsUnread))
        {
            article.IsUnread = false;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        RecalculateUnreadCounts();
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public string FeedlyAccessToken => _feedlyAccessToken;

    public async Task SetArticleRetentionDaysAsync(int days)
    {
        if (days is < 1 or > 3650)
        {
            throw new InvalidOperationException("Enter a number between 1 and 3650 days.");
        }

        _articleRetentionDays = days;
        CleanupExpiredArticles();
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public async Task SetReadingTypographyAsync(string fontFamily, string titleFontFamily, int fontSize, int titleFontSize)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || string.IsNullOrWhiteSpace(titleFontFamily))
        {
            throw new InvalidOperationException("Select a reading font.");
        }

        if (fontSize is < 12 or > 32)
        {
            throw new InvalidOperationException("Enter a font size between 12 and 32.");
        }

        if (titleFontSize is < 24 or > 64)
        {
            throw new InvalidOperationException("Enter a title size between 24 and 64.");
        }

        _readingFontFamily = fontFamily.Trim();
        _readingTitleFontFamily = titleFontFamily.Trim();
        _readingFontSize = fontSize;
        _readingTitleFontSize = titleFontSize;
        OnPropertyChanged(nameof(ReadingFontFamily));
        OnPropertyChanged(nameof(ReadingTitleFontFamily));
        OnPropertyChanged(nameof(ReadingFontSize));
        OnPropertyChanged(nameof(ReadingTitleFontSize));
        OnPropertyChanged(nameof(SelectedArticleHtml));
        await PersistAsync();
    }

    public Task AdjustReadingTypographyAsync(int amount) => SetReadingTypographyAsync(
        _readingFontFamily,
        _readingTitleFontFamily,
        Math.Clamp(_readingFontSize + amount, 12, 32),
        Math.Clamp(_readingTitleFontSize + amount, 24, 64));

    public IReadOnlyList<SpeechVoiceOption> GetInstalledTextToSpeechVoices() => _textToSpeechService.GetInstalledVoices();

    public async Task SetTextToSpeechVoiceAsync(string? voiceId)
    {
        _textToSpeechService.Stop();
        _textToSpeechVoiceId = voiceId?.Trim() ?? string.Empty;
        SetTextToSpeechState();
        OnPropertyChanged(nameof(TextToSpeechVoiceId));
        await PersistAsync();
    }

    public async Task SetTranslationTargetLanguageAsync(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        _translationTargetLanguage = language.Trim();
        _translatedTitle = null;
        _translatedHtml = null;
        OnPropertyChanged(nameof(TranslationTargetLanguage));
        OnPropertyChanged(nameof(SelectedArticleDisplayTitle));
        OnPropertyChanged(nameof(SelectedArticleHtml));
        OnPropertyChanged(nameof(TranslationToolTip));
        await PersistAsync();
    }

    public async Task SetGeneralPreferencesAsync(string unreadSortOrder, string groupBy, string appearance, int autoRefreshIntervalMinutes, int markAsReadDelaySeconds, bool displaySourceFavicons, bool showAllArticlesList, bool showSavedList, bool showUnreadList)
    {
        if (autoRefreshIntervalMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Enter a refresh interval between 1 and 1440 minutes.");
        }

        if (markAsReadDelaySeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("Enter a reading delay between 1 and 3600 seconds.");
        }

        _unreadSortOrder = unreadSortOrder == "Oldest first" ? "Oldest first" : "Newest first";
        _groupBy = groupBy == "Source" ? "Source" : "Date";
        _appearance = appearance is "Dark" or "System" ? appearance : "Light";
        _autoRefreshIntervalMinutes = autoRefreshIntervalMinutes;
        _markAsReadDelaySeconds = markAsReadDelaySeconds;
        CancelReadDelay();
        ScheduleMarkAsRead(SelectedArticle);
        _displaySourceFavicons = displaySourceFavicons;
        _showAllArticlesList = showAllArticlesList;
        _showSavedList = showSavedList;
        _showUnreadList = showUnreadList;
        ApplyGrouping();
        RefreshVisibleArticles();
        OnPropertyChanged(nameof(DisplaySourceFavicons));
        OnPropertyChanged(nameof(ShowAllArticlesList));
        OnPropertyChanged(nameof(ShowSavedList));
        OnPropertyChanged(nameof(ShowUnreadList));
        OnPropertyChanged(nameof(Appearance));
        OnPropertyChanged(nameof(AutoRefreshIntervalMinutes));
        OnPropertyChanged(nameof(MarkAsReadDelaySeconds));
        OnPropertyChanged(nameof(SelectedArticleHtml));
        await PersistAsync();
    }

    public async Task ConnectFeedlyAsync(string accessToken)
    {
        var profile = await FeedlyService.VerifyAsync(accessToken);
        _feedlyAccessToken = accessToken.Trim();
        await PersistAsync();
    }

    public async Task CreateBackupAsync(string path)
    {
        var backup = CreateState(includeArticles: false, includeFeedlyToken: false);
        backup.TextToSpeechVoiceId = string.Empty;
        await _storageService.SaveBackupAsync(path, backup);
    }

    public async Task RestoreBackupAsync(string path)
    {
        var backup = await _storageService.LoadBackupAsync(path);
        var currentFeedlyToken = _feedlyAccessToken;
        var currentTextToSpeechVoiceId = _textToSpeechVoiceId;
        backup.Articles = [];
        backup.FeedlyAccessToken = currentFeedlyToken;
        backup.TextToSpeechVoiceId = currentTextToSpeechVoiceId;
        LoadState(backup);
        CleanupExpiredArticles();
        RefreshVisibleArticles();
        await PersistAsync();
        await RefreshAllAsync();
    }

    public async Task ResetToFactoryAsync()
    {
        LoadState(new AppState());
        SelectedArticle = null;
        RefreshVisibleArticles();
        await PersistAsync();
    }

    public async Task<int> SyncFeedlyAsync()
    {
        if (string.IsNullOrWhiteSpace(_feedlyAccessToken))
        {
            throw new InvalidOperationException("Add a Feedly access token first.");
        }

        var subscriptions = await FeedlyService.GetSubscriptionsAsync(_feedlyAccessToken);
        var imported = 0;
        foreach (var subscription in subscriptions)
        {
            if (_allFeeds.Any(feed => string.Equals(feed.Url, subscription.Url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            _allFeeds.Add(subscription);
            imported++;
        }

        RebuildFeedGroups();
        await RefreshAllAsync();
        return imported;
    }

    private void RebuildFeedGroups()
    {
        FeedGroups.Clear();
        var root = new FeedGroup { Name = string.Empty, IsRoot = true };
        foreach (var feed in _allFeeds.Where(feed => string.IsNullOrWhiteSpace(feed.GroupName)).OrderBy(feed => feed.Name))
        {
            root.Feeds.Add(feed);
        }
        foreach (var folderName in _folders
                     .OrderBy(name => string.Equals(name, "RSS Feeds", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                     .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            var group = new FeedGroup { Name = folderName };
            foreach (var feed in _allFeeds.Where(feed => string.Equals(feed.GroupName, folderName, StringComparison.OrdinalIgnoreCase)).OrderBy(feed => feed.Name))
            {
                group.Feeds.Add(feed);
            }
            FeedGroups.Add(group);
        }

        if (root.Feeds.Count > 0)
        {
            FeedGroups.Add(root);
        }
    }

    private static string NormalizeFolderName(string? name) => name?.Trim() ?? string.Empty;

    private static bool IsSummaryOnlyDocument(string html, string summary)
    {
        var fallback = $"<p>{System.Net.WebUtility.HtmlEncode(summary)}</p>";
        return !string.IsNullOrWhiteSpace(summary) && html.Contains(fallback, StringComparison.Ordinal);
    }

    private static string CreateFaviconUrl(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? new Uri(uri, "/favicon.ico").AbsoluteUri : string.Empty;

    public void SelectFeed(FeedSubscription? feed)
    {
        if (feed is null)
        {
            return;
        }

        _selectedFeedId = feed.Id;
        _showUnreadOnly = false;
        _showFavoritesOnly = false;
        _showSavedOnly = false;
        RefreshVisibleArticles();
    }

    private void ApplyGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(VisibleArticles);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(_groupBy == "Source" ? nameof(ArticleItem.SourceName) : nameof(ArticleItem.DisplayDay)));
    }

    private void RefreshVisibleArticles()
    {
        IEnumerable<ArticleItem> query = _allArticles;
        if (_showUnreadOnly)
        {
            query = query.Where(article => article.IsUnread);
        }
        if (_showFavoritesOnly)
        {
            query = query.Where(article => article.IsFavorite);
        }
        if (_showSavedOnly)
        {
            query = query.Where(article => article.IsSaved);
        }
        if (!string.IsNullOrWhiteSpace(_selectedFeedId))
        {
            query = query.Where(article => article.FeedId == _selectedFeedId);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(article =>
                article.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                article.Summary.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                article.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var articles = (_showUnreadOnly && _unreadSortOrder == "Oldest first"
            ? query.OrderBy(article => article.PublishedAt)
            : query.OrderByDescending(article => article.PublishedAt)).Take(120).ToList();
        VisibleArticles.Clear();
        foreach (var article in articles)
        {
            VisibleArticles.Add(article);
        }

        ActiveSectionTitle = _selectedFeedId is not null ? _allFeeds.FirstOrDefault(feed => feed.Id == _selectedFeedId)?.Name ?? "Feed" : _showSavedOnly ? "Saved" : _showFavoritesOnly ? "Read later" : _showUnreadOnly ? "Unread" : "All Articles";
        ActiveSectionSubtitle = $"{VisibleArticles.Count} visible items - {VisibleArticles.Count(article => article.IsUnread)} to read";
        OnPropertyChanged(nameof(UnreadSectionBackground));
        OnPropertyChanged(nameof(AllArticlesSectionBackground));
        OnPropertyChanged(nameof(IsFavoritesFilterActive));
        OnPropertyChanged(nameof(IsSavedFilterActive));
        OnPropertyChanged(nameof(TotalUnreadCount));
        OnPropertyChanged(nameof(HasUnreadItems));
        OnPropertyChanged(nameof(TotalSavedCount));
        OnPropertyChanged(nameof(HasSavedItems));

        if (SelectedArticle is null || !VisibleArticles.Contains(SelectedArticle))
        {
            SelectedArticle = VisibleArticles.FirstOrDefault();
        }
    }

    private void RecalculateUnreadCounts()
    {
        var unreadByFeed = _allArticles.Where(article => article.IsUnread)
            .GroupBy(article => article.FeedId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var feed in _allFeeds)
        {
            feed.UnreadCount = unreadByFeed.GetValueOrDefault(feed.Id, 0);
        }

        OnPropertyChanged(nameof(TotalUnreadCount));
        OnPropertyChanged(nameof(HasUnreadItems));
        OnPropertyChanged(nameof(UnreadSectionBackground));
    }

    private async Task LoadArticleContentAsync(ArticleItem article)
    {
        if (!await _feedService.TryLoadArticleContentAsync(article))
        {
            return;
        }

        if (ReferenceEquals(SelectedArticle, article))
        {
            OnPropertyChanged(nameof(SelectedArticleHtml));
        }

        await PersistAsync();
    }

    private void ToggleSelectedFavorite()
    {
        if (SelectedArticle is null)
        {
            return;
        }
        SelectedArticle.IsFavorite = !SelectedArticle.IsFavorite;
        RefreshVisibleArticles();
        _ = PersistAsync();
    }

    private void ToggleSelectedSaved()
    {
        if (SelectedArticle is null)
        {
            return;
        }

        // Removing Saved only returns the article to the normal feed collection.
        // Retention cleanup decides later whether an old unsaved article expires.
        SelectedArticle.IsSaved = !SelectedArticle.IsSaved;
        RefreshVisibleArticles();
        _ = PersistAsync();
    }

    private void CleanupExpiredArticles()
    {
        var cutoff = DateTimeOffset.Now.AddDays(-_articleRetentionDays);
        _allArticles.RemoveAll(article => !article.IsSaved && article.PublishedAt < cutoff);
    }

    private void ToggleSelectedRead()
    {
        if (SelectedArticle is null)
        {
            return;
        }
        SelectedArticle.IsUnread = !SelectedArticle.IsUnread;
        if (_showUnreadOnly && !SelectedArticle.IsUnread)
        {
            // Keep the changed item visible instead of making the action appear inert.
            _showUnreadOnly = false;
        }
        RecalculateUnreadCounts();
        RefreshVisibleArticles();
        _ = PersistAsync();
    }

    private void ScheduleMarkAsRead(ArticleItem? article)
    {
        if (article is null || !article.IsUnread)
        {
            return;
        }

        _readDelayCancellation = new CancellationTokenSource();
        _ = MarkAsReadAfterDelayAsync(article, _readDelayCancellation.Token);
    }

    private async Task MarkAsReadAfterDelayAsync(ArticleItem article, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_markAsReadDelaySeconds), cancellationToken);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(SelectedArticle, article) || !article.IsUnread)
            {
                return;
            }

            article.IsUnread = false;
            RecalculateUnreadCounts();
            await PersistAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelReadDelay()
    {
        _readDelayCancellation?.Cancel();
        _readDelayCancellation?.Dispose();
        _readDelayCancellation = null;
    }

    private void OpenSelectedInBrowser()
    {
        if (SelectedArticle is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedArticle.Link,
            UseShellExecute = true
        });
    }

    private async Task TranslateSelectedArticleAsync()
    {
        var article = SelectedArticle;
        if (article is null || _isTranslating)
        {
            return;
        }

        _isTranslating = true;
        OnPropertyChanged(nameof(TranslationToolTip));
        TranslateSelectedArticleCommand.RaiseCanExecuteChanged();

        try
        {
            var translatedTitleTask = _translationService.TranslateAsync(article.Title, _translationTargetLanguage, "text");
            var articleBody = HtmlRenderer.ExtractDocumentBody(article.HtmlContent);
            var translatedBodyTask = _translationService.TranslateAsync(articleBody, _translationTargetLanguage, "html");
            await Task.WhenAll(translatedTitleTask, translatedBodyTask);

            if (!ReferenceEquals(SelectedArticle, article))
            {
                return;
            }

            _translatedTitle = translatedTitleTask.Result;
            _translatedHtml = HtmlRenderer.CreateDocument(string.Empty, translatedBodyTask.Result, string.Empty);
            OnPropertyChanged(nameof(SelectedArticleDisplayTitle));
            OnPropertyChanged(nameof(SelectedArticleHtml));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Article translation error: {exception.Message}");
        }
        finally
        {
            _isTranslating = false;
            OnPropertyChanged(nameof(TranslationToolTip));
            TranslateSelectedArticleCommand.RaiseCanExecuteChanged();
        }
    }

    private void ToggleTextToSpeech()
    {
        if (SelectedArticle is null)
        {
            return;
        }

        try
        {
            if (_textToSpeechService.IsPaused)
            {
                _textToSpeechService.Resume();
            }
            else if (_textToSpeechService.IsSpeaking)
            {
                _textToSpeechService.Pause();
            }
            else
            {
                _textToSpeechService.Speak(_translatedTitle ?? SelectedArticle.Title, _translatedHtml ?? SelectedArticle.HtmlContent, TextToSpeechVolume, _textToSpeechVoiceId);
            }

            SetTextToSpeechState();
        }
        catch (Exception exception)
        {
            _textToSpeechService.Stop();
            SetTextToSpeechState();
            Debug.WriteLine($"SAPI text-to-speech error: {exception.Message}");
        }
    }

    private void SetTextToSpeechState()
    {
        _isTextToSpeechActive = _textToSpeechService.IsSpeaking;
        _isTextToSpeechPaused = _textToSpeechService.IsPaused;
        OnPropertyChanged(nameof(IsTextToSpeechActive));
        OnPropertyChanged(nameof(TextToSpeechButtonPath));
        OnPropertyChanged(nameof(TextToSpeechToolTip));
    }

    private async Task PersistAsync()
    {
        await _storageService.SaveAsync(CreateState(includeArticles: true, includeFeedlyToken: true));
    }

    private AppState CreateState(bool includeArticles, bool includeFeedlyToken)
    {
        return new AppState
        {
            LastRefreshAt = _lastRefreshAt,
            FeedlyAccessToken = includeFeedlyToken ? _feedlyAccessToken : string.Empty,
            ArticleRetentionDays = _articleRetentionDays,
            AutoRefreshIntervalMinutes = _autoRefreshIntervalMinutes,
            MarkAsReadDelaySeconds = _markAsReadDelaySeconds,
            ReadingFontFamily = _readingFontFamily,
            ReadingTitleFontFamily = _readingTitleFontFamily,
            ReadingFontSize = _readingFontSize,
            ReadingTitleFontSize = _readingTitleFontSize,
            TextToSpeechVoiceId = _textToSpeechVoiceId,
            TranslationTargetLanguage = _translationTargetLanguage,
            UnreadSortOrder = _unreadSortOrder,
            GroupBy = _groupBy,
            Appearance = _appearance,
            DisplaySourceFavicons = _displaySourceFavicons,
            ShowAllArticlesList = _showAllArticlesList,
            ShowSavedList = _showSavedList,
            ShowUnreadList = _showUnreadList,
            Folders = [.. _folders],
            Feeds = _allFeeds.Select(feed => new FeedState
            {
                Id = feed.Id,
                Name = feed.Name,
                Url = feed.Url,
                GroupName = feed.GroupName,
                AccentHex = feed.AccentHex
                , FaviconUrl = feed.FaviconUrl
            }).ToList(),
            Articles = includeArticles ? _allArticles.Select(article => new ArticleState
            {
                Id = article.Id,
                FeedId = article.FeedId,
                SourceName = article.SourceName,
                Title = article.Title,
                Summary = article.Summary,
                HtmlContent = article.HtmlContent,
                Link = article.Link,
                PublishedAt = article.PublishedAt,
                Author = article.Author,
                ThumbnailLabel = article.ThumbnailLabel,
                ThumbnailUrl = article.ThumbnailUrl,
                FaviconUrl = article.FaviconUrl,
                AccentHex = article.ThumbnailBrush.Color.ToString(),
                IsFavorite = article.IsFavorite,
                IsSaved = article.IsSaved,
                IsUnread = article.IsUnread
            }).ToList() : []
        };
    }
}
