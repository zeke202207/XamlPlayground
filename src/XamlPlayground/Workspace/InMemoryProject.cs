using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace XamlPlayground.Workspace;

public sealed class InMemoryProject
{
    public InMemoryProject(
        string name,
        string rootNamespace,
        string templateShortName,
        string? projectFilePath = null)
    {
        Name = name;
        RootNamespace = rootNamespace;
        TemplateShortName = templateShortName;
        ProjectFilePath = NormalizeProjectFilePath(projectFilePath);
        AssemblyName = name;
    }

    public string Name { get; }

    public string RootNamespace { get; }

    public string TemplateShortName { get; }

    public string? ProjectFilePath { get; }

    public string AssemblyName { get; set; }

    public string? OutputAssemblyPath { get; set; }

    public string? TargetFramework { get; set; }

    public string? WorkspaceRootPath { get; set; }

    public string? SolutionFolderPath { get; set; }

    public bool IsMsBuildWorkspace { get; set; }

    public ObservableCollection<InMemoryProjectFile> Files { get; } = new();

    public ObservableCollection<WorkspaceAssemblyReference> AssemblyReferences { get; } = new();

    public InMemoryProjectFile AddFile(InMemoryProjectFile file)
    {
        Files.Add(file);
        return file;
    }

    public InMemoryProjectFile? FindFile(string path)
    {
        var normalizedPath = path.Replace('\\', '/').Trim('/');
        return Files.FirstOrDefault(file => string.Equals(file.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<InMemoryProjectFile> GetCSharpFiles()
    {
        return Files.Where(static file => file.IsCSharp && file.IncludeInCompilation);
    }

    public (string Path, string Text)[] GetCSharpFileSnapshot()
    {
        return GetCSharpFiles()
            .Select(static file => (file.Path, file.Text))
            .Where(static file => !string.IsNullOrWhiteSpace(file.Text))
            .ToArray();
    }

    public IEnumerable<InMemoryProjectFile> GetXamlFiles()
    {
        return Files.Where(static file => file.IsXaml);
    }

    public InMemoryProjectFile? FindCodeBehind(InMemoryProjectFile xamlFile)
    {
        return FindFile($"{xamlFile.Path}.cs");
    }

    public InMemoryProjectFile? FindXamlForCodeBehind(InMemoryProjectFile codeFile)
    {
        if (!codeFile.Path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) &&
            !codeFile.Path.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindFile(codeFile.Path[..^".cs".Length]);
    }

    private static string? NormalizeProjectFilePath(string? path)
    {
        var normalizedPath = path?.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(normalizedPath) ? null : normalizedPath;
    }
}
