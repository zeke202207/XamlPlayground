using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Settings;

namespace XamlPlayground.ViewModels.Docking;

public sealed class ControlThemesDockFactory : Factory
{
    private readonly ControlThemesDockViewModel _owner;
    private IRootDock? _rootDock;
    private ControlThemeCustomDockViewModel? _custom;
    private ControlThemeResourcesDockViewModel? _resources;
    private ControlThemeUsagesDockViewModel? _usages;
    private ControlThemeDiagnosticsDockViewModel? _diagnostics;
    private ControlThemeStatesDockViewModel? _states;
    private ControlThemeVariantsDockViewModel? _variants;
    private ControlThemePartsDockViewModel? _parts;
    private ControlThemeFluentDockViewModel? _fluent;

    public ControlThemesDockFactory(ControlThemesDockViewModel owner)
    {
        _owner = owner;
    }

    public override IRootDock CreateLayout()
    {
        _custom = new ControlThemeCustomDockViewModel(_owner.Shell);
        _resources = new ControlThemeResourcesDockViewModel(_owner.Shell);
        _usages = new ControlThemeUsagesDockViewModel(_owner.Shell);
        _diagnostics = new ControlThemeDiagnosticsDockViewModel(_owner.Shell);
        _states = new ControlThemeStatesDockViewModel(_owner.Shell);
        _variants = new ControlThemeVariantsDockViewModel(_owner.Shell);
        _parts = new ControlThemePartsDockViewModel(_owner.Shell);
        _fluent = new ControlThemeFluentDockViewModel(_owner.Shell);

        var toolDock = CreateToolDock();
        toolDock.Id = "ControlThemeTools";
        toolDock.Title = "Theme Tools";
        toolDock.Alignment = Alignment.Left;
        toolDock.CanCloseLastDockable = false;
        toolDock.VisibleDockables = CreateList<IDockable>(
            _custom,
            _resources,
            _usages,
            _diagnostics,
            _states,
            _variants,
            _parts,
            _fluent);
        toolDock.ActiveDockable = _custom;

        var rootDock = CreateRootDock();
        rootDock.Id = "ControlThemesRoot";
        rootDock.Title = _owner.Title;
        rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = toolDock;
        rootDock.DefaultDockable = toolDock;
        rootDock.VisibleDockables = CreateList<IDockable>(toolDock);
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
            [_custom!.Id!] = () => _custom,
            [_resources!.Id!] = () => _resources,
            [_usages!.Id!] = () => _usages,
            [_diagnostics!.Id!] = () => _diagnostics,
            [_states!.Id!] = () => _states,
            [_variants!.Id!] = () => _variants,
            [_parts!.Id!] = () => _parts,
            [_fluent!.Id!] = () => _fluent
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["ControlThemesRoot"] = () => _rootDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = CreateHostWindow
        };

        base.InitLayout(layout);
    }

    private static IHostWindow CreateHostWindow()
    {
        return DockSettings.ResolveFloatingWindowHostMode() == DockFloatingWindowHostMode.Managed
            ? new ManagedHostWindow()
            : new HostWindow();
    }
}
