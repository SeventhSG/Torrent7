using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Torrent7.ViewModels;

/// <summary>One place in the sidebar: a filter of the torrent table, or a page.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string title, string icon, Filter? filter = null, Page? page = null, bool danger = false)
    {
        Title = title;
        Icon = Res.Get<Geometry>(icon);
        Filter = filter;
        Page = page;
        IsDanger = danger;
    }

    public string Title { get; }
    public Geometry? Icon { get; }
    public Filter? Filter { get; }
    public Page? Page { get; }
    public bool IsDanger { get; }

    [ObservableProperty] private int count;
    [ObservableProperty] private bool isShown = true;

    /// <summary>Empty when zero, so the eye only stops on lists that hold something.</summary>
    public string CountText => Count > 0 ? Count.ToString() : "";

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));
}
