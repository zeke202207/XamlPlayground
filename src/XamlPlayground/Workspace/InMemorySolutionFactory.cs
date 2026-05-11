using System;
using System.IO;
using AvaloniaEdit.Document;

namespace XamlPlayground.Workspace;

public sealed class InMemorySolutionFactory
{
    private readonly Action<InMemoryProjectFile> _fileChanged;

    public InMemorySolutionFactory(Action<InMemoryProjectFile> fileChanged)
    {
        _fileChanged = fileChanged;
    }

    public InMemorySolution CreateSampleSolution(string name, TextDocument xaml, TextDocument code)
    {
        var projectName = CreateIdentifier(string.IsNullOrWhiteSpace(name) ? "Sample" : name);
        var solution = new InMemorySolution(projectName);
        var project = new InMemoryProject(projectName, projectName, "playground.sample");
        solution.Projects.Add(project);

        project.AddFile(new InMemoryProjectFile($"{projectName}.csproj", CreateProjectFile(projectName), ProjectFileKind.ProjectFile, _fileChanged));
        project.AddFile(new InMemoryProjectFile("App.axaml", CreateAppXaml(projectName), ProjectFileKind.Xaml, _fileChanged));
        project.AddFile(new InMemoryProjectFile("App.axaml.cs", CreateAppCode(projectName), ProjectFileKind.CSharp, _fileChanged));
        project.AddFile(new InMemoryProjectFile("Main.axaml", xaml.Text, ProjectFileKind.Xaml, _fileChanged, xaml));
        project.AddFile(new InMemoryProjectFile("Main.axaml.cs", code.Text, ProjectFileKind.CSharp, _fileChanged, code));
        project.AddFile(new InMemoryProjectFile("Styles/Resources.axaml", CreateResourcesXaml(), ProjectFileKind.Resource, _fileChanged));

        return solution;
    }

    public InMemorySolution CreateSolution(string solutionName, AvaloniaProjectTemplate template)
    {
        var projectName = CreateIdentifier(solutionName);
        var solution = new InMemorySolution(projectName);
        var project = new InMemoryProject(projectName, projectName, template.ShortName);
        solution.Projects.Add(project);

        project.AddFile(new InMemoryProjectFile($"{projectName}.csproj", CreateProjectFile(projectName, template.SupportsBrowser), ProjectFileKind.ProjectFile, _fileChanged));
        project.AddFile(new InMemoryProjectFile("App.axaml", CreateAppXaml(projectName), ProjectFileKind.Xaml, _fileChanged));
        project.AddFile(new InMemoryProjectFile("App.axaml.cs", CreateAppCode(projectName), ProjectFileKind.CSharp, _fileChanged));
        project.AddFile(new InMemoryProjectFile("Views/MainView.axaml", CreateUserControlXaml(projectName, "MainView", "Hello, world!"), ProjectFileKind.Xaml, _fileChanged));
        project.AddFile(new InMemoryProjectFile("Views/MainView.axaml.cs", CreateUserControlCode(projectName, "MainView"), ProjectFileKind.CSharp, _fileChanged));
        project.AddFile(new InMemoryProjectFile("Styles/Resources.axaml", CreateResourcesXaml(), ProjectFileKind.Resource, _fileChanged));

        if (template.ShortName == "avalonia.mvvm")
        {
            project.AddFile(new InMemoryProjectFile("ViewModels/MainViewModel.cs", CreateViewModelCode(projectName, "MainViewModel"), ProjectFileKind.CSharp, _fileChanged));
        }

        return solution;
    }

    public InMemoryProjectFile AddUserControl(InMemoryProject project)
    {
        var baseName = GetUniqueName(project, "UserControl", ".axaml", "Views");
        var xaml = project.AddFile(new InMemoryProjectFile(
            $"Views/{baseName}.axaml",
            CreateUserControlXaml(project.RootNamespace, baseName, baseName),
            ProjectFileKind.Xaml,
            _fileChanged));
        project.AddFile(new InMemoryProjectFile(
            $"Views/{baseName}.axaml.cs",
            CreateUserControlCode(project.RootNamespace, baseName),
            ProjectFileKind.CSharp,
            _fileChanged));

        return xaml;
    }

    public InMemoryProjectFile AddResourceDictionary(InMemoryProject project)
    {
        var baseName = GetUniqueName(project, "Resources", ".axaml", "Styles");
        return project.AddFile(new InMemoryProjectFile(
            $"Styles/{baseName}.axaml",
            CreateResourcesXaml(),
            ProjectFileKind.Resource,
            _fileChanged));
    }

    public InMemoryProjectFile AddControlThemeResource(
        InMemoryProject project,
        string themeKey,
        string xaml)
    {
        var path = $"Themes/{themeKey}.axaml";
        if (project.FindFile(path) is not null)
        {
            var index = 2;
            while (project.FindFile($"Themes/{themeKey}{index}.axaml") is not null)
            {
                index++;
            }

            path = $"Themes/{themeKey}{index}.axaml";
        }

        return project.AddFile(new InMemoryProjectFile(
            path,
            xaml,
            ProjectFileKind.Resource,
            _fileChanged));
    }

    public InMemoryProjectFile AddOrUpdateResource(
        InMemoryProject project,
        string path,
        string xaml)
    {
        var normalizedPath = NormalizeResourcePath(path);
        if (project.FindFile(normalizedPath) is { } existing)
        {
            existing.Text = xaml;
            return existing;
        }

        return project.AddFile(new InMemoryProjectFile(
            normalizedPath,
            xaml,
            ProjectFileKind.Resource,
            _fileChanged));
    }

