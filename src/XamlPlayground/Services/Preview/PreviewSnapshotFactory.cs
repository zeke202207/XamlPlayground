using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using XamlPlayground.Workspace;

namespace XamlPlayground.Services.Preview;

public static class PreviewSnapshotFactory
{
    public static PreviewSnapshot Create(
        InMemorySolution? solution,
        InMemoryProject project,
        InMemoryProjectFile activeXamlFile,
        string? outputAssemblyPath = null)
    {
        var activePath = NormalizePath(activeXamlFile.Path);
        var files = project.Files
            .Select(static file => new PreviewSourceFile(
                NormalizePath(file.Path),
                file.Kind,
                file.Text,
                file.IncludeInRuntimePreview,
                file.IncludeInCompilation,
                NormalizeOptionalPath(file.SourcePath)))
            .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeCodeBehindPath = project.FindCodeBehind(activeXamlFile)?.Path;
        var appXamlPath = FindAppXamlPath(project);
        var assemblyReferences = project.AssemblyReferences
            .Select(static reference => new PreviewAssemblyReference(
                reference.Name,
                NormalizeOptionalPath(reference.FilePath),
                reference.IsReferenceAssembly,
                reference.IsRuntimeAssembly,
                reference.Image is { Length: > 0 },
                GetReferenceFingerprint(reference)))
            .OrderBy(static reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PreviewSnapshot(
            Guid.NewGuid(),
            solution?.Name ?? project.Name,
            project.Name,
            project.RootNamespace,
            string.IsNullOrWhiteSpace(project.AssemblyName) ? project.Name : project.AssemblyName,
            activePath,
            NormalizeOptionalPath(activeCodeBehindPath),
            NormalizeOptionalPath(appXamlPath),
            project.TargetFramework,
            project.IsMsBuildWorkspace,
            NormalizeOptionalPath(project.ProjectFilePath),
            NormalizeOptionalPath(project.WorkspaceRootPath),
            NormalizeOptionalPath(outputAssemblyPath ?? project.OutputAssemblyPath),
            files,
            assemblyReferences);
    }

    private static string? FindAppXamlPath(InMemoryProject project)
    {
        return project.Files.FirstOrDefault(static file =>
            file.Kind == ProjectFileKind.Xaml &&
            (file.Path.Equals("App.axaml", StringComparison.OrdinalIgnoreCase) ||
             file.Path.Equals("App.xaml", StringComparison.OrdinalIgnoreCase)))?.Path;
    }

    private static string? GetReferenceFingerprint(WorkspaceAssemblyReference reference)
    {
        if (reference.Image is { Length: > 0 } image)
        {
            return $"image:{image.Length}:{Convert.ToHexString(SHA256.HashData(image))}";
        }

        if (reference.FilePath is not { } filePath || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            return $"file:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        return normalized.Trim('/');
    }
}
