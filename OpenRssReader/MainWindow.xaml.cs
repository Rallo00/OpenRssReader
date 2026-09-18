using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenRssReader.Models;
using OpenRssReader.Localization;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _automaticRefreshTimer = new();
    private readonly DispatcherTimer _podcastProgressTimer = new();
    private readonly MediaPlayer _podcastPlayer = new();
    private readonly HttpClient _podcastHttpClient = new();
    private bool _isPodcastPlaying;
    private bool _isUpdatingPodcastProgress;
    private bool _startPodcastWhenReady;
    private Uri? _podcastAudioUri;
    private int _podcastRequestVersion;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _automaticRefreshTimer.Tick += AutomaticRefreshTimer_Tick;
        _podcastProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _podcastProgressTimer.Tick += PodcastProgressTimer_Tick;
        _podcastPlayer.MediaOpened += PodcastPlayer_MediaOpened;
        _podcastPlayer.MediaEnded += PodcastPlayer_MediaEnded;
        _podcastPlayer.MediaFailed += PodcastPlayer_MediaFailed;
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            ApplyAppearance();
            ArticleBrowser.NavigateToString(_viewModel.SelectedArticleHtml);
            _ = ConfigurePodcastPlayerAsync();
            ResizeArticleBrowser();
            ConfigureAutomaticRefreshTimer();
        };
        Closed += async (_, _) =>
        {
            _automaticRefreshTimer.Stop();
            _podcastProgressTimer.Stop();
            _automaticRefreshTimer.Tick -= AutomaticRefreshTimer_Tick;
            _podcastProgressTimer.Tick -= PodcastProgressTimer_Tick;
            _podcastPlayer.Stop();
            _podcastPlayer.Close();
            _podcastPlayer.MediaOpened -= PodcastPlayer_MediaOpened;
            _podcastPlayer.MediaEnded -= PodcastPlayer_MediaEnded;
            _podcastPlayer.MediaFailed -= PodcastPlayer_MediaFailed;
            _podcastHttpClient.Dispose();
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            await _viewModel.DisposeAsync();
        };
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedArticleHtml))
        {
            ArticleBrowser.NavigateToString(_viewModel.SelectedArticleHtml);
        }
        else if (e.PropertyName == nameof(MainViewModel.Appearance))
        {
            ApplyAppearance();
        }
        else if (e.PropertyName == nameof(MainViewModel.AutoRefreshIntervalMinutes))
        {
            ConfigureAutomaticRefreshTimer();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedPodcastAudioUrl))
        {
            _ = ConfigurePodcastPlayerAsync();
        }
    }

    private void ConfigureAutomaticRefreshTimer()
    {
        _automaticRefreshTimer.Stop();
        _automaticRefreshTimer.Interval = TimeSpan.FromMinutes(_viewModel.AutoRefreshIntervalMinutes);
        _automaticRefreshTimer.Start();
    }

    private async void AutomaticRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _automaticRefreshTimer.Stop();
        try
        {
            await _viewModel.RefreshFeedsAsync();
        }
        finally
        {
            if (IsLoaded)
            {
                ConfigureAutomaticRefreshTimer();
            }
        }
    }

    private async Task ConfigurePodcastPlayerAsync()
    {
        var requestVersion = ++_podcastRequestVersion;
        _podcastProgressTimer.Stop();
        _isPodcastPlaying = false;
        _startPodcastWhenReady = false;
        _podcastAudioUri = null;
        _isUpdatingPodcastProgress = true;
        PodcastProgress.Value = 0;
        PodcastProgress.Maximum = 1;
        PodcastTimeLabel.Text = "Loading audio...";
        _isUpdatingPodcastProgress = false;
        PodcastPlayPauseButton.IsEnabled = false;
        SetPodcastPlayButtonState();
        _podcastPlayer.Stop();
        _podcastPlayer.Close();

        if (!Uri.TryCreate(_viewModel.SelectedPodcastAudioUrl, UriKind.Absolute, out var audioUri))
        {
            PodcastTimeLabel.Text = "00:00 / 00:00";
            return;
        }

        try
        {
            var cachedAudioPath = await GetCachedPodcastAudioAsync(audioUri);
            if (!IsLoaded || requestVersion != _podcastRequestVersion)
            {
                return;
            }

            _podcastAudioUri = new Uri(cachedAudioPath);
            _podcastPlayer.Open(_podcastAudioUri);
            _podcastPlayer.Volume = PodcastVolume.Value;
            PodcastPlayPauseButton.IsEnabled = true;
            PodcastTimeLabel.Text = "00:00 / 00:00";
        }
        catch (Exception)
        {
            if (IsLoaded && requestVersion == _podcastRequestVersion)
            {
                PodcastTimeLabel.Text = "Audio unavailable";
            }
        }
    }

    private async Task<string> GetCachedPodcastAudioAsync(Uri audioUri)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenRssReader",
            "podcasts");
        Directory.CreateDirectory(cacheDirectory);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(audioUri.AbsoluteUri))).ToLowerInvariant();
        var extension = Path.GetExtension(audioUri.AbsolutePath);
        var cachedAudioPath = Path.Combine(cacheDirectory, $"{hash}{(string.IsNullOrWhiteSpace(extension) ? ".mp3" : extension)}");
        if (File.Exists(cachedAudioPath) && new FileInfo(cachedAudioPath).Length > 0)
        {
            return cachedAudioPath;
        }

        var temporaryPath = $"{cachedAudioPath}.{Guid.NewGuid():N}.download";
        try
        {
            using var response = await _podcastHttpClient.GetAsync(audioUri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            File.Move(temporaryPath, cachedAudioPath, true);
            return cachedAudioPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PodcastPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_podcastAudioUri is null)
        {
            return;
        }

        if (_isPodcastPlaying)
        {
            _podcastPlayer.Pause();
            _podcastProgressTimer.Stop();
            _isPodcastPlaying = false;
            _startPodcastWhenReady = false;
        }
        else
        {
            _startPodcastWhenReady = !_podcastPlayer.NaturalDuration.HasTimeSpan;
            _podcastPlayer.Play();
            _podcastProgressTimer.Start();
            _isPodcastPlaying = true;
        }

        SetPodcastPlayButtonState();
    }

    private void PodcastPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (!_podcastPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        _isUpdatingPodcastProgress = true;
        PodcastProgress.Maximum = _podcastPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        _isUpdatingPodcastProgress = false;
        UpdatePodcastTimeLabel();

        if (_startPodcastWhenReady)
        {
            _podcastPlayer.Play();
            _startPodcastWhenReady = false;
        }
    }

    private void PodcastPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        _podcastProgressTimer.Stop();
        _isPodcastPlaying = false;
        _startPodcastWhenReady = false;
        SetPodcastPlayButtonState();
        PodcastTimeLabel.Text = "Audio unavailable";
    }

    private void PodcastPlayer_MediaEnded(object? sender, EventArgs e)
    {
        _podcastPlayer.Stop();
        _podcastProgressTimer.Stop();
        _isPodcastPlaying = false;
        _startPodcastWhenReady = false;
        _isUpdatingPodcastProgress = true;
        PodcastProgress.Value = 0;
        _isUpdatingPodcastProgress = false;
        SetPodcastPlayButtonState();
        UpdatePodcastTimeLabel();
    }

    private void PodcastProgressTimer_Tick(object? sender, EventArgs e)
    {
        if (!_podcastPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        _isUpdatingPodcastProgress = true;
        PodcastProgress.Value = Math.Min(_podcastPlayer.Position.TotalSeconds, PodcastProgress.Maximum);
        _isUpdatingPodcastProgress = false;
        UpdatePodcastTimeLabel();
    }

    private void PodcastProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPodcastProgress || !_podcastPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        _podcastPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
        UpdatePodcastTimeLabel();
    }

    private void PodcastVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _podcastPlayer.Volume = e.NewValue;
    }

    private void UpdatePodcastTimeLabel()
    {
        var duration = _podcastPlayer.NaturalDuration.HasTimeSpan ? _podcastPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        PodcastTimeLabel.Text = $"{_podcastPlayer.Position:mm\\:ss} / {duration:mm\\:ss}";
    }

    private void SetPodcastPlayButtonState()
    {
        if (PodcastPlayPauseButton.Template.FindName("PodcastPlayPauseIcon", PodcastPlayPauseButton) is System.Windows.Shapes.Path icon)
        {
            icon.Data = Geometry.Parse(_isPodcastPlaying ? "M7,5 H11 V19 H7 Z M13,5 H17 V19 H13 Z" : "M8,5 L19,12 L8,19 Z");
        }

        PodcastPlayPauseButton.ToolTip = LocalizationManager.Instance[_isPodcastPlaying ? "Main.PauseEpisode" : "Main.PlayEpisode"];
    }

    private void SubscribeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SubscribeWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private async void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new FolderEditorWindow(LocalizationManager.Instance["Dialog.CreateFolder"]) { Owner = this };
        if (window.ShowDialog() == true)
        {
            await _viewModel.CreateFolderAsync(window.FolderName);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private void EditFeedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FeedSubscription feed)
        {
            return;
        }

        new FeedEditorWindow(_viewModel, feed) { Owner = this }.ShowDialog();
    }

    private async void DeleteFeedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FeedSubscription feed)
        {
            return;
        }

        var choice = MessageBox.Show(LocalizationManager.Instance.Get("Dialog.RemoveFeedMessage", feed.Name), LocalizationManager.Instance["Dialog.RemoveFeed"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteFeedAsync(feed);
        }
    }

    private async void MarkFeedAsReadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is FeedSubscription feed)
        {
            await _viewModel.MarkFeedAsReadAsync(feed);
        }
    }

    private async void EditFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FeedGroup folder || folder.IsRoot)
        {
            return;
        }

        var window = new FolderEditorWindow(LocalizationManager.Instance["Dialog.EditFolder"], folder.Name) { Owner = this };
        if (window.ShowDialog() == true)
        {
            await _viewModel.RenameFolderAsync(folder.Name, window.FolderName);
        }
    }

    private async void DeleteFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FeedGroup folder || folder.IsRoot)
        {
            return;
        }

        var choice = MessageBox.Show(LocalizationManager.Instance.Get("Dialog.DeleteFolderMessage", folder.Name), LocalizationManager.Instance["Dialog.DeleteFolder"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteFolderAsync(folder.Name);
        }
    }

    private void FeedItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FeedSubscription feed)
        {
            _viewModel.SelectFeed(feed);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeArticleBrowser();

    private void ResizeArticleBrowser()
    {
        // The embedded browser does not reliably stretch its HWND with the Grid row.
        ArticleBrowser.Height = Math.Max(240, ActualHeight - 340);
    }

    private void ApplyAppearance()
    {
        var dark = _viewModel.Appearance == "Dark";
        // Read immutable palette entries from App, then override them only for this window.
        var palette = Application.Current.Resources;
        var resources = Resources;
        resources["SidebarBackgroundBrush"] = palette[dark ? "DarkSidebarBrush" : "LightSidebarBrush"];
        resources["ContentBackgroundBrush"] = palette[dark ? "DarkContentBrush" : "LightContentBrush"];
        resources["SurfaceBackgroundBrush"] = palette[dark ? "DarkContentBrush" : "LightSidebarBrush"];
        resources["HoverBackgroundBrush"] = palette[dark ? "DarkHoverBrush" : "LightHoverBrush"];
        resources["InputBackgroundBrush"] = palette[dark ? "DarkInputBackgroundBrush" : "LightInputBackgroundBrush"];
        resources["SelectionBackgroundBrush"] = palette[dark ? "DarkSidebarBrush" : "LightSelectionBrush"];
        resources["SeparatorBrush"] = palette[dark ? "DarkContentBrush" : "LightSeparatorBrush"];
        resources["SidebarTitleBrush"] = palette[dark ? "DarkSidebarTitleBrush" : "LightSidebarTitleBrush"];
        resources["FeedNameBrush"] = palette[dark ? "DarkFeedNameBrush" : "LightFeedNameBrush"];
        resources["PrimaryTextBrush"] = palette[dark ? "DarkTextBrush" : "LightPrimaryTextBrush"];
        resources["SecondaryTextBrush"] = palette[dark ? "DarkSecondaryTextBrush" : "LightSecondaryTextBrush"];
        resources["MutedTextBrush"] = palette[dark ? "DarkSecondaryTextBrush" : "LightMutedTextBrush"];
        resources["IconBrush"] = palette[dark ? "DarkSecondaryTextBrush" : "LightIconBrush"];
        resources["InputTextBrush"] = palette[dark ? "DarkInputTextBrush" : "LightInputTextBrush"];
    }
}
