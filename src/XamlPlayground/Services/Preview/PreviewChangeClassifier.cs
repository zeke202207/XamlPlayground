using System;
using System.Collections.Generic;
using System.Linq;
using XamlPlayground.Workspace;

namespace XamlPlayground.Services.Preview;

public static class PreviewChangeClassifier
{
    public static PreviewChangeSet Classify(PreviewSnapshot? previous, PreviewSnapshot current)
    {
        if (previous is null)
        {
            return new PreviewChangeSet(
                PreviewUpdateKind.Initial,
                current.Files.Select(static file => file.Path).ToArray(),
                ReferencesChanged: false);
        }

        var referencesChanged = !HaveSameReferences(previous.AssemblyReferences, current.AssemblyReferences);
        var changedPaths = GetChangedPaths(previous, current);
        if (changedPaths.Count == 0 && !referencesChanged)
        {
            return new PreviewChangeSet(PreviewUpdateKind.None, Array.Empty<string>(), ReferencesChanged: false);
        }

        if (referencesChanged)
        {
            return new PreviewChangeSet(
                PreviewUpdateKind.References,
                changedPaths,
                ReferencesChanged: true);
        }

        var changedFiles = changedPaths
            .Select(path => FindFile(previous, current, path))
            .Where(static file => file is not null)
            .ToArray();
        var hasCodeOrProject = changedFiles.Any(static file =>
            file!.Kind is ProjectFileKind.CSharp or ProjectFileKind.ProjectFile);
        var hasXaml = changedFiles.Any(static file => file!.Kind == ProjectFileKind.Xaml);
        var hasResources = changedFiles.Any(static file => file!.Kind == ProjectFileKind.Resource);
        var hasOther = changedFiles.Any(static file =>
            file!.Kind is not ProjectFileKind.CSharp and not ProjectFileKind.ProjectFile and not ProjectFileKind.Xaml and not ProjectFileKind.Resource);

        var kind = hasCodeOrProject
            ? PreviewUpdateKind.CodeOrProject
            : hasOther
                ? PreviewUpdateKind.Mixed
                : hasXaml && hasResources
                    ? PreviewUpdateKind.XamlAndResources
                    : hasXaml
                        ? PreviewUpdateKind.XamlOnly
                        : hasResources
                            ? PreviewUpdateKind.ResourcesOnly
                            : PreviewUpdateKind.Mixed;
        return new PreviewChangeSet(kind, changedPaths, ReferencesChanged: false);
    }

    public static bool CanApplyAsLiveXamlUpdate(PreviewChangeSet changes, PreviewSnapshot current)
    {
        return changes.Kind == PreviewUpdateKind.XamlOnly &&
               changes.ChangedPaths.Count > 0 &&
               changes.ChangedPaths.All(path =>
                   string.Equals(path, current.ActiveXamlPath, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetChangedPaths(PreviewSnapshot previous, PreviewSnapshot current)
    {
        var previousFiles = previous.Files.ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
        var currentFiles = current.Files.ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
        var paths = new SortedSet<string>(
            previousFiles.Keys.Concat(currentFiles.Keys),
            StringComparer.OrdinalIgnoreCase);
        var changedPaths = new List<string>();
        foreach (var path in paths)
        {
            if (!previousFiles.TryGetValue(path, out var previousFile) ||
                !currentFiles.TryGetValue(path, out var currentFile) ||
                previousFile.Kind != currentFile.Kind ||
                previousFile.IncludeInCompilation != currentFile.IncludeInCompilation ||
                previousFile.IncludeInRuntimePreview != currentFile.IncludeInRuntimePreview ||
                !string.Equals(previousFile.Text, currentFile.Text, StringComparison.Ordinal))
            {
                changedPaths.Add(path);
            }
        }

        return changedPaths;
    }

    private static PreviewSourceFile? FindFile(
        PreviewSnapshot previous,
        PreviewSnapshot current,
        string path)
    {
        return current.Files.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)) ??
               previous.Files.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HaveSameReferences(
        IReadOnlyList<PreviewAssemblyReference> previous,
        IReadOnlyList<PreviewAssemblyReference> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        var previousKeys = previous.Select(GetReferenceKey).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase);
        var currentKeys = current.Select(GetReferenceKey).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase);
        return previousKeys.SequenceEqual(currentKeys, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetReferenceKey(PreviewAssemblyReference reference)
    {
        return string.Join(
            "|",
            reference.Name,
            reference.FilePath ?? string.Empty,
            reference.IsReferenceAssembly,
            reference.IsRuntimeAssembly,
            reference.HasImage,
            reference.Fingerprint ?? string.Empty);
    }
}
