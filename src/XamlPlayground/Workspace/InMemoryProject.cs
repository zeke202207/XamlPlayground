using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace XamlPlayground.Workspace;

public sealed class InMemoryProject
{
    public InMemoryProject(string name, string rootNamespace, string templateShortName)
    {
        Name = name;
        RootNamespace = rootNamespace;
        TemplateShortName = templateShortName;
    }

    public string Name { get; }

    public string RootNamespace { get; }

    public string TemplateShortName { get; }

    public ObservableCollection<InMemoryProjectFile> Files { get; } = new();

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
        return Files.Where(static file => file.IsCSharp);
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
}
