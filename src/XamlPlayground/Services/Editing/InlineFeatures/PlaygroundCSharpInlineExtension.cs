using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlPlayground.Editor.Minimap.Inline;

namespace XamlPlayground.Services.Editing.InlineFeatures;

internal sealed class PlaygroundCSharpInlineExtension : PlaygroundInlineExtensionBase
{
    private readonly Func<PlaygroundInlineFeatureSnapshot> _getSnapshot;

    public PlaygroundCSharpInlineExtension(Func<PlaygroundInlineFeatureSnapshot> getSnapshot)
    {
        _getSnapshot = getSnapshot;
    }

    protected override void Build()
    {
        var snapshot = _getSnapshot();
        var current = snapshot.CurrentDocument;
        if (current is not { IsCSharp: true } || string.IsNullOrWhiteSpace(current.Text))
        {
            return;
        }

        var documents = snapshot.Documents.Where(static document => document.IsCSharp).ToArray();
        if (documents.Length == 0)
        {
            return;
        }

        var referenceIndex = BuildReferenceIndex(documents);
        var tree = CSharpSyntaxTree.ParseText(current.Text);
        var root = tree.GetRoot();

        foreach (var member in GetInterestingMembers(root).Take(80))
        {
            var identifier = GetIdentifier(member);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            var line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var references = referenceIndex.TryGetValue(identifier, out var found)
                ? found
                : Array.Empty<CSharpReference>();
            var usageCount = Math.Max(0, references.Length - 1);

            AddAnnotation(new EditorCodeAnnotation
            {
                LineNumber = line,
                Text = $"author: {GetAuthor()}",
                ToolTip = "Local author metadata",
                Priority = 0
            });

            AddAnnotation(new EditorCodeAnnotation
            {
                LineNumber = line,
                Text = $"usages: {usageCount}",
                ToolTip = $"Peek references for {identifier}",
                Priority = 1,
                Command = CreatePeekReferencesCommand(identifier, line, references)
            });
        }
    }

    private static Dictionary<string, CSharpReference[]> BuildReferenceIndex(
        IReadOnlyList<PlaygroundInlineDocumentSnapshot> documents)
    {
        var references = new Dictionary<string, List<CSharpReference>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var root = CSharpSyntaxTree.ParseText(document.Text).GetRoot();
            var lines = PlaygroundInlineFeatureHelpers.NormalizeLines(document.Text);
            foreach (var token in root.DescendantTokens().Where(static token => token.IsKind(SyntaxKind.IdentifierToken)))
            {
                if (string.IsNullOrWhiteSpace(token.ValueText))
                {
                    continue;
                }

                var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                if (!references.TryGetValue(token.ValueText, out var list))
                {
                    list = new List<CSharpReference>();
                    references[token.ValueText] = list;
                }

                list.Add(new CSharpReference(
                    document.Path,
                    line,
                    line > 0 && line <= lines.Length ? lines[line - 1].Trim() : token.ValueText));
            }
        }

        return references.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private ICommand CreatePeekReferencesCommand(
        string identifier,
        int sourceLine,
        IReadOnlyList<CSharpReference> references)
    {
        return new RelayCommand(() =>
        {
            var text = PlaygroundInlineFeatureHelpers.GetReferencesText(
                references.Select(reference => (reference.Path, reference.Line, reference.Snippet)));
            Context.ShowPeek(sourceLine, $"References: {identifier}", $"{references.Count} token occurrence(s)", text, "csharp", 220);
        });
    }

    private static IEnumerable<MemberDeclarationSyntax> GetInterestingMembers(SyntaxNode root)
    {
        return root
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(static member =>
                member is TypeDeclarationSyntax or
                    MethodDeclarationSyntax or
                    PropertyDeclarationSyntax or
                    ConstructorDeclarationSyntax)
            .OrderBy(static member => member.SpanStart);
    }

    private static string GetIdentifier(MemberDeclarationSyntax member)
    {
        return member switch
        {
            TypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => string.Empty
        };
    }

    private static string GetAuthor()
    {
        var userName = Environment.UserName;
        return userName.Contains("wieslaw", StringComparison.OrdinalIgnoreCase)
            ? "wieslaw"
            : string.IsNullOrWhiteSpace(userName)
                ? "local"
                : userName;
    }

    private sealed record CSharpReference(string Path, int Line, string Snippet);
}
