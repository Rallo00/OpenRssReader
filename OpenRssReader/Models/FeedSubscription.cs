using System.Windows.Media;
using OpenRssReader.Helpers;

namespace OpenRssReader.Models;

public sealed class FeedSubscription : ObservableObject
{
    private int _unreadCount;
    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _faviconUrl = string.Empty;

    public required string Id { get; init; }
    public required string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public required string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }
    public string GroupName { get; set; } = string.Empty;
    public required SolidColorBrush AccentBrush { get; init; }
    public string AccentHex { get; init; } = "#5A8FD8";
    public string FaviconUrl
    {
        get => _faviconUrl;
        set => SetProperty(ref _faviconUrl, value);
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (SetProperty(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(HasUnreadItems));
            }
        }
    }

    public bool HasUnreadItems => UnreadCount > 0;
}
