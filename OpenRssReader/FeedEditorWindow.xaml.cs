using System.Windows;
using OpenRssReader.Models;
using OpenRssReader.ViewModels;
using OpenRssReader.Localization;

namespace OpenRssReader;

public partial class FeedEditorWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FeedSubscription _feed;

    public FeedEditorWindow(MainViewModel viewModel, FeedSubscription feed)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _feed = feed;
        FeedNameTextBox.Text = feed.Name;
        FeedAddressTextBox.Text = feed.Url;
        FolderComboBox.ItemsSource = _viewModel.FolderNames;
        FolderComboBox.SelectedItem = _viewModel.FolderNames.FirstOrDefault(name => string.Equals(name, feed.GroupName, StringComparison.OrdinalIgnoreCase));
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = LocalizationManager.Instance["Status.SavingFeed"];
            await _viewModel.UpdateFeedAsync(_feed, FeedNameTextBox.Text, FeedAddressTextBox.Text, FolderComboBox.SelectedItem as string);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
