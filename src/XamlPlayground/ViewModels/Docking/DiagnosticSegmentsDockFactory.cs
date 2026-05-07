using System;
using System.Collections.Generic;
using Avalonia.Diagnostics;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;

namespace XamlPlayground.ViewModels.Docking;

public sealed class DiagnosticSegmentsDockFactory : Factory
{
    private readonly DiagnosticTreeDockViewModel _owner;
    private IRootDock? _rootDock;
    private DiagnosticSegmentDockViewModel? _tree;
    private DiagnosticSegmentDockViewModel? _properties;
    private DiagnosticSegmentDockViewModel? _layoutStyles;

    public DiagnosticSegmentsDockFactory(DiagnosticTreeDockViewModel owner)
    {
        _owner = owner;
    }

    public override IRootDock CreateLayout()
    {
        _tree = CreateSegment("Tree", "Tree", DevToolsTreeSegmentKind.Tree);
        _properties = CreateSegment("Properties", "Properties", DevToolsTreeSegmentKind.Properties);
        _layoutStyles = CreateSegment("LayoutStyles", "Layout / Styles", DevToolsTreeSegmentKind.LayoutStyles);

        var treeDock = CreateToolDock();
        treeDock.Id = $"{_owner.Id}TreeDock";
        treeDock.Title = "Tree";
        treeDock.Alignment = Alignment.Left;
        treeDock.Proportion = 0.35;
        treeDock.CanCloseLastDockable = false;
        treeDock.VisibleDockables = CreateList<IDockable>(_tree);
        treeDock.ActiveDockable = _tree;

        var propertiesDock = CreateToolDock();
        propertiesDock.Id = $"{_owner.Id}PropertiesDock";
        propertiesDock.Title = "Properties";
        propertiesDock.Alignment = Alignment.Left;
        propertiesDock.Proportion = 0.62;
        propertiesDock.CanCloseLastDockable = false;
        propertiesDock.VisibleDockables = CreateList<IDockable>(_properties);
        propertiesDock.ActiveDockable = _properties;

        var layoutStylesDock = CreateToolDock();
        layoutStylesDock.Id = $"{_owner.Id}LayoutStylesDock";
        layoutStylesDock.Title = "Layout / Styles";
        layoutStylesDock.Alignment = Alignment.Right;
        layoutStylesDock.Proportion = 0.38;
        layoutStylesDock.CanCloseLastDockable = false;
        layoutStylesDock.VisibleDockables = CreateList<IDockable>(_layoutStyles);
        layoutStylesDock.ActiveDockable = _layoutStyles;

        var rightDock = CreateProportionalDock();
        rightDock.Id = $"{_owner.Id}Details";
        rightDock.Title = "Details";
        rightDock.Orientation = Orientation.Horizontal;
        rightDock.IsCollapsable = false;
        rightDock.VisibleDockables = CreateList<IDockable>(
            propertiesDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            layoutStylesDock);
        rightDock.ActiveDockable = propertiesDock;

        var mainDock = CreateProportionalDock();
        mainDock.Id = $"{_owner.Id}Workspace";
        mainDock.Title = _owner.Title;
        mainDock.Orientation = Orientation.Horizontal;
        mainDock.IsCollapsable = false;
        mainDock.VisibleDockables = CreateList<IDockable>(
            treeDock,
            new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
            rightDock);
        mainDock.ActiveDockable = treeDock;

        var rootDock = CreateRootDock();
        rootDock.Id = $"{_owner.Id}Root";
        rootDock.Title = _owner.Title;
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
            window.Title = _owner.Title;
        }

        return window;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [_tree!.Id!] = () => _tree,
            [_properties!.Id!] = () => _properties,
            [_layoutStyles!.Id!] = () => _layoutStyles
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [$"{_owner.Id}Root"] = () => _rootDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = CreateHostWindow
        };

        base.InitLayout(layout);
    }

    private DiagnosticSegmentDockViewModel CreateSegment(
        string idSuffix,
        string title,
        DevToolsTreeSegmentKind segmentKind)
    {
        return new DiagnosticSegmentDockViewModel(
            _owner.Session,
            $"{_owner.Id}{idSuffix}",
            title,
            _owner.ViewKind,
            segmentKind);
    }

    private static IHostWindow CreateHostWindow()
    {
        return DockSettings.ResolveFloatingWindowHostMode() == DockFloatingWindowHostMode.Managed
            ? new ManagedHostWindow()
            : new HostWindow();
    }
}
