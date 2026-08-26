using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        FeedlyTokenTextBox.Text = _viewModel.FeedlyAccessToken;
        ArticleRetentionDaysTextBox.Text = _viewModel.ArticleRetentionDays.ToString();
        ReadingFontFamilyComboBox.ItemsSource = new[] { "Arial", "Cambria", "Georgia", "Lucida Sans Unicode", "Verdana", "Segoe UI" };
        ReadingFontFamilyComboBox.SelectedItem = _viewModel.ReadingFontFamily;
        ReadingFontSizeTextBox.Text = _viewModel.ReadingFontSize.ToString();
        ReadingTitleFontSizeTextBox.Text = _viewModel.ReadingTitleFontSize.ToString();
        SelectComboBoxItem(UnreadSortComboBox, _viewModel.UnreadSortOrder);
        SelectComboBoxItem(GroupByComboBox, _viewModel.GroupBy);
        SelectComboBoxItem(AppearanceComboBox, _viewModel.Appearance);
        DisplaySourceFaviconsCheckBox.IsChecked = _viewModel.DisplaySourceFavicons;
        ShowAllArticlesListCheckBox.IsChecked = _viewModel.ShowAllArticlesList;
        ShowSavedListCheckBox.IsChecked = _viewModel.ShowSavedList;
        ShowUnreadListCheckBox.IsChecked = _viewModel.ShowUnreadList;
        Loaded += (_, _) => ApplyActionButtonTheme();
    }

    private async void ReadingTypographyChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || ReadingFontFamilyComboBox.SelectedItem is not string fontFamily ||
            !int.TryParse(ReadingFontSizeTextBox.Text, out var fontSize) ||
            !int.TryParse(ReadingTitleFontSizeTextBox.Text, out var titleFontSize))
        {
            return;
        }

        try
        {
            await _viewModel.SetReadingTypographyAsync(fontFamily, fontSize, titleFontSize);
            ReadingStatusText.Text = "Applied to the reading panel.";
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
    }

    private async void SaveRetentionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ArticleRetentionDaysTextBox.Text, out var days))
        {
            GeneralStatusText.Text = "Enter a whole number of days.";
            return;
        }

        try
        {
            await _viewModel.SetArticleRetentionDaysAsync(days);
            GeneralStatusText.Text = "Retention policy saved.";
        }
        catch (Exception exception)
        {
            GeneralStatusText.Text = exception.Message;
        }
    }

    private async void SaveGeneralPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SetGeneralPreferencesAsync(
                SelectedText(UnreadSortComboBox),
                SelectedText(GroupByComboBox),
                SelectedText(AppearanceComboBox),
                DisplaySourceFaviconsCheckBox.IsChecked == true,
                ShowAllArticlesListCheckBox.IsChecked == true,
                ShowSavedListCheckBox.IsChecked == true,
                ShowUnreadListCheckBox.IsChecked == true);
            GeneralStatusText.Text = "Preferences saved.";
        }
        catch (Exception exception)
        {
            GeneralStatusText.Text = exception.Message;
        }
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string text)
    {
        comboBox.SelectedIndex = Enumerable.Range(0, comboBox.Items.Count)
            .FirstOrDefault(index => string.Equals(((ComboBoxItem)comboBox.Items[index]).Content?.ToString(), text, StringComparison.Ordinal));
    }

    private static string SelectedText(ComboBox comboBox) => (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

    private async void ConnectFeedlyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Connecting to Feedly...";
            await _viewModel.ConnectFeedlyAsync(FeedlyTokenTextBox.Text);
            StatusText.Text = "Feedly connected. You can now synchronize your library.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Connection failed: {exception.Message}";
        }
    }

    private async void SyncFeedlyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Synchronizing Feedly feeds...";
            var imported = await _viewModel.SyncFeedlyAsync();
            StatusText.Text = $"Synchronization complete: {imported} feeds added.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Synchronization failed: {exception.Message}";
        }
    }

    private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create backup",
            Filter = "Open RSS Reader backup (*.json)|*.json",
            FileName = $"OpenRssReader-backup-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            BackupStatusText.Text = "Creating backup...";
            await _viewModel.CreateBackupAsync(dialog.FileName);
            BackupStatusText.Text = "Backup created successfully.";
        }
        catch (Exception exception)
        {
            BackupStatusText.Text = exception.Message;
        }
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore backup",
            Filter = "Open RSS Reader backup (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (MessageBox.Show("Restore settings, folders, and feeds from this backup? Current articles will be replaced and feeds will be downloaded again.", "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            BackupStatusText.Text = "Restoring backup and downloading feeds...";
            await _viewModel.RestoreBackupAsync(dialog.FileName);
            BackupStatusText.Text = "Backup restored successfully.";
        }
        catch (Exception exception)
        {
            BackupStatusText.Text = exception.Message;
        }
    }

    private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all settings and delete all local feeds, folders, and articles? This cannot be undone.", "Initialize settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            BackupStatusText.Text = "Initializing settings...";
            await _viewModel.ResetToFactoryAsync();
            BackupStatusText.Text = "Settings have been initialized.";
        }
        catch (Exception exception)
        {
            BackupStatusText.Text = exception.Message;
        }
    }

    private void ApplyActionButtonTheme()
    {
        var style = (Style)FindResource("SettingsActionButtonStyle");
        foreach (var button in FindVisualChildren<Button>(this))
        {
            button.Style = style;
            button.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            button.SetResourceReference(Control.ForegroundProperty, "InputTextBrush");
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T item)
            {
                yield return item;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
