using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OpenRssReader.Localization;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class SubscribeWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly List<CatalogFeed> _catalogFeeds = [];
    private readonly ObservableCollection<CatalogFeed> _visibleCatalogFeeds = [];

    public SubscribeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        FolderComboBox.ItemsSource = _viewModel.FolderNames;
        AutomaticFolderComboBox.ItemsSource = _viewModel.FolderNames;
        AutomaticResultsListBox.ItemsSource = _visibleCatalogFeeds;
        LoadCatalogFeeds();
        UpdateAutomaticResults();
    }

    private void LoadCatalogFeeds()
    {
        var catalogDirectory = Path.Combine(AppContext.BaseDirectory, "rss");
        if (!Directory.Exists(catalogDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(catalogDirectory, "feedlist_*.json"))
        {
            try
            {
                var catalog = JsonSerializer.Deserialize<List<CatalogFeed>>(File.ReadAllText(path)) ?? [];
                _catalogFeeds.AddRange(catalog.Where(feed =>
                    !string.IsNullOrWhiteSpace(feed.Title) &&
                    Uri.TryCreate(feed.Link, UriKind.Absolute, out _)));
            }
            catch (JsonException)
            {
                // Ignore malformed optional catalog files and keep manual subscription available.
            }
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAutomaticResults();

    private void UpdateAutomaticResults()
    {
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        var results = _catalogFeeds
            .Where(feed => string.IsNullOrEmpty(query) ||
                           feed.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .DistinctBy(feed => feed.Link, StringComparer.OrdinalIgnoreCase)
            .OrderBy(feed => feed.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(100)
            .ToList();

        _visibleCatalogFeeds.Clear();
        foreach (var feed in results)
        {
            _visibleCatalogFeeds.Add(feed);
        }
    }

    private async void AutomaticSubscribeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutomaticResultsListBox.SelectedItem is not CatalogFeed feed)
        {
            AutomaticStatusText.Text = LocalizationManager.Instance["Subscribe.SelectFeed"];
            return;
        }

        try
        {
            AutomaticStatusText.Text = LocalizationManager.Instance["Status.CheckingFeed"];
            await _viewModel.AddFeedAsync(feed.Link, feed.Title, AutomaticFolderComboBox.SelectedItem as string);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            AutomaticStatusText.Text = exception.Message;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = LocalizationManager.Instance["Status.CheckingFeed"];
            await _viewModel.AddFeedAsync(FeedAddressTextBox.Text, FeedNameTextBox.Text, FolderComboBox.SelectedItem as string);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private sealed class CatalogFeed
    {
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
    }
}
