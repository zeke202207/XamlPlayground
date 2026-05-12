using System.Reflection;
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
using XamlPlayground.Services.Theming;
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
        viewModel.Control = new Button();

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
        Assert.Null(viewModel.Control);
    }

    [Fact]
    public async Task RuntimePreviewLoader_LoadsXClassUserControlWithCodeBehindRepeatedly()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var (projectName, codeFiles, userControlText, userControlName, userControlPath) = Dispatcher.UIThread.Invoke(() =>
        {
            var solutionFactory = new InMemorySolutionFactory(_ => { });
            var solution = solutionFactory.CreateSolution(
                "Demo",
                Assert.Single(AvaloniaProjectTemplates.All, template => template.ShortName == "avalonia.xplat"));
            var project = Assert.Single(solution.Projects);
            var userControlFile = solutionFactory.AddUserControl(project);

            return (
                project.Name,
                project.GetCSharpFileSnapshot(),
                userControlFile.Text,
                userControlFile.Name,
                userControlFile.Path);
        });

        var compileResult = await CompilerService.GetProjectAssembly(projectName, codeFiles);

        Assert.True(
            compileResult.Success,
            string.Join(Environment.NewLine, compileResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));

        Dispatcher.UIThread.Invoke(() =>
        {
            for (var i = 0; i < 2; i++)
            {
                var diagnostics = new List<RuntimeXamlDiagnostic>();
                var control = RuntimeXamlPreviewLoader.LoadControl(
                    userControlText,
                    compileResult.Assembly,
                    Path.GetFileNameWithoutExtension(userControlName),
                    userControlPath,
                    diagnostics);

                var userControl = Assert.IsAssignableFrom<UserControl>(control);
                Assert.Equal("Demo.Views.UserControl1", userControl.GetType().FullName);
                Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity >= RuntimeXamlDiagnosticSeverity.Error);
            }
        });
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
    public void RuntimePreview_InvalidSemanticXamlReportsErrorWithoutEditorFoldingCrash()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <ContentControl xmlns="https://github.com/avaloniaui">
                                               <TextBlock Text="One" />
                                               <TextBlock Text="Two" />
                                             </ContentControl>
                                             """;
            viewModel.ActiveProject = null;
            var dockable = new WorkspaceFileDocumentDockViewModel(viewModel, viewModel.ActiveXamlFile);
            var view = new WorkspaceFileEditorDockView
            {
                DataContext = dockable
            };
            var window = new Window
            {
                Width = 640,
                Height = 420,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var runTask = RunPreviewImmediately(viewModel!);
                Assert.True(runTask.IsCompleted);
                runTask.GetAwaiter().GetResult();
                PumpLayout(window!);
                PumpLayout(window!);

                Assert.Contains("multiple assignments", viewModel!.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window?.Close();
                dockable?.Dispose();
            }
        });
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
        var visualStructure = Assert.Single(dockables.OfType<VisualStructureDockViewModel>());
        var visualProperties = Assert.Single(dockables.OfType<VisualPropertiesDockViewModel>());
        var visualToolbox = Assert.Single(dockables.OfType<VisualToolboxDockViewModel>());
        var controlThemes = Assert.Single(dockables.OfType<ControlThemesDockViewModel>());
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
        Assert.Same(viewModel, visualStructure.Shell);
        Assert.Same(viewModel, visualProperties.Shell);
        Assert.Same(viewModel, visualToolbox.Shell);
        Assert.Same(viewModel, controlThemes.Shell);
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
        AssertControlThemeTools(controlThemes);
        Assert.All(diagnosticTreeTools, AssertDiagnosticTreeSegments);

        var factory = Assert.IsType<PlaygroundDockFactory>(viewModel.DockFactory);
        Assert.NotNull(factory.ContextLocator);
        var contextLocator = factory.ContextLocator;
        Assert.Same(solutionExplorer, contextLocator["SolutionExplorer"]());
        Assert.Same(visualStructure, contextLocator["VisualStructure"]());
        Assert.Same(visualProperties, contextLocator["VisualProperties"]());
        Assert.Same(visualToolbox, contextLocator["VisualToolbox"]());
        Assert.Same(controlThemes, contextLocator["ControlThemes"]());
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
    public void ControlThemesDockView_RendersNestedDockTools()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureDockTestApplicationResources();

            var viewModel = new MainViewModel(null);
            var root = Assert.IsAssignableFrom<IRootDock>(viewModel.DockLayout);
            var controlThemes = Assert.Single(Enumerate(root).OfType<ControlThemesDockViewModel>());
            var view = new ControlThemesDockView
            {
                Width = 360,
                Height = 640,
                DataContext = controlThemes
            };
            var window = new Window
            {
                Width = 380,
                Height = 680,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.Contains(
                    view.GetVisualDescendants().OfType<DockControl>(),
                    dockControl => ReferenceEquals(dockControl.Layout, controlThemes.DockLayout));
                Assert.Contains(
                    view.GetVisualDescendants().OfType<ListBox>(),
                    listBox => ReferenceEquals(listBox.ItemsSource, viewModel.FilteredControlThemes));
            }
            finally
            {
                window.Close();
            }
        });
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
    public void ControlThemeResourceBuilder_CreatesKeyedThemeResource()
    {
        var template = new FluentControlThemeTemplate(
            "{x:Type Button}",
            "Button",
            "Controls/Button.xaml",
            """
            <ControlTheme xmlns="https://github.com/avaloniaui"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:converters="clr-namespace:Sample.Converters"
                          x:Key="{x:Type Button}"
                          TargetType="Button">
              <Setter Property="Padding" Value="8" />
              <Setter Property="Tag" Value="{x:Static converters:ThemeConverter.Instance}" />
              <ControlTheme.Resources>
                <SolidColorBrush x:Key="NestedBrush" Color="Red" />
              </ControlTheme.Resources>
            </ControlTheme>
            """);

        var xaml = ControlThemeResourceBuilder.CreateResourceDictionary(template, "MyButtonTheme1");
        var themes = ControlThemeResourceBuilder.FindCustomThemes(new[] { ("Themes/MyButtonTheme1.axaml", xaml) });

        Assert.Contains("Design.PreviewWith", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MyButtonTheme1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:converters=\"clr-namespace:Sample.Converters\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NestedBrush\"", xaml, StringComparison.Ordinal);
        var theme = Assert.Single(themes);
        Assert.Equal("MyButtonTheme1", theme.Key);
        Assert.Equal("Button", theme.TargetType);
        Assert.Equal("Themes/MyButtonTheme1.axaml", theme.FilePath);
    }

    [Fact]
    public void ControlThemeResourceBuilder_CreatesPreviewWithoutContentForPlainControls()
    {
        var template = new FluentControlThemeTemplate(
            "{x:Type Calendar}",
            "Calendar",
            "Controls/Calendar.xaml",
            """
            <ControlTheme xmlns="https://github.com/avaloniaui"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          x:Key="{x:Type Calendar}"
                          TargetType="Calendar">
              <Setter Property="Padding" Value="8" />
            </ControlTheme>
            """);

        var xaml = ControlThemeResourceBuilder.CreateResourceDictionary(template, "MyCalendarTheme1");

        Assert.Contains("<Calendar Theme=\"{StaticResource MyCalendarTheme1}\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Calendar Theme=\"{StaticResource MyCalendarTheme1}\" Content=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeProjectStorage_RoundTripsThemeResourceFiles()
    {
        var json = ThemeProjectStorage.Save(
            "ThemeSet",
            new[]
            {
                ("Themes/MyButtonTheme1.axaml", "<ResourceDictionary />"),
                ("Themes\\MyTextBoxTheme1.axaml", "<ResourceDictionary />")
            });

        var document = ThemeProjectStorage.Load(json);

        Assert.Equal(ThemeProjectStorage.CurrentVersion, document.Version);
        Assert.Equal(ThemeProjectStorage.FormatName, document.Format);
        Assert.Equal("ThemeSet", document.Name);
        Assert.Contains(ThemeProjectStorage.BaseVariant, document.Variants);
        Assert.Collection(
            document.Files,
            file =>
            {
                Assert.Equal("Themes/MyButtonTheme1.axaml", file.Path);
                Assert.Equal("<ResourceDictionary />", file.Text);
                Assert.Equal(ThemeProjectStorage.ResourceFileKind, file.Kind);
                Assert.Equal(ThemeProjectStorage.BaseVariant, file.Variant);
            },
            file =>
            {
                Assert.Equal("Themes/MyTextBoxTheme1.axaml", file.Path);
                Assert.Equal("<ResourceDictionary />", file.Text);
                Assert.Equal(ThemeProjectStorage.ResourceFileKind, file.Kind);
                Assert.Equal(ThemeProjectStorage.BaseVariant, file.Variant);
            });
    }

    [Fact]
    public void ThemeProjectStorage_LoadsVersionOneAndInfersThemeVariants()
    {
        var json = """
                   {
                     "version": 1,
                     "name": "LegacyThemeSet",
                     "files": [
                       {
                         "path": "Themes/Palette.Light.axaml",
                         "text": "<ResourceDictionary />"
                       },
                       {
                         "path": "Themes/Palette.Dark.axaml",
                         "text": "<ResourceDictionary />"
                       }
                     ]
                   }
                   """;

        var document = ThemeProjectStorage.Load(json);

        Assert.Equal(ThemeProjectStorage.CurrentVersion, document.Version);
        Assert.Equal("LegacyThemeSet", document.Name);
        Assert.Contains("light", document.Variants);
        Assert.Contains("dark", document.Variants);
        Assert.Contains(document.Files, file => file.Path == "Themes/Palette.Light.axaml" && file.Variant == "light");
        Assert.Contains(document.Files, file => file.Path == "Themes/Palette.Dark.axaml" && file.Variant == "dark");
    }

    [Fact]
    public void InMemorySolutionFactory_AddOrUpdateResource_UpdatesExistingThemeResource()
    {
        var changedFiles = new List<string>();
        var solutionFactory = new InMemorySolutionFactory(file => changedFiles.Add(file.Path));
        var solution = solutionFactory.CreateSolution("ThemeApp", AvaloniaProjectTemplates.All[0]);
        var project = Assert.Single(solution.Projects);

        var file = solutionFactory.AddOrUpdateResource(project, "Themes/MyButtonTheme1.axaml", "<ResourceDictionary />");
        var updatedFile = solutionFactory.AddOrUpdateResource(project, "Themes/MyButtonTheme1.axaml", "<ResourceDictionary><SolidColorBrush x:Key=\"Brush\" /></ResourceDictionary>");

        Assert.Same(file, updatedFile);
        Assert.Equal("Themes/MyButtonTheme1.axaml", file.Path);
        Assert.Equal("<ResourceDictionary><SolidColorBrush x:Key=\"Brush\" /></ResourceDictionary>", file.Text);
        Assert.Contains("Themes/MyButtonTheme1.axaml", changedFiles);
    }

    [Fact]
    public void InMemorySolutionFactory_AddOrUpdateResource_DoesNotOverwriteNonResourceFiles()
    {
        var solutionFactory = new InMemorySolutionFactory(_ => { });
        var solution = solutionFactory.CreateSolution("ThemeApp", AvaloniaProjectTemplates.All[0]);
        var project = Assert.Single(solution.Projects);
        var mainView = project.FindFile("Views/MainView.axaml");
        Assert.NotNull(mainView);
        var originalMainViewText = mainView.Text;

        var loadedFile = solutionFactory.AddOrUpdateResource(
            project,
            "Views/MainView.axaml",
            "<ResourceDictionary />");

        Assert.Equal(originalMainViewText, mainView.Text);
        Assert.Equal(ProjectFileKind.Resource, loadedFile.Kind);
        Assert.Equal("Themes/MainView.axaml", loadedFile.Path);
        Assert.Equal("<ResourceDictionary />", loadedFile.Text);
    }

    [Fact]
    public void ThemeProjectFiles_IncludeResourceDependenciesWhenCustomThemeExists()
    {
        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        var resourcesFile = project.FindFile("Styles/Resources.axaml");
        Assert.NotNull(resourcesFile);
        resourcesFile.Text = """
                             <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                               <SolidColorBrush x:Key="SharedBrush" Color="Red" />
                             </ResourceDictionary>
                             """;
        project.AddFile(new InMemoryProjectFile(
            "Themes/MyButtonTheme1.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTheme x:Key="MyButtonTheme1" TargetType="Button">
                <Setter Property="Background" Value="{DynamicResource SharedBrush}" />
              </ControlTheme>
            </ResourceDictionary>
            """,
            ProjectFileKind.Resource));

        var method = typeof(MainViewModel).GetMethod(
            "GetControlThemeProjectFiles",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var themeFiles = Assert.IsAssignableFrom<IReadOnlyList<InMemoryProjectFile>>(
            method.Invoke(viewModel, null));

        Assert.Contains(themeFiles, file => file.Path == "Themes/MyButtonTheme1.axaml");
        Assert.Contains(themeFiles, file => file.Path == "Styles/Resources.axaml");
    }

    [Fact]
    public void CreateCustomControlThemeCommand_AddsThemeResourceAndAppliesSelectedControl()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            mainFile.Text = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <StackPanel>
                                <Button x:Name="ActionButton" Content="Save" />
                              </StackPanel>
                            </UserControl>
                            """;
            var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);

            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            Assert.True(viewModel.CreateCustomControlThemeCommand.CanExecute(null));

            viewModel.CreateCustomControlThemeCommand.Execute(null);

            var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml");
            Assert.NotNull(themeFile);
            Assert.Contains("x:Key=\"MyButtonTheme1\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains("Theme=\"{StaticResource MyButtonTheme1}\"", mainFile.Text, StringComparison.Ordinal);
            Assert.Same(themeFile, viewModel.ActiveXamlFile);
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyButtonTheme1" && theme.TargetType == "Button");

            var diagnostics = new List<RuntimeXamlDiagnostic>();
            RuntimeXamlPreviewLoader.ApplyProjectResources(
                new[] { (themeFile.Path, themeFile.Text) },
                localAssembly: null,
                diagnostics);
            var preview = RuntimeXamlPreviewLoader.LoadResourceDictionaryPreview(
                themeFile.Text,
                localAssembly: null,
                themeFile.Path,
                diagnostics);

            Assert.NotNull(preview);
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Title.Contains("StaticResource", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            RuntimeXamlPreviewLoader.ApplyProjectResources(
                Array.Empty<(string Path, string Text)>(),
                localAssembly: null,
                new List<RuntimeXamlDiagnostic>());
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ResourceDictionaryPreview_PreservesRootNamespacesUsedByMarkupExtensions()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            const string xaml = """
                                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                    xmlns:tests="clr-namespace:XamlPlayground.Tests;assembly=XamlPlayground.Tests">
                                  <Design.PreviewWith>
                                    <TextBlock Text="{x:Static tests:RuntimePreviewDesignData.Message}" />
                                  </Design.PreviewWith>
                                </ResourceDictionary>
                                """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();

            var preview = RuntimeXamlPreviewLoader.LoadResourceDictionaryPreview(
                xaml,
                typeof(RuntimePreviewDesignData).Assembly,
                "Themes/Preview.axaml",
                diagnostics);

            var userControl = Assert.IsType<UserControl>(preview);
            var textBlock = Assert.IsType<TextBlock>(userControl.Content);
            Assert.Equal(RuntimePreviewDesignData.Message, textBlock.Text);
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity >= RuntimeXamlDiagnosticSeverity.Error);
        });
    }

    [Fact]
    public void CreateCustomControlThemeCommand_CreatesThemeFromSelectedFluentTemplateWithoutPreviewSelection()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            var template = Assert.Single(
                viewModel.FluentControlThemeTemplates,
                candidate => candidate.TargetType == "Button");

            viewModel.SelectedFluentControlThemeTemplate = template;

            Assert.Equal("Fluent template: Button", viewModel.ControlThemeSelectedTargetType);
            Assert.True(viewModel.CreateCustomControlThemeCommand.CanExecute(null));

            viewModel.CreateCustomControlThemeCommand.Execute(null);

            var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml");
            Assert.NotNull(themeFile);
            Assert.Contains("x:Key=\"MyButtonTheme1\"", themeFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Theme=\"{StaticResource MyButtonTheme1}\"", mainFile.Text, StringComparison.Ordinal);
            Assert.Same(themeFile, viewModel.ActiveXamlFile);
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyButtonTheme1" && theme.TargetType == "Button");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateCustomControlThemeCommand_PreservesExistingCustomThemesWhenCreatingAnotherTargetType()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            mainFile.Text = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <StackPanel>
                                <Button x:Name="ActionButton" Content="Save" />
                                <CheckBox x:Name="AcceptCheckBox" Content="Accept" />
                              </StackPanel>
                            </UserControl>
                            """;
            var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);

            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            Assert.NotNull(viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml"));
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyButtonTheme1" && theme.TargetType == "Button");

            viewModel.ReturnFromThemeEditScopeCommand.Execute(null);
            Assert.Same(mainFile, viewModel.ActiveXamlFile);
            viewModel.ControlThemeSearchText = "CheckBox";
            Assert.Empty(viewModel.FilteredControlThemes);

            var checkBoxStart = mainFile.Text.IndexOf("<CheckBox", StringComparison.Ordinal);
            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, checkBoxStart, 0, checkBoxStart));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            Assert.Equal(string.Empty, viewModel.ControlThemeSearchText);
            Assert.NotNull(viewModel.ActiveProject.FindFile("Themes/MyButtonTheme1.axaml"));
            Assert.NotNull(viewModel.ActiveProject.FindFile("Themes/MyCheckBoxTheme1.axaml"));
            Assert.Equal(2, viewModel.FilteredControlThemes.Count);
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyButtonTheme1" && theme.TargetType == "Button");
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyCheckBoxTheme1" && theme.TargetType == "CheckBox");
            Assert.Contains("Theme=\"{StaticResource MyButtonTheme1}\"", mainFile.Text, StringComparison.Ordinal);
            Assert.Contains("Theme=\"{StaticResource MyCheckBoxTheme1}\"", mainFile.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ControlThemeSearchText_FiltersCustomAndFluentThemeLists()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            mainFile.Text = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Button x:Name="ActionButton" Content="Save" />
                            </UserControl>
                            """;
            var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);
            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            viewModel.ControlThemeSearchText = "MyButton";

            var customTheme = Assert.Single(viewModel.FilteredControlThemes);
            Assert.Equal("MyButtonTheme1", customTheme.Key);
            Assert.Empty(viewModel.FilteredFluentControlThemeTemplates);

            viewModel.ControlThemeSearchText = "Controls/Button";

            Assert.Empty(viewModel.FilteredControlThemes);
            var fluentTemplate = Assert.Single(viewModel.FilteredFluentControlThemeTemplates);
            Assert.Equal("Button", fluentTemplate.TargetType);
            Assert.Equal("Controls/Button.xaml", fluentTemplate.SourcePath);

            viewModel.ControlThemeSearchText = "missing";

            Assert.Empty(viewModel.FilteredControlThemes);
            Assert.Empty(viewModel.FilteredFluentControlThemeTemplates);

            viewModel.ActiveXamlFile = mainFile;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            var contextMenuThemes = Assert.Single(viewModel.GetControlThemesForSelectedVisualElement());
            Assert.Equal("MyButtonTheme1", contextMenuThemes.Key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ThemeResourceCommands_RenameDuplicateAndConfirmDeletingUsedResource()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            mainFile.Text = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Button x:Name="ActionButton" Content="Save" />
                            </UserControl>
                            """;
            var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);
            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml");
            Assert.NotNull(themeFile);
            viewModel.SelectedThemeResource = Assert.Single(viewModel.ThemeResources, resource => resource.Key == "MyButtonTheme1");
            viewModel.ThemeResourceKeyEditText = "PrimaryButtonTheme";

            Assert.True(viewModel.RenameSelectedThemeResourceCommand.CanExecute(null));
            viewModel.RenameSelectedThemeResourceCommand.Execute(null);

            Assert.Contains("x:Key=\"PrimaryButtonTheme\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains("Theme=\"{StaticResource PrimaryButtonTheme}\"", mainFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("MyButtonTheme1", mainFile.Text, StringComparison.Ordinal);

            viewModel.DuplicateSelectedThemeResourceCommand.Execute(null);

            Assert.Contains(viewModel.ThemeResources, resource => resource.Key == "PrimaryButtonThemeCopy");
            Assert.Contains("x:Key=\"PrimaryButtonThemeCopy\"", themeFile.Text, StringComparison.Ordinal);

            viewModel.SelectedThemeResource = Assert.Single(viewModel.ThemeResources, resource => resource.Key == "PrimaryButtonTheme");
            viewModel.DeleteSelectedThemeResourceCommand.Execute(null);

            Assert.True(viewModel.IsThemeResourceDeleteDialogOpen);
            Assert.Contains("Review deletion", viewModel.ThemeResourceDeleteDialogMessage, StringComparison.Ordinal);
            Assert.Contains(viewModel.ThemeResourceDeleteChanges, change =>
                change.FilePath == mainFile.Path &&
                change.Diff.Contains("Theme=\"{StaticResource PrimaryButtonTheme}\"", StringComparison.Ordinal));
            Assert.Contains(viewModel.ThemeResourceDeleteChanges, change =>
                change.FilePath == themeFile.Path &&
                change.Diff.Contains("x:Key=\"PrimaryButtonTheme\"", StringComparison.Ordinal));
            Assert.Contains("x:Key=\"PrimaryButtonTheme\"", themeFile.Text, StringComparison.Ordinal);

            viewModel.ConfirmThemeResourceDeleteCommand.Execute(null);

            Assert.False(viewModel.IsThemeResourceDeleteDialogOpen);
            Assert.DoesNotContain("Theme=\"{StaticResource PrimaryButtonTheme}\"", mainFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("x:Key=\"PrimaryButtonTheme\"", themeFile.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ThemeResourceCommands_EditSelectedDuplicateKeyByLine()
    {
        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        var themeFile = project.AddFile(new InMemoryProjectFile(
            "Themes/Palette.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="VariantBrush" Color="White" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="VariantBrush" Color="Black" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """,
            ProjectFileKind.Resource));
        RefreshControlThemes(viewModel);
        var duplicateResources = viewModel.ThemeResources
            .Where(resource => resource.Key == "VariantBrush")
            .OrderBy(resource => resource.Line)
            .ToArray();
        Assert.Equal(2, duplicateResources.Length);

        viewModel.SelectedThemeResource = duplicateResources[1];
        viewModel.ThemeResourceKeyEditText = "DarkVariantBrush";
        Assert.True(viewModel.RenameSelectedThemeResourceCommand.CanExecute(null));
        viewModel.RenameSelectedThemeResourceCommand.Execute(null);

        Assert.Contains("x:Key=\"VariantBrush\" Color=\"White\"", themeFile.Text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DarkVariantBrush\" Color=\"Black\"", themeFile.Text, StringComparison.Ordinal);
        Assert.Contains("References were left unchanged", viewModel.ControlThemeStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualPropertyResourcePicker_SuggestsCompatibleResourcesAndOpensReference()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var project = viewModel.ActiveProject!;
        project.AddFile(new InMemoryProjectFile(
            "Themes/Palette.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:sys="clr-namespace:System;assembly=System.Runtime">
              <SolidColorBrush x:Key="PickerBrush" Color="Red" />
              <DataTemplate x:Key="PersonTemplate" DataType="sys:String">
                <TextBlock Text="{Binding}" />
              </DataTemplate>
              <x:Array x:Key="SampleItems" Type="{x:Type sys:String}">
                <sys:String>One</sys:String>
              </x:Array>
              <sys:Object x:Key="SampleData" />
            </ResourceDictionary>
            """,
            ProjectFileKind.Resource));
        RefreshControlThemes(viewModel);
        var mainFile = viewModel.ActiveXamlFile!;
        mainFile.Text = """
                        <UserControl xmlns="https://github.com/avaloniaui"
                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                          <StackPanel>
                            <Button x:Name="ActionButton" Content="Save" />
                            <ListBox x:Name="ItemsList" />
                            <ContentControl x:Name="DataHost" />
                          </StackPanel>
                        </UserControl>
                        """;
        var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);
        Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
        viewModel.SelectedVisualEditorAvailableProperty =
            viewModel.VisualEditorAvailableProperties.First(property => property.Name == "Background");

        Assert.Contains("{DynamicResource PickerBrush}", viewModel.VisualEditorPropertyOptions);
        Assert.Contains("{StaticResource PickerBrush}", viewModel.VisualEditorPropertyOptions);

        viewModel.SelectedVisualEditorPropertyOption = "{DynamicResource PickerBrush}";
        viewModel.ApplyVisualEditorPropertyCommand.Execute(null);

        Assert.Contains("Background=\"{DynamicResource PickerBrush}\"", mainFile.Text, StringComparison.Ordinal);
        Assert.True(viewModel.OpenVisualEditorPropertyResourceCommand.CanExecute(null));
        viewModel.OpenVisualEditorPropertyResourceCommand.Execute(null);
        Assert.Equal("Themes/Palette.axaml", viewModel.ActiveXamlFile!.Path);

        viewModel.ActiveXamlFile = mainFile;
        var listBoxStart = mainFile.Text.IndexOf("<ListBox", StringComparison.Ordinal);
        Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, listBoxStart, 0, listBoxStart));
        viewModel.SelectedVisualEditorAvailableProperty =
            viewModel.VisualEditorAvailableProperties.First(property => property.Name == "ItemsSource");
        Assert.Contains("{StaticResource SampleItems}", viewModel.VisualEditorPropertyOptions);
        viewModel.SelectedVisualEditorAvailableProperty =
            viewModel.VisualEditorAvailableProperties.First(property => property.Name == "ItemTemplate");
        Assert.Contains("{StaticResource PersonTemplate}", viewModel.VisualEditorPropertyOptions);

        var contentStart = mainFile.Text.IndexOf("<ContentControl", StringComparison.Ordinal);
        Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, contentStart, 0, contentStart));
        viewModel.SelectedVisualEditorAvailableProperty =
            viewModel.VisualEditorAvailableProperties.First(property => property.Name == "DataContext");
        Assert.Contains("{StaticResource SampleData}", viewModel.VisualEditorPropertyOptions);
    }

    [Fact]
    public void ThemeStateAndVariantCommands_EditThemeDictionary()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var themeRoot = CreateTemporaryFluentThemeRoot();
        var previousThemeRoot = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", themeRoot);

        try
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var mainFile = viewModel.ActiveXamlFile!;
            mainFile.Text = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Button x:Name="ActionButton" Content="Save" />
                            </UserControl>
                            """;
            var buttonStart = mainFile.Text.IndexOf("<Button", StringComparison.Ordinal);
            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml");
            Assert.NotNull(themeFile);
            viewModel.SelectedControlTheme = Assert.Single(viewModel.ControlThemes, theme => theme.Key == "MyButtonTheme1");
            viewModel.SelectedThemePreviewState =
                viewModel.ThemePreviewStates.First(state => state.State == "pointerover");
            viewModel.ThemeStateSetterPropertyName = "Opacity";
            viewModel.ThemeStateSetterValue = "0.8";

            Assert.True(viewModel.ApplyThemeStateSetterCommand.CanExecute(null));
            viewModel.ApplyThemeStateSetterCommand.Execute(null);

            Assert.Contains("Selector=\"^:pointerover\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains("Property=\"Opacity\" Value=\"0.8\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains(viewModel.ThemeVariants, variant => variant.Name == ThemeProjectStorage.BaseVariant);

            viewModel.SelectedThemeTemplatePart = Assert.Single(
                viewModel.ThemeTemplateParts,
                part => part.Name == "PART_ContentPresenter");
            viewModel.ThemeTemplatePartSetterPropertyName = "Opacity";
            viewModel.ThemeTemplatePartSetterValue = "0.7";

            Assert.True(viewModel.ApplyThemeTemplatePartSetterCommand.CanExecute(null));
            viewModel.ApplyThemeTemplatePartSetterCommand.Execute(null);

            Assert.Contains(
                "Selector=\"^ /template/ ContentPresenter#PART_ContentPresenter:pointerover\"",
                themeFile.Text,
                StringComparison.Ordinal);
            Assert.Contains("Property=\"Opacity\" Value=\"0.7\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains(viewModel.ThemeTemplatePartSelectors, selector =>
                selector.PartName == "PART_ContentPresenter" &&
                selector.State == "pointerover");

            Assert.True(viewModel.CreateThemeVariantPreviewCommand.CanExecute(null));
            viewModel.CreateThemeVariantPreviewCommand.Execute(null);

            Assert.Contains("RequestedThemeVariant=\"Light\"", themeFile.Text, StringComparison.Ordinal);
            Assert.Contains("RequestedThemeVariant=\"Dark\"", themeFile.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
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
                TextBox? errorTextBox = null;
                for (var i = 0; i < 25; i++)
                {
                    var errorsViews = dockControl.GetVisualDescendants().OfType<ErrorsDockView>().ToArray();
                    var errorsView = errorsViews.FirstOrDefault(view => ReferenceEquals(view.DataContext, errors)) ??
                                     errorsViews.SingleOrDefault();
                    errorTextBox = errorsView?.GetVisualDescendants().OfType<TextBox>().SingleOrDefault();
                    if (errorTextBox?.Text == "Broken sample")
                    {
                        break;
                    }

                    PumpLayout(window);
                }

                Assert.NotNull(errorTextBox);
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

    private static string CreateTemporaryFluentThemeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"XamlPlaygroundFluentTheme-{Guid.NewGuid():N}");
        var controls = Path.Combine(root, "Controls");
        Directory.CreateDirectory(controls);
        File.WriteAllText(
            Path.Combine(root, "FluentTheme.xaml"),
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" />
            """);
        File.WriteAllText(
            Path.Combine(controls, "Button.xaml"),
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTheme x:Key="{x:Type Button}" TargetType="Button">
                <Setter Property="Padding" Value="8" />
                <Setter Property="Template">
                  <ControlTemplate>
                    <ContentPresenter x:Name="PART_ContentPresenter"
                                      Content="{TemplateBinding Content}"
                                      Padding="{TemplateBinding Padding}" />
                  </ControlTemplate>
                </Setter>
              </ControlTheme>
            </ResourceDictionary>
            """);
        File.WriteAllText(
            Path.Combine(controls, "CheckBox.xaml"),
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTheme x:Key="{x:Type CheckBox}" TargetType="CheckBox">
                <Setter Property="Padding" Value="8" />
                <Setter Property="Template">
                  <ControlTemplate>
                    <ContentPresenter x:Name="PART_ContentPresenter"
                                      Content="{TemplateBinding Content}"
                                      Padding="{TemplateBinding Padding}" />
                  </ControlTemplate>
                </Setter>
              </ControlTheme>
            </ResourceDictionary>
            """);
        return root;
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task RunPreviewImmediately(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod(
            "RunInternal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method.Invoke(viewModel, null));
    }

    private static void RefreshControlThemes(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod(
            "RefreshControlThemes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, null);
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

    private static void AssertControlThemeTools(ControlThemesDockViewModel controlThemes)
    {
        var root = Assert.IsAssignableFrom<IRootDock>(controlThemes.DockLayout);
        Assert.NotNull(controlThemes.DockFactory);
        Assert.Equal(DockFloatingWindowHostMode.Default, root.FloatingWindowHostMode);

        var tools = Enumerate(root).OfType<ControlThemePanelDockViewModel>().ToList();
        Assert.Collection(
            tools,
            tool => AssertControlThemeTool<ControlThemeCustomDockViewModel>(tool, "ControlThemeCustom", "Custom", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeResourcesDockViewModel>(tool, "ControlThemeResources", "Resources", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeUsagesDockViewModel>(tool, "ControlThemeUsages", "Usages", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeDiagnosticsDockViewModel>(tool, "ControlThemeDiagnostics", "Diagnostics", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeStatesDockViewModel>(tool, "ControlThemeStates", "States", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeVariantsDockViewModel>(tool, "ControlThemeVariants", "Variants", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemePartsDockViewModel>(tool, "ControlThemeParts", "Parts", controlThemes.Shell),
            tool => AssertControlThemeTool<ControlThemeFluentDockViewModel>(tool, "ControlThemeFluent", "Fluent", controlThemes.Shell));

        var factory = Assert.IsType<ControlThemesDockFactory>(controlThemes.DockFactory);
        Assert.NotNull(factory.ContextLocator);
        Assert.Same(tools[0], factory.ContextLocator["ControlThemeCustom"]());
        Assert.Same(tools[1], factory.ContextLocator["ControlThemeResources"]());
        Assert.Same(tools[2], factory.ContextLocator["ControlThemeUsages"]());
        Assert.Same(tools[3], factory.ContextLocator["ControlThemeDiagnostics"]());
        Assert.Same(tools[4], factory.ContextLocator["ControlThemeStates"]());
        Assert.Same(tools[5], factory.ContextLocator["ControlThemeVariants"]());
        Assert.Same(tools[6], factory.ContextLocator["ControlThemeParts"]());
        Assert.Same(tools[7], factory.ContextLocator["ControlThemeFluent"]());
    }

    private static void AssertControlThemeTool<TTool>(
        ControlThemePanelDockViewModel tool,
        string id,
        string title,
        MainViewModel shell)
        where TTool : ControlThemePanelDockViewModel
    {
        Assert.IsType<TTool>(tool);
        Assert.Equal(id, tool.Id);
        Assert.Equal(title, tool.Title);
        Assert.Same(shell, tool.Shell);
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

public static class RuntimePreviewDesignData
{
    public const string Message = "Preview namespace value";
}
