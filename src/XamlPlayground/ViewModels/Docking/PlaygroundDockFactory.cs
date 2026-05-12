using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Diagnostics;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using XamlPlayground;

namespace XamlPlayground.ViewModels.Docking;

public sealed class PlaygroundDockFactory : Factory
{
    private readonly MainViewModel _shell;
    private IRootDock? _rootDock;
    private IDocumentDock? _editorDock;
    private SolutionExplorerDockViewModel? _solutionExplorer;
    private VisualStructureDockViewModel? _visualStructure;
    private VisualPropertiesDockViewModel? _visualProperties;
    private VisualToolboxDockViewModel? _visualToolbox;
    private ControlThemesDockViewModel? _controlThemes;
    private PreviewDockViewModel? _preview;
    private DiagnosticTreeDockViewModel? _combinedTree;
    private DiagnosticTreeDockViewModel? _logicalTree;
    private DiagnosticTreeDockViewModel? _visualTree;
    private DiagnosticToolDockViewModel? _events;
    private DiagnosticToolDockViewModel? _resources;
    private DiagnosticToolDockViewModel? _assets;
    private ErrorsDockViewModel? _errors;
    private IToolDock? _bottomDock;

    public PlaygroundDockFactory(MainViewModel shell)
    {
        _shell = shell;
    }

    public override IRootDock CreateLayout()
    {
        _solutionExplorer = new SolutionExplorerDockViewModel(_shell);
        _visualStructure = new VisualStructureDockViewModel(_shell);
        _visualProperties = new VisualPropertiesDockViewModel(_shell);
        _visualToolbox = new VisualToolboxDockViewModel(_shell);
        _controlThemes = new ControlThemesDockViewModel(_shell);
        _preview = new PreviewDockViewModel(_shell);
        _combinedTree = CreateDiagnosticsTreeTool("DiagnosticsCombinedTree", "Combined Tree", DevToolsViewKind.CombinedTree);
        _logicalTree = CreateDiagnosticsTreeTool("DiagnosticsLogicalTree", "Logical Tree", DevToolsViewKind.LogicalTree);
        _visualTree = CreateDiagnosticsTreeTool("DiagnosticsVisualTree", "Visual Tree", DevToolsViewKind.VisualTree);
        _events = CreateDiagnosticsTool("DiagnosticsEvents", "Events", DevToolsViewKind.Events);
        _resources = CreateDiagnosticsTool("DiagnosticsResources", "Resources", DevToolsViewKind.Resources);
        _assets = CreateDiagnosticsTool("DiagnosticsAssets", "Assets", DevToolsViewKind.Assets);
        _errors = new ErrorsDockViewModel(_shell);

        var editorDock = CreateDocumentDock();
        editorDock.Id = "Editors";
        editorDock.Title = "Editors";
        editorDock.IsCollapsable = false;
        editorDock.CanCloseLastDockable = false;
        editorDock.EnableWindowDrag = true;
        editorDock.VisibleDockables = CreateList<IDockable>();
        if (editorDock is INotifyPropertyChanged notifyEditorDock)
        {
            notifyEditorDock.PropertyChanged += EditorDockOnPropertyChanged;
        }
        _editorDock = editorDock;

        var bottomDock = CreateToolDock();
        bottomDock.Id = "Bottom";
        bottomDock.Title = "Bottom";
        bottomDock.Alignment = Alignment.Bottom;
        bottomDock.Proportion = 0.28;
        bottomDock.CanCloseLastDockable = false;
        bottomDock.VisibleDockables = CreateList<IDockable>(
            _combinedTree,
            _logicalTree,
            _visualTree,
            _events,
            _resources,
            _assets,
            _errors);
        bottomDock.ActiveDockable = Utilities.IsBrowser() ? _errors : _combinedTree;
        _bottomDock = bottomDock;

        var centerDock = CreateProportionalDock();
        centerDock.Id = "Center";
        centerDock.Title = "Center";
        centerDock.Orientation = Orientation.Vertical;
        centerDock.IsCollapsable = false;
        centerDock.VisibleDockables = CreateList<IDockable>(
            editorDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            bottomDock);
        centerDock.ActiveDockable = editorDock;

        var previewDock = CreateToolDock();
        previewDock.Id = "PreviewDock";
        previewDock.Title = "Preview";
        previewDock.Alignment = Alignment.Right;
        previewDock.Proportion = 0.36;
        previewDock.CanCloseLastDockable = false;
        previewDock.VisibleDockables = CreateList<IDockable>(_preview);
        previewDock.ActiveDockable = _preview;

        var solutionDock = CreateToolDock();
        solutionDock.Id = "SolutionExplorerDock";
        solutionDock.Title = "Solution Explorer";
        solutionDock.Alignment = Alignment.Right;
        solutionDock.Proportion = 0.22;
        solutionDock.CanCloseLastDockable = false;
        solutionDock.VisibleDockables = CreateList<IDockable>(
            _solutionExplorer,
            _visualStructure,
            _visualProperties,
            _visualToolbox,
            _controlThemes);
        solutionDock.ActiveDockable = _solutionExplorer;

        var mainDock = CreateProportionalDock();
        mainDock.Id = "Workspace";
        mainDock.Title = "Workspace";
        mainDock.Orientation = Orientation.Horizontal;
        mainDock.IsCollapsable = false;
        mainDock.VisibleDockables = CreateList<IDockable>(
            centerDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            previewDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            solutionDock);
        mainDock.ActiveDockable = centerDock;

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.Title = "Xaml Playground";
        rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = mainDock;
        rootDock.DefaultDockable = mainDock;
        rootDock.VisibleDockables = CreateList<IDockable>(mainDock);
        rootDock.LeftPinnedDockables = CreateList<IDockable>();
        rootDock.RightPinnedDockables = CreateList<IDockable>();
        rootDock.TopPinnedDockables = CreateList<IDockable>();
        rootDock.BottomPinnedDockables = CreateList<IDockable>();
        rootDock.FloatingWindowHostMode = DockFloatingWindowHostMode.Default;

        _rootDock = rootDock;

        return rootDock;
    }

