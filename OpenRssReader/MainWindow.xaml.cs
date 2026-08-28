using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenRssReader.Models;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _automaticRefreshTimer = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _automaticRefreshTimer.Tick += AutomaticRefreshTimer_Tick;
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            ApplyAppearance();
            ArticleBrowser.NavigateToString(_viewModel.SelectedArticleHtml);
            ResizeArticleBrowser();
            ConfigureAutomaticRefreshTimer();
        };
        Closed += async (_, _) =>
        {
            _automaticRefreshTimer.Stop();
            _automaticRefreshTimer.Tick -= AutomaticRefreshTimer_Tick;
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

    private void SubscribeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SubscribeWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private async void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new FolderEditorWindow("Create folder") { Owner = this };
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

        var choice = MessageBox.Show($"Remove '{feed.Name}' and its saved articles?", "Remove feed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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

        var window = new FolderEditorWindow("Edit folder", folder.Name) { Owner = this };
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

        var choice = MessageBox.Show($"Delete '{folder.Name}'? Its feeds will remain in RSS Feeds.", "Delete folder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
