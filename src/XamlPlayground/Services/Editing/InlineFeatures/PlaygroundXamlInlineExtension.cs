using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using XamlPlayground.Editor.Minimap.Inline;
using XamlPlayground.Services.Theming;

namespace XamlPlayground.Services.Editing.InlineFeatures;

internal sealed class PlaygroundXamlInlineExtension : PlaygroundInlineExtensionBase
{
    private const int MaxInlineReferenceControls = 32;
    private readonly Func<PlaygroundInlineFeatureSnapshot> _getSnapshot;

    public PlaygroundXamlInlineExtension(Func<PlaygroundInlineFeatureSnapshot> getSnapshot)
    {
        _getSnapshot = getSnapshot;
    }

    protected override void Build()
    {
        var snapshot = _getSnapshot();
        var current = snapshot.CurrentDocument;
        if (current is not { IsXaml: true } || string.IsNullOrWhiteSpace(current.Text))
        {
            return;
        }

        var resourceDocuments = snapshot.Documents
            .Where(static document => document.IsXaml)
            .Select(static document => new ThemeResourceDocument(document.Path, document.Text, document.IsResource))
            .ToArray();
        if (resourceDocuments.Length == 0)
        {
            return;
        }

        var analysis = ResourceDictionaryAnalyzer.Analyze(resourceDocuments);
        AddDefinitionAnnotations(snapshot, current, analysis);
        AddReferencePeekControls(snapshot, current, analysis);
    }

