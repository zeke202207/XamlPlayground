using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
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
using Microsoft.CodeAnalysis.CSharp;
using XamlPlayground.Controls;
using XamlPlayground.Views.Docking;
using XamlPlayground.Services;
using XamlPlayground.Services.Theming;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.VisualEditing;
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
    public void ResourceEditorKeyChange_RequeriesCreateCommand()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            ResourceEditorRawXaml = string.Empty
        };
        var raised = false;
        viewModel.CreateResourceCommand.CanExecuteChanged += (_, _) => raised = true;

        viewModel.ResourceEditorKey = "CreatedBrush";

        Assert.True(raised);
        Assert.True(viewModel.CreateResourceCommand.CanExecute(null));
    }

    [Fact]
    public void StyleSetterGridEdit_UpdatesAppliedSetterValue()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <UserControl.Styles>
                                             <Style Selector="Button.primary">
                                               <Setter Property="Background" Value="Blue" />
                                             </Style>
                                           </UserControl.Styles>
                                           <Button Classes="primary" />
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        var setter = Assert.IsType<StyleSetterEditorViewModel>(viewModel.SelectedStyleEditorSetter);

        setter.Value = "Red";
        viewModel.ApplyStyleEditorCommand.Execute(null);

        Assert.Equal("Red", viewModel.StyleEditorValue);
        Assert.Contains("Value=\"Red\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"Blue\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleSetterGridPropertyRename_ReplacesSelectedSetter()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <UserControl.Styles>
                                             <Style Selector="Button.primary">
                                               <Setter Property="Background" Value="Blue" />
                                             </Style>
                                           </UserControl.Styles>
                                           <Button Classes="primary" />
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        var setter = Assert.IsType<StyleSetterEditorViewModel>(viewModel.SelectedStyleEditorSetter);

        setter.PropertyName = "Foreground";
        setter.Value = "Red";
        viewModel.ApplyStyleEditorCommand.Execute(null);

        Assert.Contains("Property=\"Foreground\" Value=\"Red\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"Background\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Single(viewModel.StyleEditorSetters);
    }

    [Fact]
    public void ResourceFileTextChange_RefreshesDesignInspection()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var resourceFile = Assert.Single(
            viewModel.ActiveProject!.Files,
            file => file.Path == "Styles/Resources.axaml");
        viewModel.RefreshDesignInspectorsCommand.Execute(null);

        resourceFile.Text = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <SolidColorBrush x:Key="PanelBrush" Color="#112233" />
                            </ResourceDictionary>
                            """;

        var resourceNodes = viewModel.ResourceInspectorNodes.SelectMany(static node => node.Children);
        Assert.Contains(resourceNodes, static node => node.Title == "PanelBrush");
        Assert.DoesNotContain(resourceNodes, static node => node.Title == "AccentBrush");
        Assert.Contains("1 resource(s)", viewModel.DesignInspectionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingEditorRawMarkup_ReplacesObjectElementBinding()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <TextBlock>
                                             <TextBlock.Text>
                                               <Binding Path="Title" />
                                             </TextBlock.Text>
                                           </TextBlock>
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);

        viewModel.BindingEditorRawValue = "{Binding DisplayName, Mode=OneWay}";
        viewModel.ApplyBindingEditorCommand.Execute(null);

        Assert.Contains("<Binding Path=\"DisplayName\" Mode=\"OneWay\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Path=\"Title\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingEditorFieldEdits_AreAppliedWithoutBuildingRawValue()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <TextBlock Text="{Binding Title, Mode=TwoWay}" />
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.SelectedBindingInspectorNode = viewModel.BindingInspectorNodes
            .SelectMany(static node => node.Children)
            .Single(static node => node.Binding?.Path == "Title");

        viewModel.BindingEditorPath = "DisplayName";
        viewModel.BindingEditorMode = "OneWay";
        viewModel.ApplyBindingEditorCommand.Execute(null);

        Assert.Contains("Text=\"{Binding DisplayName, Mode=OneWay}\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Title", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoWay", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingEditorFieldEdits_PreserveMultiBindingChildren()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <TextBlock>
                                             <TextBlock.Text>
                                               <MultiBinding StringFormat="{}{0} {1}">
                                                 <Binding Path="FirstName" />
                                                 <Binding Path="LastName" />
                                               </MultiBinding>
                                             </TextBlock.Text>
                                           </TextBlock>
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.SelectedBindingInspectorNode = viewModel.BindingInspectorNodes
            .SelectMany(static node => node.Children)
            .Single(static node => node.Binding?.Kind == "MultiBinding");

        viewModel.BindingEditorStringFormat = "{}{0}, {1}";
        viewModel.ApplyBindingEditorCommand.Execute(null);

        Assert.Contains("<MultiBinding StringFormat=\"{}{0}, {1}\">", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"FirstName\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"LastName\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<MultiBinding StringFormat=\"{}{0}, {1}\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingEditorBuildMarkup_PreservesMultiBindingChildren()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <TextBlock>
                                             <TextBlock.Text>
                                               <MultiBinding StringFormat="{}{0} {1}">
                                                 <Binding Path="FirstName" />
                                                 <Binding Path="LastName" />
                                               </MultiBinding>
                                             </TextBlock.Text>
                                           </TextBlock>
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.SelectedBindingInspectorNode = viewModel.BindingInspectorNodes
            .SelectMany(static node => node.Children)
            .Single(static node => node.Binding?.Kind == "MultiBinding");

        viewModel.BindingEditorStringFormat = "{}{0}, {1}";
        viewModel.BuildBindingMarkupCommand.Execute(null);

        Assert.Contains("<MultiBinding StringFormat=\"{}{0}, {1}\">", viewModel.BindingEditorRawValue, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"FirstName\" />", viewModel.BindingEditorRawValue, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"LastName\" />", viewModel.BindingEditorRawValue, StringComparison.Ordinal);
        Assert.DoesNotContain("<MultiBinding StringFormat=\"{}{0}, {1}\" />", viewModel.BindingEditorRawValue, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceEditorFieldEdits_AreAppliedWithoutEditingRawXaml()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui"
                                                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                           <UserControl.Resources>
                                             <SolidColorBrush x:Key="AccentBrush">#112233</SolidColorBrush>
                                           </UserControl.Resources>
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.SelectedResourceInspectorNode = viewModel.ResourceInspectorNodes
            .SelectMany(static node => node.Children)
            .Single(node => node.Resource?.Key == "AccentBrush" &&
                            node.Resource.FilePath == viewModel.ActiveXamlFile.Path);

        viewModel.ResourceEditorKey = "PanelBrush";
        viewModel.ResourceEditorValue = "#445566";
        viewModel.ApplyResourceEditorCommand.Execute(null);

        Assert.Contains("x:Key=\"PanelBrush\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("#445566", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentBrush", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#112233", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceEditorFieldApply_PreservesControlThemeTargetType()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui"
                                                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                           <UserControl.Resources>
                                             <ControlTheme x:Key="PrimaryButtonTheme" TargetType="Button">
                                               <Setter Property="Background" Value="Blue" />
                                             </ControlTheme>
                                           </UserControl.Resources>
                                         </UserControl>
                                         """;
        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.SelectedResourceInspectorNode = viewModel.ResourceInspectorNodes
            .SelectMany(static node => node.Children)
            .Single(node => node.Resource?.Key == "PrimaryButtonTheme" &&
                            node.Resource.FilePath == viewModel.ActiveXamlFile.Path);

        viewModel.ResourceEditorKey = "SecondaryButtonTheme";
        viewModel.ResourceEditorValue = """<Setter Property="Background" Value="Red" />""";
        viewModel.ApplyResourceEditorCommand.Execute(null);

        Assert.Contains("x:Key=\"SecondaryButtonTheme\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"Button\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("Value=\"Red\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryButtonTheme", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleSetterUnknownProperty_IsPreservedWhenStyleIsSelected()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.ActiveXamlFile!.Text = """
                                         <UserControl xmlns="https://github.com/avaloniaui">
                                           <UserControl.Styles>
                                             <Style Selector="Button">
                                               <Setter Property="local:Custom.Value" Value="Original" />
                                             </Style>
                                           </UserControl.Styles>
                                           <Button />
                                         </UserControl>
                                         """;

        viewModel.RefreshDesignInspectorsCommand.Execute(null);
        viewModel.StyleEditorValue = "Changed";
        viewModel.ApplyStyleEditorCommand.Execute(null);

        Assert.Equal("local:Custom.Value", viewModel.StyleEditorPropertyName);
        Assert.Null(viewModel.SelectedStyleEditorAvailableProperty);
        Assert.Contains("Property=\"local:Custom.Value\" Value=\"Changed\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"Background\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
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
    public void SolutionStorage_RoundTripsFullSolution()
    {
        var changedFiles = new List<string>();
        var solutionFactory = new InMemorySolutionFactory(file => changedFiles.Add(file.Path));
        var solution = solutionFactory.CreateSolution(
            "RoundTripApp",
            Assert.Single(AvaloniaProjectTemplates.All, template => template.ShortName == "avalonia.xplat"));
        var project = Assert.Single(solution.Projects);
        var userControl = solutionFactory.AddUserControl(project);
        userControl.Text = """
                           <UserControl xmlns="https://github.com/avaloniaui">
                             <TextBlock Text="Imported solution" />
                           </UserControl>
                           """;
        solutionFactory.AddControlThemeResource(
            project,
            "ExternalTheme",
            "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" />",
            includeInRuntimePreview: false);

        var json = SolutionStorage.Save(solution);
        var loaded = SolutionStorage.Load(json, file => changedFiles.Add($"loaded:{file.Path}"));

        Assert.Contains("\"format\": \"xamlplayground.solution\"", json, StringComparison.Ordinal);
        Assert.Equal("RoundTripApp", loaded.Name);
        var loadedProject = Assert.Single(loaded.Projects);
        Assert.Equal(project.Name, loadedProject.Name);
        Assert.Equal(project.RootNamespace, loadedProject.RootNamespace);
        Assert.Equal(project.TemplateShortName, loadedProject.TemplateShortName);
        Assert.Equal(project.Files.Count, loadedProject.Files.Count);
        Assert.Contains(loadedProject.Files, file => file.Path == "Views/UserControl1.axaml" &&
                                                     file.Text.Contains("Imported solution", StringComparison.Ordinal) &&
                                                     file.Kind == ProjectFileKind.Xaml);
        var isolatedTheme = Assert.Single(loadedProject.Files, file => file.Path == "Themes/ExternalTheme.axaml");
        Assert.Equal(ProjectFileKind.Resource, isolatedTheme.Kind);
        Assert.False(isolatedTheme.IncludeInRuntimePreview);
    }

    [Fact]
    public void StandardSolutionStorage_WritesSlnAndSlnxProjectReferences()
    {
        var solutionFactory = new InMemorySolutionFactory(_ => { });
        var solution = solutionFactory.CreateSolution(
            "StandardApp",
            Assert.Single(AvaloniaProjectTemplates.All, template => template.ShortName == "avalonia.xplat"));

        var sln = StandardSolutionStorage.SaveSln(solution);
        var slnEntries = StandardSolutionStorage.ParseSolutionEntries("StandardApp.sln", sln);
        var slnx = StandardSolutionStorage.SaveSlnx(solution);
        var slnxEntries = StandardSolutionStorage.ParseSolutionEntries("StandardApp.slnx", slnx);

        var slnEntry = Assert.Single(slnEntries);
        Assert.Equal("StandardApp", slnEntry.Name);
        Assert.Equal("StandardApp/StandardApp.csproj", slnEntry.Path);
        var slnxEntry = Assert.Single(slnxEntries);
        Assert.Equal("StandardApp", slnxEntry.Name);
        Assert.Equal("StandardApp/StandardApp.csproj", slnxEntry.Path);
    }

    [Fact]
    public void SolutionStorage_RoundTripsProjectFilePath()
    {
        var solution = new InMemorySolution("StandardApp");
        var project = new InMemoryProject(
            "StandardApp",
            "StandardApp",
            "standard.csproj",
            "src/StandardApp/StandardApp.csproj");
        project.AddFile(new InMemoryProjectFile("StandardApp.csproj", "<Project />", ProjectFileKind.ProjectFile));
        solution.Projects.Add(project);

        var loaded = SolutionStorage.Load(SolutionStorage.Save(solution));
        var loadedProject = Assert.Single(loaded.Projects);

        Assert.Equal("src/StandardApp/StandardApp.csproj", loadedProject.ProjectFilePath);
    }

    [Fact]
    public void StandardSolutionStorage_PreservesImportedProjectPathOnExport()
    {
        var solution = new InMemorySolution("StandardApp");
        var project = new InMemoryProject(
            "StandardApp",
            "StandardApp",
            "standard.csproj",
            "src/StandardApp/StandardApp.csproj");
        project.AddFile(new InMemoryProjectFile("StandardApp.csproj", "<Project />", ProjectFileKind.ProjectFile));
        solution.Projects.Add(project);

        var slnEntry = Assert.Single(StandardSolutionStorage.ParseSolutionEntries(
            "StandardApp.sln",
            StandardSolutionStorage.SaveSln(solution)));
        var slnxEntry = Assert.Single(StandardSolutionStorage.ParseSolutionEntries(
            "StandardApp.slnx",
            StandardSolutionStorage.SaveSlnx(solution)));

        Assert.Equal("src/StandardApp/StandardApp.csproj", slnEntry.Path);
        Assert.Equal("src/StandardApp/StandardApp.csproj", slnxEntry.Path);
    }

    [Fact]
    public void StandardSolutionStorage_WritesSlnProjectTypeGuidForProjectExtension()
    {
        var solution = new InMemorySolution("Mixed");
        var csharpProject = new InMemoryProject("CSharpApp", "CSharpApp", "standard.csproj");
        csharpProject.AddFile(new InMemoryProjectFile("CSharpApp.csproj", "<Project />", ProjectFileKind.ProjectFile));
        var visualBasicProject = new InMemoryProject("VisualBasicApp", "VisualBasicApp", "standard.vbproj");
        visualBasicProject.AddFile(new InMemoryProjectFile("VisualBasicApp.vbproj", "<Project />", ProjectFileKind.ProjectFile));
        var fsharpProject = new InMemoryProject("FSharpApp", "FSharpApp", "standard.fsproj");
        fsharpProject.AddFile(new InMemoryProjectFile("FSharpApp.fsproj", "<Project />", ProjectFileKind.ProjectFile));
        solution.Projects.Add(csharpProject);
        solution.Projects.Add(visualBasicProject);
        solution.Projects.Add(fsharpProject);

        var sln = StandardSolutionStorage.SaveSln(solution);

        Assert.Contains("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"CSharpApp\"", sln, StringComparison.Ordinal);
        Assert.Contains("Project(\"{F184B08F-C81C-45F6-A57F-5ABD9991F28F}\") = \"VisualBasicApp\"", sln, StringComparison.Ordinal);
        Assert.Contains("Project(\"{F2A71F9B-5D33-465A-A702-920D77279786}\") = \"FSharpApp\"", sln, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardSolutionStorage_LoadsVisualBasicAndFSharpSourceFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"XamlPlaygroundMixedSolution-{Guid.NewGuid():N}");
        try
        {
            var visualBasicRoot = Path.Combine(root, "VisualBasicApp");
            var fsharpRoot = Path.Combine(root, "FSharpApp");
            Directory.CreateDirectory(visualBasicRoot);
            Directory.CreateDirectory(fsharpRoot);
            var slnxPath = Path.Combine(root, "Mixed.slnx");
            File.WriteAllText(slnxPath, """
                                        <Solution>
                                          <Project Path="VisualBasicApp/VisualBasicApp.vbproj" />
                                          <Project Path="FSharpApp/FSharpApp.fsproj" />
                                        </Solution>
                                        """);
            File.WriteAllText(Path.Combine(visualBasicRoot, "VisualBasicApp.vbproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(visualBasicRoot, "Module1.vb"), "Public Module Module1\nEnd Module");
            File.WriteAllText(Path.Combine(fsharpRoot, "FSharpApp.fsproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(fsharpRoot, "Program.fs"), "module Program");

            var solution = StandardSolutionStorage.LoadFromLocalPath(slnxPath, File.ReadAllText(slnxPath));

            var visualBasicProject = Assert.Single(
                solution.Projects,
                project => project.Name == "VisualBasicApp");
            var fsharpProject = Assert.Single(
                solution.Projects,
                project => project.Name == "FSharpApp");
            Assert.Contains(visualBasicProject.Files, file => file.Path == "Module1.vb" && file.Kind == ProjectFileKind.Text);
            Assert.Contains(fsharpProject.Files, file => file.Path == "Program.fs" && file.Kind == ProjectFileKind.Text);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void StandardSolutionStorage_LoadsLocalSlnxProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"XamlPlaygroundStandardSolution-{Guid.NewGuid():N}");
        try
        {
            var projectRoot = Path.Combine(root, "ImportedApp");
            var sharedRoot = Path.Combine(root, "Shared");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Views"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Styles"));
            Directory.CreateDirectory(sharedRoot);
            var slnxPath = Path.Combine(root, "ImportedApp.slnx");
            File.WriteAllText(slnxPath, """
                                        <Solution>
                                          <Project Path="ImportedApp/ImportedApp.csproj" />
                                        </Solution>
                                        """);
            File.WriteAllText(Path.Combine(projectRoot, "ImportedApp.csproj"), """
                                                                               <Project Sdk="Microsoft.NET.Sdk">
                                                                                 <PropertyGroup>
                                                                                   <RootNamespace>Imported.Root</RootNamespace>
                                                                                 </PropertyGroup>
                                                                                 <ItemGroup>
                                                                                   <Compile Include="..\Shared\Foo.cs" />
                                                                                   <Compile Include="..\Shared\Bar.cs" Link="Linked\Bar.cs" />
                                                                                 </ItemGroup>
                                                                               </Project>
                                                                               """);
            File.WriteAllText(Path.Combine(projectRoot, "Views", "MainView.axaml"), """
                                                                                    <UserControl xmlns="https://github.com/avaloniaui">
                                                                                      <TextBlock Text="Imported" />
                                                                                    </UserControl>
                                                                                    """);
            File.WriteAllText(Path.Combine(projectRoot, "Styles", "Resources.axaml"), """
                                                                                      <ResourceDictionary xmlns="https://github.com/avaloniaui" />
                                                                                      """);
            File.WriteAllText(Path.Combine(projectRoot, "Styles.axaml"), """
                                                                         <Styles xmlns="https://github.com/avaloniaui">
                                                                           <Style Selector="Button" />
                                                                         </Styles>
                                                                         """);
            File.WriteAllText(Path.Combine(projectRoot, "Views", "MainView.axaml.cs"), "namespace Imported.Root.Views; public partial class MainView { }");
            File.WriteAllText(Path.Combine(sharedRoot, "Foo.cs"), "namespace Shared; public sealed class Foo { }");
            File.WriteAllText(Path.Combine(sharedRoot, "Bar.cs"), "namespace Shared; public sealed class Bar { }");

            var solution = StandardSolutionStorage.LoadFromLocalPath(slnxPath, File.ReadAllText(slnxPath));

            Assert.Equal("ImportedApp", solution.Name);
            var project = Assert.Single(solution.Projects);
            Assert.Equal("ImportedApp", project.Name);
            Assert.Equal("Imported.Root", project.RootNamespace);
            Assert.Contains(project.Files, file => file.Path == "ImportedApp.csproj" && file.Kind == ProjectFileKind.ProjectFile);
            Assert.Contains(project.Files, file => file.Path == "Views/MainView.axaml" && file.Kind == ProjectFileKind.Xaml);
            Assert.Contains(project.Files, file => file.Path == "Styles/Resources.axaml" && file.Kind == ProjectFileKind.Resource);
            Assert.Contains(project.Files, file => file.Path == "Styles.axaml" && file.Kind == ProjectFileKind.Resource);
            Assert.Contains(project.Files, file => file.Path == "Views/MainView.axaml.cs" && file.Kind == ProjectFileKind.CSharp);
            Assert.Contains(project.Files, file => file.Path == "Linked/Shared/Foo.cs" && file.Kind == ProjectFileKind.CSharp);
            Assert.Contains(project.Files, file => file.Path == "Linked/Bar.cs" && file.Kind == ProjectFileKind.CSharp);
            Assert.DoesNotContain(project.Files, file => file.Path == "File.txt");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void MsBuildWorkspaceLoader_IgnoresRootBuildAndSourceControlPaths()
    {
        var method = typeof(MsBuildWorkspaceLoader).GetMethod(
            "IsIgnoredProjectPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True((bool)method.Invoke(null, new object[] { "bin/Debug/Generated.cs" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "obj/project.assets.json" })!);
        Assert.True((bool)method.Invoke(null, new object[] { ".git/config" })!);
        Assert.True((bool)method.Invoke(null, new object[] { ".vs/config/applicationhost.config" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "src/Bindable/View.cs" })!);
    }

    [Fact]
    public void MsBuildWorkspaceLoader_ExcludesSiblingProjectFoldersFromRootStorageProject()
    {
        var excludeMethod = typeof(MsBuildWorkspaceLoader).GetMethod(
            "GetExcludedStorageProjectFolders",
            BindingFlags.Static | BindingFlags.NonPublic);
        var scopeMethod = typeof(MsBuildWorkspaceLoader).GetMethod(
            "IsStorageProjectFileInScope",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(excludeMethod);
        Assert.NotNull(scopeMethod);

        var excludedFolders = Assert.IsAssignableFrom<IEnumerable<string>>(excludeMethod.Invoke(
            null,
            new object[] { string.Empty, new[] { string.Empty, "Lib", "Samples/Controls" } }));
        var excluded = excludedFolders.ToArray();

        Assert.Contains("Lib", excluded);
        Assert.Contains("Samples/Controls", excluded);
        Assert.True((bool)scopeMethod.Invoke(null, new object[] { string.Empty, "App.axaml", excluded })!);
        Assert.True((bool)scopeMethod.Invoke(null, new object[] { string.Empty, "ViewModels/AppViewModel.cs", excluded })!);
        Assert.False((bool)scopeMethod.Invoke(null, new object[] { string.Empty, "Lib/LibView.axaml", excluded })!);
        Assert.False((bool)scopeMethod.Invoke(null, new object[] { string.Empty, "Samples/Controls/Demo.cs", excluded })!);
        Assert.False((bool)scopeMethod.Invoke(null, new object[] { string.Empty, "Lib/bin/Debug/net10.0/Lib.dll", excluded })!);
    }

    [Fact]
    public void MsBuildWorkspaceLoader_AllowsNestedProjectToExcludeItsChildProjectsOnly()
    {
        var excludeMethod = typeof(MsBuildWorkspaceLoader).GetMethod(
            "GetExcludedStorageProjectFolders",
            BindingFlags.Static | BindingFlags.NonPublic);
        var scopeMethod = typeof(MsBuildWorkspaceLoader).GetMethod(
            "IsStorageProjectFileInScope",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(excludeMethod);
        Assert.NotNull(scopeMethod);

        var parentExcluded = Assert.IsAssignableFrom<IEnumerable<string>>(excludeMethod.Invoke(
            null,
            new object[] { "src/App", new[] { string.Empty, "src/App", "src/App/Plugin", "src/Lib" } }));
        var parentExcludedArray = parentExcluded.ToArray();
        var childExcluded = Assert.IsAssignableFrom<IEnumerable<string>>(excludeMethod.Invoke(
            null,
            new object[] { "src/App/Plugin", new[] { string.Empty, "src/App", "src/App/Plugin", "src/Lib" } }));
        var childExcludedArray = childExcluded.ToArray();

        Assert.Contains("src/App/Plugin", parentExcludedArray);
        Assert.Contains("src/Lib", parentExcludedArray);
        Assert.True((bool)scopeMethod.Invoke(null, new object[] { "src/App", "src/App/MainView.axaml", parentExcludedArray })!);
        Assert.False((bool)scopeMethod.Invoke(null, new object[] { "src/App", "src/App/Plugin/PluginView.axaml", parentExcludedArray })!);

        Assert.DoesNotContain("src/App", childExcludedArray);
        Assert.Contains("src/Lib", childExcludedArray);
        Assert.True((bool)scopeMethod.Invoke(null, new object[] { "src/App/Plugin", "src/App/Plugin/PluginView.axaml", childExcludedArray })!);
        Assert.False((bool)scopeMethod.Invoke(null, new object[] { "src/App/Plugin", "src/App/MainView.axaml", childExcludedArray })!);
    }

    [Fact]
    public void MsBuildWorkspaceLoader_ResolvesNestedStorageSolutionProjectPaths()
    {
        var method = typeof(MsBuildWorkspaceLoader).GetMethod(
            "ResolveStorageSolutionProjectPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Equal(
            "src/App/App.csproj",
            method.Invoke(null, new object[] { "src/App.slnx", "App/App.csproj" }));
        Assert.Equal(
            "src/App/App.csproj",
            method.Invoke(null, new object[] { "src/Solutions/App.sln", "../App/App.csproj" }));
        Assert.Equal(
            "App/App.csproj",
            method.Invoke(null, new object[] { "App.sln", "App/App.csproj" }));
    }

    [Fact]
    public void InMemoryProject_GetCSharpFiles_ExcludesEditableNonCompilationFiles()
    {
        var project = new InMemoryProject("ImportedApp", "ImportedApp", "msbuild");
        var compileFile = project.AddFile(new InMemoryProjectFile(
            "Main.cs",
            "namespace ImportedApp; public sealed class Main { }",
            ProjectFileKind.CSharp));
        project.AddFile(new InMemoryProjectFile(
            "Extra.cs",
            "namespace ImportedApp; public sealed class Extra { }",
            ProjectFileKind.CSharp,
            includeInCompilation: false));
        project.AddFile(new InMemoryProjectFile(
            "Main.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));

        Assert.Same(compileFile, Assert.Single(project.GetCSharpFiles()));
    }

    [Fact]
    public void StandardSolutionStorage_ResolvesStorageExplicitIncludesInsideSolutionRoot()
    {
        var method = typeof(StandardSolutionStorage).GetMethod(
            "TryResolveSolutionRelativePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Equal(
            "Shared/Foo.cs",
            method.Invoke(null, new object[] { "ImportedApp", @"..\Shared\Foo.cs" }));
        Assert.Equal(
            "ImportedApp/Views/MainView.axaml",
            method.Invoke(null, new object[] { "ImportedApp", @"Views\MainView.axaml" }));
        Assert.Null(method.Invoke(null, new object[] { "ImportedApp", @"..\..\Outside\Foo.cs" }));
    }

    [Fact]
    public void StandardSolutionStorage_MapsExplicitExportPathsToProjectIncludeLocations()
    {
        var method = typeof(StandardSolutionStorage).GetMethod(
            "CreateExplicitExportPaths",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var projectText = """
                          <Project Sdk="Microsoft.NET.Sdk">
                            <ItemGroup>
                              <Compile Include="..\Shared\Foo.cs" />
                              <Compile Include="..\Shared\Bar.cs" Link="Linked\Bar.cs" />
                            </ItemGroup>
                          </Project>
                          """;

        var paths = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            method.Invoke(null, new object[] { "ImportedApp", projectText }));

        Assert.Equal("Shared/Foo.cs", paths["Linked/Shared/Foo.cs"]);
        Assert.Equal("Shared/Bar.cs", paths["Linked/Bar.cs"]);

        var nestedPaths = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            method.Invoke(null, new object[] { "src/ImportedApp", projectText }));
        Assert.Equal("src/Shared/Foo.cs", nestedPaths["Linked/Shared/Foo.cs"]);
        Assert.Equal("src/Shared/Bar.cs", nestedPaths["Linked/Bar.cs"]);
    }

    [Fact]
    public void ExportSolution_DoesNotMarkCleanForStandardMetadataOnlyExtensions()
    {
        var method = typeof(MainViewModel).GetMethod(
            "IsStandardSolutionMetadataExtension",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True((bool)method.Invoke(null, new object[] { ".sln" })!);
        Assert.True((bool)method.Invoke(null, new object[] { ".slnx" })!);
        Assert.False((bool)method.Invoke(null, new object[] { ".xamlsln" })!);
    }

    [Fact]
    public void LoadSolution_SelectsFirstPreviewableProject()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var solution = new InMemorySolution("Workspace");
        var buildProject = new InMemoryProject("_build", "_build", "msbuild");
        buildProject.AddFile(new InMemoryProjectFile(
            "Build.cs",
            "using Nuke.Common; class Build : NukeBuild { }",
            ProjectFileKind.CSharp));
        var appProject = new InMemoryProject("App", "App", "msbuild");
        var xamlFile = appProject.AddFile(new InMemoryProjectFile(
            "Views/MainView.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));
        solution.Projects.Add(buildProject);
        solution.Projects.Add(appProject);
        var viewModel = new MainViewModel(null);
        var method = typeof(MainViewModel).GetMethod(
            "LoadSolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method.Invoke(viewModel, new object[] { solution });

        Assert.Same(appProject, viewModel.ActiveProject);
        Assert.Same(xamlFile, viewModel.ActiveXamlFile);
    }

    [Fact]
    public void LoadSolution_CollapsesSolutionExplorerBelowRootChildren()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var solution = new InMemorySolution("Workspace");
        var appProject = new InMemoryProject("App", "App", "msbuild");
        appProject.AddFile(new InMemoryProjectFile(
            "App.axaml",
            "<Application xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));
        appProject.AddFile(new InMemoryProjectFile(
            "Views/MainView.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));
        appProject.AddFile(new InMemoryProjectFile(
            "Views/Nested/DetailsView.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));
        solution.Projects.Add(appProject);
        var viewModel = new MainViewModel(null);
        var method = typeof(MainViewModel).GetMethod(
            "LoadSolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method.Invoke(viewModel, new object[] { solution });

        var solutionNode = Assert.Single(viewModel.SolutionExplorerNodes);
        var projectNode = Assert.Single(solutionNode.Children);
        var viewsNode = Assert.Single(projectNode.Children, static node => node.Title == "Views");
        var nestedNode = Assert.Single(viewsNode.Children, static node => node.Title == "Nested");

        Assert.True(solutionNode.IsExpanded);
        Assert.True(projectNode.IsExpanded);
        Assert.False(viewsNode.IsExpanded);
        Assert.False(nestedNode.IsExpanded);
    }

    [Fact]
    public void ActivateWorkspaceFileFromDocument_DoesNotPreviewPreviousProjectXamlAgainstCodeOnlyProject()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var solution = new InMemorySolution("Workspace");
        var appProject = new InMemoryProject("App", "App", "msbuild");
        var xamlFile = appProject.AddFile(new InMemoryProjectFile(
            "Views/MainView.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Xaml));
        var buildProject = new InMemoryProject("_build", "_build", "msbuild");
        var buildFile = buildProject.AddFile(new InMemoryProjectFile(
            "Build.cs",
            "using Nuke.Common; class Build : NukeBuild { }",
            ProjectFileKind.CSharp));
        solution.Projects.Add(appProject);
        solution.Projects.Add(buildProject);
        var viewModel = new MainViewModel(null);
        var method = typeof(MainViewModel).GetMethod(
            "LoadSolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, new object[] { solution });
        Assert.Same(xamlFile, viewModel.ActiveXamlFile);

        var activateMethod = typeof(MainViewModel).GetMethod(
            "ActivateWorkspaceFileFromDocument",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(activateMethod);
        activateMethod.Invoke(viewModel, new object[] { buildFile });

        Assert.Same(buildProject, viewModel.ActiveProject);
        Assert.Same(buildFile, viewModel.ActiveCodeFile);
        Assert.Null(viewModel.ActiveXamlFile);
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
    public async Task CompilerService_UsesWorkspaceReferenceAssembliesWithoutRuntimeReferenceMixing()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var systemRuntimeReference = WorkspaceAssemblyReference.FromPath(
            GetNetCoreReferenceAssemblyPath("System.Runtime"),
            isRuntimeAssembly: true);
        Assert.NotNull(systemRuntimeReference);
        Assert.True(systemRuntimeReference.IsReferenceAssembly);

        const string code = """
            using System.Reflection;

            [assembly: AssemblyTitle("WorkspaceCompile")]

            public sealed class WorkspaceType
            {
                public string Name { get; } = "Demo";
            }
            """;

        var result = await CompilerService.GetProjectAssembly(
            "WorkspaceCompile",
            new[] { ("WorkspaceType.cs", code) },
            new[] { systemRuntimeReference },
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Id is "CS0433" or "CS0518" or "CS8021" or "CS8632");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public async Task CompilerService_LoadsWorkspaceRuntimeReferencesInIsolatedContext()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var dependencyName = "WorkspaceDependency" + Guid.NewGuid().ToString("N");
        var consumerName = "WorkspaceConsumer" + Guid.NewGuid().ToString("N");
        var dependencyImage = CompileTestAssemblyImage(
            dependencyName,
            """
            namespace WorkspaceDependency;

            public sealed class WorkspaceDependencyType
            {
                public string Value { get; } = "Isolated";
            }
            """);
        var dependencyReference = WorkspaceAssemblyReference.FromImage(
            dependencyName + ".dll",
            dependencyImage,
            isRuntimeAssembly: true);
        Assert.NotNull(dependencyReference);

        var result = await CompilerService.GetProjectAssembly(
            consumerName,
            new[]
            {
                (Path: "Consumer.cs", Text: """
                    using WorkspaceDependency;

                    public sealed class WorkspaceConsumerType
                    {
                        public WorkspaceDependencyType Dependency { get; } = new();
                    }
                    """)
            },
            new[] { dependencyReference },
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        Assert.NotNull(result.Context);
        Assert.NotSame(AssemblyLoadContext.Default, result.Context);

        var dependencyAssembly = Assert.Single(
            result.LoadedAssemblies,
            assembly => string.Equals(assembly.GetName().Name, dependencyName, StringComparison.Ordinal));
        Assert.Same(result.Context, AssemblyLoadContext.GetLoadContext(dependencyAssembly));
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly =>
            string.Equals(assembly.GetName().Name, dependencyName, StringComparison.Ordinal));
    }

    [Fact]
    public void WorkspaceAssemblyLoadContext_SharesAvaloniaAssembliesWithHost()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var avaloniaControlsReference = WorkspaceAssemblyReference.FromPath(
            typeof(Control).Assembly.Location,
            isRuntimeAssembly: true);
        Assert.NotNull(avaloniaControlsReference);
        var context = new WorkspaceAssemblyLoadContext(
            "WorkspaceIsolationTest",
            new[] { avaloniaControlsReference });

        var loadedAssembly = context.LoadAssemblyReference(avaloniaControlsReference);

        Assert.Same(typeof(Control).Assembly, loadedAssembly);
        context.Unload();
    }

    [Fact]
    public void WorkspaceAssemblyLoadContext_DoesNotShareAvaloniaNamedPrivateAssembliesWithHost()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var hostDataGridAssembly = typeof(DataGrid).Assembly;
        Assert.Equal("Avalonia.Controls.DataGrid", hostDataGridAssembly.GetName().Name);
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(hostDataGridAssembly));

        var workspaceImage = CompileVersionedPreviewDependencyImage(
            "Avalonia.Controls.DataGrid",
            "42.0.0.0",
            "Workspace private DataGrid dependency");
        var workspaceReference = WorkspaceAssemblyReference.FromImage(
            "Avalonia.Controls.DataGrid.dll",
            workspaceImage,
            isRuntimeAssembly: true);
        Assert.NotNull(workspaceReference);
        var context = new WorkspaceAssemblyLoadContext(
            "WorkspaceIsolationTest",
            new[] { workspaceReference });

        var loadedAssembly = context.LoadAssemblyReference(workspaceReference);

        Assert.NotNull(loadedAssembly);
        Assert.NotSame(hostDataGridAssembly, loadedAssembly);
        Assert.Equal("Avalonia.Controls.DataGrid", loadedAssembly.GetName().Name);
        Assert.Equal(new Version(42, 0, 0, 0), loadedAssembly.GetName().Version);
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(loadedAssembly));
        context.Unload();
    }

    [Fact]
    public async Task RuntimePreview_LoadsDifferentAvaloniaNamedDependencyVersionsPerProjectAndUpdatesDevToolsRoot()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var hostDataGridAssembly = typeof(DataGrid).Assembly;
        Assert.Equal("Avalonia.Controls.DataGrid", hostDataGridAssembly.GetName().Name);
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(hostDataGridAssembly));

        var firstProject = CreateVersionedPreviewProject(
            "WorkspacePreviewOne",
            "Project one dependency",
            "10.0.0.0");
        var secondProject = CreateVersionedPreviewProject(
            "WorkspacePreviewTwo",
            "Project two dependency",
            "20.0.0.0");
        var firstResult = await CompileVersionedPreviewProjectAsync(firstProject);
        var secondResult = await CompileVersionedPreviewProjectAsync(secondProject);

        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var solution = new InMemorySolution("WorkspaceIsolationIntegration");
                solution.Projects.Add(firstProject.Project);
                solution.Projects.Add(secondProject.Project);
                var viewModel = new MainViewModel(null)
                {
                    EnableAutoRun = false
                };
                LoadSolutionIntoViewModel(viewModel, solution);

                ActivateWorkspaceFile(viewModel, firstProject.XamlFile);
                ShowPreviewForDevTools(viewModel, LoadVersionedPreviewControl(firstProject, firstResult));
                var firstAssembly = AssertPreviewAndDevToolsAccess(
                    viewModel,
                    firstProject.ExpectedText,
                    firstProject.ExpectedVersion);

                ActivateWorkspaceFile(viewModel, secondProject.XamlFile);
                ShowPreviewForDevTools(viewModel, LoadVersionedPreviewControl(secondProject, secondResult));
                var secondAssembly = AssertPreviewAndDevToolsAccess(
                    viewModel,
                    secondProject.ExpectedText,
                    secondProject.ExpectedVersion);

                Assert.NotSame(firstAssembly, secondAssembly);
                Assert.NotSame(
                    AssemblyLoadContext.GetLoadContext(firstAssembly),
                    AssemblyLoadContext.GetLoadContext(secondAssembly));
                Assert.Same(hostDataGridAssembly, typeof(DataGrid).Assembly);
            });
        }
        finally
        {
            firstResult.Context?.Unload();
            secondResult.Context?.Unload();
        }
    }

    [Fact]
    public void MsBuildWorkspaceLoader_EmitsProjectReferencesFromSourceWhenOutputIsMissingOrStale()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), $"XamlPlaygroundProjectReference-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(root, "Referenced");
        var sourcePath = Path.Combine(projectDirectory, "ReferencedType.cs");
        var outputPath = Path.Combine(projectDirectory, "bin", "Debug", "net10.0", "Referenced.dll");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(sourcePath, "public sealed class ReferencedType { }");

            var referencedId = ProjectId.CreateNewId("Referenced");
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
                referencedId,
                VersionStamp.Create(),
                "Referenced",
                "Referenced",
                LanguageNames.CSharp,
                filePath: Path.Combine(projectDirectory, "Referenced.csproj"),
                outputFilePath: outputPath));
            solution = solution.AddDocument(
                DocumentId.CreateNewId(referencedId),
                "ReferencedType.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From("public sealed class ReferencedType { }"),
                filePath: sourcePath);
            Assert.True(workspace.TryApplyChanges(solution));
            var referencedProject = workspace.CurrentSolution.GetProject(referencedId);
            Assert.NotNull(referencedProject);
            var method = typeof(MsBuildWorkspaceLoader).GetMethod(
                "ShouldEmitProjectReferenceFromSource",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            Assert.True((bool)method.Invoke(null, new object[] { referencedProject })!);

            File.WriteAllText(outputPath, "not a real assembly");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow);
            Assert.False((bool)method.Invoke(null, new object[] { referencedProject })!);

            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(10));
            Assert.True((bool)method.Invoke(null, new object[] { referencedProject })!);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
        var solutionExplorer = Assert.Single(dockables.Where(static dockable => dockable.GetType() == typeof(SolutionExplorerDockViewModel)).Cast<SolutionExplorerDockViewModel>());
        var msBuildWorkspace = Assert.Single(dockables.OfType<MsBuildWorkspaceDockViewModel>());
        var visualStructure = Assert.Single(dockables.OfType<VisualStructureDockViewModel>());
        var visualProperties = Assert.Single(dockables.OfType<VisualPropertiesDockViewModel>());
        var visualToolbox = Assert.Single(dockables.OfType<VisualToolboxDockViewModel>());
        var visualAnimations = Assert.Single(dockables.OfType<VisualAnimationsDockViewModel>());
        var animationTimelineSheet = Assert.Single(dockables.OfType<AnimationTimelineSheetDockViewModel>());
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
        Assert.Same(viewModel, msBuildWorkspace.Shell);
        Assert.Same(viewModel, visualStructure.Shell);
        Assert.Same(viewModel, visualProperties.Shell);
        Assert.Same(viewModel, visualToolbox.Shell);
        Assert.Same(viewModel, visualAnimations.Shell);
        Assert.Same(viewModel, animationTimelineSheet.Shell);
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
        Assert.Same(msBuildWorkspace, contextLocator["MsBuildWorkspace"]());
        Assert.Same(visualStructure, contextLocator["VisualStructure"]());
        Assert.Same(visualProperties, contextLocator["VisualProperties"]());
        Assert.Same(visualToolbox, contextLocator["VisualToolbox"]());
        Assert.Same(visualAnimations, contextLocator["VisualAnimations"]());
        Assert.Same(animationTimelineSheet, contextLocator["AnimationTimelineSheet"]());
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
    public void AnimationTimelineSelection_ClearsStaleKeyFrameState()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(100, "1", string.Empty)
            });

        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTimelineTrack = track;

        Assert.NotNull(viewModel.SelectedAnimationTimelineKeyFrame);

        viewModel.SelectedAnimationTimelineTrack = null;

        Assert.Null(viewModel.SelectedAnimationTimelineKeyFrame);

        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTargetOption = null;

        Assert.Empty(viewModel.AnimationTimelineTracks);
        Assert.Equal("Select a visual element or control theme to edit animations.", viewModel.AnimationPlaybackStatus);
    }

    [Fact]
    public void AnimationTimelineKeyFrameCommands_EditSelectedTrack()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(100, "1", string.Empty)
            });

        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTimelineTrack = track;
        viewModel.SelectedAnimationTimelineKeyFrame = track.KeyFrames[0];

        Assert.True(viewModel.DuplicateAnimationKeyFrameCommand.CanExecute(null));
        viewModel.DuplicateAnimationKeyFrameCommand.Execute(null);

        var duplicate = Assert.Single(track.KeyFrames, frame => frame.CuePercent == 10);
        Assert.Same(duplicate, viewModel.SelectedAnimationTimelineKeyFrame);
        Assert.Equal("0", duplicate.Value);

        Assert.True(viewModel.SelectNextAnimationKeyFrameCommand.CanExecute(null));
        viewModel.SelectNextAnimationKeyFrameCommand.Execute(null);
        Assert.Equal(100, viewModel.SelectedAnimationTimelineKeyFrame?.CuePercent);

        viewModel.AnimationCuePercent = 25;
        viewModel.AnimationKeyFrameValue = "0.25";
        Assert.True(viewModel.UpdateAnimationKeyFrameCommand.CanExecute(null));
        viewModel.UpdateAnimationKeyFrameCommand.Execute(null);

        Assert.Contains(track.KeyFrames, frame => frame.CuePercent == 25 && frame.Value == "0.25");
        Assert.DoesNotContain(track.KeyFrames, frame => frame.CuePercent == 100);
    }

    [Fact]
    public void AnimationTimelineKeyFrameUpdate_AvoidsDuplicateCuePositions()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(100, "1", string.Empty)
            });

        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTimelineTrack = track;
        viewModel.SelectedAnimationTimelineKeyFrame = track.KeyFrames[0];
        viewModel.AnimationCuePercent = 100;
        viewModel.AnimationKeyFrameValue = "0.9";

        viewModel.UpdateAnimationKeyFrameCommand.Execute(null);

        Assert.Equal(2, track.KeyFrames.Select(static frame => frame.CuePercent).Distinct().Count());
        Assert.DoesNotContain(
            track.KeyFrames.GroupBy(static frame => frame.CuePercent),
            group => group.Count() > 1);
        Assert.Equal(99, viewModel.SelectedAnimationTimelineKeyFrame?.CuePercent);
        Assert.Equal("0.9", viewModel.SelectedAnimationTimelineKeyFrame?.Value);
    }

    [Fact]
    public void AnimationTimelineKeyFrameCommit_UsesDraggedCueAndAvoidsDuplicatePositions()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(50, "0.5", string.Empty)
            });

        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTimelineTrack = track;
        viewModel.SelectedAnimationTimelineKeyFrame = track.KeyFrames[0];
        track.KeyFrames[0].CuePercent = 50;
        track.KeyFrames[0].Value = "0.25";

        viewModel.CommitAnimationKeyFrameEditCommand.Execute(null);

        Assert.Equal(new[] { 50, 51 }, track.KeyFrames.Select(static frame => frame.CuePercent));
        Assert.Equal(51, viewModel.SelectedAnimationTimelineKeyFrame?.CuePercent);
        Assert.Equal(51, viewModel.AnimationCuePercent);
        Assert.Equal("0.25", viewModel.SelectedAnimationTimelineKeyFrame?.Value);
    }

    [Fact]
    public void AnimationTimelineKeyboardCommands_NudgeSeekAndPlayback()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(50, "0.5", string.Empty),
                new(100, "1", string.Empty)
            });

        viewModel.SelectedAnimationTargetOption = new AnimationTargetOptionViewModel("visual-selection", "Selected Button", "Visual", "^");
        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
        viewModel.SelectedAnimationTimelineTrack = track;
        viewModel.SelectedAnimationTimelineKeyFrame = track.KeyFrames[1];

        Assert.True(viewModel.NudgeAnimationKeyFrameRightCommand.CanExecute(null));
        viewModel.NudgeAnimationKeyFrameRightCommand.Execute(null);
        Assert.Equal(51, viewModel.SelectedAnimationTimelineKeyFrame?.CuePercent);

        viewModel.NudgeAnimationKeyFrameLeftLargeCommand.Execute(null);
        Assert.Equal(41, viewModel.SelectedAnimationTimelineKeyFrame?.CuePercent);
        Assert.Equal("41%", viewModel.AnimationCurrentTimeText);

        Assert.True(viewModel.SeekAnimationEndCommand.CanExecute(null));
        viewModel.SeekAnimationEndCommand.Execute(null);
        Assert.Equal(100, viewModel.AnimationCurrentTimePercent);

        Assert.True(viewModel.PlayAnimationTimelineCommand.CanExecute(null));
        viewModel.AnimationDurationText = "0:0:1";
        viewModel.PlayAnimationTimelineCommand.Execute(null);
        Assert.True(viewModel.AnimationTimelinePlaying);
        Assert.Equal(0, viewModel.AnimationCurrentTimePercent);
        Assert.Equal("Pause", viewModel.AnimationPlaybackButtonText);

        Assert.True(viewModel.StopAnimationTimelineCommand.CanExecute(null));
        viewModel.StopAnimationTimelineCommand.Execute(null);
        Assert.False(viewModel.AnimationTimelinePlaying);
        Assert.Equal("Play", viewModel.AnimationPlaybackButtonText);
    }

    [Fact]
    public void AnimationTimelineFrameSetters_InterpolateEditableValues()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel>
        {
            new(
                "^",
                "Opacity",
                new ObservableCollection<AnimationTimelineKeyFrameViewModel>
                {
                    new(0, "0", string.Empty),
                    new(100, "1", string.Empty)
                }),
            new(
                "^",
                "Margin",
                new ObservableCollection<AnimationTimelineKeyFrameViewModel>
                {
                    new(0, "0,0,0,0", string.Empty),
                    new(100, "10,20,30,40", string.Empty)
                })
        };
        viewModel.AnimationCurrentTimePercent = 50;

        var setters = viewModel.GetAnimationFrameSettersForCurrentTime();

        Assert.Contains(setters, setter => setter.PropertyName == "Opacity" && setter.Value == "0.5");
        Assert.Contains(setters, setter => setter.PropertyName == "Margin" && setter.Value == "5,10,15,20");
    }

    [Fact]
    public void AnimationTimeline_CanEditDocumentStyleTarget()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        Assert.NotNull(viewModel.ActiveXamlFile);
        var xamlFile = viewModel.ActiveXamlFile;
        xamlFile.Text = """
                        <Grid xmlns="https://github.com/avaloniaui">
                          <Grid.Styles>
                            <Style Selector="Button.primary" />
                          </Grid.Styles>
                          <Button Classes="primary" />
                        </Grid>
                        """;

        viewModel.RefreshVisualEditorCommand.Execute(null);
        var styleTarget = Assert.Single(
            viewModel.AnimationTargetOptions,
            option => option.Scope == "Style" && option.Selector == "Button.primary");
        viewModel.SelectedAnimationTargetOption = styleTarget;
        viewModel.AnimationPropertyName = "Opacity";
        viewModel.AnimationKeyFrameValue = "1";

        viewModel.AddAnimationTrackCommand.Execute(null);
        viewModel.ApplyAnimationTimelineCommand.Execute(null);

        Assert.Contains("<Style Selector=\"Button.primary\">", xamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("<Style.Animations>", xamlFile.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"1\" />", xamlFile.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineTrack_KeySummaryTracksKeyFrameEdits()
    {
        var keyFrame = new AnimationTimelineKeyFrameViewModel(0, "0", string.Empty);
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel> { keyFrame });
        var changed = false;
        track.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AnimationTimelineTrackViewModel.KeySummary))
            {
                changed = true;
            }
        };

        keyFrame.Value = "0.5";

        Assert.True(changed);
        Assert.Equal("0% = 0.5", track.KeySummary);

        changed = false;
        track.KeyFrames.Clear();
        Assert.True(changed);

        changed = false;
        keyFrame.Value = "1";
        Assert.False(changed);
    }

    [Fact]
    public void AnimationTimelineTrack_ToDefinitionDeduplicatesCueCollisions()
    {
        var track = new AnimationTimelineTrackViewModel(
            "^",
            "Opacity",
            new ObservableCollection<AnimationTimelineKeyFrameViewModel>
            {
                new(0, "0", string.Empty),
                new(50, "0.4", string.Empty),
                new(50, "0.5", string.Empty),
                new(100, "1", string.Empty)
            });

        var definition = track.ToDefinition();

        Assert.Equal(new[] { 0, 50, 100 }, definition.KeyFrames.Select(static frame => frame.CuePercent));
        Assert.Equal("0.5", Assert.Single(definition.KeyFrames, frame => frame.CuePercent == 50).Value);
        Assert.Equal(420, track.KeyFrames[^1].TimelineLeft);
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
    public void AnimationTimelineDockView_RendersForVisualAndThemeContexts()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureDockTestApplicationResources();

            var viewModel = new MainViewModel(null);
            var visualView = new AnimationTimelineDockView
            {
                Width = 360,
                Height = 640,
                DataContext = new VisualAnimationsDockViewModel(viewModel)
            };
            var themeView = new AnimationTimelineDockView
            {
                Width = 360,
                Height = 640,
                DataContext = new ControlThemeAnimationsDockViewModel(viewModel)
            };
            var window = new Window
            {
                Width = 760,
                Height = 700,
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Children =
                    {
                        visualView,
                        themeView
                    }
                }
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.NotEmpty(visualView.GetVisualDescendants().OfType<Slider>());
                Assert.NotEmpty(themeView.GetVisualDescendants().OfType<Slider>());
                Assert.Contains(
                    visualView.GetVisualDescendants().OfType<ComboBox>(),
                    comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.AnimationTargetOptions));
                Assert.Contains(
                    themeView.GetVisualDescendants().OfType<ComboBox>(),
                    comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.AnimationTargetOptions));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnimationTimelineSheetDockView_RendersClassicalTimelineTool()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureDockTestApplicationResources();

            var viewModel = new MainViewModel(null);
            var track = new AnimationTimelineTrackViewModel(
                "^",
                "Opacity",
                new ObservableCollection<AnimationTimelineKeyFrameViewModel>
                {
                    new(0, "0", string.Empty),
                    new(60, "1", string.Empty)
                });
            viewModel.AnimationTimelineTracks = new ObservableCollection<AnimationTimelineTrackViewModel> { track };
            viewModel.SelectedAnimationTimelineTrack = track;
            viewModel.SelectedAnimationTimelineKeyFrame = track.KeyFrames[1];

            var view = new AnimationTimelineSheetDockView
            {
                Width = 900,
                Height = 340,
                DataContext = new AnimationTimelineSheetDockViewModel(viewModel)
            };
            var window = new Window
            {
                Width = 940,
                Height = 380,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var timeline = Assert.Single(view.GetVisualDescendants().OfType<AnimationKeyFrameTimelineControl>());
                Assert.Same(viewModel.AnimationTimelineTracks, timeline.Tracks);
                Assert.Same(track, timeline.SelectedTrack);
                var preview = Assert.Single(view.GetVisualDescendants().OfType<AnimationMockPreviewControl>());
                Assert.Same(viewModel.AnimationTimelineTracks, preview.Tracks);
                Assert.Contains(
                    view.GetVisualDescendants().OfType<ComboBox>(),
                    comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.AnimationTargetOptions));
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
    public void SolutionExplorerSearch_FiltersTreeToMatchingAncestorPath()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);

        viewModel.SolutionExplorerSearchText = "Resources.axaml";

        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "Main.axaml"));

        viewModel.ApplySolutionExplorerSearchNow();

        var solutionNode = Assert.Single(viewModel.SolutionExplorerNodes);
        var projectNode = Assert.Single(solutionNode.Children);
        var folderNode = Assert.Single(projectNode.Children);
        var fileNode = Assert.Single(folderNode.Children);
        Assert.True(solutionNode.IsExpanded);
        Assert.True(projectNode.IsExpanded);
        Assert.True(folderNode.IsExpanded);
        Assert.Equal("Styles", folderNode.Title);
        Assert.Equal("Resources.axaml", fileNode.Title);
        Assert.Null(FindNode(viewModel.SolutionExplorerNodes, "Main.axaml"));

        viewModel.SolutionExplorerSearchText = string.Empty;

        viewModel.ApplySolutionExplorerSearchNow();

        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "Main.axaml"));
    }

    [Fact]
    public void SolutionExplorerSearch_DirectMatchesReuseSourceSubtree()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var sourceRoot = Assert.Single(viewModel.SolutionExplorerNodes);

        viewModel.SolutionExplorerSearchText = sourceRoot.Title;

        viewModel.ApplySolutionExplorerSearchNow();

        Assert.Same(sourceRoot, Assert.Single(viewModel.SolutionExplorerNodes));
    }

    [Fact]
    public void SolutionExplorerRegexSearch_FiltersTreeToMatchingAncestorPath()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            SolutionExplorerSearchUseRegex = true,
            SolutionExplorerSearchText = @"Resources\.a?xaml"
        };

        viewModel.ApplySolutionExplorerSearchNow();

        var solutionNode = Assert.Single(viewModel.SolutionExplorerNodes);
        var projectNode = Assert.Single(solutionNode.Children);
        var folderNode = Assert.Single(projectNode.Children);
        var fileNode = Assert.Single(folderNode.Children);
        Assert.False(viewModel.HasSolutionExplorerSearchError);
        Assert.Null(viewModel.SolutionExplorerSearchError);
        Assert.Equal("Styles", folderNode.Title);
        Assert.Equal("Resources.axaml", fileNode.Title);
        Assert.Null(FindNode(viewModel.SolutionExplorerNodes, "Main.axaml"));
    }

    [Fact]
    public void SolutionExplorerRegexSearch_InvalidPatternShowsErrorAndLeavesTreeVisible()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            SolutionExplorerSearchUseRegex = true,
            SolutionExplorerSearchText = "["
        };

        viewModel.ApplySolutionExplorerSearchNow();

        Assert.True(viewModel.HasSolutionExplorerSearchError);
        Assert.StartsWith("Invalid regex:", viewModel.SolutionExplorerSearchError, StringComparison.Ordinal);
        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "Main.axaml"));
        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "Resources.axaml"));
    }

    [Fact]
    public void SolutionExplorerSearch_SeesThemeFilesAddedDuringActiveSearch()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        viewModel.SolutionExplorerSearchText = "NewTheme.axaml";
        viewModel.ApplySolutionExplorerSearchNow();
        Assert.Null(FindNode(viewModel.SolutionExplorerNodes, "NewTheme.axaml"));
        var themeFile = project.AddFile(new InMemoryProjectFile(
            "Themes/NewTheme.axaml",
            "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" />",
            ProjectFileKind.Resource));
        var method = typeof(MainViewModel).GetMethod(
            "RefreshWorkspaceAfterThemeFileChanges",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method.Invoke(viewModel, new object?[] { themeFile });

        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "NewTheme.axaml"));

        viewModel.SolutionExplorerSearchText = string.Empty;
        viewModel.ApplySolutionExplorerSearchNow();

        Assert.NotNull(FindNode(viewModel.SolutionExplorerNodes, "NewTheme.axaml"));
    }

    [Fact]
    public void SolutionExplorerNodeCommands_ExpandAndCollapseSubtree()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var solutionNode = Assert.Single(viewModel.SolutionExplorerNodes);

        solutionNode.CollapseRecursiveCommand.Execute(null);

        Assert.All(FlattenNodes(viewModel.SolutionExplorerNodes), static node => Assert.False(node.IsExpanded));

        solutionNode.ExpandRecursiveCommand.Execute(null);

        Assert.All(FlattenNodes(viewModel.SolutionExplorerNodes), static node => Assert.True(node.IsExpanded));

        solutionNode.CollapseCommand.Execute(null);

        Assert.False(solutionNode.IsExpanded);
        Assert.All(solutionNode.Children, static node => Assert.True(node.IsExpanded));
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
    public void ThemeProjectSourceLoader_LoadsThemeFolderThroughThemeProjectFormat()
    {
        var themeRoot = CreateTemporaryFluentThemeRoot();

        try
        {
            var source = ThemeProjectSourceLoader.LoadFromDirectory(themeRoot);
            var catalog = new FluentControlThemeCatalog(source);

            Assert.Equal(themeRoot, source.SourceRoot);
            Assert.Contains(source.Project.Files, file => file.Path == "FluentTheme.xaml");
            Assert.Contains(source.Project.Files, file => file.Path == "Controls/Button.xaml");
            Assert.Contains(catalog.Templates, template =>
                template.TargetType == "Button" &&
                template.SourcePath == "Controls/Button.xaml");
        }
        finally
        {
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ThemeProjectSourceLoader_LoadsBundledFluentThemeProject()
    {
        var source = ThemeProjectSourceLoader.LoadEmbeddedFluentThemeProject();
        var catalog = new FluentControlThemeCatalog(source);

        Assert.Null(source.SourceRoot);
        Assert.Contains(source.Project.Files, file => file.Path == "FluentTheme.xaml");
        Assert.Contains(source.Project.Files, file => file.Path == "Controls/Button.xaml");
        Assert.Contains(catalog.Templates, template => template.TargetType == "Button");
    }

    [Fact]
    public void FluentControlThemeCatalog_PrefersConcreteTemplateOverBasedOnAlias()
    {
        var source = CreateMaterialLikeThemeSource();
        var catalog = new FluentControlThemeCatalog(source);

        var template = catalog.FindDefaultTemplate("CheckBox");

        Assert.NotNull(template);
        Assert.Equal("MaterialCheckBox", template.Key);
    }

    [Fact]
    public void FluentControlThemeCatalog_PreservesRootNamespacesUsedInMemberValues()
    {
        var source = CreateMaterialLikeThemeSource();
        var catalog = new FluentControlThemeCatalog(source);
        var template = Assert.Single(catalog.Templates, template => template.Key == "MaterialCheckBox");

        var xaml = ControlThemeResourceBuilder.CreateResourceDictionary(template, "MyCheckBoxTheme1");

        Assert.Contains("xmlns:assists=\"clr-namespace:Material.Styles.Assists\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"assists:SelectionControlAssist.Size\"", xaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:ripple=\"clr-namespace:Material.Ripple;assembly=Material.Ripple\"", xaml, StringComparison.Ordinal);
        XDocument.Parse(xaml);
    }

    [Fact]
    public void CreateCustomControlThemeCommand_IsolatesExternalSourceTemplatesFromRuntimePreview()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = true
        };
        var source = CreateMaterialLikeThemeSource();
        var catalog = new FluentControlThemeCatalog(source);
        var catalogField = typeof(MainViewModel).GetField(
            "_controlThemeCatalog",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(catalogField);
        catalogField.SetValue(viewModel, catalog);
        var loadTemplates = typeof(MainViewModel).GetMethod(
            "LoadFluentControlThemeTemplates",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadTemplates);
        loadTemplates.Invoke(viewModel, null);
        viewModel.SelectedFluentControlThemeTemplate = Assert.Single(
            viewModel.FluentControlThemeTemplates,
            template => template.Key == "MaterialCheckBox");

        Assert.True(viewModel.CreateCustomControlThemeCommand.CanExecute(null));
        viewModel.CreateCustomControlThemeCommand.Execute(null);

        var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyCheckBoxTheme1.axaml");
        Assert.NotNull(themeFile);
        Assert.False(themeFile.IncludeInRuntimePreview);
        Assert.Contains("xmlns:assists=\"clr-namespace:Material.Styles.Assists\"", themeFile.Text, StringComparison.Ordinal);
        Assert.Contains("xmlns:ripple=\"clr-namespace:Material.Ripple;assembly=Material.Ripple\"", themeFile.Text, StringComparison.Ordinal);
        Assert.Same(themeFile, viewModel.ActiveXamlFile);
        Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "MyCheckBoxTheme1");
        Assert.Contains("isolated", viewModel.ControlThemeStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsolatedControlThemeEdits_DoNotSchedulePreview()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var source = CreateMaterialLikeThemeSource();
        var catalog = new FluentControlThemeCatalog(source);
        var catalogField = typeof(MainViewModel).GetField(
            "_controlThemeCatalog",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(catalogField);
        catalogField.SetValue(viewModel, catalog);
        var loadTemplates = typeof(MainViewModel).GetMethod(
            "LoadFluentControlThemeTemplates",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadTemplates);
        loadTemplates.Invoke(viewModel, null);
        viewModel.SelectedFluentControlThemeTemplate = Assert.Single(
            viewModel.FluentControlThemeTemplates,
            template => template.Key == "MaterialCheckBox");
        viewModel.CreateCustomControlThemeCommand.Execute(null);
        var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyCheckBoxTheme1.axaml");
        Assert.NotNull(themeFile);
        Assert.Same(themeFile, viewModel.ActiveXamlFile);
        Assert.False(themeFile.IncludeInRuntimePreview);

        var timerField = typeof(MainViewModel).GetField(
            "_timer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(timerField);
        Assert.Null(timerField.GetValue(viewModel));

        viewModel.EnableAutoRun = true;
        themeFile.Text += Environment.NewLine;

        Assert.Null(timerField.GetValue(viewModel));
        Assert.Null(viewModel.LastErrorMessage);
    }

    [Fact]
    public async Task ThemeProjectSourceLoader_RejectsNonHttpsRepositoryUrls()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => ThemeProjectSourceLoader.LoadFromRemoteGitRepositoryAsync("http://github.com/owner/theme"));
    }

    [Fact]
    public void ThemeProjectSourceLoader_SelectsNestedFluentThemeRoot()
    {
        var method = typeof(ThemeProjectSourceLoader).GetMethod(
            "SelectPreferredThemeRootFiles",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var files = new (string Path, string Text)[]
        {
            ("repository/src/Avalonia.Themes.Fluent/FluentTheme.xaml", "<ResourceDictionary />"),
            ("repository/src/Avalonia.Themes.Fluent/Controls/Button.xaml", "<ResourceDictionary />"),
            ("repository/samples/Unrelated.xaml", "<UserControl />")
        };

        var result = ((IEnumerable<(string Path, string Text)>)method.Invoke(null, new object[] { files })!).ToArray();

        Assert.Collection(
            result,
            file => Assert.Equal("FluentTheme.xaml", file.Path),
            file => Assert.Equal("Controls/Button.xaml", file.Path));
    }

    [Fact]
    public void ApplyControlThemeProject_PreservesFluentCatalogWhenLoadingSavedThemeProject()
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
            Assert.Contains(viewModel.FluentControlThemeTemplates, template => template.TargetType == "Button");

            var themeProject = ThemeProjectStorage.CreateDocument(
                "SavedTheme",
                new[]
                {
                    (
                        "Themes/SavedButton.axaml",
                        """
                        <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                          <ControlTheme x:Key="SavedButtonTheme" TargetType="Button" />
                        </ResourceDictionary>
                        """)
                });
            var method = typeof(MainViewModel).GetMethod(
                "ApplyControlThemeProject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var loadedCount = Assert.IsType<int>(method.Invoke(
                viewModel,
                new object[] { viewModel.ActiveProject!, themeProject, "Theme project: SavedTheme", false }));

            Assert.Equal(1, loadedCount);
            Assert.Contains(viewModel.FluentControlThemeTemplates, template => template.TargetType == "Button");
            Assert.Contains(viewModel.ControlThemes, theme => theme.Key == "SavedButtonTheme");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH", previousThemeRoot);
            Directory.Delete(themeRoot, recursive: true);
        }
    }

    [Fact]
    public void ApplyControlThemeProject_IsolatesExternalClrThemeResources()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var themeProject = ThemeProjectStorage.CreateDocument(
            "SavedMaterialTheme",
            new[]
            {
                (
                    "Themes/SavedMaterialButton.axaml",
                    """
                    <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                        xmlns:assists="clr-namespace:Material.Styles.Assists">
                      <ControlTheme x:Key="SavedMaterialButtonTheme" TargetType="Button">
                        <Setter Property="assists:ButtonAssist.CornerRadius" Value="4" />
                      </ControlTheme>
                    </ResourceDictionary>
                    """)
            });
        var method = typeof(MainViewModel).GetMethod(
            "ApplyControlThemeProject",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var loadedCount = Assert.IsType<int>(method.Invoke(
            viewModel,
            new object[] { viewModel.ActiveProject!, themeProject, "Theme project: SavedMaterialTheme", false }));

        Assert.Equal(1, loadedCount);
        var file = viewModel.ActiveProject!.FindFile("Themes/SavedMaterialButton.axaml");
        Assert.NotNull(file);
        Assert.False(file.IncludeInRuntimePreview);
    }

    [Fact]
    public void ApplyControlThemeProject_KeepsSameProjectClrNamespacesInRuntimePreview()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null)
        {
            EnableAutoRun = false
        };
        var project = viewModel.ActiveProject!;
        var themeProject = ThemeProjectStorage.CreateDocument(
            "SavedLocalTheme",
            new[]
            {
                (
                    "Themes/SavedLocalButton.axaml",
                    $$"""
                    <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                        xmlns:local="clr-namespace:{{project.RootNamespace}}.Converters">
                      <ControlTheme x:Key="SavedLocalButtonTheme" TargetType="Button">
                        <Setter Property="Tag" Value="{x:Static local:ThemeConverter.Instance}" />
                      </ControlTheme>
                    </ResourceDictionary>
                    """)
            });
        var method = typeof(MainViewModel).GetMethod(
            "ApplyControlThemeProject",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var loadedCount = Assert.IsType<int>(method.Invoke(
            viewModel,
            new object[] { project, themeProject, "Theme project: SavedLocalTheme", false }));

        Assert.Equal(1, loadedCount);
        var file = project.FindFile("Themes/SavedLocalButton.axaml");
        Assert.NotNull(file);
        Assert.True(file.IncludeInRuntimePreview);
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
    public void InMemorySolutionFactory_AddOrUpdateResource_ReplacesResourceWhenPreviewIsolationChanges()
    {
        var solutionFactory = new InMemorySolutionFactory(_ => { });
        var solution = solutionFactory.CreateSolution("ThemeApp", AvaloniaProjectTemplates.All[0]);
        var project = Assert.Single(solution.Projects);
        var file = solutionFactory.AddOrUpdateResource(project, "Themes/MyButtonTheme1.axaml", "<ResourceDictionary />");

        var isolatedFile = solutionFactory.AddOrUpdateResource(
            project,
            "Themes/MyButtonTheme1.axaml",
            "<ResourceDictionary><SolidColorBrush x:Key=\"Brush\" /></ResourceDictionary>",
            includeInRuntimePreview: false);

        Assert.NotSame(file, isolatedFile);
        Assert.Equal("Themes/MyButtonTheme1.axaml", isolatedFile.Path);
        Assert.False(isolatedFile.IncludeInRuntimePreview);
        Assert.DoesNotContain(project.Files, candidate => ReferenceEquals(candidate, file));
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
    public void ProjectResources_LoadDependenciesBeforeConsumers()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            const string themeXaml = """
                                     <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                       <Design.PreviewWith>
                                         <Button Theme="{StaticResource MyButtonTheme}" />
                                       </Design.PreviewWith>
                                       <ControlTheme x:Key="MyButtonTheme" TargetType="Button">
                                         <Setter Property="Background">
                                           <Setter.Value>
                                             <StaticResource ResourceKey="AccentBrush" />
                                           </Setter.Value>
                                         </Setter>
                                       </ControlTheme>
                                     </ResourceDictionary>
                                     """;
            const string colorsXaml = """
                                      <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                        <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                                      </ResourceDictionary>
                                      """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();

            try
            {
                RuntimeXamlPreviewLoader.ApplyProjectResources(
                    new[]
                    {
                        ("Themes/Button.axaml", themeXaml),
                        ("Themes/Colors.axaml", colorsXaml)
                    },
                    localAssembly: null,
                    diagnostics);
                var preview = RuntimeXamlPreviewLoader.LoadResourceDictionaryPreview(
                    themeXaml,
                    localAssembly: null,
                    "Themes/Button.axaml",
                    diagnostics);

                Assert.NotNull(preview);
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity >= RuntimeXamlDiagnosticSeverity.Error);
            }
            finally
            {
                RuntimeXamlPreviewLoader.ApplyProjectResources(
                    Array.Empty<(string Path, string Text)>(),
                    localAssembly: null,
                    new List<RuntimeXamlDiagnostic>());
            }
        });
    }

    [Fact]
    public void ProjectResources_LoadRelativeMergeResourceIncludesWithDocumentUri()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            const string paletteXaml = """
                                       <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                         <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                                       </ResourceDictionary>
                                       """;
            const string resourcesXaml = """
                                         <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                           <ResourceDictionary.MergedDictionaries>
                                             <MergeResourceInclude Source="Palette.axaml" />
                                           </ResourceDictionary.MergedDictionaries>
                                         </ResourceDictionary>
                                         """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();

            try
            {
                RuntimeXamlPreviewLoader.ApplyProjectResources(
                    new[]
                    {
                        ("Themes/Resources.axaml", resourcesXaml),
                        ("Themes/Palette.axaml", paletteXaml)
                    },
                    localAssembly: null,
                    diagnostics,
                    documentAssemblyName: "DemoApp");

                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity >= RuntimeXamlDiagnosticSeverity.Error);
            }
            finally
            {
                RuntimeXamlPreviewLoader.ApplyProjectResources(
                    Array.Empty<(string Path, string Text)>(),
                    localAssembly: null,
                    new List<RuntimeXamlDiagnostic>());
            }
        });
    }

    [Fact]
    public void RuntimePreview_LoadsControlWithRelativeResourceIncludeFromWorkspaceResources()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            const string colorsXaml = """
                                      <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                        <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                                      </ResourceDictionary>
                                      """;
            const string viewXaml = """
                                    <UserControl xmlns="https://github.com/avaloniaui"
                                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                      <UserControl.Resources>
                                        <ResourceDictionary>
                                          <ResourceDictionary.MergedDictionaries>
                                            <ResourceInclude Source="Resources/Colors.axaml" />
                                          </ResourceDictionary.MergedDictionaries>
                                        </ResourceDictionary>
                                      </UserControl.Resources>
                                      <Border Name="RootBorder" Background="{StaticResource AccentBrush}" />
                                    </UserControl>
                                    """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();

            var preview = RuntimeXamlPreviewLoader.LoadControl(
                viewXaml,
                localAssembly: null,
                fallbackRootTypeName: null,
                documentName: "Views/MainView.axaml",
                diagnostics,
                resourceFiles: new[] { ("Views/Resources/Colors.axaml", colorsXaml) },
                documentAssemblyName: "DemoApp");

            Assert.NotNull(preview);
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
    public void CreateCustomControlThemeCommand_FallsBackToSelectedFluentTemplateForUnsupportedSelection()
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
                                <TextBlock Text="Unsupported selected container" />
                              </StackPanel>
                            </UserControl>
                            """;
            var stackPanelStart = mainFile.Text.IndexOf("<StackPanel", StringComparison.Ordinal);
            var template = Assert.Single(
                viewModel.FluentControlThemeTemplates,
                candidate => candidate.TargetType == "Button");

            Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, stackPanelStart, 0, stackPanelStart));
            viewModel.SelectedFluentControlThemeTemplate = template;

            Assert.True(viewModel.CreateCustomControlThemeCommand.CanExecute(null));
            viewModel.CreateCustomControlThemeCommand.Execute(null);

            var themeFile = viewModel.ActiveProject!.FindFile("Themes/MyButtonTheme1.axaml");
            Assert.NotNull(themeFile);
            Assert.Contains("x:Key=\"MyButtonTheme1\"", themeFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Theme=\"{StaticResource MyButtonTheme1}\"", mainFile.Text, StringComparison.Ordinal);
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
    public void ThemeResourceCommands_AllowsRenameCollisionInDifferentThemeScope()
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
                  <SolidColorBrush x:Key="PrimaryBrushLight" Color="White" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="PrimaryBrush" Color="Black" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """,
            ProjectFileKind.Resource));
        RefreshControlThemes(viewModel);

        viewModel.SelectedThemeResource = Assert.Single(viewModel.ThemeResources, resource => resource.Key == "PrimaryBrushLight");
        viewModel.ThemeResourceKeyEditText = "PrimaryBrush";

        Assert.True(viewModel.RenameSelectedThemeResourceCommand.CanExecute(null));
        viewModel.RenameSelectedThemeResourceCommand.Execute(null);

        Assert.Contains("x:Key=\"PrimaryBrush\" Color=\"White\"", themeFile.Text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PrimaryBrush\" Color=\"Black\"", themeFile.Text, StringComparison.Ordinal);
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
              <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
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
        Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
        viewModel.VisualEditorPropertyValue = "{StaticResource ResourceKey='PickerBrush'}";
        Assert.True(viewModel.OpenVisualEditorPropertyResourceCommand.CanExecute(null));
        viewModel.OpenVisualEditorPropertyResourceCommand.Execute(null);
        Assert.Equal("Themes/Palette.axaml", viewModel.ActiveXamlFile!.Path);

        viewModel.ActiveXamlFile = mainFile;
        Assert.True(viewModel.SelectVisualEditorSourceRange(mainFile.Path, buttonStart, 0, buttonStart));
        viewModel.VisualEditorPropertyValue = "{StaticResource {x:Type Button}}";
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
                "Selector=\"^:pointerover /template/ ContentPresenter#PART_ContentPresenter\"",
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

            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
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
                Assert.Equal("Broken sample", errors.LastErrorMessage);

                TextBox? errorTextBox = null;
                for (var i = 0; i < 25; i++)
                {
                    var errorsViews = dockControl.GetVisualDescendants().OfType<ErrorsDockView>().ToArray();
                    var errorsView = errorsViews.FirstOrDefault(view => ReferenceEquals(view.DataContext, errors)) ??
                                     errorsViews.SingleOrDefault();
                    if (errorsView is not null)
                    {
                        errors.NotifyLastErrorMessageChanged();
                        PumpLayout(window);
                    }

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

    private static ThemeProjectSource CreateMaterialLikeThemeSource()
    {
        var project = ThemeProjectStorage.CreateDocument(
            "MaterialLike",
            new[]
            {
                (
                    "Material.Styles/Resources/Themes/CheckBox.axaml",
                    """
                    <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                        xmlns:assists="clr-namespace:Material.Styles.Assists"
                                        xmlns:ripple="clr-namespace:Material.Ripple;assembly=Material.Ripple">
                      <ControlTheme x:Key="MaterialCheckBox" TargetType="CheckBox">
                        <Setter Property="assists:SelectionControlAssist.Size" Value="24" />
                        <Setter Property="Template">
                          <ControlTemplate>
                            <ripple:RippleEffect />
                          </ControlTemplate>
                        </Setter>
                      </ControlTheme>
                      <ControlTheme x:Key="{x:Type CheckBox}" TargetType="CheckBox"
                                    BasedOn="{StaticResource MaterialCheckBox}" />
                    </ResourceDictionary>
                    """)
            });

        return new ThemeProjectSource(project, "Material-like source", SourceRoot: null);
    }

    private static VersionedPreviewProject CreateVersionedPreviewProject(
        string projectName,
        string expectedText,
        string dependencyVersion)
    {
        var dependencyImage = CompileVersionedPreviewDependencyImage(
            "Avalonia.Controls.DataGrid",
            dependencyVersion,
            expectedText);
        var dependencyReference = WorkspaceAssemblyReference.FromImage(
            "Avalonia.Controls.DataGrid.dll",
            dependencyImage,
            isRuntimeAssembly: true);
        Assert.NotNull(dependencyReference);

        var project = new InMemoryProject(projectName, projectName, "integration")
        {
            AssemblyName = projectName,
            CSharpParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            CSharpCompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        };
        project.AssemblyReferences.Add(dependencyReference);
        var xamlFile = project.AddFile(new InMemoryProjectFile(
            "Views/MainView.axaml",
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:collision="clr-namespace:WorkspaceCollisionControls;assembly=Avalonia.Controls.DataGrid">
              <collision:VersionedTextBlock />
            </UserControl>
            """,
            ProjectFileKind.Xaml));
        project.AddFile(new InMemoryProjectFile(
            "PreviewReference.cs",
            """
            using WorkspaceCollisionControls;

            namespace WorkspacePreview;

            public static class PreviewReference
            {
                public static string Text => VersionedTextBlock.VersionText;
            }
            """,
            ProjectFileKind.CSharp));

        return new VersionedPreviewProject(
            project,
            xamlFile,
            expectedText,
            Version.Parse(dependencyVersion));
    }

    private static byte[] CompileVersionedPreviewDependencyImage(
        string assemblyName,
        string version,
        string text)
    {
        var escapedText = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return CompileTestAssemblyImage(
            assemblyName,
            $$"""
            using System.Reflection;
            using Avalonia.Controls;

            [assembly: AssemblyVersion("{{version}}")]

            namespace WorkspaceCollisionControls;

            public sealed class VersionedTextBlock : TextBlock
            {
                public VersionedTextBlock()
                {
                    Text = VersionText;
                }

                public static string VersionText => "{{escapedText}}";
            }
            """,
            new[]
            {
                MetadataReference.CreateFromFile(typeof(Control).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(AvaloniaObject).Assembly.Location)
            });
    }

    private static void LoadSolutionIntoViewModel(MainViewModel viewModel, InMemorySolution solution)
    {
        var method = typeof(MainViewModel).GetMethod(
            "LoadSolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, new object[] { solution });
    }

    private static void ActivateWorkspaceFile(MainViewModel viewModel, InMemoryProjectFile file)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ActivateWorkspaceFileFromDocument",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);
        method.Invoke(viewModel, new object[] { file });
    }

    private static async Task<ScriptCompilationResult> CompileVersionedPreviewProjectAsync(
        VersionedPreviewProject project)
    {
        var result = await CompilerService.GetProjectAssembly(
            project.Project.AssemblyName,
            project.Project.GetCSharpFileSnapshot(),
            project.Project.AssemblyReferences,
            project.Project.CSharpParseOptions,
            project.Project.CSharpCompilationOptions);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        Assert.NotNull(result.Assembly);
        Assert.Contains(result.LoadedAssemblies, assembly =>
            string.Equals(assembly.GetName().Name, "Avalonia.Controls.DataGrid", StringComparison.Ordinal) &&
            assembly.GetName().Version == project.ExpectedVersion);
        return result;
    }

    private static Control LoadVersionedPreviewControl(
        VersionedPreviewProject project,
        ScriptCompilationResult compilation)
    {
        var diagnostics = new List<RuntimeXamlDiagnostic>();
        var control = RuntimeXamlPreviewLoader.LoadControl(
            project.XamlFile.Text,
            compilation.Assembly,
            fallbackRootTypeName: null,
            documentName: project.XamlFile.Path,
            diagnostics: diagnostics,
            documentAssemblyName: project.Project.AssemblyName);

        Assert.NotNull(control);
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity >= RuntimeXamlDiagnosticSeverity.Error);
        return control;
    }

    private static void ShowPreviewForDevTools(MainViewModel viewModel, Control control)
    {
        var root = new Border
        {
            Name = "GeneratedSampleScope",
            Child = control
        };
        viewModel.Control = root;
        viewModel.DiagnosticsRoot = root;
    }

    private static Assembly AssertPreviewAndDevToolsAccess(
        MainViewModel viewModel,
        string expectedText,
        Version expectedVersion)
    {
        var root = Assert.IsType<Border>(viewModel.DiagnosticsRoot);
        Assert.Same(root, viewModel.Control);
        var preview = Assert.IsType<UserControl>(root.Child);
        var versionedTextBlock = Assert.IsAssignableFrom<TextBlock>(preview.Content);
        Assert.Equal(expectedText, versionedTextBlock.Text);

        var previewAssembly = versionedTextBlock.GetType().Assembly;
        Assert.Equal("Avalonia.Controls.DataGrid", previewAssembly.GetName().Name);
        Assert.Equal(expectedVersion, previewAssembly.GetName().Version);
        var previewContext = AssemblyLoadContext.GetLoadContext(previewAssembly);
        Assert.NotNull(previewContext);
        Assert.NotSame(AssemblyLoadContext.Default, previewContext);

        var rootDock = Assert.IsAssignableFrom<IRootDock>(viewModel.DockLayout);
        var diagnosticTreeTools = Enumerate(rootDock).OfType<DiagnosticTreeDockViewModel>().ToArray();
        Assert.NotEmpty(diagnosticTreeTools);
        Assert.All(diagnosticTreeTools, tool =>
        {
            Assert.Same(root, tool.Session.Root);
            Assert.All(
                Enumerate(tool.DockLayout).OfType<DiagnosticSegmentDockViewModel>(),
                segment => Assert.Same(root, segment.Session.Root));
        });

        var diagnosticTools = Enumerate(rootDock).OfType<DiagnosticToolDockViewModel>().ToArray();
        Assert.NotEmpty(diagnosticTools);
        Assert.All(diagnosticTools, tool => Assert.Same(root, tool.Shell.DiagnosticsRoot));

        return previewAssembly;
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
            tool => AssertControlThemeTool<ControlThemeAnimationsDockViewModel>(tool, "ControlThemeAnimations", "Animations", controlThemes.Shell),
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
        Assert.Same(tools[7], factory.ContextLocator["ControlThemeAnimations"]());
        Assert.Same(tools[8], factory.ContextLocator["ControlThemeFluent"]());
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

    private static IEnumerable<SolutionExplorerNodeViewModel> FlattenNodes(
        IEnumerable<SolutionExplorerNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in FlattenNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string GetNetCoreReferenceAssemblyPath(string assemblyName)
    {
        var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent;
        Assert.NotNull(dotnetRoot);

        var referencePackRoot = Path.Combine(dotnetRoot.FullName, "packs", "Microsoft.NETCore.App.Ref");
        Assert.True(Directory.Exists(referencePackRoot), $"Missing .NET reference pack directory: {referencePackRoot}");

        var targetFrameworkFolder = $"ref/net{Environment.Version.Major}.0";
        var path = Directory
            .EnumerateFiles(referencePackRoot, assemblyName + ".dll", SearchOption.AllDirectories)
            .Where(candidate => candidate.Replace('\\', '/').Contains(targetFrameworkFolder, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        Assert.NotNull(path);
        return path;
    }

    private static byte[] CompileTestAssemblyImage(
        string assemblyName,
        string code,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = SyntaxFactory.ParseSyntaxTree(
            code,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            assemblyName + ".cs");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(GetNetCoreReferenceAssemblyPath("System.Runtime"))
        };
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        return stream.ToArray();
    }

    private sealed record VersionedPreviewProject(
        InMemoryProject Project,
        InMemoryProjectFile XamlFile,
        string ExpectedText,
        Version ExpectedVersion);
}

public static class RuntimePreviewDesignData
{
    public const string Message = "Preview namespace value";
}