    public InMemoryProjectFile AddImportedThemeResource(
        InMemoryProject project,
        string fileName,
        string xaml)
    {
        var safeFileName = CreateSafeThemeFileName(fileName);
        var path = CreateUniqueResourcePath(project, $"Themes/{safeFileName}");

        return project.AddFile(new InMemoryProjectFile(
            path,
            xaml,
            ProjectFileKind.Resource,
            _fileChanged));
    }

    private static string GetUniqueName(InMemoryProject project, string prefix, string extension, string folder)
    {
        var index = 1;
        while (project.FindFile($"{folder}/{prefix}{index}{extension}") is not null)
        {
            index++;
        }

        return $"{prefix}{index}";
    }

    private static string CreateUniqueResourcePath(InMemoryProject project, string preferredPath)
    {
        var normalizedPath = NormalizeResourcePath(preferredPath);
        if (project.FindFile(normalizedPath) is null)
        {
            return normalizedPath;
        }

        var folder = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        var extension = Path.GetExtension(normalizedPath);
        var name = Path.GetFileNameWithoutExtension(normalizedPath);
        var index = 2;
        string candidate;
        do
        {
            var fileName = $"{name}{index}{extension}";
            candidate = string.IsNullOrWhiteSpace(folder)
                ? fileName
                : $"{folder}/{fileName}";
            index++;
        }
        while (project.FindFile(candidate) is not null);

        return candidate;
    }

    private static string NormalizeResourcePath(string path)
    {
        var normalizedPath = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            normalizedPath.Contains("../", StringComparison.Ordinal) ||
            normalizedPath.Equals("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedPath))
        {
            normalizedPath = "Themes/Theme.axaml";
        }

        return normalizedPath;
    }

    private static string CreateSafeThemeFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "ImportedTheme";
        }

        if (!extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".axaml";
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        return $"{name}{extension}";
    }

    private static string CreateIdentifier(string value)
    {
        var identifier = string.Empty;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                identifier += ch;
            }
        }

        return string.IsNullOrWhiteSpace(identifier) ? "App1" : identifier;
    }

    private static string CreateProjectFile(string projectName, bool browser = true)
    {
        var outputType = browser ? "Exe" : "WinExe";
        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            $"    <OutputType>{outputType}</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>\n" +
            $"    <RootNamespace>{projectName}</RootNamespace>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"Avalonia\" Version=\"12.0.2\" />\n" +
            "    <PackageReference Include=\"Avalonia.Themes.Fluent\" Version=\"12.0.2\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
    }

    private static string CreateAppXaml(string projectName)
    {
        return
            "<Application xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"             x:Class=\"{projectName}.App\">\n" +
            "  <Application.Styles>\n" +
            "    <FluentTheme />\n" +
            "  </Application.Styles>\n" +
            "</Application>\n";
    }

    private static string CreateAppCode(string projectName)
    {
        return
            "using Avalonia;\n" +
            "using Avalonia.Controls.ApplicationLifetimes;\n" +
            "using Avalonia.Markup.Xaml;\n" +
            "\n" +
            $"namespace {projectName};\n" +
            "\n" +
            "public partial class App : Application\n" +
            "{\n" +
            "    public override void Initialize()\n" +
            "    {\n" +
            "        AvaloniaXamlLoader.Load(this);\n" +
            "    }\n" +
            "\n" +
            "    public override void OnFrameworkInitializationCompleted()\n" +
            "    {\n" +
            "        base.OnFrameworkInitializationCompleted();\n" +
            "    }\n" +
            "}\n";
    }

    private static string CreateUserControlXaml(string projectName, string className, string text)
    {
        return
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"             x:Class=\"{projectName}.Views.{className}\">\n" +
            "  <Grid RowDefinitions=\"Auto,Auto\" Margin=\"24\">\n" +
            $"    <TextBlock Text=\"{text}\" FontSize=\"24\" />\n" +
            "    <Button Grid.Row=\"1\" Name=\"ActionButton\" Content=\"Click me\" Margin=\"0,16,0,0\" HorizontalAlignment=\"Left\" />\n" +
            "  </Grid>\n" +
            "</UserControl>\n";
    }

    private static string CreateUserControlCode(string projectName, string className)
    {
        return
            "using Avalonia.Controls;\n" +
            "using Avalonia.Interactivity;\n" +
            "\n" +
            $"namespace {projectName}.Views;\n" +
            "\n" +
            $"public partial class {className} : UserControl\n" +
            "{\n" +
            "    private int _clicks;\n" +
            "\n" +
            $"    public {className}()\n" +
            "    {\n" +
            "    }\n" +
            "\n" +
            "    protected override void OnLoaded(RoutedEventArgs e)\n" +
            "    {\n" +
            "        base.OnLoaded(e);\n" +
            "        if (this.FindControl<Button>(\"ActionButton\") is { } button)\n" +
            "        {\n" +
            "            button.Click += (_, _) => button.Content = $\"Clicked {_clicks += 1}\";\n" +
            "        }\n" +
            "    }\n" +
            "}\n";
    }

    private static string CreateResourcesXaml()
    {
        return
            "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <SolidColorBrush x:Key=\"AccentBrush\" Color=\"#0A84FF\" />\n" +
            "</ResourceDictionary>\n";
    }

    private static string CreateViewModelCode(string projectName, string className)
    {
        return
            $"namespace {projectName}.ViewModels;\n" +
            "\n" +
            $"public sealed class {className}\n" +
            "{\n" +
            "    public string Greeting { get; } = \"Hello from the view model\";\n" +
            "}\n";
    }
}
