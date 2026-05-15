using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XamlPlayground.ViewModels.Docking;

public sealed record DockToolDescriptor(string Id, string Title, DockToolRegion Region);

public sealed record DockPerspectiveDescriptor(string Id, string Title);

public enum DockToolRegion
{
    Left,
    Right,
    Preview,
    Bottom
}

public sealed partial class DockToolMenuItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isVisible;

    public DockToolMenuItemViewModel(DockToolDescriptor descriptor, ICommand command)
    {
        Id = descriptor.Id;
        Title = descriptor.Title;
        Command = command;
    }

    public string Id { get; }

    public string Title { get; }

    public ICommand Command { get; }

    public string Header => IsVisible ? $"{Title} (Shown)" : $"{Title} (Hidden)";

    partial void OnIsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(Header));
    }
}

public sealed partial class DockPerspectiveMenuItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCurrent;

    public DockPerspectiveMenuItemViewModel(DockPerspectiveDescriptor descriptor, ICommand command)
    {
        Id = descriptor.Id;
        Title = descriptor.Title;
        Command = command;
    }

    public string Id { get; }

    public string Title { get; }

    public ICommand Command { get; }

    public string Header => IsCurrent ? $"{Title} (Current)" : Title;

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(Header));
    }
}
