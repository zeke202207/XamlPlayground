using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Diagnostics;
using Avalonia.VisualTree;

namespace XamlPlayground.Services.VisualEditing;

public sealed record VisualTreeNodeSnapshot(
    string RuntimeId,
    string TypeName,
    string? Name,
    Rect Bounds,
    IReadOnlyList<int> Path,
    string? SourceUri,
    int? SourceLineNumber,
    int? SourceLinePosition,
    IReadOnlyList<VisualTreeNodeSnapshot> Children);

public interface IVisualTreeSnapshotService
{
    VisualTreeNodeSnapshot Snapshot(Visual root);
}

public sealed class AvaloniaVisualTreeSnapshotService : IVisualTreeSnapshotService
{
    public VisualTreeNodeSnapshot Snapshot(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return SnapshotCore(root, Array.Empty<int>());
    }

    private static VisualTreeNodeSnapshot SnapshotCore(Visual visual, IReadOnlyList<int> path)
    {
        var children = visual.GetVisualChildren().ToArray();
        var childSnapshots = new List<VisualTreeNodeSnapshot>(children.Length);
        for (var i = 0; i < children.Length; i++)
        {
            childSnapshots.Add(SnapshotCore(children[i], path.Concat(new[] { i }).ToArray()));
        }

        var bounds = visual.GetTransformedBounds()?.Bounds ?? default;
        var sourceInfo = XamlSourceInfo.GetXamlSourceInfo(visual);
        return new VisualTreeNodeSnapshot(
            CreateRuntimeId(visual, path),
            visual.GetType().Name,
            (visual as Control)?.Name,
            bounds,
            path.ToArray(),
            sourceInfo?.SourceUri?.ToString(),
            sourceInfo?.LineNumber,
            sourceInfo?.LinePosition,
            childSnapshots);
    }

    private static string CreateRuntimeId(Visual visual, IReadOnlyList<int> path)
    {
        var name = (visual as Control)?.Name;
        var key = string.IsNullOrWhiteSpace(name) ? string.Join(".", path) : name;
        return $"{visual.GetType().FullName}:{key}";
    }
}

public sealed class XamlVisualTreeMapper
{
    public XamlElementSnapshot? FindXamlElement(
        VisualTreeNodeSnapshot visualNode,
        XamlDocumentSnapshot xamlSnapshot,
        bool allowTypeFallback = true)
    {
        ArgumentNullException.ThrowIfNull(visualNode);
        ArgumentNullException.ThrowIfNull(xamlSnapshot);

        if (TryFindBySourceInfo(visualNode, xamlSnapshot) is { } bySourceInfo)
        {
            return bySourceInfo;
        }

        if (!string.IsNullOrWhiteSpace(visualNode.Name))
        {
            var byName = xamlSnapshot.Elements.FirstOrDefault(element =>
                string.Equals(element.Name, visualNode.Name, StringComparison.Ordinal));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (!allowTypeFallback)
        {
            return null;
        }

        var typeMatches = xamlSnapshot.Elements
            .Where(element =>
                string.Equals(element.TypeName, visualNode.TypeName, StringComparison.Ordinal) ||
                string.Equals(GetLocalName(element.TypeName), visualNode.TypeName, StringComparison.Ordinal))
            .ToArray();

        if (typeMatches.Length == 1)
        {
            return typeMatches[0];
        }

        if (typeMatches.Length > 1)
        {
            return null;
        }

        return null;
    }

    private static XamlElementSnapshot? TryFindBySourceInfo(
        VisualTreeNodeSnapshot visualNode,
        XamlDocumentSnapshot xamlSnapshot)
    {
        if (visualNode.SourceLineNumber is not { } lineNumber ||
            visualNode.SourceLinePosition is not { } linePosition ||
            lineNumber < 1 ||
            linePosition < 1)
        {
            return null;
        }

        var offset = GetOffsetFromLinePosition(xamlSnapshot.Text, lineNumber, linePosition);
        if (offset < 0)
        {
            return null;
        }

        return xamlSnapshot.Elements
            .Where(element =>
                (string.Equals(element.TypeName, visualNode.TypeName, StringComparison.Ordinal) ||
                 string.Equals(GetLocalName(element.TypeName), visualNode.TypeName, StringComparison.Ordinal)) &&
                element.Start <= offset &&
                offset <= element.Start + element.Length)
            .OrderByDescending(static element => element.Path.Count)
            .ThenBy(static element => element.Length)
            .FirstOrDefault();
    }

    private static int GetOffsetFromLinePosition(string text, int lineNumber, int linePosition)
    {
        var currentLine = 1;
        var currentLineStart = 0;
        for (var i = 0; i < text.Length && currentLine < lineNumber; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            currentLine++;
            currentLineStart = i + 1;
        }

        if (currentLine != lineNumber)
        {
            return -1;
        }

        return Math.Min(text.Length, currentLineStart + linePosition - 1);
    }

    private static string GetLocalName(string typeName)
    {
        var index = typeName.IndexOf(':', StringComparison.Ordinal);
        return index < 0 ? typeName : typeName[(index + 1)..];
    }
}
