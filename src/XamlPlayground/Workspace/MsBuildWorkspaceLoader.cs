using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using XamlPlayground.Services;

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
            await RestoreLocalWorkspaceIfNeededAsync(solutionPath, progress, cancellationToken);
            EnsureMSBuildRegistered();
            using var workspace = MSBuildWorkspace.Create();
            using var workspaceFailed = workspace.RegisterWorkspaceFailedHandler(
                e => ReportWorkspaceDiagnostic(e.Diagnostic, progress));
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
            await RestoreLocalWorkspaceIfNeededAsync(projectPath, progress, cancellationToken);
            EnsureMSBuildRegistered();
            using var workspace = MSBuildWorkspace.Create();
            using var workspaceFailed = workspace.RegisterWorkspaceFailedHandler(
                e => ReportWorkspaceDiagnostic(e.Diagnostic, progress));
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

        var projectEntries = entries
            .Select(entry => new
            {
                Entry = entry,
                Path = solutionFile is null
                    ? NormalizePath(entry.Path)
                    : ResolveStorageSolutionProjectPath(solutionFile.RelativePath, entry.Path)
            })
            .ToArray();
        var projectFolders = projectEntries
            .Select(static entry => GetDirectoryName(entry.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var projectEntry in projectEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileByPath.TryGetValue(projectEntry.Path, out var projectFile))
            {
                progress?.Report($"Skipped missing project {projectEntry.Path}.");
                continue;
            }

            var project = CreateStorageProject(projectFile, projectEntry.Entry, files, assemblies, projectFolders, fileChanged);
            solution.Projects.Add(project);
        }

        if (solution.Projects.Count == 0)
        {
            throw new InvalidDataException("No supported projects could be loaded from the selected directory.");
        }

        await AddStorageProjectReferencesAsync(solution, progress, cancellationToken);
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
        var projectFolders = roslynSolution.Projects
            .Select(static project => project.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetDirectoryName(path!))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var project in roslynSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Indexing {project.Name}...");
            var inMemoryProject = await CreateProjectFromRoslynAsync(
                project,
                workspaceRoot,
                solutionFolders,
                projectFolders,
                fileChanged,
                progress,
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
        IReadOnlyList<string> projectFolders,
        Action<InMemoryProjectFile>? fileChanged,
        IProgress<string>? progress,
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
            IsMsBuildWorkspace = true,
            CSharpParseOptions = project.ParseOptions as CSharpParseOptions,
            CSharpCompilationOptions = project.CompilationOptions as CSharpCompilationOptions
        };

        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedProjectFolders = GetExcludedLocalProjectFolders(projectDirectory, projectFolders);
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

        foreach (var filePath in EnumerateProjectFiles(projectDirectory, projectFilePath, excludedProjectFolders))
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

        await AddRoslynReferencesAsync(inMemoryProject, project, progress, cancellationToken);
        return inMemoryProject;
    }

    private static InMemoryProject CreateStorageProject(
        StorageTextFile projectFile,
        StandardSolutionProjectEntry entry,
        IReadOnlyList<StorageTextFile> files,
        IReadOnlyList<StorageAssemblyFile> assemblies,
        IReadOnlyList<string> projectFolders,
        Action<InMemoryProjectFile>? fileChanged)
    {
        var projectFolder = GetDirectoryName(projectFile.RelativePath);
        var excludedProjectFolders = GetExcludedStorageProjectFolders(projectFolder, projectFolders);
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
                     .Where(file => IsStorageProjectFileInScope(projectFolder, file.RelativePath, excludedProjectFolders) &&
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
                     .Where(assembly => IsStorageProjectFileInScope(projectFolder, assembly.RelativePath, excludedProjectFolders) &&
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

    private static async Task AddStorageProjectReferencesAsync(
        InMemorySolution solution,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var projectsByPath = solution.Projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectFilePath))
            .ToDictionary(static project => NormalizePath(project.ProjectFilePath!), StringComparer.OrdinalIgnoreCase);
        var emittedReferences = new Dictionary<string, Task<WorkspaceAssemblyReference?>>(StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddStorageProjectReferencesToProjectAsync(
                project,
                projectsByPath,
                emittedReferences,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                completed,
                progress,
                cancellationToken);
        }
    }

    private static async Task AddStorageProjectReferencesToProjectAsync(
        InMemoryProject project,
        IReadOnlyDictionary<string, InMemoryProject> projectsByPath,
        Dictionary<string, Task<WorkspaceAssemblyReference?>> emittedReferences,
        HashSet<string> visiting,
        HashSet<string> completed,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var projectKey = GetStorageProjectKey(project);
        if (completed.Contains(projectKey))
        {
            return;
        }

        if (!visiting.Add(projectKey))
        {
            progress?.Report($"Skipped cyclic project reference for {project.Name}.");
            return;
        }

        foreach (var referencedProject in GetStorageProjectReferences(project, projectsByPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddStorageProjectReferencesToProjectAsync(
                referencedProject,
                projectsByPath,
                emittedReferences,
                visiting,
                completed,
                progress,
                cancellationToken);

            var reference = await GetOrCreateStorageProjectReferenceAssemblyAsync(
                referencedProject,
                emittedReferences,
                progress,
                cancellationToken);
            if (reference is { })
            {
                ReplaceReference(project, reference);
            }

            foreach (var transitiveReference in referencedProject.AssemblyReferences)
            {
                if (reference is { } && IsProjectAssemblyReference(referencedProject, transitiveReference))
                {
                    continue;
                }

                AddReference(project, transitiveReference);
            }
        }

        visiting.Remove(projectKey);
        completed.Add(projectKey);
    }

    private static IEnumerable<InMemoryProject> GetStorageProjectReferences(
        InMemoryProject project,
        IReadOnlyDictionary<string, InMemoryProject> projectsByPath)
    {
        var projectFile = project.Files.FirstOrDefault(static file => file.Kind == ProjectFileKind.ProjectFile);
        if (projectFile is null)
        {
            yield break;
        }

        var projectDirectory = GetDirectoryName(project.ProjectFilePath ?? projectFile.Path);
        foreach (var referencePath in ReadProjectReferencePaths(projectFile.Text))
        {
            var resolvedPath = CollapseRelativeSegments(
                string.IsNullOrWhiteSpace(projectDirectory)
                    ? referencePath
                    : $"{projectDirectory.TrimEnd('/')}/{referencePath}");
            if (projectsByPath.TryGetValue(resolvedPath, out var referencedProject) &&
                !ReferenceEquals(project, referencedProject))
            {
                yield return referencedProject;
            }
        }
    }

    private static IReadOnlyList<string> ReadProjectReferencePaths(string projectText)
    {
        try
        {
            var document = XDocument.Parse(projectText, LoadOptions.None);
            return document
                .Descendants()
                .Where(static element => element.Name.LocalName == "ProjectReference")
                .Select(static element => element.Attribute("Include")?.Value)
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(static include => NormalizePath(include!))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Task<WorkspaceAssemblyReference?> GetOrCreateStorageProjectReferenceAssemblyAsync(
        InMemoryProject referencedProject,
        Dictionary<string, Task<WorkspaceAssemblyReference?>> emittedReferences,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var projectKey = GetStorageProjectKey(referencedProject);
        if (!emittedReferences.TryGetValue(projectKey, out var task))
        {
            task = CreateStorageProjectReferenceAssemblyAsync(referencedProject, progress, cancellationToken);
            emittedReferences[projectKey] = task;
        }

        return task;
    }

    private static async Task<WorkspaceAssemblyReference?> CreateStorageProjectReferenceAssemblyAsync(
        InMemoryProject referencedProject,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceFiles = referencedProject.GetCSharpFileSnapshot();
            if (sourceFiles.Length == 0)
            {
                return null;
            }

            var parseOptions = referencedProject.CSharpParseOptions ??
                               CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var syntaxTrees = sourceFiles
                .Where(static file => !string.IsNullOrWhiteSpace(file.Text))
                .Select(file => CSharpSyntaxTree.ParseText(file.Text, parseOptions, file.Path, cancellationToken: cancellationToken))
                .ToArray();
            var references = await GetStorageCompilationReferencesAsync(referencedProject);
            var compilationOptions = (referencedProject.CSharpCompilationOptions ??
                                      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release);
            var assemblyName = string.IsNullOrWhiteSpace(referencedProject.AssemblyName)
                ? referencedProject.Name
                : referencedProject.AssemblyName;
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                compilationOptions);

            using var stream = new MemoryStream();
            var result = compilation.Emit(stream, cancellationToken: cancellationToken);
            if (!result.Success)
            {
                var firstError = result.Diagnostics.FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                if (firstError is { })
                {
                    progress?.Report($"Could not compile storage project reference {referencedProject.Name}: {firstError.GetMessage()}");
                }

                return null;
            }

            return WorkspaceAssemblyReference.FromImage(
                assemblyName + ".dll",
                stream.ToArray(),
                isRuntimeAssembly: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            progress?.Report($"Could not compile storage project reference {referencedProject.Name}: {exception.Message}");
            return null;
        }
    }

    private static async Task<IReadOnlyList<PortableExecutableReference>> GetStorageCompilationReferencesAsync(
        InMemoryProject project)
    {
        var references = new List<PortableExecutableReference>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workspaceReference in project.AssemblyReferences)
        {
            if (IsProjectAssemblyReference(project, workspaceReference))
            {
                continue;
            }

            var metadataReference = workspaceReference.CreateMetadataReference();
            var key = workspaceReference.Image is { Length: > 0 }
                ? workspaceReference.Name
                : metadataReference is { }
                    ? GetReferenceKey(metadataReference)
                    : workspaceReference.Name;
            if (metadataReference is { } && !string.IsNullOrWhiteSpace(key) && keys.Add(key))
            {
                references.Add(metadataReference);
            }
        }

        foreach (var baseReference in await CompilerService.GetMetadataReferences())
        {
            var key = GetReferenceKey(baseReference);
            if (!string.IsNullOrWhiteSpace(key) && keys.Add(key))
            {
                references.Add(baseReference);
            }
        }

        return references;
    }

    private static string GetStorageProjectKey(InMemoryProject project)
    {
        return string.IsNullOrWhiteSpace(project.ProjectFilePath)
            ? project.Name
            : NormalizePath(project.ProjectFilePath);
    }

    private static bool IsProjectAssemblyReference(InMemoryProject project, WorkspaceAssemblyReference reference)
    {
        return string.Equals(reference.Name, GetProjectAssemblyName(project), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectAssemblyName(InMemoryProject project)
    {
        return string.IsNullOrWhiteSpace(project.AssemblyName)
            ? project.Name
            : project.AssemblyName;
    }

    private static string? GetReferenceKey(PortableExecutableReference reference)
    {
        var path = reference.FilePath ?? reference.Display;
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFileNameWithoutExtension(path);
    }

    private static async Task AddRoslynReferencesAsync(
        InMemoryProject inMemoryProject,
        Project project,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
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
            if (referencedProject is null)
            {
                continue;
            }

            var outputReference = GetProjectOutputReference(referencedProject);
            if (ShouldEmitProjectReferenceFromSourceDuringLoad(referencedProject, outputReference))
            {
                var sourceReference = await CreateProjectReferenceAssemblyAsync(referencedProject, null, cancellationToken);
                if (sourceReference is { })
                {
                    AddReference(inMemoryProject, sourceReference);
                    continue;
                }
            }

            AddReference(inMemoryProject, outputReference);
        }

        AddReference(inMemoryProject, WorkspaceAssemblyReference.FromPath(
            project.OutputFilePath,
            isRuntimeAssembly: true));
    }

    private static bool ShouldEmitProjectReferenceFromSource(Project referencedProject)
    {
        var outputPath = referencedProject.OutputFilePath;
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return true;
        }

        DateTime outputWriteTime;
        try
        {
            outputWriteTime = File.GetLastWriteTimeUtc(outputPath);
        }
        catch
        {
            return true;
        }

        foreach (var document in referencedProject.Documents)
        {
            var filePath = document.FilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(filePath) > outputWriteTime)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldEmitProjectReferenceFromSourceDuringLoad(
        Project referencedProject,
        WorkspaceAssemblyReference? outputReference)
    {
        return outputReference is null || ShouldEmitProjectReferenceFromSource(referencedProject);
    }

    private static WorkspaceAssemblyReference? GetProjectOutputReference(Project project)
    {
        return WorkspaceAssemblyReference.FromPath(project.OutputFilePath, isRuntimeAssembly: true) ??
               FindExistingProjectOutputReference(project);
    }

    private static WorkspaceAssemblyReference? FindExistingProjectOutputReference(Project project)
    {
        var outputFileName = Path.GetFileName(project.OutputFilePath);
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            outputFileName = (string.IsNullOrWhiteSpace(project.AssemblyName) ? project.Name : project.AssemblyName) + ".dll";
        }

        if (!WorkspaceAssemblyReference.IsAssemblyFile(outputFileName))
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(project.FilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var binDirectory = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        var targetFramework = TryGetTargetFrameworkFromOutputPath(project.OutputFilePath);
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(binDirectory, outputFileName, SearchOption.AllDirectories)
                .Where(static path => WorkspaceAssemblyReference.IsAssemblyFile(path))
                .OrderByDescending(path => IsTargetFrameworkMatch(path, targetFramework))
                .ThenByDescending(GetLastWriteTimeUtcOrMin)
                .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (WorkspaceAssemblyReference.FromPath(candidate, isRuntimeAssembly: true) is { } reference)
            {
                return reference;
            }
        }

        return null;
    }

    private static DateTime GetLastWriteTimeUtcOrMin(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static async Task<WorkspaceAssemblyReference?> CreateProjectReferenceAssemblyAsync(
        Project referencedProject,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var compilation = await referencedProject
                .GetCompilationAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (compilation is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            var result = compilation.Emit(stream, cancellationToken: cancellationToken);
            if (!result.Success)
            {
                var firstError = result.Diagnostics.FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                if (firstError is { })
                {
                    progress?.Report($"Could not compile project reference {referencedProject.Name}: {firstError.GetMessage()}");
                }

                return null;
            }

            var assemblyName = string.IsNullOrWhiteSpace(referencedProject.AssemblyName)
                ? referencedProject.Name
                : referencedProject.AssemblyName!;
            return WorkspaceAssemblyReference.FromImage(
                assemblyName + ".dll",
                stream.ToArray(),
                isRuntimeAssembly: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            progress?.Report($"Could not compile project reference {referencedProject.Name}: {exception.Message}");
            return null;
        }
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

    private static void ReplaceReference(InMemoryProject project, WorkspaceAssemblyReference reference)
    {
        for (var i = project.AssemblyReferences.Count - 1; i >= 0; i--)
        {
            if (string.Equals(project.AssemblyReferences[i].Name, reference.Name, StringComparison.OrdinalIgnoreCase))
            {
                project.AssemblyReferences.RemoveAt(i);
            }
        }

        AddReference(project, reference);
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

    private static IEnumerable<string> EnumerateProjectFiles(
        string? projectDirectory,
        string projectFilePath,
        IEnumerable<string>? excludedProjectFolders = null)
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

        var excludedFolders = excludedProjectFolders?.ToArray() ?? Array.Empty<string>();
        foreach (var file in files)
        {
            if (file.Equals(projectFilePath, StringComparison.OrdinalIgnoreCase) ||
                IsUnderExcludedLocalProjectFolder(file, excludedFolders) ||
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
                case IStorageFolder childFolder when IsBuildOutputDirectoryName(childFolder.Name):
                    if (childFolder.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        await EnumerateAssemblyStorageFolderAsync(childFolder, path, assemblies, cancellationToken);
                    }

                    break;
                case IStorageFolder childFolder:
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

    private static async Task RestoreLocalWorkspaceIfNeededAsync(
        string workspacePath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!LocalWorkspaceNeedsRestore(workspacePath))
        {
            return;
        }

        progress?.Report("Restoring MSBuild workspace packages...");
        var result = await RunDotNetAsync("restore", workspacePath, cancellationToken);
        foreach (var line in result.OutputLines)
        {
            progress?.Report(line);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"dotnet restore failed for {workspacePath}.{Environment.NewLine}{string.Join(Environment.NewLine, result.OutputLines)}");
        }
    }

    private static void ReportWorkspaceDiagnostic(WorkspaceDiagnostic diagnostic, IProgress<string>? progress)
    {
        if (progress is null || !ShouldReportWorkspaceDiagnosticMessage(diagnostic.Message))
        {
            return;
        }

        progress.Report($"Workspace diagnostic: {diagnostic.Message}");
    }

    private static bool ShouldReportWorkspaceDiagnosticMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return !IsNuGetAuditVulnerabilityMessage(message);
    }

    private static bool IsNuGetAuditVulnerabilityMessage(string message)
    {
        return message.Contains("has a known", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("severity vulnerability", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LocalWorkspaceNeedsRestore(string workspacePath)
    {
        foreach (var projectPath in EnumerateLocalWorkspaceProjectPaths(workspacePath))
        {
            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                continue;
            }

            if (!File.Exists(Path.Combine(projectDirectory, "obj", "project.assets.json")))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateLocalWorkspaceProjectPaths(string workspacePath)
    {
        if (IsSupportedProjectPath(workspacePath))
        {
            yield return workspacePath;
            yield break;
        }

        if (!IsSolutionPath(workspacePath) || !File.Exists(workspacePath))
        {
            yield break;
        }

        IReadOnlyList<StandardSolutionProjectEntry> entries;
        try
        {
            entries = StandardSolutionStorage.ParseSolutionEntries(
                Path.GetFileName(workspacePath),
                File.ReadAllText(workspacePath));
        }
        catch
        {
            yield break;
        }

        var solutionDirectory = Path.GetDirectoryName(workspacePath) ?? string.Empty;
        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(solutionDirectory, entry.Path));
            if (File.Exists(projectPath))
            {
                yield return projectPath;
            }
        }
    }

    private static async Task<DotNetProcessResult> RunDotNetAsync(
        string command,
        string targetPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.ArgumentList.Add(targetPath);
        process.StartInfo.ArgumentList.Add("--nologo");

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start dotnet.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var lines = SplitProcessOutput(await outputTask, await errorTask);
        return new DotNetProcessResult(process.ExitCode, lines);
    }

    private static IReadOnlyList<string> SplitProcessOutput(params string[] outputs)
    {
        return outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .SelectMany(static output => output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct()
            .ToArray();
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

    private static bool IsBuildOutputDirectoryName(string name)
    {
        return name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetExcludedLocalProjectFolders(
        string? projectDirectory,
        IEnumerable<string> projectFolders)
    {
        var normalizedProjectDirectory = NormalizeLocalPath(projectDirectory);
        return projectFolders
            .Select(NormalizeLocalPath)
            .Where(static folder => !string.IsNullOrWhiteSpace(folder))
            .Where(folder => !IsSameOrUnderProjectFolder(normalizedProjectDirectory, folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUnderExcludedLocalProjectFolder(
        string filePath,
        IEnumerable<string> excludedProjectFolders)
    {
        var normalizedFilePath = NormalizeLocalPath(filePath);
        return excludedProjectFolders.Any(folder => IsUnderProjectFolder(folder, normalizedFilePath));
    }

    private static bool IsUnderProjectFolder(string projectFolder, string filePath)
    {
        return string.IsNullOrWhiteSpace(projectFolder) ||
               filePath.StartsWith(projectFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetExcludedStorageProjectFolders(
        string projectFolder,
        IEnumerable<string> projectFolders)
    {
        var normalizedProjectFolder = NormalizePath(projectFolder);
        return projectFolders
            .Select(NormalizePath)
            .Where(static folder => !string.IsNullOrWhiteSpace(folder))
            .Where(folder => !IsSameOrUnderProjectFolder(normalizedProjectFolder, folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsStorageProjectFileInScope(
        string projectFolder,
        string filePath,
        IEnumerable<string> excludedProjectFolders)
    {
        if (!IsUnderProjectFolder(projectFolder, filePath))
        {
            return false;
        }

        return !excludedProjectFolders.Any(folder => IsUnderProjectFolder(folder, filePath));
    }

    private static bool IsSameOrUnderProjectFolder(string path, string projectFolder)
    {
        var normalizedPath = NormalizePath(path).TrimEnd('/');
        var normalizedProjectFolder = NormalizePath(projectFolder).TrimEnd('/');
        return string.Equals(normalizedPath, normalizedProjectFolder, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedProjectFolder + "/", StringComparison.OrdinalIgnoreCase);
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

    private static string ResolveStorageSolutionProjectPath(string solutionPath, string projectPath)
    {
        var solutionDirectory = GetDirectoryName(solutionPath);
        var normalizedProjectPath = NormalizePath(projectPath);
        if (string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return normalizedProjectPath;
        }

        return CollapseRelativeSegments($"{solutionDirectory.TrimEnd('/')}/{normalizedProjectPath}");
    }

    private static string CollapseRelativeSegments(string path)
    {
        var segments = new List<string>();
        foreach (var segment in NormalizePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals(".", StringComparison.Ordinal))
            {
                continue;
            }

            if (segment.Equals("..", StringComparison.Ordinal))
            {
                if (segments.Count == 0)
                {
                    segments.Add(segment);
                }
                else if (segments[^1].Equals("..", StringComparison.Ordinal))
                {
                    segments.Add(segment);
                }
                else
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string NormalizeLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return NormalizePath(Path.GetFullPath(path));
        }
        catch
        {
            return NormalizePath(path);
        }
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

    private sealed record DotNetProcessResult(int ExitCode, IReadOnlyList<string> OutputLines);
}
