using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenRssReader.Helpers;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IReadOnlyList<T> items)
    {
        if (this.SequenceEqual(items))
        {
            return;
        }

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
