using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using XamlPlayground.Editor.Minimap.Inline;

namespace XamlPlayground.Services.Editing.InlineFeatures;

internal static class PlaygroundInlineFeatureHelpers
{
    public static int GetLineNumber(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    public static string GetLineWindow(string text, int lineNumber, int contextLines)
    {
        var lines = NormalizeLines(text);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        lineNumber = Math.Clamp(lineNumber, 1, lines.Length);
        var start = Math.Max(1, lineNumber - contextLines);
        var end = Math.Min(lines.Length, lineNumber + contextLines);

        return string.Join(
            Environment.NewLine,
            Enumerable.Range(start, end - start + 1)
                .Select(line => $"{line,4}: {lines[line - 1]}"));
    }

    public static string GetReferencesText(IEnumerable<(string Path, int Line, string Snippet)> references)
    {
        var lines = references
            .OrderBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Line)
            .Select(reference => $"{reference.Path}:{reference.Line}: {reference.Snippet.Trim()}")
            .ToArray();

        return lines.Length == 0 ? "No references found." : string.Join(Environment.NewLine, lines);
    }

    public static string[] NormalizeLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    public static Control CreateInlineTextButton(Control owner, string text, Action action)
    {
        var foreground = EditorInlineTheme.Brush(
            owner,
            "DockTabSelectedForegroundBrush",
            new SolidColorBrush(Color.FromRgb(31, 95, 191)));

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.Normal,
            Foreground = foreground,
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var link = new Border
        {
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            MinHeight = 0,
            Height = 18,
            Background = Brushes.Transparent,
            Child = textBlock,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        link.PointerPressed += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        return link;
    }
}
