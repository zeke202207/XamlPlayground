using System;
using System.Collections.Generic;
using Avalonia.Diagnostics;
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
    private XamlEditorDockViewModel? _xamlEditor;
    private CodeEditorDockViewModel? _codeEditor;
    private PreviewDockViewModel? _preview;
    private DiagnosticToolDockViewModel? _combinedTree;
    private DiagnosticToolDockViewModel? _logicalTree;
    private DiagnosticToolDockViewModel? _visualTree;
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
        _xamlEditor = new XamlEditorDockViewModel(_shell);
        _codeEditor = new CodeEditorDockViewModel(_shell);
        _preview = new PreviewDockViewModel(_shell);
        _combinedTree = CreateDiagnosticsTool("DiagnosticsCombinedTree", "Combined Tree", DevToolsViewKind.CombinedTree);
        _logicalTree = CreateDiagnosticsTool("DiagnosticsLogicalTree", "Logical Tree", DevToolsViewKind.LogicalTree);
        _visualTree = CreateDiagnosticsTool("DiagnosticsVisualTree", "Visual Tree", DevToolsViewKind.VisualTree);
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
        editorDock.VisibleDockables = CreateList<IDockable>(_xamlEditor, _codeEditor);
        editorDock.ActiveDockable = _xamlEditor;

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
        previewDock.Proportion = 0.45;
        previewDock.CanCloseLastDockable = false;
        previewDock.VisibleDockables = CreateList<IDockable>(_preview);
        previewDock.ActiveDockable = _preview;

        var mainDock = CreateProportionalDock();
        mainDock.Id = "Workspace";
        mainDock.Title = "Workspace";
        mainDock.Orientation = Orientation.Horizontal;
        mainDock.IsCollapsable = false;
        mainDock.VisibleDockables = CreateList<IDockable>(
            centerDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            previewDock);
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
            ["XamlEditor"] = () => _xamlEditor,
            ["CodeEditor"] = () => _codeEditor,
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

    public void ActivateErrors()
    {
        if (_bottomDock is null || _errors is null)
        {
            return;
        }

        _bottomDock.ActiveDockable = _errors;
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
}
