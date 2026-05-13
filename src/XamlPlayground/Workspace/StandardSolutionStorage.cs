using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;

namespace XamlPlayground.Workspace;

public static class StandardSolutionStorage
{
    private const string SolutionFolderProjectTypeGuid = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
    private const string CSharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
    private static readonly XName IncludeAttribute = "Include";

    public static InMemorySolution LoadFromLocalPath(
        string solutionPath,
        string solutionText,
        Action<InMemoryProjectFile>? fileChanged = null)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrWhiteSpace(solutionDirectory) || !Directory.Exists(solutionDirectory))
        {
            throw new InvalidDataException("Unable to resolve the solution directory.");
        }

        var entries = ParseSolutionEntries(Path.GetFileName(solutionPath), solutionText);
        return CreateSolution(
            Path.GetFileNameWithoutExtension(solutionPath),
            entries,
            entry => ReadLocalProject(solutionDirectory, entry, fileChanged));
    }

    public static async Task<InMemorySolution> LoadFromStorageFolderAsync(
        string solutionFileName,
        string solutionText,
        IStorageFolder solutionRoot,
        Action<InMemoryProjectFile>? fileChanged = null)
    {
        var entries = ParseSolutionEntries(solutionFileName, solutionText);
        var projects = new List<InMemoryProject>();
        foreach (var entry in entries)
        {
            projects.Add(await ReadStorageProjectAsync(solutionRoot, entry, fileChanged));
        }

        return CreateSolution(Path.GetFileNameWithoutExtension(solutionFileName), projects);
    }

    public static string SaveSln(InMemorySolution solution)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        builder.AppendLine("VisualStudioVersion = 17.14.0.0");
        builder.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        foreach (var project in solution.Projects)
        {
            var projectFileName = GetProjectFileName(project);
            var projectPath = $"{EscapeSlnValue(project.Name)}/{EscapeSlnValue(projectFileName)}";
            var projectGuid = CreateStableGuid($"{solution.Name}/{project.Name}");
            builder.AppendLine(
                $"Project(\"{CSharpProjectTypeGuid}\") = \"{EscapeSlnValue(project.Name)}\", \"{projectPath}\", \"{projectGuid}\"");
            builder.AppendLine("EndProject");
        }

        builder.AppendLine("Global");
        builder.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        builder.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        builder.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");

        foreach (var project in solution.Projects)
        {
            var projectGuid = CreateStableGuid($"{solution.Name}/{project.Name}");
            builder.AppendLine($"\t\t{projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            builder.AppendLine($"\t\t{projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            builder.AppendLine($"\t\t{projectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            builder.AppendLine($"\t\t{projectGuid}.Release|Any CPU.Build.0 = Release|Any CPU");
        }

        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        builder.AppendLine("\t\tHideSolutionNode = FALSE");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("EndGlobal");
        return builder.ToString();
    }

    public static string SaveSlnx(InMemorySolution solution)
    {
        var document = new XDocument(
            new XElement("Solution",
                solution.Projects.Select(project =>
                    new XElement("Project",
                        new XAttribute("Path", $"{project.Name}/{GetProjectFileName(project)}")))));
        return document.ToString(SaveOptions.None) + Environment.NewLine;
    }

    public static async Task ExportStandardSolutionFolderAsync(
        InMemorySolution solution,
        IStorageFolder targetFolder)
    {
        await WriteStorageFileAsync(targetFolder, $"{solution.Name}.slnx", SaveSlnx(solution));
        await WriteStorageFileAsync(targetFolder, $"{solution.Name}.sln", SaveSln(solution));

        foreach (var project in solution.Projects)
        {
            var projectFolder = await GetOrCreateFolderAsync(targetFolder, project.Name);
            foreach (var file in project.Files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                await WriteStorageFilePathAsync(projectFolder, file.Path, file.Text);
            }
        }
    }

    public static IReadOnlyList<StandardSolutionProjectEntry> ParseSolutionEntries(
        string solutionFileName,
        string solutionText)
    {
        var extension = Path.GetExtension(solutionFileName);
        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSlnx(solutionText);
        }

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSln(solutionText);
        }

        throw new InvalidDataException("Only .sln and .slnx files can be loaded as standard solutions.");
    }

    private static InMemorySolution CreateSolution(
        string solutionName,
        IReadOnlyList<StandardSolutionProjectEntry> entries,
        Func<StandardSolutionProjectEntry, InMemoryProject> loadProject)
    {
        return CreateSolution(solutionName, entries.Select(loadProject).ToList());
    }

    private static InMemorySolution CreateSolution(
        string solutionName,
        IReadOnlyList<InMemoryProject> projects)
    {
        if (projects.Count == 0)
        {
            throw new InvalidDataException("Solution file does not reference any supported projects.");
        }

        var solution = new InMemorySolution(string.IsNullOrWhiteSpace(solutionName) ? "Solution" : solutionName);
        foreach (var project in projects)
        {
            solution.Projects.Add(project);
        }

        return solution;
    }

    private static IReadOnlyList<StandardSolutionProjectEntry> ParseSln(string solutionText)
    {
        var projects = new List<StandardSolutionProjectEntry>();
        var projectRegex = new Regex(
            "^Project\\(\"(?<type>[^\"]+)\"\\)\\s*=\\s*\"(?<name>[^\"]+)\",\\s*\"(?<path>[^\"]+)\",\\s*\"(?<id>[^\"]+)\"",
            RegexOptions.CultureInvariant);

        foreach (var rawLine in solutionText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            var match = projectRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var type = match.Groups["type"].Value;
            var path = match.Groups["path"].Value;
            if (type.Equals(SolutionFolderProjectTypeGuid, StringComparison.OrdinalIgnoreCase) ||
                !IsSupportedProjectPath(path))
            {
                continue;
            }

            projects.Add(new StandardSolutionProjectEntry(
                match.Groups["name"].Value,
                NormalizeSolutionReferencePath(path)));
        }

        return projects;
    }

    private static IReadOnlyList<StandardSolutionProjectEntry> ParseSlnx(string solutionText)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(solutionText, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("The selected .slnx file is not valid XML.", exception);
        }

        return document
            .Descendants()
            .Where(static element => element.Name.LocalName == "Project")
            .Select(static element => element.Attribute("Path")?.Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path) && IsSupportedProjectPath(path!))
            .Select(static path =>
            {
                var projectPath = path!;
                return new StandardSolutionProjectEntry(
                    Path.GetFileNameWithoutExtension(projectPath) ?? "Project",
                    NormalizeSolutionReferencePath(projectPath));
            })
            .ToArray();
    }

    private static InMemoryProject ReadLocalProject(
        string solutionDirectory,
        StandardSolutionProjectEntry entry,
        Action<InMemoryProjectFile>? fileChanged)
    {
        var projectPath = Path.GetFullPath(Path.Combine(solutionDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file '{entry.Path}' was not found.", projectPath);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
                               ?? throw new InvalidDataException($"Unable to resolve project directory for '{entry.Path}'.");
        var projectText = File.ReadAllText(projectPath);
        var project = CreateProject(entry, projectText);
        var projectFileName = Path.GetFileName(projectPath);
        project.AddFile(new InMemoryProjectFile(
            projectFileName,
            projectText,
            ProjectFileKind.ProjectFile,
            fileChanged));

        foreach (var filePath in EnumerateLocalProjectFiles(projectDirectory, projectPath, projectText))
        {
            var relativePath = SolutionStorage.NormalizeProjectPath(Path.GetRelativePath(projectDirectory, filePath));
            if (project.FindFile(relativePath) is not null)
            {
                continue;
            }

            var text = File.ReadAllText(filePath);
            project.AddFile(new InMemoryProjectFile(
                relativePath,
                text,
                ClassifyFile(relativePath, text),
                fileChanged));
        }

        return project;
    }

    private static async Task<InMemoryProject> ReadStorageProjectAsync(
        IStorageFolder solutionRoot,
        StandardSolutionProjectEntry entry,
        Action<InMemoryProjectFile>? fileChanged)
    {
        var projectFile = await GetStorageFileByPathAsync(solutionRoot, entry.Path);
        var projectText = await ReadStorageFileTextAsync(projectFile);
        var project = CreateProject(entry, projectText);
        var projectFileName = Path.GetFileName(entry.Path);
        project.AddFile(new InMemoryProjectFile(
            projectFileName,
            projectText,
            ProjectFileKind.ProjectFile,
            fileChanged));

        var projectFolderPath = Path.GetDirectoryName(entry.Path)?.Replace('\\', '/');
        var projectFolder = string.IsNullOrWhiteSpace(projectFolderPath)
            ? solutionRoot
            : await GetStorageFolderByPathAsync(solutionRoot, projectFolderPath);

        var files = await EnumerateStorageProjectFilesAsync(projectFolder, string.Empty);
        foreach (var file in files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (project.FindFile(file.Path) is not null)
            {
                continue;
            }

            project.AddFile(new InMemoryProjectFile(
                file.Path,
                file.Text,
                ClassifyFile(file.Path, file.Text),
                fileChanged));
        }

        return project;
    }

    private static InMemoryProject CreateProject(
        StandardSolutionProjectEntry entry,
        string projectText)
    {
        var projectName = string.IsNullOrWhiteSpace(entry.Name)
            ? Path.GetFileNameWithoutExtension(entry.Path)
            : entry.Name;
        var rootNamespace = ReadProjectProperty(projectText, "RootNamespace");
        return new InMemoryProject(
            projectName,
            string.IsNullOrWhiteSpace(rootNamespace) ? CreateIdentifier(projectName) : rootNamespace,
            $"standard.{Path.GetExtension(entry.Path).TrimStart('.')}");
    }

    private static IEnumerable<string> EnumerateLocalProjectFiles(
        string projectDirectory,
        string projectPath,
        string projectText)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            if (HasIgnoredPathSegment(Path.GetRelativePath(projectDirectory, file)) ||
                file.Equals(projectPath, StringComparison.OrdinalIgnoreCase) ||
                !IsImportableFilePath(file))
            {
                continue;
            }

            files.Add(file);
        }

        foreach (var explicitPath in ReadExplicitProjectIncludes(projectText))
        {
            if (HasGlobSyntax(explicitPath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, explicitPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fullPath) &&
                !fullPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase) &&
                IsImportableFilePath(fullPath))
            {
                files.Add(fullPath);
            }
        }

        return files;
    }

    private static async Task<IReadOnlyList<(string Path, string Text)>> EnumerateStorageProjectFilesAsync(
        IStorageFolder folder,
        string prefix)
    {
        var files = new List<(string Path, string Text)>();
        await foreach (var item in folder.GetItemsAsync())
        {
            var path = string.IsNullOrWhiteSpace(prefix)
                ? item.Name
                : $"{prefix}/{item.Name}";
            switch (item)
            {
                case IStorageFile file when IsImportableFilePath(path):
                    files.Add((SolutionStorage.NormalizeProjectPath(path), await ReadStorageFileTextAsync(file)));
                    break;
                case IStorageFolder childFolder when !IsIgnoredDirectoryName(childFolder.Name):
                    files.AddRange(await EnumerateStorageProjectFilesAsync(childFolder, path));
                    break;
            }
        }

        return files;
    }

    private static IEnumerable<string> ReadExplicitProjectIncludes(string projectText)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(projectText, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var element in document.Descendants())
        {
            if (!IsProjectItemElement(element.Name.LocalName))
            {
                continue;
            }

            var include = element.Attribute(IncludeAttribute)?.Value;
            if (!string.IsNullOrWhiteSpace(include))
            {
                yield return include;
            }
        }
    }

    private static string? ReadProjectProperty(string projectText, string propertyName)
    {
        try
        {
            var document = XDocument.Parse(projectText, LoadOptions.PreserveWhitespace);
            return document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == propertyName)
                ?.Value
                .Trim();
        }
        catch
        {
            return null;
        }
    }

    private static ProjectFileKind ClassifyFile(string path, string text)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.CSharp;
        }

        if (extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase))
        {
            return IsResourceDictionary(text) ? ProjectFileKind.Resource : ProjectFileKind.Xaml;
        }

        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.ProjectFile;
        }

        return ProjectFileKind.Text;
    }

    private static bool IsResourceDictionary(string text)
    {
        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return document.Root?.Name.LocalName == "ResourceDictionary";
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedProjectPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImportableFilePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".targets", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectItemElement(string localName)
    {
        return localName is "Compile" or "AvaloniaResource" or "Page" or "ApplicationDefinition" or
            "EmbeddedResource" or "Content" or "None";
    }

    private static bool HasIgnoredPathSegment(string path)
    {
        return SolutionStorage.NormalizeProjectPath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(IsIgnoredDirectoryName);
    }

    private static bool IsIgnoredDirectoryName(string name)
    {
        return name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGlobSyntax(string path)
    {
        return path.Contains('*', StringComparison.Ordinal) ||
               path.Contains('?', StringComparison.Ordinal);
    }

    private static string GetProjectFileName(InMemoryProject project)
    {
        return project.Files.FirstOrDefault(static file =>
                   file.Kind == ProjectFileKind.ProjectFile &&
                   IsSupportedProjectPath(file.Path))?.Name ??
               $"{project.Name}.csproj";
    }

    private static string EscapeSlnValue(string value)
    {
        return value.Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeSolutionReferencePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Trim('/');
    }

    private static string CreateStableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.Take(16).ToArray()).ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
    }

    private static async Task<IStorageFile> GetStorageFileByPathAsync(
        IStorageFolder root,
        string path)
    {
        var segments = SolutionStorage.NormalizeProjectPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new FileNotFoundException("Storage file path is empty.");
        }

        var folder = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            folder = await folder.GetFolderAsync(segments[i]) ??
                     throw new DirectoryNotFoundException($"Folder '{segments[i]}' was not found.");
        }

        return await folder.GetFileAsync(segments[^1]) ??
               throw new FileNotFoundException($"File '{segments[^1]}' was not found.");
    }

    private static async Task<IStorageFolder> GetStorageFolderByPathAsync(
        IStorageFolder root,
        string path)
    {
        var folder = root;
        foreach (var segment in SolutionStorage.NormalizeProjectPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            folder = await folder.GetFolderAsync(segment) ??
                     throw new DirectoryNotFoundException($"Folder '{segment}' was not found.");
        }

        return folder;
    }

    private static async Task<string> ReadStorageFileTextAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteStorageFilePathAsync(
        IStorageFolder root,
        string path,
        string text)
    {
        var segments = SolutionStorage.NormalizeProjectPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var folder = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            folder = await GetOrCreateFolderAsync(folder, segments[i]);
        }

        await WriteStorageFileAsync(folder, segments[^1], text);
    }

    private static async Task WriteStorageFileAsync(
        IStorageFolder folder,
        string fileName,
        string text)
    {
        IStorageFile file;
        try
        {
            file = await folder.GetFileAsync(fileName) ??
                   throw new FileNotFoundException($"File '{fileName}' was not found.");
        }
        catch
        {
            file = await folder.CreateFileAsync(fileName) ??
                   throw new IOException($"Unable to create file '{fileName}'.");
        }

        await using var stream = await file.OpenWriteAsync();
        try
        {
            stream.SetLength(0);
        }
        catch (NotSupportedException)
        {
        }

        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }

    private static async Task<IStorageFolder> GetOrCreateFolderAsync(
        IStorageFolder folder,
        string folderName)
    {
        try
        {
            return await folder.GetFolderAsync(folderName) ??
                   throw new DirectoryNotFoundException($"Folder '{folderName}' was not found.");
        }
        catch
        {
            return await folder.CreateFolderAsync(folderName) ??
                   throw new IOException($"Unable to create folder '{folderName}'.");
        }
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
}

public sealed record StandardSolutionProjectEntry(
    string Name,
    string Path);
