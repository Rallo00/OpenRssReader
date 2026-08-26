using System.Windows;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class SubscribeWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SubscribeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        FolderComboBox.ItemsSource = _viewModel.FolderNames;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Checking feed...";
            await _viewModel.AddFeedAsync(FeedAddressTextBox.Text, FeedNameTextBox.Text, FolderComboBox.SelectedItem as string);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }
}
