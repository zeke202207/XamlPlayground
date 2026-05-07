using Avalonia.Diagnostics;
using Dock.Model.Mvvm.Controls;

namespace XamlPlayground.ViewModels.Docking;

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
