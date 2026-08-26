using System.Collections.ObjectModel;

namespace OpenRssReader.Models;

public sealed class FeedGroup
{
    public required string Name { get; init; }
    public bool IsRoot { get; init; }
    public ObservableCollection<FeedSubscription> Feeds { get; } = [];
}
