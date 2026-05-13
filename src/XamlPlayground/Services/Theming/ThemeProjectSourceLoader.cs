using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace XamlPlayground.Services.Theming;

public sealed record ThemeProjectSource(
    ThemeProjectDocument Project,
    string Description,
    string? SourceRoot);

public static class ThemeProjectSourceLoader
{
    private const string AvaloniaFluentThemeRelativePath = "src/Avalonia.Themes.Fluent";
    private const string ExternalAvaloniaFluentThemeRelativePath = "external/Avalonia/src/Avalonia.Themes.Fluent";
    private const string EmbeddedFluentThemeResourcePrefix = "XamlPlayground.EmbeddedThemes.AvaloniaFluent/";
    private static readonly string[] ThemeFileExtensions = { ".xaml", ".axaml" };
    private static readonly string[] IgnoredDirectoryNames = { ".git", "bin", "obj" };

    public static ThemeProjectSource LoadDefaultFluentThemeProject()
    {
        if (OperatingSystem.IsBrowser())
        {
            return TryLoadEmbeddedFluentThemeProject(out var browserEmbeddedSource)
                ? browserEmbeddedSource
                : CreateMissingFluentThemeSource();
        }

        var environmentOverride = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        if (TryLoadFluentThemeDirectory(environmentOverride, out var environmentSource))
        {
            return environmentSource;
        }

        foreach (var basePath in EnumerateBasePaths())
        {
            var externalCandidate = Path.GetFullPath(Path.Combine(basePath, ExternalAvaloniaFluentThemeRelativePath));
            if (TryLoadFluentThemeDirectory(externalCandidate, out var externalSource))
            {
                return externalSource;
            }

            var siblingCandidate = Path.GetFullPath(Path.Combine(basePath, "..", "Avalonia", AvaloniaFluentThemeRelativePath));
            if (TryLoadFluentThemeDirectory(siblingCandidate, out var siblingSource))
            {
                return siblingSource;
            }
        }

        if (TryLoadEmbeddedFluentThemeProject(out var embeddedSource))
        {
            return embeddedSource;
        }

        return CreateMissingFluentThemeSource();
    }

    public static ThemeProjectSource LoadEmbeddedFluentThemeProject()
    {
        if (TryLoadEmbeddedFluentThemeProject(out var source))
        {
            return source;
        }

        throw new InvalidDataException("Bundled Avalonia Fluent theme resources were not found in this build.");
    }

    private static ThemeProjectSource CreateMissingFluentThemeSource()
    {
        return new ThemeProjectSource(
            ThemeProjectStorage.CreateDocument("AvaloniaFluent", Array.Empty<(string Path, string Text)>()),
            "Fluent theme source not found.",
            SourceRoot: null);
    }

    public static bool TryLoadEmbeddedFluentThemeProject([NotNullWhen(true)] out ThemeProjectSource? source)
    {
        var assembly = typeof(ThemeProjectSourceLoader).Assembly;
        var files = assembly
            .GetManifestResourceNames()
            .Where(static name => name.StartsWith(EmbeddedFluentThemeResourcePrefix, StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    return default;
                }

                using var reader = new StreamReader(stream);
                var path = name[EmbeddedFluentThemeResourcePrefix.Length..];
                return (Path: ThemeProjectStorage.NormalizeProjectPath(path), Text: reader.ReadToEnd());
            })
            .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
            .ToArray();

        if (files.Length == 0)
        {
            source = null;
            return false;
        }

