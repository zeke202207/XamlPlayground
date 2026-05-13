using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace XamlPlayground.Workspace;

public static class MsBuildWorkspaceLoader
{
    private const string MsBuildTemplateName = "msbuild";

    public static async Task<InMemorySolution> LoadLocalWorkspaceAsync(
        string workspacePath,
        Action<InMemoryProjectFile>? fileChanged = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(workspacePath))
        {
            var entry = FindLocalWorkspaceEntry(workspacePath);
            if (entry is null)
            {
                throw new InvalidDataException("The selected directory does not contain a .sln, .slnx, or project file.");
            }

            workspacePath = entry;
        }

        var extension = Path.GetExtension(workspacePath);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadLocalSolutionAsync(workspacePath, fileChanged, progress, cancellationToken);
        }

        if (IsSupportedProjectPath(workspacePath))
        {
            return await LoadLocalProjectAsync(workspacePath, fileChanged, progress, cancellationToken);
        }

        throw new InvalidDataException("Only .sln, .slnx, .csproj, .fsproj, and .vbproj workspaces are supported.");
    }

    public static async Task<InMemorySolution> LoadLocalSolutionAsync(
        string solutionPath,
        Action<InMemoryProjectFile>? fileChanged = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var previousDirectory = Directory.GetCurrentDirectory();
        var solutionDirectory = Path.GetDirectoryName(solutionPath);
        if (!string.IsNullOrWhiteSpace(solutionDirectory))
        {
            Directory.SetCurrentDirectory(solutionDirectory);
        }

        try
        {
            EnsureMSBuildRegistered();
            using var workspace = MSBuildWorkspace.Create();
            using var workspaceFailed = workspace.RegisterWorkspaceFailedHandler(
                e => progress?.Report($"Workspace diagnostic: {e.Diagnostic.Message}"));
            var loadProgress = new Progress<ProjectLoadProgress>(p => progress?.Report(FormatProgress(p)));
            var solution = await workspace.OpenSolutionAsync(solutionPath, loadProgress, cancellationToken);
            return await CreateSolutionFromRoslynAsync(
                Path.GetFileNameWithoutExtension(solutionPath),
                solution,
                solutionPath,
                fileChanged,
                progress,
                cancellationToken);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    public static async Task<InMemorySolution> LoadLocalProjectAsync(
        string projectPath,
        Action<InMemoryProjectFile>? fileChanged = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var previousDirectory = Directory.GetCurrentDirectory();
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            Directory.SetCurrentDirectory(projectDirectory);
        }

        try
        {
            EnsureMSBuildRegistered();
            using var workspace = MSBuildWorkspace.Create();
            using var workspaceFailed = workspace.RegisterWorkspaceFailedHandler(
                e => progress?.Report($"Workspace diagnostic: {e.Diagnostic.Message}"));
            var loadProgress = new Progress<ProjectLoadProgress>(p => progress?.Report(FormatProgress(p)));
            var project = await workspace.OpenProjectAsync(projectPath, loadProgress, cancellationToken);
            return await CreateSolutionFromRoslynAsync(
                Path.GetFileNameWithoutExtension(projectPath),
                project.Solution,
                projectPath,
                fileChanged,
                progress,
                cancellationToken);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    public static async Task<InMemorySolution> LoadStorageFolderAsync(
        IStorageFolder rootFolder,
        Action<InMemoryProjectFile>? fileChanged = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = new List<StorageTextFile>();
        var assemblies = new List<StorageAssemblyFile>();
        await EnumerateStorageFolderAsync(rootFolder, string.Empty, files, assemblies, cancellationToken);

        var solutionFile = files
            .Where(static file => IsSolutionPath(file.RelativePath))
            .OrderBy(static file => file.RelativePath.Count(static ch => ch == '/'))
            .ThenBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        IReadOnlyList<StandardSolutionProjectEntry> entries = solutionFile is not null
            ? StandardSolutionStorage.ParseSolutionEntries(solutionFile.RelativePath, solutionFile.Text)
            : files
                .Where(static file => IsSupportedProjectPath(file.RelativePath) && !IsIgnoredProjectPath(file.RelativePath))
                .Select(static file => new StandardSolutionProjectEntry(
                    Path.GetFileNameWithoutExtension(file.RelativePath),
                    file.RelativePath))
                .ToArray();

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The selected directory does not contain a supported solution or project file.");
        }

        var solutionName = solutionFile is null
            ? rootFolder.Name
            : Path.GetFileNameWithoutExtension(solutionFile.RelativePath);
        var solution = new InMemorySolution(string.IsNullOrWhiteSpace(solutionName) ? "Workspace" : solutionName);
        var fileByPath = files.ToDictionary(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = NormalizePath(entry.Path);
            if (!fileByPath.TryGetValue(projectPath, out var projectFile))
            {
                progress?.Report($"Skipped missing project {projectPath}.");
                continue;
            }

            var project = CreateStorageProject(projectFile, entry, files, assemblies, fileChanged);
            solution.Projects.Add(project);
        }

        if (solution.Projects.Count == 0)
        {
            throw new InvalidDataException("No supported projects could be loaded from the selected directory.");
        }

        return solution;
    }

    private static async Task<InMemorySolution> CreateSolutionFromRoslynAsync(
        string solutionName,
        Solution roslynSolution,
        string workspacePath,
        Action<InMemoryProjectFile>? fileChanged,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var solution = new InMemorySolution(string.IsNullOrWhiteSpace(solutionName) ? "Workspace" : solutionName);
        var workspaceRoot = File.Exists(workspacePath)
            ? Path.GetDirectoryName(workspacePath)
            : workspacePath;
        var solutionFolders = File.Exists(workspacePath) && IsSolutionPath(workspacePath)
            ? ParseSolutionFolders(workspacePath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in roslynSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Indexing {project.Name}...");
            var inMemoryProject = await CreateProjectFromRoslynAsync(
                project,
                workspaceRoot,
                solutionFolders,
                fileChanged,
                cancellationToken);
            solution.Projects.Add(inMemoryProject);
        }

        if (solution.Projects.Count == 0)
        {
            throw new InvalidDataException("The workspace did not contain any projects.");
        }

        return solution;
    }

    private static async Task<InMemoryProject> CreateProjectFromRoslynAsync(
        Project project,
        string? workspaceRoot,
        IReadOnlyDictionary<string, string> solutionFolders,
        Action<InMemoryProjectFile>? fileChanged,
        CancellationToken cancellationToken)
    {
        var projectFilePath = project.FilePath ?? string.Empty;
        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        var projectRootNamespace = string.IsNullOrWhiteSpace(project.DefaultNamespace)
            ? CreateIdentifier(project.Name)
            : project.DefaultNamespace;
        var inMemoryProject = new InMemoryProject(project.Name, projectRootNamespace, MsBuildTemplateName, projectFilePath)
        {
            AssemblyName = string.IsNullOrWhiteSpace(project.AssemblyName) ? project.Name : project.AssemblyName!,
            OutputAssemblyPath = project.OutputFilePath,
            TargetFramework = TryGetTargetFrameworkFromOutputPath(project.OutputFilePath),
            WorkspaceRootPath = workspaceRoot,
            SolutionFolderPath = project.FilePath is { } path && solutionFolders.TryGetValue(path, out var folder)
                ? folder
                : null,
            IsMsBuildWorkspace = true
        };

        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath))
        {
            AddLocalFile(
                inMemoryProject,
                projectFilePath,
                Path.GetFileName(projectFilePath),
                ProjectFileKind.ProjectFile,
                fileChanged,
                addedPaths);
        }

        foreach (var document in project.Documents)
        {
            var filePath = document.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var text = await document.GetTextAsync(cancellationToken);
            AddLocalFile(
                inMemoryProject,
                filePath,
                GetRelativePath(projectFilePath, filePath, document.Name),
                ClassifyFile(filePath, text.ToString()),
                fileChanged,
                addedPaths,
                text.ToString());
        }

        foreach (var document in project.AdditionalDocuments)
        {
            var filePath = document.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var text = await document.GetTextAsync(cancellationToken);
            AddLocalFile(
                inMemoryProject,
                filePath,
                GetRelativePath(projectFilePath, filePath, document.Name),
                ClassifyFile(filePath, text.ToString()),
                fileChanged,
                addedPaths,
                text.ToString(),
                includeInCompilation: false);
        }

        foreach (var filePath in EnumerateProjectFiles(projectDirectory, projectFilePath))
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            AddLocalFile(
                inMemoryProject,
                filePath,
                GetRelativePath(projectFilePath, filePath, Path.GetFileName(filePath)),
                ClassifyFile(filePath, text),
                fileChanged,
                addedPaths,
                text,
                includeInCompilation: false);
        }

        AddRoslynReferences(inMemoryProject, project);
        return inMemoryProject;
    }

    private static InMemoryProject CreateStorageProject(
        StorageTextFile projectFile,
        StandardSolutionProjectEntry entry,
        IReadOnlyList<StorageTextFile> files,
        IReadOnlyList<StorageAssemblyFile> assemblies,
        Action<InMemoryProjectFile>? fileChanged)
    {
        var projectFolder = GetDirectoryName(projectFile.RelativePath);
        var projectName = string.IsNullOrWhiteSpace(entry.Name)
            ? Path.GetFileNameWithoutExtension(projectFile.RelativePath)
            : entry.Name;
        var rootNamespace = ReadProjectProperty(projectFile.Text, "RootNamespace");
        var assemblyName = ReadProjectProperty(projectFile.Text, "AssemblyName");
        var targetFramework = ReadProjectProperty(projectFile.Text, "TargetFramework") ??
                              ReadProjectProperty(projectFile.Text, "TargetFrameworks")?.Split(';').FirstOrDefault();
        var project = new InMemoryProject(
            projectName,
            string.IsNullOrWhiteSpace(rootNamespace) ? CreateIdentifier(projectName) : rootNamespace,
            "browser.storage",
            projectFile.RelativePath)
        {
            AssemblyName = string.IsNullOrWhiteSpace(assemblyName) ? projectName : assemblyName,
            TargetFramework = targetFramework,
            WorkspaceRootPath = string.Empty,
            IsMsBuildWorkspace = true
        };

        project.AddFile(new InMemoryProjectFile(
            Path.GetFileName(projectFile.RelativePath),
            projectFile.Text,
            ProjectFileKind.ProjectFile,
            fileChanged,
            sourceStorageFile: projectFile.File));

        foreach (var file in files
                     .Where(file => IsUnderProjectFolder(projectFolder, file.RelativePath) &&
                                    !IsIgnoredProjectPath(file.RelativePath) &&
                                    !string.Equals(file.RelativePath, projectFile.RelativePath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = GetProjectRelativePath(projectFolder, file.RelativePath);
            if (!IsImportableFilePath(relativePath))
            {
                continue;
            }

            project.AddFile(new InMemoryProjectFile(
                relativePath,
                file.Text,
                ClassifyFile(relativePath, file.Text),
                fileChanged,
                sourceStorageFile: file.File));
        }

        foreach (var assembly in assemblies
                     .Where(assembly => IsUnderProjectFolder(projectFolder, assembly.RelativePath) &&
                                        assembly.RelativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static assembly => assembly.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            AddReference(project, WorkspaceAssemblyReference.FromImage(
                assembly.RelativePath,
                assembly.Image,
                isRuntimeAssembly: true));
        }

        return project;
    }

    private static void AddRoslynReferences(InMemoryProject inMemoryProject, Project project)
    {
        foreach (var metadataReference in project.MetadataReferences.OfType<PortableExecutableReference>())
        {
            var referencePath = metadataReference.FilePath;
            AddReference(inMemoryProject, WorkspaceAssemblyReference.FromPath(referencePath, isRuntimeAssembly: true));

            if (ResolveRuntimeAssemblyPath(referencePath, inMemoryProject.TargetFramework) is { } runtimePath &&
                !string.Equals(runtimePath, referencePath, StringComparison.OrdinalIgnoreCase))
            {
                AddReference(inMemoryProject, WorkspaceAssemblyReference.FromPath(runtimePath, isRuntimeAssembly: true));
            }
        }

        foreach (var projectReference in project.ProjectReferences)
        {
            var referencedProject = project.Solution.GetProject(projectReference.ProjectId);
            AddReference(inMemoryProject, WorkspaceAssemblyReference.FromPath(
                referencedProject?.OutputFilePath,
                isRuntimeAssembly: true));
        }

        AddReference(inMemoryProject, WorkspaceAssemblyReference.FromPath(
            project.OutputFilePath,
            isRuntimeAssembly: true));
    }

    private static void AddReference(InMemoryProject project, WorkspaceAssemblyReference? reference)
    {
        if (reference is null)
        {
            return;
        }

        if (project.AssemblyReferences.Any(existing =>
                string.Equals(existing.Name, reference.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FilePath, reference.FilePath, StringComparison.OrdinalIgnoreCase) &&
                existing.IsRuntimeAssembly == reference.IsRuntimeAssembly))
        {
            return;
        }

        project.AssemblyReferences.Add(reference);
    }

    private static void AddLocalFile(
        InMemoryProject project,
        string fullPath,
        string relativePath,
        ProjectFileKind kind,
        Action<InMemoryProjectFile>? fileChanged,
        HashSet<string> addedPaths,
        string? knownText = null,
        bool includeInCompilation = true)
    {
        var normalizedRelativePath = NormalizePath(relativePath);
        if (!addedPaths.Add(normalizedRelativePath))
        {
            return;
        }

        var text = knownText ?? File.ReadAllText(fullPath);
        project.AddFile(new InMemoryProjectFile(
            normalizedRelativePath,
            text,
            kind,
            fileChanged,
            includeInCompilation: includeInCompilation,
            sourcePath: fullPath));
    }

    private static IEnumerable<string> EnumerateProjectFiles(string? projectDirectory, string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (file.Equals(projectFilePath, StringComparison.OrdinalIgnoreCase) ||
                IsIgnoredProjectPath(file) ||
                !IsImportableFilePath(file))
            {
                continue;
            }

            yield return file;
        }
    }

    private static async Task EnumerateStorageFolderAsync(
        IStorageFolder folder,
        string prefix,
        List<StorageTextFile> files,
        List<StorageAssemblyFile> assemblies,
        CancellationToken cancellationToken)
    {
        await foreach (var item in folder.GetItemsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrWhiteSpace(prefix) ? item.Name : $"{prefix}/{item.Name}";
            switch (item)
            {
                case IStorageFolder childFolder when IsIgnoredDirectoryName(childFolder.Name):
                    break;
                case IStorageFolder childFolder when !IsIgnoredDirectoryName(childFolder.Name):
                    await EnumerateStorageFolderAsync(childFolder, path, files, assemblies, cancellationToken);
                    break;
                case IStorageFile file when IsImportableFilePath(path):
                    files.Add(new StorageTextFile(NormalizePath(path), file, await ReadStorageFileTextAsync(file)));
                    break;
                case IStorageFile file when WorkspaceAssemblyReference.IsAssemblyFile(path):
                    if (await TryReadStorageFileBytesAsync(file) is { } image)
                    {
                        assemblies.Add(new StorageAssemblyFile(NormalizePath(path), image));
                    }
                    break;
            }
        }
    }

    private static async Task EnumerateAssemblyStorageFolderAsync(
        IStorageFolder folder,
        string prefix,
        List<StorageAssemblyFile> assemblies,
        CancellationToken cancellationToken)
    {
        await foreach (var item in folder.GetItemsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrWhiteSpace(prefix) ? item.Name : $"{prefix}/{item.Name}";
            switch (item)
            {
                case IStorageFolder childFolder:
                    await EnumerateAssemblyStorageFolderAsync(childFolder, path, assemblies, cancellationToken);
                    break;
                case IStorageFile file when WorkspaceAssemblyReference.IsAssemblyFile(path):
                    if (await TryReadStorageFileBytesAsync(file) is { } image)
                    {
                        assemblies.Add(new StorageAssemblyFile(NormalizePath(path), image));
                    }
                    break;
            }
        }
    }

    private static async Task<string> ReadStorageFileTextAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<byte[]?> TryReadStorageFileBytesAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureMSBuildRegistered()
    {
        EnsureDotnetRoot();
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instances = MSBuildLocator.QueryVisualStudioInstances()
            .OrderByDescending(static instance => instance.Version)
            .ToArray();
        var instance = instances.FirstOrDefault();
        if (instance is not null)
        {
            MSBuildLocator.RegisterInstance(instance);
        }
        else
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static void EnsureDotnetRoot()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            return;
        }

        var arm64Root = Environment.GetEnvironmentVariable("DOTNET_ROOT_ARM64");
        if (!string.IsNullOrWhiteSpace(arm64Root))
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", arm64Root);
        }
    }

    private static string? FindLocalWorkspaceEntry(string directory)
    {
        var topLevelSolutions = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSolutionPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (topLevelSolutions.FirstOrDefault() is { } topLevelSolution)
        {
            return topLevelSolution;
        }

        var nestedSolution = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => IsSolutionPath(path) && !IsIgnoredProjectPath(path))
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (nestedSolution is not null)
        {
            return nestedSolution;
        }

        return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => IsSupportedProjectPath(path) && !IsIgnoredProjectPath(path))
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, string> ParseSolutionFolders(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnxSolutionFolders(solutionPath)
            : ParseSlnSolutionFolders(solutionPath);
    }

    private static IReadOnlyDictionary<string, string> ParseSlnSolutionFolders(string solutionPath)
    {
        const string solutionFolderGuid = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var projectPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nestedProjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var inNestedProjects = false;
        foreach (var rawLine in File.ReadLines(solutionPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSlnProjectLine(line, out var typeGuid, out var name, out var path, out var projectGuid))
                {
                    continue;
                }

                if (string.Equals(typeGuid, solutionFolderGuid, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(projectGuid) && !string.IsNullOrWhiteSpace(name))
                    {
                        folderNames[projectGuid] = name;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(projectGuid) && !string.IsNullOrWhiteSpace(path))
                {
                    projectPaths[projectGuid] = Path.GetFullPath(Path.Combine(solutionDirectory, path));
                }

                continue;
            }

            if (line.StartsWith("GlobalSection(NestedProjects)", StringComparison.OrdinalIgnoreCase))
            {
                inNestedProjects = true;
                continue;
            }

            if (!inNestedProjects)
            {
                continue;
            }

            if (line.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
            {
                inNestedProjects = false;
                continue;
            }

            var parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                nestedProjects[parts[0]] = parts[1];
            }
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projectPaths)
        {
            var folders = new List<string>();
            var parent = nestedProjects.TryGetValue(project.Key, out var parentGuid) ? parentGuid : null;
            while (!string.IsNullOrWhiteSpace(parent) && folderNames.TryGetValue(parent, out var folderName))
            {
                folders.Add(folderName);
                parent = nestedProjects.TryGetValue(parent, out var next) ? next : null;
            }

            if (folders.Count == 0)
            {
                continue;
            }

            folders.Reverse();
            result[project.Value] = string.Join('/', folders.Select(NormalizeSolutionFolderName).Where(static name => !string.IsNullOrWhiteSpace(name)));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseSlnxSolutionFolders(string solutionPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? string.Empty;

        XDocument document;
        try
        {
            document = XDocument.Load(solutionPath);
        }
        catch
        {
            return result;
        }

        var root = document.Root;
        if (root is null)
        {
            return result;
        }

        foreach (var project in root.Elements("Project"))
        {
            AddSlnxProjectFolder(result, solutionDirectory, project, NormalizeSolutionFolderName((string?)project.Attribute("Folder")));
        }

        foreach (var folder in root.Elements("Folder"))
        {
            VisitSlnxFolder(result, solutionDirectory, folder, string.Empty);
        }

        return result;
    }

    private static void VisitSlnxFolder(
        Dictionary<string, string> result,
        string solutionDirectory,
        XElement folder,
        string parentPath)
    {
        var name = NormalizeSolutionFolderName((string?)folder.Attribute("Name"));
        var currentPath = CombineSolutionFolderPath(parentPath, name);

        foreach (var project in folder.Elements("Project"))
        {
            AddSlnxProjectFolder(result, solutionDirectory, project, currentPath);
        }

        foreach (var childFolder in folder.Elements("Folder"))
        {
            VisitSlnxFolder(result, solutionDirectory, childFolder, currentPath);
        }
    }

    private static void AddSlnxProjectFolder(
        Dictionary<string, string> result,
        string solutionDirectory,
        XElement project,
        string? folderPath)
    {
        var projectPath = (string?)project.Attribute("Path");
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        result[Path.GetFullPath(Path.Combine(solutionDirectory, projectPath))] = folderPath;
    }

    private static bool TryParseSlnProjectLine(
        string line,
        out string? typeGuid,
        out string? name,
        out string? path,
        out string? projectGuid)
    {
        typeGuid = null;
        name = null;
        path = null;
        projectGuid = null;

        var typeStart = line.IndexOf('"');
        var typeEnd = typeStart < 0 ? -1 : line.IndexOf('"', typeStart + 1);
        if (typeStart < 0 || typeEnd < 0)
        {
            return false;
        }

        typeGuid = line[(typeStart + 1)..typeEnd];
        var fields = line
            .Split('=', 2, StringSplitOptions.TrimEntries)
            .ElementAtOrDefault(1)?
            .Split(',', StringSplitOptions.TrimEntries);
        if (fields is null || fields.Length < 3)
        {
            return false;
        }

        name = TrimSlnQuotes(fields[0]);
        path = TrimSlnQuotes(fields[1]);
        projectGuid = TrimSlnQuotes(fields[2]);
        return true;
    }

    private static string TrimSlnQuotes(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string? NormalizeSolutionFolderName(string? name)
    {
        var normalized = name?.Replace('\\', '/').Trim().Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string CombineSolutionFolderPath(string parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(child))
        {
            return parent;
        }

        return string.IsNullOrWhiteSpace(parent)
            ? child
            : parent + "/" + child;
    }

    private static string? ResolveRuntimeAssemblyPath(string? referencePath, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(referencePath) || !WorkspaceAssemblyReference.IsReferenceAssemblyPath(referencePath))
        {
            return referencePath;
        }

        var normalized = referencePath.Replace('\\', '/');
        var libCandidate = normalized
            .Replace("/ref/", "/lib/", StringComparison.OrdinalIgnoreCase)
            .Replace("/refint/", "/lib/", StringComparison.OrdinalIgnoreCase);
        if (File.Exists(libCandidate))
        {
            return libCandidate;
        }

        var fileName = Path.GetFileName(referencePath);
        var packageRoot = GetPackageRootForReferenceAssembly(referencePath);
        if (packageRoot is null || !Directory.Exists(packageRoot))
        {
            return null;
        }

        var candidates = Directory.EnumerateFiles(packageRoot, fileName, SearchOption.AllDirectories)
            .Where(path => path.Replace('\\', '/').Contains("/lib/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => IsTargetFrameworkMatch(path, targetFramework))
            .ThenByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? GetPackageRootForReferenceAssembly(string referencePath)
    {
        var directory = Path.GetDirectoryName(referencePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, "ref", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "refint", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(directory);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool IsTargetFrameworkMatch(string path, string? targetFramework)
    {
        return !string.IsNullOrWhiteSpace(targetFramework) &&
               path.Replace('\\', '/').Contains("/" + targetFramework + "/", StringComparison.OrdinalIgnoreCase);
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
            return XDocument.Parse(text, LoadOptions.None).Root?.Name.LocalName is "ResourceDictionary" or "Styles";
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadProjectProperty(string projectText, string propertyName)
    {
        try
        {
            return XDocument.Parse(projectText, LoadOptions.None)
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
               extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedProjectPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSolutionPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredProjectPath(string path)
    {
        var normalized = NormalizePath(path);
        return IsIgnoredPathSegment(normalized, "bin") ||
               IsIgnoredPathSegment(normalized, "obj") ||
               IsIgnoredPathSegment(normalized, ".git") ||
               IsIgnoredPathSegment(normalized, ".vs");
    }

    private static bool IsIgnoredPathSegment(string normalizedPath, string segment)
    {
        return normalizedPath.Equals(segment, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/" + segment + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredDirectoryName(string name)
    {
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderProjectFolder(string projectFolder, string filePath)
    {
        return string.IsNullOrWhiteSpace(projectFolder) ||
               filePath.StartsWith(projectFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectRelativePath(string projectFolder, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            return NormalizePath(filePath);
        }

        var prefix = projectFolder.TrimEnd('/') + "/";
        return filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizePath(filePath[prefix.Length..])
            : NormalizePath(filePath);
    }

    private static string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string GetRelativePath(string? projectPath, string filePath, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return NormalizePath(fallbackName);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return NormalizePath(fallbackName);
        }

        try
        {
            return NormalizePath(Path.GetRelativePath(projectDirectory, filePath));
        }
        catch
        {
            return NormalizePath(fallbackName);
        }
    }

    private static string? TryGetTargetFrameworkFromOutputPath(string? outputFilePath)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return null;
        }

        var parts = outputFilePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (!parts[i].Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!parts[i + 1].Equals("Debug", StringComparison.OrdinalIgnoreCase) &&
                !parts[i + 1].Equals("Release", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return parts[i + 2];
        }

        return null;
    }

    private static string FormatProgress(ProjectLoadProgress progress)
    {
        var filePath = progress.FilePath ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        var targetFramework = string.IsNullOrWhiteSpace(progress.TargetFramework)
            ? string.Empty
            : $" ({progress.TargetFramework})";
        return string.IsNullOrWhiteSpace(fileName)
            ? progress.Operation.ToString()
            : $"{progress.Operation}: {fileName}{targetFramework}";
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

    private sealed record StorageTextFile(string RelativePath, IStorageFile File, string Text);

    private sealed record StorageAssemblyFile(string RelativePath, byte[] Image);
}
