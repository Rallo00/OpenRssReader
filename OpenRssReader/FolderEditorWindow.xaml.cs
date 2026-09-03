using System.Windows;
using OpenRssReader.Localization;

namespace OpenRssReader;

public partial class FolderEditorWindow : Window
{
    public FolderEditorWindow(string title, string folderName = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        FolderNameTextBox.Text = folderName;
        Loaded += (_, _) => FolderNameTextBox.Focus();
    }

    public string FolderName => FolderNameTextBox.Text.Trim();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderName))
        {
            StatusText.Text = LocalizationManager.Instance["Status.EnterFolderName"];
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
