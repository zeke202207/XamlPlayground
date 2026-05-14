using System;
using System.Collections.Generic;
using System.Linq;
using XamlPlayground.Editor.Minimap;
using XamlPlayground.Services.Theming;

namespace XamlPlayground.Services.Editing.InlineFeatures;

internal static class PlaygroundEditorContextActions
{
    public static bool TryShowResourceDefinitionPeek(MinimapTextEditor editor, int offset)
    {
        if (!TryGetResourceContext(editor, offset, out var context))
        {
            return false;
        }

        context.Editor.ShowInlinePeek(
            context.SourceLine,
            context.Key,
            $"{context.Definition.FilePath}:{context.Definition.Line}",
            context.DefinitionDocument.Text,
            "xaml",
            300);
        return true;
    }

    public static bool TryGoToResourceDefinition(MinimapTextEditor editor, int offset)
    {
        if (!TryGetResourceContext(editor, offset, out var context))
        {
            return false;
        }

        if (string.Equals(context.Snapshot.CurrentPath, context.Definition.FilePath, StringComparison.OrdinalIgnoreCase) &&
            context.Definition.Line is > 0 &&
            context.Editor.Document is { } document)
        {
            var line = document.GetLineByNumber(Math.Clamp(context.Definition.Line.Value, 1, document.LineCount));
            context.Editor.Select(line.Offset, line.Length);
            context.Editor.CaretOffset = line.Offset;
            context.Editor.ScrollToLine(Math.Max(1, line.LineNumber - 4));
            return true;
        }

        return TryShowResourceDefinitionPeek(editor, offset);
    }

    public static bool TryShowResourceReferencesPeek(MinimapTextEditor editor, int offset)
    {
        if (!TryGetResourceContext(editor, offset, out var context))
        {
            return false;
        }

        var text = PlaygroundInlineFeatureHelpers.GetReferencesText(
            context.References.Select(reference => (reference.FilePath, reference.Line, reference.Snippet)));
        context.Editor.ShowInlinePeek(
            context.SourceLine,
            $"References: {context.Key}",
            $"{context.References.Count} reference(s)",
            text,
            "text",
            220);
        return true;
    }

    public static bool CanResolveResourceAt(MinimapTextEditor editor, int offset)
    {
        return TryGetResourceContext(editor, offset, out _);
    }

    private static bool TryGetResourceContext(
        MinimapTextEditor editor,
        int offset,
        out ResourceContext context)
    {
        context = default;
        var snapshot = PlaygroundInlineFeatures.TryCreateSnapshot(editor);
        var current = snapshot?.CurrentDocument;
        if (snapshot is null ||
            current is not { IsXaml: true } ||
            editor.Document is null)
        {
            return false;
        }

        var resourceDocuments = snapshot.Documents
            .Where(static document => document.IsXaml)
            .Select(static document => new ThemeResourceDocument(document.Path, document.Text, document.IsResource))
            .ToArray();
        if (resourceDocuments.Length == 0)
        {
            return false;
        }

        var analysis = ResourceDictionaryAnalyzer.Analyze(resourceDocuments);
        var text = current.Text;
        offset = Math.Clamp(offset, 0, text.Length);
        string key;
        int sourceLine;
        ThemeResourceDefinition? definition;

        if (TryFindReferenceAt(text, offset, out var match))
        {
            key = match.Key;
            sourceLine = PlaygroundInlineFeatureHelpers.GetLineNumber(text, match.Start);
            definition = analysis.Resources.FirstOrDefault(resource =>
                string.Equals(resource.Key, key, StringComparison.Ordinal));
        }
        else if (TryFindDefinitionAt(current, text, offset, analysis, out var foundDefinition))
        {
            if (foundDefinition is null)
            {
                return false;
            }

            definition = foundDefinition;
            key = definition.Key;
            sourceLine = definition.Line ?? PlaygroundInlineFeatureHelpers.GetLineNumber(text, offset);
        }
        else
        {
            return false;
        }

        if (definition is null ||
            snapshot.Documents.FirstOrDefault(document =>
                string.Equals(document.Path, definition.FilePath, StringComparison.OrdinalIgnoreCase)) is not { } definitionDocument)
        {
            return false;
        }

        var references = analysis.References
            .Where(reference => string.Equals(reference.Key, key, StringComparison.Ordinal))
            .OrderBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Line)
            .ToArray();

        context = new ResourceContext(editor, snapshot, key, sourceLine, definition, definitionDocument, references);
        return true;
    }

    private static bool TryFindReferenceAt(string text, int offset, out ResourceReferenceMatch match)
    {
        var references = ResourceReferenceParser.Find(text).ToArray();
        match = references.FirstOrDefault(reference =>
            offset >= reference.Start &&
            offset <= reference.Start + reference.Length);
        if (match.Length > 0)
        {
            return true;
        }

        var line = PlaygroundInlineFeatureHelpers.GetLineNumber(text, offset);
        match = references.FirstOrDefault(reference =>
            PlaygroundInlineFeatureHelpers.GetLineNumber(text, reference.Start) == line);
        return match.Length > 0;
    }

    private static bool TryFindDefinitionAt(
        PlaygroundInlineDocumentSnapshot current,
        string text,
        int offset,
        ThemeResourceAnalysis analysis,
        out ThemeResourceDefinition? definition)
    {
        definition = analysis.Resources
            .Where(resource => string.Equals(resource.FilePath, current.Path, StringComparison.OrdinalIgnoreCase))
            .Where(resource => resource.Line is > 0)
            .FirstOrDefault(resource => IsDefinitionLineMatch(text, offset, resource));
        return definition is not null;
    }

    private static bool IsDefinitionLineMatch(string text, int offset, ThemeResourceDefinition definition)
    {
        if (definition.Line is not > 0)
        {
            return false;
        }

        var lineNumber = PlaygroundInlineFeatureHelpers.GetLineNumber(text, offset);
        if (lineNumber != definition.Line.Value)
        {
            return false;
        }

        var lineStart = GetLineStartOffset(text, lineNumber);
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd];
        var keyIndex = line.IndexOf(definition.Key, StringComparison.Ordinal);
        return keyIndex < 0 ||
               (offset >= lineStart + keyIndex &&
                offset <= lineStart + keyIndex + definition.Key.Length);
    }

    private static int GetLineStartOffset(string text, int lineNumber)
    {
        if (lineNumber <= 1)
        {
            return 0;
        }

        var line = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            line++;
            if (line == lineNumber)
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    private readonly record struct ResourceContext(
        MinimapTextEditor Editor,
        PlaygroundInlineFeatureSnapshot Snapshot,
        string Key,
        int SourceLine,
        ThemeResourceDefinition Definition,
        PlaygroundInlineDocumentSnapshot DefinitionDocument,
        IReadOnlyList<ThemeResourceReference> References);
}
