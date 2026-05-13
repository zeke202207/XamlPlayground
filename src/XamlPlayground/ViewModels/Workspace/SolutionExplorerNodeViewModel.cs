using System;
using System.Collections.ObjectModel;
using System.Text;
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
        OpenCommand = openCommand ?? new RelayCommand(static () => { }, static () => false);
        ExpandCommand = new RelayCommand(() => IsExpanded = true);
        CollapseCommand = new RelayCommand(() => IsExpanded = false);
        ExpandRecursiveCommand = new RelayCommand(() => SetExpandedRecursive(true));
        CollapseRecursiveCommand = new RelayCommand(() => SetExpandedRecursive(false));
        SearchText = CreateSearchText(title, project, file);
        NormalizedSearchText = SearchText.ToLowerInvariant();
        _isExpanded = kind is ProjectFileKind.Solution or ProjectFileKind.Project or ProjectFileKind.Folder;
    }

    public string Title { get; }

    public ProjectFileKind Kind { get; }

    public InMemoryProject? Project { get; }

    public InMemoryProjectFile? File { get; }

    public ObservableCollection<SolutionExplorerNodeViewModel> Children { get; } = new();

    public ICommand OpenCommand { get; }

    public ICommand ExpandCommand { get; }

    public ICommand CollapseCommand { get; }

    public ICommand ExpandRecursiveCommand { get; }

    public ICommand CollapseRecursiveCommand { get; }

    public string SearchText { get; }

    public string NormalizedSearchText { get; }

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

    public bool MatchesLiteralSearch(string normalizedSearchText)
    {
        return NormalizedSearchText.Contains(normalizedSearchText, StringComparison.Ordinal);
    }

    public SolutionExplorerNodeViewModel CloneShallow()
    {
        return new SolutionExplorerNodeViewModel(Title, Kind, OpenCommand, Project, File)
        {
            IsExpanded = IsExpanded
        };
    }

    public void SetExpandedRecursive(bool isExpanded)
    {
        IsExpanded = isExpanded;

        foreach (var child in Children)
        {
            child.SetExpandedRecursive(isExpanded);
        }
    }

    private static string CreateSearchText(
        string title,
        InMemoryProject? project,
        InMemoryProjectFile? file)
    {
        var builder = new StringBuilder(title);

        if (project is { })
        {
            builder.Append(' ');
            builder.Append(project.Name);
            builder.Append(' ');
            builder.Append(project.RootNamespace);
            builder.Append(' ');
            builder.Append(project.TemplateShortName);
        }

        if (file is { })
        {
            builder.Append(' ');
            builder.Append(file.Path);
            builder.Append(' ');
            builder.Append(file.Kind);
        }

        return builder.ToString();
    }
}