    public override IDockWindow? CreateWindowFrom(IDockable dockable)
    {
        var window = base.CreateWindowFrom(dockable);
        if (window is not null)
        {
            window.Title = "Xaml Playground";
        }

        return window;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["SolutionExplorer"] = () => _solutionExplorer,
            ["VisualStructure"] = () => _visualStructure,
            ["VisualProperties"] = () => _visualProperties,
            ["VisualToolbox"] = () => _visualToolbox,
            ["ControlThemes"] = () => _controlThemes,
            ["Preview"] = () => _preview,
            ["DiagnosticsCombinedTree"] = () => _combinedTree,
            ["DiagnosticsLogicalTree"] = () => _logicalTree,
            ["DiagnosticsVisualTree"] = () => _visualTree,
            ["DiagnosticsEvents"] = () => _events,
            ["DiagnosticsResources"] = () => _resources,
            ["DiagnosticsAssets"] = () => _assets,
            ["Errors"] = () => _errors
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = CreateHostWindow
        };

        base.InitLayout(layout);
    }

    public void ResetDocuments(IEnumerable<XamlPlayground.Workspace.InMemoryProjectFile> files)
    {
        if (_editorDock is null)
        {
            return;
        }

        var documents = files
            .Where(static file => file.CanEdit)
            .Take(2)
            .Select(CreateWorkspaceDocument)
            .Cast<IDockable>()
            .ToArray();

        _editorDock.VisibleDockables = CreateList<IDockable>(documents);
        _editorDock.ActiveDockable = documents.FirstOrDefault();

        if (_editorDock.ActiveDockable is WorkspaceFileDocumentDockViewModel document)
        {
            _shell.ActivateWorkspaceFileFromDocument(document.File);
        }
    }

    public void OpenDocument(XamlPlayground.Workspace.InMemoryProjectFile file)
    {
        if (_editorDock is null)
        {
            return;
        }

        var visibleDockables = _editorDock.VisibleDockables ?? CreateList<IDockable>();
        _editorDock.VisibleDockables = visibleDockables;

        var existing = visibleDockables
            .OfType<WorkspaceFileDocumentDockViewModel>()
            .FirstOrDefault(document => ReferenceEquals(document.File, file));

        if (existing is null)
        {
            existing = CreateWorkspaceDocument(file);
            visibleDockables.Add(existing);
        }

        _editorDock.ActiveDockable = existing;
        _shell.ActivateWorkspaceFileFromDocument(file);
    }

    public void ActivateErrors()
    {
        if (_bottomDock is null || _errors is null)
        {
            return;
        }

        _bottomDock.ActiveDockable = _errors;
        _errors.NotifyLastErrorMessageChanged();
        Dispatcher.UIThread.Post(_errors.NotifyLastErrorMessageChanged, DispatcherPriority.Loaded);
    }

    private static IHostWindow CreateHostWindow()
    {
        return DockSettings.ResolveFloatingWindowHostMode() == DockFloatingWindowHostMode.Managed
            ? new ManagedHostWindow()
            : new HostWindow();
    }

    private DiagnosticToolDockViewModel CreateDiagnosticsTool(
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        return new DiagnosticToolDockViewModel(_shell, id, title, viewKind);
    }

    private DiagnosticTreeDockViewModel CreateDiagnosticsTreeTool(
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        return new DiagnosticTreeDockViewModel(_shell, id, title, viewKind);
    }

    private WorkspaceFileDocumentDockViewModel CreateWorkspaceDocument(XamlPlayground.Workspace.InMemoryProjectFile file)
    {
        return new WorkspaceFileDocumentDockViewModel(_shell, file);
    }

    private void EditorDockOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IDocumentDock.ActiveDockable) ||
            _editorDock?.ActiveDockable is not WorkspaceFileDocumentDockViewModel document)
        {
            return;
        }

        _shell.ActivateWorkspaceFileFromDocument(document.File);
    }
}