        source = CreateSourceFromFiles(
            "AvaloniaFluent",
            "Bundled Avalonia Fluent theme",
            sourceRoot: null,
            files,
            stripCommonArchiveRoot: false);
        return true;
    }

    public static bool TryLoadFluentThemeDirectory(
        string? directory,
        [NotNullWhen(true)] out ThemeProjectSource? source)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            source = null;
            return false;
        }

        var root = ResolveFluentThemeRoot(directory);
        if (root is null)
        {
            source = null;
            return false;
        }

        source = CreateSourceFromDirectory(
            root,
            "AvaloniaFluent",
            $"Fluent source: {root}",
            sourceRoot: root);
        return true;
    }

    public static ThemeProjectSource LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Theme folder '{directory}' does not exist.");
        }

        var root = ResolvePreferredThemeRoot(directory);
        var name = new DirectoryInfo(root).Name;
        return CreateSourceFromDirectory(
            root,
            name,
            $"Theme folder: {root}",
            sourceRoot: root);
    }

    public static async Task<ThemeProjectSource> LoadFromStorageFolderAsync(IStorageFolder folder)
    {
        if (folder.TryGetLocalPath() is { } localPath && Directory.Exists(localPath))
        {
            return LoadFromDirectory(localPath);
        }

        var files = await ReadStorageFolderThemeFilesAsync(folder, string.Empty);
        return CreateSourceFromFiles(
            string.IsNullOrWhiteSpace(folder.Name) ? "ThemeFolder" : folder.Name,
            $"Theme folder: {folder.Name}",
            sourceRoot: null,
            files,
            stripCommonArchiveRoot: false);
    }

    public static async Task<ThemeProjectSource> LoadFromRemoteGitRepositoryAsync(string repositoryUrl)
    {
        using var client = CreateHttpClient();
        return await LoadFromRemoteGitRepositoryAsync(repositoryUrl, client);
    }

    internal static async Task<ThemeProjectSource> LoadFromRemoteGitRepositoryAsync(
        string repositoryUrl,
        HttpClient client)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri) ||
            repositoryUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Theme repository must be an absolute HTTPS URL.");
        }

        if (IsZipArchiveUri(repositoryUri))
        {
            return await LoadFromArchiveUrlAsync(client, repositoryUri, $"Theme archive: {repositoryUri}");
        }

        Exception? archiveException = null;
        foreach (var archiveUri in await CreateArchiveDownloadCandidatesAsync(repositoryUri, client))
        {
            try
            {
                return await LoadFromArchiveUrlAsync(client, archiveUri, $"Git repository: {repositoryUri}");
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
            {
                archiveException = exception;
            }
        }

        if (!OperatingSystem.IsBrowser())
        {
            return await LoadFromGitCloneAsync(repositoryUrl);
        }

        throw new NotSupportedException(
            archiveException is null
                ? "This repository host does not expose a known zip archive URL. Use a GitHub/GitLab URL or a direct .zip archive URL in the browser."
                : $"Failed to load repository archive: {archiveException.Message}");
    }

    private static ThemeProjectSource CreateSourceFromDirectory(
        string root,
        string name,
        string description,
        string? sourceRoot)
    {
        var files = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsThemeResourceFile)
            .Where(static path => !HasIgnoredPathSegment(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => (
                Path: ThemeProjectStorage.NormalizeProjectPath(Path.GetRelativePath(root, path)),
                Text: File.ReadAllText(path)))
            .ToArray();

        return CreateSourceFromFiles(name, description, sourceRoot, files, stripCommonArchiveRoot: false);
    }

    private static ThemeProjectSource CreateSourceFromFiles(
        string name,
        string description,
        string? sourceRoot,
        IReadOnlyList<(string Path, string Text)> files,
        bool stripCommonArchiveRoot)
    {
        var normalizedFiles = files
            .Select(static file => (
                Path: ThemeProjectStorage.NormalizeProjectPath(file.Path),
                file.Text))
            .Where(static file => IsThemeResourcePath(file.Path))
            .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (stripCommonArchiveRoot)
        {
            normalizedFiles = StripSingleCommonRoot(normalizedFiles);
        }

        normalizedFiles = SelectPreferredThemeRootFiles(normalizedFiles);

        if (normalizedFiles.Length == 0)
        {
            throw new InvalidDataException("No .xaml or .axaml theme files were found.");
        }

        return new ThemeProjectSource(
            ThemeProjectStorage.CreateDocument(name, normalizedFiles),
            description,
            sourceRoot);
    }

    private static async Task<ThemeProjectSource> LoadFromArchiveUrlAsync(
        HttpClient client,
        Uri archiveUri,
        string description)
    {
        using var response = await client.GetAsync(archiveUri);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException($"Archive download failed with HTTP {(int)response.StatusCode}.");
        }

        await using var download = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        await download.CopyToAsync(memory);
        memory.Position = 0;

        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var files = archive
            .Entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Where(static entry => IsThemeResourcePath(entry.FullName))
            .Where(static entry => !HasIgnoredPathSegment(entry.FullName))
            .OrderBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                return (Path: entry.FullName, Text: reader.ReadToEnd());
            })
            .ToArray();

        return CreateSourceFromFiles(
            Path.GetFileNameWithoutExtension(archiveUri.AbsolutePath) is { Length: > 0 } archiveName
                ? archiveName
                : "ThemeRepository",
            description,
            sourceRoot: null,
            files,
            stripCommonArchiveRoot: true);
    }

    private static async Task<IReadOnlyList<Uri>> CreateArchiveDownloadCandidatesAsync(
        Uri repositoryUri,
        HttpClient client)
    {
        if (TryParseGitHubRepository(repositoryUri, out var owner, out var repository, out var githubBranch))
        {
            var defaultBranch = githubBranch ?? await TryResolveGitHubDefaultBranchAsync(client, owner, repository);
            return CreateBranchCandidates(defaultBranch)
                .Select(branch => new Uri($"https://github.com/{owner}/{repository}/archive/refs/heads/{Uri.EscapeDataString(branch)}.zip"))
                .ToArray();
        }

        if (TryParseGitLabRepository(repositoryUri, out var gitLabHost, out var projectPath, out var gitLabBranch))
        {
            var defaultBranch = gitLabBranch ?? await TryResolveGitLabDefaultBranchAsync(client, gitLabHost, projectPath);
            var encodedProjectPath = Uri.EscapeDataString(projectPath);
            return CreateBranchCandidates(defaultBranch)
                .Select(branch => new Uri($"https://{gitLabHost}/api/v4/projects/{encodedProjectPath}/repository/archive.zip?sha={Uri.EscapeDataString(branch)}"))
                .ToArray();
        }

        return Array.Empty<Uri>();
    }

    private static async Task<ThemeProjectSource> LoadFromGitCloneAsync(string repositoryUrl)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"XamlPlaygroundThemeRepo-{Guid.NewGuid():N}");
        try
        {
            var result = await RunProcessAsync("git", new[] { "clone", "--depth", "1", repositoryUrl, tempRoot });
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? $"git clone failed with exit code {result.ExitCode}."
                        : result.Error.Trim());
            }

            var source = LoadFromDirectory(tempRoot);
            return new ThemeProjectSource(
                source.Project,
                $"Git repository: {repositoryUrl}",
                SourceRoot: null);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<string?> TryResolveGitHubDefaultBranchAsync(
        HttpClient client,
        string owner,
        string repository)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repository}");
            AddUserAgent(request);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            return json.RootElement.TryGetProperty("default_branch", out var branch)
                ? branch.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryResolveGitLabDefaultBranchAsync(
        HttpClient client,
        string host,
        string projectPath)
    {
        try
        {
            var encodedProjectPath = Uri.EscapeDataString(projectPath);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/api/v4/projects/{encodedProjectPath}");
            AddUserAgent(request);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            return json.RootElement.TryGetProperty("default_branch", out var branch)
                ? branch.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> CreateBranchCandidates(string? preferredBranch)
    {
        if (!string.IsNullOrWhiteSpace(preferredBranch))
        {
            yield return preferredBranch;
        }

        yield return "main";
        yield return "master";
    }

    private static bool TryParseGitHubRepository(
        Uri uri,
        [NotNullWhen(true)] out string? owner,
        [NotNullWhen(true)] out string? repository,
        out string? branch)
    {
        owner = null;
        repository = null;
        branch = null;

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repository = TrimGitSuffix(segments[1]);
        if (segments.Length >= 4 && string.Equals(segments[2], "tree", StringComparison.OrdinalIgnoreCase))
        {
            branch = string.Join("/", segments[3..]);
        }

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repository);
    }

    private static bool TryParseGitLabRepository(
        Uri uri,
        [NotNullWhen(true)] out string? host,
        [NotNullWhen(true)] out string? projectPath,
        out string? branch)
    {
        host = null;
        projectPath = null;
        branch = null;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var markerIndex = Array.FindIndex(segments, static segment => segment == "-");
        var projectSegments = markerIndex >= 0 ? segments[..markerIndex] : segments;
        if (projectSegments.Length < 2)
        {
            return false;
        }

        if (!uri.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) && markerIndex < 0)
        {
            return false;
        }

        host = uri.Host;
        projectPath = string.Join("/", projectSegments.Select(TrimGitSuffix));
        if (markerIndex >= 0 &&
            segments.Length > markerIndex + 2 &&
            string.Equals(segments[markerIndex + 1], "tree", StringComparison.OrdinalIgnoreCase))
        {
            branch = string.Join("/", segments[(markerIndex + 2)..]);
        }

        return !string.IsNullOrWhiteSpace(projectPath);
    }

    private static (string Path, string Text)[] StripSingleCommonRoot(
        IReadOnlyList<(string Path, string Text)> files)
    {
        if (files.Count == 0)
        {
            return files.ToArray();
        }

        var firstSegments = files
            .Select(static file => file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        if (firstSegments.Any(static segments => segments.Length < 2))
        {
            return files.ToArray();
        }

        var commonRoot = firstSegments[0][0];
        if (firstSegments.Any(segments => !string.Equals(segments[0], commonRoot, StringComparison.OrdinalIgnoreCase)))
        {
            return files.ToArray();
        }

        return files
            .Select(file => (
                Path: ThemeProjectStorage.NormalizeProjectPath(file.Path[(commonRoot.Length + 1)..]),
                file.Text))
            .ToArray();
    }

    private static (string Path, string Text)[] SelectPreferredThemeRootFiles(
        IReadOnlyList<(string Path, string Text)> files)
    {
        var prefix = FindPreferredThemeRootPrefix(files.Select(static file => file.Path));
        if (prefix is null)
        {
            return files.ToArray();
        }

        return files
            .Where(file => file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(file => (
                Path: ThemeProjectStorage.NormalizeProjectPath(file.Path[prefix.Length..]),
                file.Text))
            .ToArray();
    }

    private static string? FindPreferredThemeRootPrefix(IEnumerable<string> paths)
    {
        var pathArray = paths
            .Select(ThemeProjectStorage.NormalizeProjectPath)
            .ToArray();
        if (pathArray.Contains("FluentTheme.xaml", StringComparer.OrdinalIgnoreCase) &&
            pathArray.Any(static path => path.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        foreach (var fluentThemePath in pathArray.Where(static path => path.EndsWith("/FluentTheme.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = fluentThemePath[..^"FluentTheme.xaml".Length];
            if (pathArray.Any(path => path.StartsWith(prefix + "Controls/", StringComparison.OrdinalIgnoreCase)))
            {
                return prefix;
            }
        }

        return null;
    }

    private static string ResolvePreferredThemeRoot(string directory)
    {
        return ResolveFluentThemeRoot(directory) ?? directory;
    }

    private static string? ResolveFluentThemeRoot(string directory)
    {
        if (IsFluentThemeRoot(directory))
        {
            return Path.GetFullPath(directory);
        }

        foreach (var relativePath in new[]
                 {
                     AvaloniaFluentThemeRelativePath,
                     ExternalAvaloniaFluentThemeRelativePath
                 })
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (IsFluentThemeRoot(candidate))
            {
                return candidate;
            }
        }

        try
        {
            foreach (var fluentThemeFile in Directory.EnumerateFiles(directory, "FluentTheme.xaml", SearchOption.AllDirectories))
            {
                var candidate = Path.GetDirectoryName(fluentThemeFile);
                if (IsFluentThemeRoot(candidate))
                {
                    return Path.GetFullPath(candidate!);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool IsFluentThemeRoot(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "FluentTheme.xaml")) &&
               Directory.Exists(Path.Combine(path, "Controls"));
    }

    private static async Task<IReadOnlyList<(string Path, string Text)>> ReadStorageFolderThemeFilesAsync(
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
                case IStorageFile file when IsThemeResourcePath(path):
                    await using (var stream = await file.OpenReadAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        files.Add((ThemeProjectStorage.NormalizeProjectPath(path), await reader.ReadToEndAsync()));
                    }

                    break;

                case IStorageFolder childFolder when !IsIgnoredDirectoryName(childFolder.Name):
                    files.AddRange(await ReadStorageFolderThemeFilesAsync(childFolder, path));
                    break;
            }
        }

        return files;
    }

    private static IEnumerable<string> EnumerateBasePaths()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            yield return current.FullName;
        }
    }

    private static bool IsThemeResourceFile(string path)
    {
        return IsThemeResourcePath(path) && File.Exists(path);
    }

    private static bool IsThemeResourcePath(string path)
    {
        var extension = Path.GetExtension(path);
        return ThemeFileExtensions.Any(candidate => extension.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasIgnoredPathSegment(string path)
    {
        return ThemeProjectStorage.NormalizeProjectPath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(IsIgnoredDirectoryName);
    }

    private static bool IsIgnoredDirectoryName(string name)
    {
        return IgnoredDirectoryNames.Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsZipArchiveUri(Uri uri)
    {
        return uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimGitSuffix(string value)
    {
        return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        if (!OperatingSystem.IsBrowser())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XamlPlayground/1.0");
        }

        return client;
    }

    private static void AddUserAgent(HttpRequestMessage request)
    {
        if (!OperatingSystem.IsBrowser())
        {
            request.Headers.UserAgent.ParseAdd("XamlPlayground/1.0");
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temporary git clones.
        }
    }
}
