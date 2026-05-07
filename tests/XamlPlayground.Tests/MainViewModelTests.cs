using Avalonia;
using Avalonia.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.CodeAnalysis;
using XamlPlayground.Views.Docking;
using XamlPlayground.Services;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.Workspace;
using XamlPlayground.Workspace;

namespace XamlPlayground.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void NewFileCommand_AddsUserControlToActiveProject()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var previousCount = viewModel.Samples.Count;
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        var previousFileCount = project.Files.Count;

        viewModel.NewFileCommand.Execute(null);

        Assert.Equal(previousCount, viewModel.Samples.Count);
        Assert.Equal(previousFileCount + 2, project.Files.Count);
        var userControl = project.FindFile("Views/UserControl1.axaml");
        var codeBehind = project.FindFile("Views/UserControl1.axaml.cs");
        Assert.NotNull(userControl);
        Assert.NotNull(codeBehind);
        Assert.Contains("<UserControl", userControl.Text, StringComparison.Ordinal);
        Assert.Contains("UserControl1", userControl.Text, StringComparison.Ordinal);
        Assert.Contains("partial class UserControl1", codeBehind.Text, StringComparison.Ordinal);
        Assert.Same(userControl, viewModel.ActiveXamlFile);
    }

    [Fact]
    public void RuntimeXamlLoader_DiagnosticHandler_ReportsRuntimeXamlDiagnostics()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var xaml = """
                   <UserControl xmlns="https://github.com/avaloniaui">
                     <Button Name="test" Content="Click Me" />
                     <Button Name="test" Content="Click Me" />
                   </UserControl>
                   """;
        var diagnostics = new List<RuntimeXamlDiagnostic>();

        var exception = Assert.ThrowsAny<Exception>(() => AvaloniaRuntimeXamlLoader.Load(
            new RuntimeXamlLoaderDocument(xaml) { Document = "Main.axaml" },
            new RuntimeXamlLoaderConfiguration
            {
                LocalAssembly = typeof(App).Assembly,
                DiagnosticHandler = diagnostic =>
                {
                    diagnostics.Add(diagnostic);
                    return diagnostic.Severity;
                }
            }));

        Assert.Contains("multiple assignments", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == RuntimeXamlDiagnosticSeverity.Error &&
            diagnostic.Title.Contains("multiple assignments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompilerService_ReturnsCSharpCompilerDiagnostics()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var code = "public class SampleView { public void Broken( { } }";

        var result = await CompilerService.GetScriptAssembly(code);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void DockLayout_CreatesExpectedWorkspacePanes()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);

        var root = Assert.IsAssignableFrom<IRootDock>(viewModel.DockLayout);
        Assert.NotNull(viewModel.DockFactory);
        Assert.Equal(DockFloatingWindowHostMode.Default, root.FloatingWindowHostMode);

        var dockables = Enumerate(root).ToList();
        var documents = dockables.OfType<WorkspaceFileDocumentDockViewModel>().ToList();
        var solutionExplorer = Assert.Single(dockables.OfType<SolutionExplorerDockViewModel>());
        var preview = Assert.Single(dockables.OfType<PreviewDockViewModel>());
        var diagnosticTreeTools = dockables.OfType<DiagnosticTreeDockViewModel>().ToList();
        var diagnosticTools = dockables.OfType<DiagnosticToolDockViewModel>().ToList();
        var errors = Assert.Single(dockables.OfType<ErrorsDockViewModel>());

        Assert.Collection(
            documents,
            document =>
            {
                Assert.Same(viewModel, document.Shell);
                Assert.Equal("Main.axaml", document.File.Path);
            },
            document =>
            {
                Assert.Same(viewModel, document.Shell);
                Assert.Equal("Main.axaml.cs", document.File.Path);
            });
        Assert.Same(viewModel, solutionExplorer.Shell);
        Assert.Same(viewModel, preview.Shell);
        Assert.All(diagnosticTreeTools, diagnosticTool => Assert.Same(viewModel, diagnosticTool.Shell));
        Assert.All(diagnosticTools, diagnosticTool => Assert.Same(viewModel, diagnosticTool.Shell));
        Assert.Same(viewModel, errors.Shell);
        Assert.Collection(
            diagnosticTreeTools,
            diagnosticTool => AssertDiagnosticTreeTool(diagnosticTool, "DiagnosticsCombinedTree", "Combined Tree", DevToolsViewKind.CombinedTree),
            diagnosticTool => AssertDiagnosticTreeTool(diagnosticTool, "DiagnosticsLogicalTree", "Logical Tree", DevToolsViewKind.LogicalTree),
            diagnosticTool => AssertDiagnosticTreeTool(diagnosticTool, "DiagnosticsVisualTree", "Visual Tree", DevToolsViewKind.VisualTree));
        Assert.Collection(
            diagnosticTools,
            diagnosticTool => AssertDiagnosticTool(diagnosticTool, "DiagnosticsEvents", "Events", DevToolsViewKind.Events),
            diagnosticTool => AssertDiagnosticTool(diagnosticTool, "DiagnosticsResources", "Resources", DevToolsViewKind.Resources),
            diagnosticTool => AssertDiagnosticTool(diagnosticTool, "DiagnosticsAssets", "Assets", DevToolsViewKind.Assets));
        Assert.All(diagnosticTreeTools, AssertDiagnosticTreeSegments);

        var factory = Assert.IsType<PlaygroundDockFactory>(viewModel.DockFactory);
        Assert.NotNull(factory.ContextLocator);
        var contextLocator = factory.ContextLocator;
        Assert.Same(solutionExplorer, contextLocator["SolutionExplorer"]());
        Assert.Same(preview, contextLocator["Preview"]());
        Assert.Same(diagnosticTreeTools[0], contextLocator["DiagnosticsCombinedTree"]());
        Assert.Same(diagnosticTreeTools[1], contextLocator["DiagnosticsLogicalTree"]());
        Assert.Same(diagnosticTreeTools[2], contextLocator["DiagnosticsVisualTree"]());
        Assert.Same(diagnosticTools[0], contextLocator["DiagnosticsEvents"]());
        Assert.Same(diagnosticTools[1], contextLocator["DiagnosticsResources"]());
        Assert.Same(diagnosticTools[2], contextLocator["DiagnosticsAssets"]());
        Assert.Same(errors, contextLocator["Errors"]());
    }

    [Fact]
    public void NewProjectWizard_CreatesBrowserSafeAvaloniaProjectStructure()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ShowNewProjectWizardCommand.Execute(null);

        Assert.True(viewModel.NewProjectWizard.IsOpen);
        viewModel.NewProjectWizard.SolutionName = "BrowserApp";
        viewModel.NewProjectWizard.SelectedTemplate = Assert.Single(
            viewModel.NewProjectWizard.Templates,
            template => template.ShortName == "avalonia.xplat");

        viewModel.CreateProjectCommand.Execute(null);

        Assert.False(viewModel.NewProjectWizard.IsOpen);
        var solution = viewModel.Solution;
        Assert.NotNull(solution);
        var project = Assert.Single(solution.Projects);
        Assert.Equal("BrowserApp", solution.Name);
        Assert.Equal("avalonia.xplat", project.TemplateShortName);
        Assert.NotNull(project.FindFile("BrowserApp.csproj"));
        Assert.NotNull(project.FindFile("App.axaml"));
        Assert.NotNull(project.FindFile("App.axaml.cs"));
        Assert.NotNull(project.FindFile("Views/MainView.axaml"));
        Assert.NotNull(project.FindFile("Views/MainView.axaml.cs"));
        Assert.NotNull(project.FindFile("Styles/Resources.axaml"));
        Assert.Equal("Views/MainView.axaml", viewModel.ActiveXamlFile?.Path);
        Assert.Equal("Views/MainView.axaml.cs", viewModel.ActiveCodeFile?.Path);
    }

    [Fact]
    public void SolutionExplorerNodeCommand_OpensEditableFileInDocumentDock()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var root = Assert.IsAssignableFrom<IRootDock>(viewModel.DockLayout);
        var resourceNode = FindNode(viewModel.SolutionExplorerNodes, "Resources.axaml");
        Assert.NotNull(resourceNode);

        resourceNode.OpenCommand.Execute(null);

        Assert.Equal("Styles/Resources.axaml", viewModel.ActiveXamlFile?.Path);
        Assert.Contains(
            Enumerate(root).OfType<WorkspaceFileDocumentDockViewModel>(),
            document => document.File.Path == "Styles/Resources.axaml");
    }

    [Fact]
    public void ToggleThemeCommand_SwitchesApplicationThemeVariant()
    {
        TestApplication.EnsureAvaloniaInitialized();
        Assert.NotNull(Application.Current);

        SetRequestedThemeVariant(ThemeVariant.Light);

        try
        {
            var viewModel = new MainViewModel(null);

            Assert.False(viewModel.IsDarkTheme);
            Assert.True(viewModel.IsLightTheme);
            Assert.Equal("Switch to dark theme", viewModel.ThemeToggleToolTip);

            viewModel.ToggleThemeCommand.Execute(null);

            Assert.True(viewModel.IsDarkTheme);
            Assert.False(viewModel.IsLightTheme);
            Assert.Equal("Switch to light theme", viewModel.ThemeToggleToolTip);
            Assert.Equal(ThemeVariant.Dark, GetRequestedThemeVariant());

            viewModel.ToggleThemeCommand.Execute(null);

            Assert.False(viewModel.IsDarkTheme);
            Assert.True(viewModel.IsLightTheme);
            Assert.Equal(ThemeVariant.Light, GetRequestedThemeVariant());
        }
        finally
        {
            SetRequestedThemeVariant(ThemeVariant.Light);
        }
    }

    [Fact]
    public void DockControl_SwitchesRenderedDocumentAndToolContent()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureDockTestApplicationResources();

            var viewModel = new MainViewModel(null);
            var root = Assert.IsAssignableFrom<IRootDock>(viewModel.DockLayout);
            var factory = Assert.IsAssignableFrom<IFactory>(viewModel.DockFactory);
            var editorDock = Assert.Single(Enumerate(root).OfType<IDocumentDock>(), dock => dock.Id == "Editors");
            var bottomDock = Assert.Single(Enumerate(root).OfType<IToolDock>(), dock => dock.Id == "Bottom");
            var codeDocument = Assert.Single(
                Enumerate(root).OfType<WorkspaceFileDocumentDockViewModel>(),
                document => document.File.Path == "Main.axaml.cs");
            var errors = Assert.Single(Enumerate(root).OfType<ErrorsDockViewModel>());

            var dockControl = new DockControl
            {
                Layout = root,
                Factory = factory,
                InitializeFactory = false,
                InitializeLayout = false
            };
            ControlRecyclingDataTemplate.SetControlRecycling(
                dockControl,
                new ControlRecycling { TryToUseIdAsKey = true });

            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = dockControl
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var xamlView = Assert.Single(dockControl.GetVisualDescendants().OfType<WorkspaceFileEditorDockView>());
                var xamlTextEditor = Assert.Single(xamlView.GetVisualDescendants().OfType<TextEditor>());
                Assert.Same(viewModel.ActiveXamlFile!.Document, xamlTextEditor.Document);

                editorDock.ActiveDockable = codeDocument;
                PumpLayout(window);

                var codeView = Assert.Single(dockControl.GetVisualDescendants().OfType<WorkspaceFileEditorDockView>());
                var codeTextEditor = Assert.Single(codeView.GetVisualDescendants().OfType<TextEditor>());
                Assert.Same(codeDocument.File.Document, codeTextEditor.Document);

                viewModel.LastErrorMessage = "Broken sample";
                PumpLayout(window);

                Assert.Same(errors, bottomDock.ActiveDockable);
                var errorsView = Assert.Single(dockControl.GetVisualDescendants().OfType<ErrorsDockView>());
                var errorTextBox = Assert.Single(errorsView.GetVisualDescendants().OfType<TextBox>());
                Assert.Equal("Broken sample", errorTextBox.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ThemeVariant GetRequestedThemeVariant()
    {
        return Dispatcher.UIThread.Invoke(() => Application.Current!.RequestedThemeVariant!);
    }

    private static void SetRequestedThemeVariant(ThemeVariant themeVariant)
    {
        Dispatcher.UIThread.Invoke(() => Application.Current!.RequestedThemeVariant = themeVariant);
    }

    private static void EnsureDockTestApplicationResources()
    {
        var application = Application.Current!;
        if (!application.DataTemplates.OfType<ViewLocator>().Any())
        {
            application.DataTemplates.Insert(0, new ViewLocator());
        }

        if (!application.Styles.OfType<DockFluentTheme>().Any())
        {
            application.Styles.Add(new DockFluentTheme { DensityStyle = DockDensityStyle.Compact });
        }
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertDiagnosticTreeTool(
        DiagnosticTreeDockViewModel diagnosticTool,
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        Assert.Equal(id, diagnosticTool.Id);
        Assert.Equal(title, diagnosticTool.Title);
        Assert.Equal(viewKind, diagnosticTool.ViewKind);
        Assert.NotNull(diagnosticTool.DockFactory);
        Assert.NotNull(diagnosticTool.DockLayout);
        Assert.NotNull(diagnosticTool.Session);
    }

    private static void AssertDiagnosticTool(
        DiagnosticToolDockViewModel diagnosticTool,
        string id,
        string title,
        DevToolsViewKind viewKind)
    {
        Assert.Equal(id, diagnosticTool.Id);
        Assert.Equal(title, diagnosticTool.Title);
        Assert.Equal(viewKind, diagnosticTool.ViewKind);
    }

    private static void AssertDiagnosticTreeSegments(DiagnosticTreeDockViewModel diagnosticTool)
    {
        var segments = Enumerate(diagnosticTool.DockLayout)
            .OfType<DiagnosticSegmentDockViewModel>()
            .ToList();

        Assert.Collection(
            segments,
            segment => AssertDiagnosticSegment(segment, $"{diagnosticTool.Id}Tree", "Tree", diagnosticTool.ViewKind, DevToolsTreeSegmentKind.Tree, diagnosticTool.Session),
            segment => AssertDiagnosticSegment(segment, $"{diagnosticTool.Id}Properties", "Properties", diagnosticTool.ViewKind, DevToolsTreeSegmentKind.Properties, diagnosticTool.Session),
            segment => AssertDiagnosticSegment(segment, $"{diagnosticTool.Id}LayoutStyles", "Layout / Styles", diagnosticTool.ViewKind, DevToolsTreeSegmentKind.LayoutStyles, diagnosticTool.Session));
    }

    private static void AssertDiagnosticSegment(
        DiagnosticSegmentDockViewModel diagnosticSegment,
        string id,
        string title,
        DevToolsViewKind viewKind,
        DevToolsTreeSegmentKind segmentKind,
        DevToolsSession session)
    {
        Assert.Equal(id, diagnosticSegment.Id);
        Assert.Equal(title, diagnosticSegment.Title);
        Assert.Equal(viewKind, diagnosticSegment.ViewKind);
        Assert.Equal(segmentKind, diagnosticSegment.SegmentKind);
        Assert.Same(session, diagnosticSegment.Session);
    }

    private static IEnumerable<IDockable> Enumerate(IDockable dockable)
    {
        yield return dockable;

        if (dockable is IDock dock && dock.VisibleDockables is { } visibleDockables)
        {
            foreach (var child in visibleDockables.SelectMany(Enumerate))
            {
                yield return child;
            }
        }
    }

    private static SolutionExplorerNodeViewModel? FindNode(
        IEnumerable<SolutionExplorerNodeViewModel> nodes,
        string title)
    {
        foreach (var node in nodes)
        {
            if (node.Title == title)
            {
                return node;
            }

            if (FindNode(node.Children, title) is { } child)
            {
                return child;
            }
        }

        return null;
    }
}
