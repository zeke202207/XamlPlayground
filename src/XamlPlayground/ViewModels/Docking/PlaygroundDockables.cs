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
        shell.PropertyChanged += ShellOnPropertyChanged;
    }

    public MainViewModel Shell { get; }

    public InMemoryProjectFile File { get; }

    public string Extension => File.Extension;

    public string? VisualEditorSourceSelectionFilePath => Shell.VisualEditorSourceSelectionFilePath;

    public int VisualEditorSourceSelectionStart => Shell.VisualEditorSourceSelectionStart;

    public int VisualEditorSourceSelectionLength => Shell.VisualEditorSourceSelectionLength;

    public int VisualEditorSourceSelectionVersion => Shell.VisualEditorSourceSelectionVersion;

    public void Dispose()
    {
        File.PropertyChanged -= FileOnPropertyChanged;
        Shell.PropertyChanged -= ShellOnPropertyChanged;
    }

    private void FileOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InMemoryProjectFile.IsDirty))
        {
            Title = File.IsDirty ? $"{File.Name}*" : File.Name;
        }
    }

    private void ShellOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.VisualEditorSourceSelectionFilePath):
                OnPropertyChanged(nameof(VisualEditorSourceSelectionFilePath));
                break;
            case nameof(MainViewModel.VisualEditorSourceSelectionStart):
                OnPropertyChanged(nameof(VisualEditorSourceSelectionStart));
                break;
            case nameof(MainViewModel.VisualEditorSourceSelectionLength):
                OnPropertyChanged(nameof(VisualEditorSourceSelectionLength));
                break;
            case nameof(MainViewModel.VisualEditorSourceSelectionVersion):
                OnPropertyChanged(nameof(VisualEditorSourceSelectionVersion));
                break;
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

public class SolutionExplorerDockViewModel : Tool
{
    public SolutionExplorerDockViewModel(MainViewModel shell)
        : this(shell, "SolutionExplorer", "Solution Explorer")
    {
    }

    protected SolutionExplorerDockViewModel(MainViewModel shell, string id, string title)
    {
        Shell = shell;
        Id = id;
        Title = title;
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class MsBuildWorkspaceDockViewModel : SolutionExplorerDockViewModel
{
    public MsBuildWorkspaceDockViewModel(MainViewModel shell)
        : base(shell, "MsBuildWorkspace", "MSBuild Workspace")
    {
    }
}

public sealed class VisualStructureDockViewModel : Tool
{
    public VisualStructureDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "VisualStructure";
        Title = "Structure";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class VisualPropertiesDockViewModel : Tool
{
    public VisualPropertiesDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "VisualProperties";
        Title = "Properties";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class VisualToolboxDockViewModel : Tool
{
    public VisualToolboxDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "VisualToolbox";
        Title = "Toolbox";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class VisualAnimationsDockViewModel : Tool
{
    public VisualAnimationsDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "VisualAnimations";
        Title = "Animations";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class StylesInspectorDockViewModel : Tool
{
    public StylesInspectorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "StylesInspector";
        Title = "Styles";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class BindingsInspectorDockViewModel : Tool
{
    public BindingsInspectorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "BindingsInspector";
        Title = "Bindings";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class ResourcesInspectorDockViewModel : Tool
{
    public ResourcesInspectorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "ResourcesInspector";
        Title = "Resources";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class AnimationTimelineSheetDockViewModel : Tool
{
    public AnimationTimelineSheetDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "AnimationTimelineSheet";
        Title = "Timeline";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class StyleEditorDockViewModel : Tool
{
    public StyleEditorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "StyleEditor";
        Title = "Style Editor";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class BindingEditorDockViewModel : Tool
{
    public BindingEditorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "BindingEditor";
        Title = "Binding Editor";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class ResourceEditorDockViewModel : Tool
{
    public ResourceEditorDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "ResourceEditor";
        Title = "Resource Editor";
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class ControlThemesDockViewModel : Tool
{
    public ControlThemesDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "ControlThemes";
        Title = "Themes";
        CanClose = false;
        KeepPinnedDockableVisible = true;

        var factory = new ControlThemesDockFactory(this);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        DockFactory = factory;
        DockLayout = layout;
    }

    public MainViewModel Shell { get; }

    public IFactory DockFactory { get; }

    public IRootDock DockLayout { get; }
}

public abstract class ControlThemePanelDockViewModel : Tool
{
    protected ControlThemePanelDockViewModel(MainViewModel shell, string id, string title)
    {
        Shell = shell;
        Id = id;
        Title = title;
        CanClose = false;
        KeepPinnedDockableVisible = true;
    }

    public MainViewModel Shell { get; }
}

public sealed class ControlThemeCustomDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeCustomDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeCustom", "Custom")
    {
    }
}

public sealed class ControlThemeResourcesDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeResourcesDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeResources", "Resources")
    {
    }
}

public sealed class ControlThemeUsagesDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeUsagesDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeUsages", "Usages")
    {
    }
}

public sealed class ControlThemeDiagnosticsDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeDiagnosticsDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeDiagnostics", "Diagnostics")
    {
    }
}

public sealed class ControlThemeStatesDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeStatesDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeStates", "States")
    {
    }
}

public sealed class ControlThemeVariantsDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeVariantsDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeVariants", "Variants")
    {
    }
}

public sealed class ControlThemePartsDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemePartsDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeParts", "Parts")
    {
    }
}

public sealed class ControlThemeAnimationsDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeAnimationsDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeAnimations", "Animations")
    {
    }
}

public sealed class ControlThemeFluentDockViewModel : ControlThemePanelDockViewModel
{
    public ControlThemeFluentDockViewModel(MainViewModel shell)
        : base(shell, "ControlThemeFluent", "Fluent")
    {
    }
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

public sealed class ErrorsDockViewModel : Tool, IDisposable
{
    public ErrorsDockViewModel(MainViewModel shell)
    {
        Shell = shell;
        Id = "Errors";
        Title = "Errors";
        CanClose = false;
        KeepPinnedDockableVisible = true;
        shell.PropertyChanged += ShellOnPropertyChanged;
    }

    public MainViewModel Shell { get; }

    public string? LastErrorMessage => Shell.LastErrorMessage;

    public void NotifyLastErrorMessageChanged()
    {
        OnPropertyChanged(nameof(LastErrorMessage));
    }

    public void Dispose()
    {
        Shell.PropertyChanged -= ShellOnPropertyChanged;
    }

    private void ShellOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LastErrorMessage))
        {
            NotifyLastErrorMessageChanged();
        }
    }
}
