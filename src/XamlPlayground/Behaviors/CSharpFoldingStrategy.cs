using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace XamlPlayground.Behaviors;

internal sealed class CSharpFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private static IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var sourceText = SourceText.From(document.Text);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetRoot();
        var foldings = new List<NewFolding>();
        var seen = new HashSet<(int StartOffset, int EndOffset)>();

        AddBraceFoldings(root, sourceText, foldings, seen);
        AddRegionFoldings(root, sourceText, foldings, seen);
        AddCommentFoldings(root, sourceText, foldings, seen);

        foldings.Sort(static (left, right) => left.StartOffset.CompareTo(right.StartOffset));
        return foldings;
    }

    private static void AddBraceFoldings(
        SyntaxNode root,
        SourceText sourceText,
        List<NewFolding> foldings,
        HashSet<(int StartOffset, int EndOffset)> seen)
    {
        foreach (var node in root.DescendantNodes(static node => !node.IsMissing))
        {
            if (!TryGetBraceRange(node, out var openBrace, out var closeBrace))
            {
                continue;
            }

            AddFolding(
                sourceText,
                foldings,
                seen,
                openBrace.Span.Start,
                closeBrace.Span.End,
                GetFoldName(sourceText, node.SpanStart));
        }
    }

    private static bool TryGetBraceRange(SyntaxNode node, out SyntaxToken openBrace, out SyntaxToken closeBrace)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax typeDeclaration:
                openBrace = typeDeclaration.OpenBraceToken;
                closeBrace = typeDeclaration.CloseBraceToken;
                break;
            case NamespaceDeclarationSyntax namespaceDeclaration:
                openBrace = namespaceDeclaration.OpenBraceToken;
                closeBrace = namespaceDeclaration.CloseBraceToken;
                break;
            case BlockSyntax block:
                openBrace = block.OpenBraceToken;
                closeBrace = block.CloseBraceToken;
                break;
            case AccessorListSyntax accessorList:
                openBrace = accessorList.OpenBraceToken;
                closeBrace = accessorList.CloseBraceToken;
                break;
            case InitializerExpressionSyntax initializer:
                openBrace = initializer.OpenBraceToken;
                closeBrace = initializer.CloseBraceToken;
                break;
            case SwitchStatementSyntax switchStatement:
                openBrace = switchStatement.OpenBraceToken;
                closeBrace = switchStatement.CloseBraceToken;
                break;
            default:
                openBrace = default;
                closeBrace = default;
                return false;
        }

        return !openBrace.IsMissing && !closeBrace.IsMissing;
    }

    private static void AddRegionFoldings(
        SyntaxNode root,
        SourceText sourceText,
        List<NewFolding> foldings,
        HashSet<(int StartOffset, int EndOffset)> seen)
    {
        var stack = new Stack<RegionDirectiveTriviaSyntax>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            var structure = trivia.GetStructure();
            switch (structure)
            {
                case RegionDirectiveTriviaSyntax region:
                    stack.Push(region);
                    break;
                case EndRegionDirectiveTriviaSyntax endRegion when stack.Count > 0:
                    var regionStart = stack.Pop();
                    AddFolding(
                        sourceText,
                        foldings,
                        seen,
                        regionStart.FullSpan.Start,
                        endRegion.FullSpan.End,
                        GetFoldName(sourceText, regionStart.FullSpan.Start));
                    break;
            }
        }
    }

    private static void AddCommentFoldings(
        SyntaxNode root,
        SourceText sourceText,
        List<NewFolding> foldings,
        HashSet<(int StartOffset, int EndOffset)> seen)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!IsFoldableComment(trivia))
            {
                continue;
            }

            AddFolding(
                sourceText,
                foldings,
                seen,
                trivia.FullSpan.Start,
                trivia.FullSpan.End,
                GetFoldName(sourceText, trivia.FullSpan.Start));
        }
    }

    private static bool IsFoldableComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
               trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
               trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia);
    }

    private static void AddFolding(
        SourceText sourceText,
        List<NewFolding> foldings,
        HashSet<(int StartOffset, int EndOffset)> seen,
        int startOffset,
        int endOffset,
        string name)
    {
        if (startOffset < 0 ||
            endOffset <= startOffset ||
            endOffset > sourceText.Length ||
            !seen.Add((startOffset, endOffset)))
        {
            return;
        }

        var startLine = sourceText.Lines.GetLineFromPosition(startOffset);
        var endLine = sourceText.Lines.GetLineFromPosition(endOffset);
        if (startLine.LineNumber >= endLine.LineNumber)
        {
            return;
        }

        foldings.Add(new NewFolding(startOffset, endOffset)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "..." : name
        });
    }

    private static string GetFoldName(SourceText sourceText, int offset)
    {
        if (sourceText.Length == 0)
        {
            return "...";
        }

        var line = sourceText.Lines.GetLineFromPosition(Math.Clamp(offset, 0, sourceText.Length - 1));
        var text = sourceText.ToString(TextSpan.FromBounds(line.Start, line.End)).Trim();
        return text.Length == 0
            ? "..."
            : text;
    }
}