    private void AddDefinitionAnnotations(
        PlaygroundInlineFeatureSnapshot snapshot,
        PlaygroundInlineDocumentSnapshot current,
        ThemeResourceAnalysis analysis)
    {
        var referencesByKey = analysis.References
            .GroupBy(static reference => reference.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        foreach (var resource in analysis.Resources
                     .Where(resource => string.Equals(resource.FilePath, current.Path, StringComparison.OrdinalIgnoreCase))
                     .Where(resource => resource.Line is > 0)
                     .Take(80))
        {
            var usageCount = referencesByKey.TryGetValue(resource.Key, out var references)
                ? references.Length
                : 0;

            AddAnnotation(new EditorCodeAnnotation
            {
                LineNumber = resource.Line!.Value,
                Text = $"{resource.ResourceType}: {resource.Key}",
                ToolTip = resource.TargetType is { Length: > 0 }
                    ? $"TargetType: {resource.TargetType}"
                    : "XAML resource definition",
                Priority = 0
            });

            AddAnnotation(new EditorCodeAnnotation
            {
                LineNumber = resource.Line.Value,
                Text = $"usages: {usageCount}",
                ToolTip = "Peek resource usages",
                Priority = 1,
                Command = CreatePeekUsagesCommand(resource.Key, references ?? Array.Empty<ThemeResourceReference>())
            });

            if (string.Equals(resource.ResourceType, "ControlTheme", StringComparison.Ordinal) &&
                GetDocument(snapshot, resource.FilePath) is { } resourceDocument)
            {
                var theme = ControlThemeAnalyzer.Analyze(resourceDocument.Text, resource.Key);
                if (theme != ControlThemeAnalysis.Empty)
                {
                    AddAnnotation(new EditorCodeAnnotation
                    {
                        LineNumber = resource.Line.Value,
                        Text = $"parts: {theme.Parts.Count}",
                        ToolTip = "Peek control theme structure",
                        Priority = 2,
                        Command = CreatePeekThemeCommand(resourceDocument, resource.Line.Value, theme)
                    });

                    AddAnnotation(new EditorCodeAnnotation
                    {
                        LineNumber = resource.Line.Value,
                        Text = $"states: {theme.AvailableStates.Count}",
                        ToolTip = string.Join(", ", theme.AvailableStates),
                        Priority = 3
                    });
                }
            }
        }

        foreach (var diagnostic in analysis.Diagnostics
                     .Where(diagnostic => string.Equals(diagnostic.FilePath, current.Path, StringComparison.OrdinalIgnoreCase))
                     .Where(diagnostic => diagnostic.Line is > 0)
                     .Where(static diagnostic => diagnostic.Severity == ThemeResourceDiagnosticSeverity.Error ||
                                                 !diagnostic.Message.StartsWith("Resource '", StringComparison.Ordinal))
                     .Take(40))
        {
            AddAnnotation(new EditorCodeAnnotation
            {
                LineNumber = diagnostic.Line!.Value,
                Text = diagnostic.Severity == ThemeResourceDiagnosticSeverity.Error ? "resource error" : "resource warning",
                ToolTip = diagnostic.Message,
                Priority = -1
            });
        }
    }

    private void AddReferencePeekControls(
        PlaygroundInlineFeatureSnapshot snapshot,
        PlaygroundInlineDocumentSnapshot current,
        ThemeResourceAnalysis analysis)
    {
        var definitions = analysis.Resources
            .GroupBy(static resource => resource.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var match in ResourceReferenceParser.Find(current.Text).Take(MaxInlineReferenceControls))
        {
            var line = PlaygroundInlineFeatureHelpers.GetLineNumber(current.Text, match.Start);
            PlaygroundInlineDocumentSnapshot? definitionDocument = null;
            var hasDefinition = definitions.TryGetValue(match.Key, out var definition);
            if (hasDefinition)
            {
                definitionDocument = GetDocument(snapshot, definition!.FilePath);
                hasDefinition = definitionDocument is not null;
            }

            AddInlineControl(new EditorInlineControl
            {
                Offset = GetReferenceAnchorOffset(current.Text, match),
                ControlFactory = () =>
                {
                    var control = PlaygroundInlineFeatureHelpers.CreateInlineTextButton(
                        Context.Editor,
                        "Peek",
                        () =>
                        {
                            if (hasDefinition)
                            {
                                ShowResourceDefinition(line, match.Key, definition!, definitionDocument!);
                            }
                            else
                            {
                                ShowUnresolvedResource(line, match.Key);
                            }
                        });
                    ToolTip.SetTip(control, hasDefinition
                        ? $"Peek {match.Key}"
                        : $"Resource '{match.Key}' was not found");
                    return control;
                }
            });
        }
    }

    private static int GetReferenceAnchorOffset(string text, ResourceReferenceMatch match)
    {
        var offset = match.Start + match.Length;
        if (offset < text.Length && text[offset] is '"' or '\'')
        {
            offset++;
        }

        return Math.Clamp(offset, 0, text.Length);
    }

    private ICommand CreatePeekUsagesCommand(string key, IReadOnlyList<ThemeResourceReference> references)
    {
        return new RelayCommand(() =>
        {
            var line = references.FirstOrDefault()?.Line ?? 1;
            var text = PlaygroundInlineFeatureHelpers.GetReferencesText(
                references.Select(reference => (reference.FilePath, reference.Line, reference.Snippet)));
            Context.ShowPeek(line, $"Usages of {key}", $"{references.Count} reference(s)", text, "text", 180);
        });
    }

    private ICommand CreatePeekThemeCommand(
        PlaygroundInlineDocumentSnapshot document,
        int line,
        ControlThemeAnalysis theme)
    {
        return new RelayCommand(() =>
        {
            var parts = theme.Parts.Count == 0
                ? "No named template parts."
                : string.Join(Environment.NewLine, theme.Parts.Select(part => $"part {part.Name}: {part.Type} at line {part.Line}"));
            var states = theme.AvailableStates.Count == 0
                ? "No states found."
                : "states: " + string.Join(", ", theme.AvailableStates);
            var bindings = theme.TemplateBindings.Count == 0
                ? "No template bindings found."
                : string.Join(Environment.NewLine, theme.TemplateBindings.Select(binding => $"binding {binding.Property}: line {binding.Line}"));

            Context.ShowPeek(
                line,
                $"ControlTheme {theme.Key}",
                document.Path,
                string.Join(Environment.NewLine + Environment.NewLine, parts, states, bindings),
                "text",
                220);
        });
    }

    private void ShowResourceDefinition(
        int sourceLine,
        string key,
        ThemeResourceDefinition definition,
        PlaygroundInlineDocumentSnapshot definitionDocument)
    {
        Context.ShowPeek(
            sourceLine,
            key,
            $"{definition.FilePath}:{definition.Line}",
            definitionDocument.Text,
            "xaml",
            300);
    }

    private void ShowUnresolvedResource(int sourceLine, string key)
    {
        Context.ShowPeek(
            sourceLine,
            key,
            "Resource definition not found",
            $"Resource '{key}' was not found in the current sample or workspace XAML resources.",
            "text",
            180);
    }

    private static PlaygroundInlineDocumentSnapshot? GetDocument(
        PlaygroundInlineFeatureSnapshot snapshot,
        string path)
    {
        return snapshot.Documents.FirstOrDefault(document =>
            string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}
