using Avalonia.Controls;
using Avalonia.Controls.Templates;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.Views;
using XamlPlayground.Views.Docking;

namespace XamlPlayground;

public partial class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is MainViewModel)
        {
            return new MainView();
        }

        return data switch
        {
            WorkspaceFileDocumentDockViewModel => new WorkspaceFileEditorDockView(),
            SolutionExplorerDockViewModel => new SolutionExplorerDockView(),
            VisualStructureDockViewModel => new VisualStructureDockView(),
            VisualPropertiesDockViewModel => new VisualPropertiesDockView(),
            VisualToolboxDockViewModel => new VisualToolboxDockView(),
            VisualAnimationsDockViewModel => new AnimationTimelineDockView(),
            AnimationTimelineSheetDockViewModel => new AnimationTimelineSheetDockView(),
            ControlThemesDockViewModel => new ControlThemesDockView(),
            XamlEditorDockViewModel => new XamlEditorDockView(),
            CodeEditorDockViewModel => new CodeEditorDockView(),
            PreviewDockViewModel => new PreviewDockView(),
            DiagnosticTreeDockViewModel => new DiagnosticsTreeDockView(),
            DiagnosticSegmentDockViewModel => new DiagnosticSegmentDockView(),
            DiagnosticToolDockViewModel => new DiagnosticsDockView(),
            ErrorsDockViewModel => new ErrorsDockView(),
            _ => new TextBlock { Text = "Not Found: " + data?.GetType().FullName }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase || data?.GetType().Namespace == typeof(XamlEditorDockViewModel).Namespace;
    }
}
