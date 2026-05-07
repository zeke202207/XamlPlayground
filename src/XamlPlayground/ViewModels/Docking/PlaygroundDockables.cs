using System;
using System.ComponentModel;
using Avalonia.Diagnostics;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels.Docking;

public sealed class WorkspaceFileDocumentDockViewModel : Document
{
    public WorkspaceFileDocumentDockViewModel(MainViewModel shell, InMemoryProjectFile file)
    {
        Shell = shell;
        File = file;
        Id = $"Document:{file.Path}";
        Title = file.Name;
        CanClose = true;
        file.PropertyChanged += FileOnPropertyChanged;
    }

    public MainViewModel Shell { get; }

    public InMemoryProjectFile File { get; }

    public string Extension => File.Extension;

    public void Dispose()
    {
        File.PropertyChanged -= FileOnPropertyChanged;
    }

    private void FileOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InMemoryProjectFile.IsDirty))
        {
            Title = File.IsDirty ? $"{File.Name}*" : File.Name;
        }
    }
}

public sealed class XamlEditorDockViewModel : Document
{
    public XamlEditorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "XamlEditor";
        Title = "Xaml";
        CanClose = false;
    }

    public MainViewModel Shell { get; }
}

public sealed class CodeEditorDockViewModel : Document
{
    public CodeEditorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "CodeEditor";
        Title = "Code";
        CanClose = false;
    }

    public MainViewModel Shell { get; }
}

public sealed class PreviewDockViewModel : Tool
{
    public PreviewDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "Preview";
        Title = "Preview";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class SolutionExplorerDockViewModel : Tool
{
    public SolutionExplorerDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "SolutionExplorer";
        Title = "Solution Explorer";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class DiagnosticTreeDockViewModel : Tool, IDisposable
{
    public DiagnosticTreeDockViewModel(
        MainViewModel shell,
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        Shell = shell;
        Id = id;
        Title = title;
        ViewKind = viewKind;
        CanClose = false;
        KeepPinnedDockableVisible = true;
        Session = new DevToolsSession
        {
            Root = shell.DiagnosticsRoot,
            Options = CreateOptions()
        };

        var factory = new DiagnosticSegmentsDockFactory(this);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        DockFactory = factory;
        DockLayout = layout;

        shell.PropertyChanged += OnShellPropertyChanged;
    }

    public MainViewModel Shell { get; }

    public DevToolsViewKind ViewKind { get; }

    public DevToolsSession Session { get; }

    public IFactory DockFactory { get; }

    public IRootDock DockLayout { get; }

    public void Dispose()
    {
        Shell.PropertyChanged -= OnShellPropertyChanged;
        Session.Dispose();
    }

    private static DevToolsOptions CreateOptions()
    {
        return new DevToolsOptions
        {
            ShowMenu = false,
            ShowResourcesTab = false,
            ShowAssetsTab = false,
            ShowEventsTab = false,
            ScopeEventsToRoot = true
        };
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DiagnosticsRoot))
        {
            Session.Root = Shell.DiagnosticsRoot;
        }
    }
}

public sealed class DiagnosticSegmentDockViewModel : Tool
{
    public DiagnosticSegmentDockViewModel(
        DevToolsSession session,
        string id,
        string title,
        DevToolsViewKind viewKind,
        DevToolsTreeSegmentKind segmentKind)
    {
        Session = session;
        Id = id;
        Title = title;
        ViewKind = viewKind;
        SegmentKind = segmentKind;
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public DevToolsSession Session { get; }

    public DevToolsViewKind ViewKind { get; }

    public DevToolsTreeSegmentKind SegmentKind { get; }
}

public sealed class DiagnosticToolDockViewModel : Tool
{
    public DiagnosticToolDockViewModel(
        MainViewModel shell,
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        Shell = shell;
        Id = id;
        Title = title;
        ViewKind = viewKind;
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }

    public DevToolsViewKind ViewKind { get; }
}

public sealed class ErrorsDockViewModel : Tool
{
    public ErrorsDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "Errors";
        Title = "Errors";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}
