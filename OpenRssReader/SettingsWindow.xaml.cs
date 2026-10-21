using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenRssReader.Localization;
using OpenRssReader.Services;
using OpenRssReader.ViewModels;

namespace OpenRssReader;

public partial class SettingsWindow : Window
{
    private static readonly IReadOnlyDictionary<string, string> ApplicationLanguageCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Albanian"] = "sq", ["Basque"] = "eu", ["Belarusian"] = "be", ["Bosnian"] = "bs",
        ["Bulgarian"] = "bg", ["Catalan"] = "ca", ["Croatian"] = "hr", ["Czech"] = "cs",
        ["Danish"] = "da", ["Dutch"] = "nl", ["English"] = "en", ["Estonian"] = "et",
        ["Finnish"] = "fi", ["French"] = "fr", ["Galician"] = "gl", ["German"] = "de",
        ["Greek"] = "el", ["Hungarian"] = "hu", ["Icelandic"] = "is", ["Irish"] = "ga",
        ["Italian"] = "it", ["Latvian"] = "lv", ["Lithuanian"] = "lt", ["Luxembourgish"] = "lb",
        ["Macedonian"] = "mk", ["Maltese"] = "mt", ["Norwegian"] = "no", ["Polish"] = "pl",
        ["Portuguese"] = "pt", ["Romanian"] = "ro", ["Serbian"] = "sr", ["Slovak"] = "sk",
        ["Slovenian"] = "sl", ["Spanish"] = "es", ["Swedish"] = "sv", ["Ukrainian"] = "uk",
        ["Welsh"] = "cy"
    };

    private static readonly IReadOnlyList<string> WorldLanguages = CultureInfo
        .GetCultures(CultureTypes.NeutralCultures)
        .Select(culture => culture.EnglishName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private readonly MainViewModel _viewModel;
    private readonly GitHubUpdateService _updateService = new();
    private bool _isInitializing = true;
    private bool _isUpdatingSpeechControls;
    private bool _isInstallingKokoro;
    private readonly DispatcherTimer _readingSpeedTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        CurrentVersionText.Text = GitHubUpdateService.CurrentVersionLabel;
        _isInitializing = true;
        FreshRssServerUrlTextBox.Text = _viewModel.FreshRssServerUrl;
        FreshRssUsernameTextBox.Text = _viewModel.FreshRssUsername;
        FreshRssPasswordBox.Password = _viewModel.FreshRssPassword;
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
        PopulateKokoroVoices(_viewModel.TranslationTargetLanguage);
        SelectSpeechEngine(_viewModel.UseKokoroTextToSpeech);
        ReadingSpeedSlider.Value = _viewModel.ReadingSpeed;
        SelectComboBoxItem(UnreadSortComboBox, _viewModel.UnreadSortOrder);
        SelectComboBoxItem(GroupByComboBox, _viewModel.GroupBy);
        SelectComboBoxItem(ThemeComboBox, _viewModel.Appearance);
        SelectApplicationLanguage(_viewModel.ApplicationLanguage);
        DisplaySourceFaviconsCheckBox.IsChecked = _viewModel.DisplaySourceFavicons;
        ShowAllArticlesListCheckBox.IsChecked = _viewModel.ShowAllArticlesList;
        ShowSavedListCheckBox.IsChecked = _viewModel.ShowSavedList;
        ShowUnreadListCheckBox.IsChecked = _viewModel.ShowUnreadList;
        DisplayPodcastListCheckbox.IsChecked = _viewModel.ShowPodcastList;
        _isInitializing = false;
        _readingSpeedTimer.Tick += async (_, _) =>
        {
            _readingSpeedTimer.Stop();
            await SaveReadingSpeedAsync();
        };
        Closed += async (_, _) =>
        {
            _readingSpeedTimer.Stop();
            await SaveReadingSpeedAsync();
        };
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

    private void ReadingSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing)
        {
            return;
        }

        _readingSpeedTimer.Stop();
        _readingSpeedTimer.Start();
    }

    private async Task SaveReadingSpeedAsync()
    {
        try
        {
            await _viewModel.SetReadingSpeedAsync(ReadingSpeedSlider.Value);
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
    }

    private async void TextToSpeechVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isInstallingKokoro || _isUpdatingSpeechControls || SapiEngineRadioButton.IsChecked != true)
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

    private async void SpeechEngineChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isUpdatingSpeechControls || _isInstallingKokoro)
        {
            return;
        }

        try
        {
            if (KokoroEngineRadioButton.IsChecked == true)
            {
                var language = TranslateLanguageComboBox.SelectedItem as string ?? _viewModel.TranslationTargetLanguage;
                await ConfirmAndInstallKokoroAsync(language);
            }
            else
            {
                await _viewModel.SetKokoroTextToSpeechAsync(false);
                ReadingStatusText.Text = LocalizationManager.Instance["Settings.KokoroDisabledStatus"];
            }
        }
        catch (Exception exception)
        {
            KokoroStatusText.Text = exception.Message;
        }
        finally
        {
            SelectSpeechEngine(_viewModel.UseKokoroTextToSpeech);
        }
    }

    private async void KokoroVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isUpdatingSpeechControls || _isInstallingKokoro ||
            !_viewModel.UseKokoroTextToSpeech || KokoroVoiceComboBox.SelectedValue is not string voiceId)
        {
            return;
        }

        try
        {
            await _viewModel.SetKokoroTextToSpeechAsync(true, voiceId);
        }
        catch (Exception exception)
        {
            KokoroStatusText.Text = exception.Message;
        }
    }

    private async Task<bool> ConfirmAndInstallKokoroAsync(string language)
    {
        var localization = LocalizationManager.Instance;
        if (_viewModel.GetKokoroVoices(language).Count == 0)
        {
            MessageBox.Show(this, localization.Get("Settings.KokoroUnsupportedLanguage", language),
                localization["Settings.ConfigureReadingVoice"], MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var result = MessageBox.Show(this, localization.Get("Settings.KokoroDownloadConfirm", language),
            localization["Settings.ConfigureReadingVoice"], MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        _isInstallingKokoro = true;
        SapiEngineRadioButton.IsEnabled = false;
        KokoroEngineRadioButton.IsEnabled = false;
        TranslateLanguageComboBox.IsEnabled = false;
        KokoroVoiceComboBox.IsEnabled = false;
        TextToSpeechVoiceComboBox.IsEnabled = false;
        KokoroDownloadProgress.Value = 0;
        KokoroDownloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value =>
        {
            if (_isInstallingKokoro)
            {
                KokoroDownloadProgress.Value = value * 100;
                KokoroStatusText.Text = localization.Get("Settings.KokoroDownloading", $"{value:P0}");
            }
        });

        try
        {
            KokoroStatusText.Text = localization["Settings.KokoroPreparing"];
            await _viewModel.InstallKokoroAsync(language, progress);
            PopulateKokoroVoices(language);
            await _viewModel.SetKokoroTextToSpeechAsync(true, KokoroVoiceComboBox.SelectedValue as string);
            SelectSpeechEngine(true);
            KokoroStatusText.Text = localization["Settings.KokoroInstalled"];
            ReadingStatusText.Text = localization["Settings.KokoroEnabledStatus"];
            await Dispatcher.InvokeAsync(() => KokoroVoiceComboBox.BringIntoView(), DispatcherPriority.Loaded);
            return true;
        }
        finally
        {
            _isInstallingKokoro = false;
            SapiEngineRadioButton.IsEnabled = true;
            KokoroEngineRadioButton.IsEnabled = true;
            TranslateLanguageComboBox.IsEnabled = true;
            KokoroVoiceComboBox.IsEnabled = true;
            TextToSpeechVoiceComboBox.IsEnabled = true;
            KokoroDownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateKokoroVoices(string language)
    {
        _isUpdatingSpeechControls = true;
        try
        {
            var voices = _viewModel.GetKokoroVoices(language);
            KokoroVoiceComboBox.ItemsSource = voices;
            KokoroVoiceComboBox.SelectedItem = voices.FirstOrDefault(voice => voice.Id == _viewModel.KokoroVoiceId)
                ?? voices.FirstOrDefault();
        }
        finally
        {
            _isUpdatingSpeechControls = false;
        }
    }

    private void SelectSpeechEngine(bool useKokoro)
    {
        _isUpdatingSpeechControls = true;
        try
        {
            SapiEngineRadioButton.IsChecked = !useKokoro;
            KokoroEngineRadioButton.IsChecked = useKokoro;
        }
        finally
        {
            _isUpdatingSpeechControls = false;
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
        if (_isInitializing || _isInstallingKokoro || TranslateLanguageComboBox.SelectedItem is not string language)
        {
            return;
        }

        try
        {
            if (_viewModel.UseKokoroTextToSpeech && !await ConfirmAndInstallKokoroAsync(language))
            {
                return;
            }

            await _viewModel.SetTranslationTargetLanguageAsync(language);
            PopulateKokoroVoices(language);
        }
        catch (Exception exception)
        {
            ReadingStatusText.Text = exception.Message;
        }
        finally
        {
            _isInitializing = true;
            TranslateLanguageComboBox.SelectedItem = _viewModel.TranslationTargetLanguage;
            _isInitializing = false;
            SelectSpeechEngine(_viewModel.UseKokoroTextToSpeech);
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
            _readingSpeedTimer.Stop();
            await _viewModel.SetReadingSpeedAsync(ReadingSpeedSlider.Value);
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
                ShowUnreadListCheckBox.IsChecked == true,
                DisplayPodcastListCheckbox.IsChecked == true,
                SelectedApplicationLanguageCode());
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
            .FirstOrDefault(index =>
            {
                var item = (ComboBoxItem)comboBox.Items[index];
                return string.Equals(item.Tag?.ToString() ?? item.Content?.ToString(), text, StringComparison.Ordinal);
            });
    }

    private static string SelectedText(ComboBox comboBox)
    {
        var item = comboBox.SelectedItem as ComboBoxItem;
        return item?.Tag?.ToString() ?? item?.Content?.ToString() ?? string.Empty;
    }

    private void SelectApplicationLanguage(string languageCode)
    {
        var languageName = ApplicationLanguageCodes.FirstOrDefault(pair => string.Equals(pair.Value, languageCode, StringComparison.OrdinalIgnoreCase)).Key;
        SelectComboBoxItem(ApplicationLanguageComboBox, string.IsNullOrEmpty(languageName) ? "English" : languageName);
    }

    private string SelectedApplicationLanguageCode()
    {
        var languageName = SelectedText(ApplicationLanguageComboBox);
        return ApplicationLanguageCodes.TryGetValue(languageName, out var languageCode) ? languageCode : "en";
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AboutStatusText.Text = "Checking for updates...";
            var release = await _updateService.CheckForUpdateAsync();
            if (release is null)
            {
                AboutStatusText.Text = LocalizationManager.Instance["Settings.LatestVersion"];
                return;
            }

            var changelog = string.IsNullOrWhiteSpace(release.Changelog) ? "No changelog provided." : release.Changelog;
            var message = $"New application update available.\nCurrent version: {GitHubUpdateService.CurrentVersionLabel}\nNew version: {release.TagName}\n\nChangelog:\n{changelog}\n\nInstall this update now?";
            if (MessageBox.Show(message, "Open RSS Reader update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            {
                AboutStatusText.Text = "Update postponed.";
                return;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var backupPath = Path.Combine(desktop, $"OpenRssReader-update-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            AboutStatusText.Text = "Creating backup and downloading update...";
            await _viewModel.CreateBackupAsync(backupPath);
            var archivePath = await _updateService.DownloadAsync(release);
            _updateService.StartInstaller(archivePath, backupPath);
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            AboutStatusText.Text = $"Update check failed: {exception.Message}";
        }
    }

    private async void SaveFreshRssButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FreshRssStatusText.Text = "Saving FreshRSS settings...";
            await _viewModel.SetFreshRssConfigurationAsync(FreshRssServerUrlTextBox.Text, FreshRssUsernameTextBox.Text, FreshRssPasswordBox.Password);
            FreshRssStatusText.Text = "FreshRSS settings saved.";
        }
        catch (Exception exception)
        {
            FreshRssStatusText.Text = exception.Message;
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
