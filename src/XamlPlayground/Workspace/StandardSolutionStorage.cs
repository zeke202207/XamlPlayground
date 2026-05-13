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
    private const string VisualBasicProjectTypeGuid = "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}";
    private const string FSharpProjectTypeGuid = "{F2A71F9B-5D33-465A-A702-920D77279786}";
    private static readonly XName IncludeAttribute = "Include";
    private static readonly XName LinkAttribute = "Link";

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
            var projectTypeGuid = GetProjectTypeGuid(projectFileName);
            var projectGuid = CreateStableGuid($"{solution.Name}/{project.Name}");
            builder.AppendLine(
                $"Project(\"{projectTypeGuid}\") = \"{EscapeSlnValue(project.Name)}\", \"{projectPath}\", \"{projectGuid}\"");
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
            var projectFolderPath = SolutionStorage.NormalizeProjectPath(project.Name);
            var projectFile = GetProjectFile(project);
            var explicitExportPaths = projectFile is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : CreateExplicitExportPaths(projectFolderPath, projectFile.Text);
            foreach (var file in project.Files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                var targetPath = explicitExportPaths.TryGetValue(file.Path, out var explicitPath)
                    ? explicitPath
                    : CombineSolutionPath(projectFolderPath, file.Path);
                if (targetPath is not null)
                {
                    await WriteStorageFilePathAsync(targetFolder, targetPath, file.Text);
                }
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

        foreach (var file in EnumerateLocalProjectFiles(projectDirectory, projectPath, projectText))
        {
            if (project.FindFile(file.ProjectPath) is not null)
            {
                continue;
            }

            var text = File.ReadAllText(file.FullPath);
            project.AddFile(new InMemoryProjectFile(
                file.ProjectPath,
                text,
                ClassifyFile(file.ProjectPath, text),
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

        var projectFolderPath = Path.GetDirectoryName(entry.Path)?.Replace('\\', '/') ?? string.Empty;
        var projectFolder = string.IsNullOrWhiteSpace(projectFolderPath)
            ? solutionRoot
            : await GetStorageFolderByPathAsync(solutionRoot, projectFolderPath);

        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in await EnumerateStorageProjectFilesAsync(projectFolder, string.Empty))
        {
            files.TryAdd(file.Path, file.Text);
        }

        foreach (var include in ReadExplicitProjectIncludes(projectText))
        {
            if (HasGlobSyntax(include.Include) ||
                TryResolveSolutionRelativePath(projectFolderPath, include.Include) is not { } includePath ||
                HasIgnoredPathSegment(includePath) ||
                !IsImportableFilePath(includePath))
            {
                continue;
            }

            var includeFile = await TryGetStorageFileByPathAsync(solutionRoot, includePath);
            if (includeFile is null)
            {
                continue;
            }

            var projectFilePath = CreateStorageExplicitProjectFilePath(projectFolderPath, includePath, include);
            if (!HasIgnoredPathSegment(projectFilePath) &&
                !files.ContainsKey(projectFilePath))
            {
                files.TryAdd(
                    projectFilePath,
                    await ReadStorageFileTextAsync(includeFile));
            }
        }

        foreach (var file in files.OrderBy(static file => file.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (project.FindFile(file.Key) is not null)
            {
                continue;
            }

            project.AddFile(new InMemoryProjectFile(
                file.Key,
                file.Value,
                ClassifyFile(file.Key, file.Value),
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

    private static IEnumerable<LocalProjectFile> EnumerateLocalProjectFiles(
        string projectDirectory,
        string projectPath,
        string projectText)
    {
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            var projectFilePath = SolutionStorage.NormalizeProjectPath(Path.GetRelativePath(projectDirectory, file));
            if (HasIgnoredPathSegment(projectFilePath) ||
                file.Equals(projectPath, StringComparison.OrdinalIgnoreCase) ||
                !IsImportableFilePath(file))
            {
                continue;
            }

            files.TryAdd(projectFilePath, file);
        }

        foreach (var include in ReadExplicitProjectIncludes(projectText))
        {
            if (HasGlobSyntax(include.Include))
            {
                continue;
            }

            var localIncludePath = include.Include
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, localIncludePath));
            if (File.Exists(fullPath) &&
                !fullPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase) &&
                IsImportableFilePath(fullPath))
            {
                var projectFilePath = CreateLocalExplicitProjectFilePath(projectDirectory, fullPath, include);
                if (!HasIgnoredPathSegment(projectFilePath))
                {
                    if (files.TryGetValue(projectFilePath, out var existingFullPath) &&
                        fullPath.Equals(existingFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    files.TryAdd(CreateUniqueProjectPath(files.Keys, projectFilePath), fullPath);
                }
            }
        }

        return files.Select(static file => new LocalProjectFile(file.Value, file.Key));
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

    private static IEnumerable<ProjectItemInclude> ReadExplicitProjectIncludes(string projectText)
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
                yield return new ProjectItemInclude(include, element.Attribute(LinkAttribute)?.Value);
            }
        }
    }

    private static string CreateLocalExplicitProjectFilePath(
        string projectDirectory,
        string fullPath,
        ProjectItemInclude include)
    {
        if (!string.IsNullOrWhiteSpace(include.Link))
        {
            return SolutionStorage.NormalizeProjectPath(include.Link);
        }

        var relativePath = Path.GetRelativePath(projectDirectory, fullPath);
        var normalizedRelativePath = relativePath.Replace('\\', '/');
        if (!normalizedRelativePath.StartsWith("../", StringComparison.Ordinal) &&
            !normalizedRelativePath.Equals("..", StringComparison.Ordinal))
        {
            return SolutionStorage.NormalizeProjectPath(normalizedRelativePath);
        }

        return CreateLinkedProjectPath(include.Include, Path.GetFileName(fullPath));
    }

    private static string CreateStorageExplicitProjectFilePath(
        string projectFolderPath,
        string solutionFilePath,
        ProjectItemInclude include)
    {
        if (!string.IsNullOrWhiteSpace(include.Link))
        {
            return SolutionStorage.NormalizeProjectPath(include.Link);
        }

        if (TryGetProjectRelativePath(projectFolderPath, solutionFilePath, out var relativePath))
        {
            return SolutionStorage.NormalizeProjectPath(relativePath);
        }

        return CreateLinkedProjectPath(include.Include, Path.GetFileName(solutionFilePath));
    }

    private static Dictionary<string, string> CreateExplicitExportPaths(
        string projectFolderPath,
        string projectText)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var include in ReadExplicitProjectIncludes(projectText))
        {
            if (HasGlobSyntax(include.Include) ||
                TryResolveSolutionRelativePath(projectFolderPath, include.Include) is not { } targetPath ||
                HasIgnoredPathSegment(targetPath) ||
                !IsImportableFilePath(targetPath))
            {
                continue;
            }

            var projectFilePath = CreateStorageExplicitProjectFilePath(projectFolderPath, targetPath, include);
            if (!HasIgnoredPathSegment(projectFilePath))
            {
                paths.TryAdd(projectFilePath, targetPath);
            }
        }

        return paths;
    }

    private static string CreateLinkedProjectPath(
        string includePath,
        string fallbackFileName)
    {
        var normalizedInclude = includePath.Replace('\\', '/').Trim('/');
        var segments = normalizedInclude
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(static segment => segment is not "." and not "..")
            .ToArray();
        var linkedPath = segments.Length == 0
            ? fallbackFileName
            : string.Join('/', segments);
        return SolutionStorage.NormalizeProjectPath($"Linked/{linkedPath}");
    }

    private static string CreateUniqueProjectPath(IEnumerable<string> existingPaths, string path)
    {
        var existing = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var extension = Path.GetExtension(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var index = 2;
        while (true)
        {
            var candidateName = $"{fileName}{index}{extension}";
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateName
                : $"{directory}/{candidateName}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }

            index++;
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
            return IsAvaloniaResourceFile(text) ? ProjectFileKind.Resource : ProjectFileKind.Xaml;
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

    private static bool IsAvaloniaResourceFile(string text)
    {
        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return document.Root?.Name.LocalName is "ResourceDictionary" or "Styles";
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
        return GetProjectFile(project)?.Name ??
               $"{project.Name}.csproj";
    }

    private static InMemoryProjectFile? GetProjectFile(InMemoryProject project)
    {
        return project.Files.FirstOrDefault(static file =>
            file.Kind == ProjectFileKind.ProjectFile &&
            IsSupportedProjectPath(file.Path));
    }

    private static string GetProjectTypeGuid(string projectFileName)
    {
        var extension = Path.GetExtension(projectFileName);
        if (extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return VisualBasicProjectTypeGuid;
        }

        if (extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            return FSharpProjectTypeGuid;
        }

        return CSharpProjectTypeGuid;
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

    private static string? CombineSolutionPath(string folderPath, string path)
    {
        return TryResolveSolutionRelativePath(folderPath, path);
    }

    private static string? TryResolveSolutionRelativePath(string baseFolderPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            IsRootedProjectPath(relativePath))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in NormalizeRelativePath(baseFolderPath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is not "." and not "..")
            {
                segments.Add(segment);
            }
        }

        foreach (var segment in relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case ".." when segments.Count > 0:
                    segments.RemoveAt(segments.Count - 1);
                    break;
                case "..":
                    return null;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static bool TryGetProjectRelativePath(
        string projectFolderPath,
        string solutionFilePath,
        out string relativePath)
    {
        var normalizedProjectFolder = NormalizeRelativePath(projectFolderPath);
        var normalizedFilePath = NormalizeRelativePath(solutionFilePath);
        if (string.IsNullOrWhiteSpace(normalizedProjectFolder))
        {
            relativePath = normalizedFilePath;
            return true;
        }

        if (normalizedFilePath.StartsWith($"{normalizedProjectFolder}/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = normalizedFilePath[(normalizedProjectFolder.Length + 1)..];
            return true;
        }

        relativePath = string.Empty;
        return false;
    }

    private static bool IsRootedProjectPath(string path)
    {
        return Path.IsPathRooted(path) ||
               Regex.IsMatch(path, "^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string CreateStableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.Take(16).ToArray()).ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
    }

    private sealed record ProjectItemInclude(string Include, string? Link);

    private sealed record LocalProjectFile(string FullPath, string ProjectPath);

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

    private static async Task<IStorageFile?> TryGetStorageFileByPathAsync(
        IStorageFolder root,
        string path)
    {
        try
        {
            return await GetStorageFileByPathAsync(root, path);
        }
        catch
        {
            return null;
        }
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
