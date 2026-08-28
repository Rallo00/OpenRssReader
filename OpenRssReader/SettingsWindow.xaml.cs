using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class SettingsWindow : Window
{
    private static readonly IReadOnlyList<string> WorldLanguages = CultureInfo
        .GetCultures(CultureTypes.NeutralCultures)
        .Select(culture => culture.EnglishName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private readonly MainViewModel _viewModel;
    private bool _isInitializing;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _isInitializing = true;
        FeedlyTokenTextBox.Text = _viewModel.FeedlyAccessToken;
        ArticleRetentionDaysInput.Text = _viewModel.ArticleRetentionDays.ToString();
        AutoRefreshIntervalInput.Text = _viewModel.AutoRefreshIntervalMinutes.ToString();
        MarkAsReadDelayInput.Text = _viewModel.MarkAsReadDelaySeconds.ToString();
        var readingFonts = new[] { "Arial", "Cambria", "Georgia", "Lucida Sans Unicode", "Verdana", "Segoe UI" };
        ReadingFontFamilyComboBox.ItemsSource = readingFonts;
        ReadingTitleFontFamilyComboBox.ItemsSource = readingFonts;
        ReadingFontFamilyComboBox.SelectedItem = _viewModel.ReadingFontFamily;
        ReadingTitleFontFamilyComboBox.SelectedItem = _viewModel.ReadingTitleFontFamily;
        ReadingFontSizeTextBox.Text = _viewModel.ReadingFontSize.ToString();
        ReadingTitleFontSizeTextBox.Text = _viewModel.ReadingTitleFontSize.ToString();
        TranslateLanguageComboBox.ItemsSource = WorldLanguages;
        TranslateLanguageComboBox.SelectedItem = _viewModel.TranslationTargetLanguage;
        var installedVoices = _viewModel.GetInstalledTextToSpeechVoices();
        TextToSpeechVoiceComboBox.ItemsSource = installedVoices;
        TextToSpeechVoiceComboBox.SelectedValue = _viewModel.TextToSpeechVoiceId;
        if (TextToSpeechVoiceComboBox.SelectedIndex < 0 && installedVoices.Count > 0)
        {
            TextToSpeechVoiceComboBox.SelectedIndex = 0;
        }
        SelectComboBoxItem(UnreadSortComboBox, _viewModel.UnreadSortOrder);
        SelectComboBoxItem(GroupByComboBox, _viewModel.GroupBy);
        SelectComboBoxItem(ThemeComboBox, _viewModel.Appearance);
        DisplaySourceFaviconsCheckBox.IsChecked = _viewModel.DisplaySourceFavicons;
        ShowAllArticlesListCheckBox.IsChecked = _viewModel.ShowAllArticlesList;
        ShowSavedListCheckBox.IsChecked = _viewModel.ShowSavedList;
        ShowUnreadListCheckBox.IsChecked = _viewModel.ShowUnreadList;
        _isInitializing = false;
    }

    private async void ReadingTypographyChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || ReadingFontFamilyComboBox.SelectedItem is not string fontFamily ||
            ReadingTitleFontFamilyComboBox.SelectedItem is not string titleFontFamily ||
            !int.TryParse(ReadingFontSizeTextBox.Text, out var fontSize) ||
            !int.TryParse(ReadingTitleFontSizeTextBox.Text, out var titleFontSize))
        {
            return;
        }

        try
        {
            await _viewModel.SetReadingTypographyAsync(fontFamily, titleFontFamily, fontSize, titleFontSize);
            ReadingStatusText.Text = "Applied to the reading panel.";
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
    }

    private async void TextToSpeechVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        try
        {
            await _viewModel.SetTextToSpeechVoiceAsync(TextToSpeechVoiceComboBox.SelectedValue as string);
            ReadingStatusText.Text = "Microsoft SAPI voice applied.";
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
    }

    private void TextToSpeechVoiceComboBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            Dispatcher.BeginInvoke(
                () => comboBox.IsDropDownOpen = true,
                DispatcherPriority.Input);
        }
    }

    private async void TranslationLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || TranslateLanguageComboBox.SelectedItem is not string language)
        {
            return;
        }

        try
        {
            await _viewModel.SetTranslationTargetLanguageAsync(language);
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
    }

    private async void SaveRetentionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ArticleRetentionDaysInput.Text, out var days))
        {
            GeneralStatusText.Text = "Enter a valid number of days.";
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
        if (!int.TryParse(ArticleRetentionDaysInput.Text, out var retentionDays) ||
            !int.TryParse(AutoRefreshIntervalInput.Text, out var autoRefreshInterval) ||
            !int.TryParse(MarkAsReadDelayInput.Text, out var markAsReadDelay))
        {
            GeneralStatusText.Text = "Enter valid numeric values.";
            return;
        }

        try
        {
            await _viewModel.SetArticleRetentionDaysAsync(retentionDays);
            await _viewModel.SetGeneralPreferencesAsync(
                SelectedText(UnreadSortComboBox),
                SelectedText(GroupByComboBox),
                SelectedText(ThemeComboBox),
                autoRefreshInterval,
                markAsReadDelay,
                DisplaySourceFaviconsCheckBox.IsChecked == true,
                ShowAllArticlesListCheckBox.IsChecked == true,
                ShowSavedListCheckBox.IsChecked == true,
                ShowUnreadListCheckBox.IsChecked == true);
            GeneralStatusText.Text = "Preferences saved.";
            DialogResult = true;
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

}
