using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels.Workspace;

public sealed partial class SolutionExplorerNodeViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isExpanded;

    public SolutionExplorerNodeViewModel(
        string title,
        ProjectFileKind kind,
        ICommand? openCommand = null,
        InMemoryProject? project = null,
        InMemoryProjectFile? file = null)
    {
        Title = title;
        Kind = kind;
        Project = project;
        File = file;
        OpenCommand = openCommand ?? new RelayCommand(() => { });
        _isExpanded = kind is ProjectFileKind.Solution or ProjectFileKind.Project or ProjectFileKind.Folder;
    }

    public string Title { get; }

    public ProjectFileKind Kind { get; }

    public InMemoryProject? Project { get; }

    public InMemoryProjectFile? File { get; }

    public ObservableCollection<SolutionExplorerNodeViewModel> Children { get; } = new();

    public ICommand OpenCommand { get; }

    public string Icon => Kind switch
    {
        ProjectFileKind.Solution => "\u25c7",
        ProjectFileKind.Project => "\u25a3",
        ProjectFileKind.Folder => "\u25b8",
        ProjectFileKind.Xaml => "X",
        ProjectFileKind.Resource => "R",
        ProjectFileKind.CSharp => "C#",
        ProjectFileKind.ProjectFile => "{}",
        _ => "\u2022"
    };
}
