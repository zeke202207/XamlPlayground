using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Language.Xml;
using XamlPlayground.Services.VisualEditing;

namespace XamlPlayground.Services.Editing;

internal static class XamlTextEditor
{
    public const string DefaultIndentUnit = "  ";

    public static bool TryParse(string xaml, out XmlDocumentSyntax document, out string? error)
    {
        try
        {
            _ = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            document = Parser.ParseText(xaml);
            error = document.RootSyntax is null
                ? "The XAML document does not contain a root element."
                : null;
            if (error is not null)
            {
                return false;
            }

            if (document.ContainsDiagnostics)
            {
                error = GetFirstDiagnosticDescription(document) ?? "XAML contains syntax errors.";
                return false;
            }

            return true;
        }
        catch (XmlException exception)
        {
            document = Parser.ParseText(string.Empty);
            error = exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            document = Parser.ParseText(string.Empty);
            error = exception.Message;
            return false;
        }
    }

    public static IXmlElementSyntax? FindVisualElement(IXmlElementSyntax root, XamlElementSelector selector)
    {
        var elements = GetVisualDescendantsAndSelf(root).ToArray();

        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            return elements.FirstOrDefault(element =>
                string.Equals(GetAttributeValue(element, "x:Name") ??
                              GetAttributeValue(element, "Name") ??
                              GetAttributeValueByLocalName(element, "Name"),
                    selector.Name,
                    StringComparison.Ordinal));
        }

        if (selector.Path is { } path)
        {
            var current = root;
            foreach (var index in path)
            {
                var children = GetDirectVisualChildren(current).ToArray();
                if (index < 0 || index >= children.Length)
                {
                    return null;
                }

                current = children[index];
            }

            return current;
        }

        if (!string.IsNullOrWhiteSpace(selector.TypeName))
        {
            return elements.FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, selector.TypeName, StringComparison.Ordinal) ||
                string.Equals(element.NameNode.FullName, selector.TypeName, StringComparison.Ordinal));
        }

        return null;
    }

    public static IEnumerable<IXmlElementSyntax> DescendantsAndSelf(IXmlElementSyntax element)
    {
        yield return element;

        foreach (var child in DirectChildElements(element))
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    public static IEnumerable<IXmlElementSyntax> DirectChildElements(IXmlElementSyntax element)
    {
        return element.Content.OfType<IXmlElementSyntax>();
    }

    public static IXmlElementSyntax? FindDirectChildByFullName(IXmlElementSyntax element, string fullName)
    {
        return DirectChildElements(element)
            .FirstOrDefault(child => string.Equals(child.NameNode.FullName, fullName, StringComparison.Ordinal));
    }

    public static IXmlElementSyntax? FindDirectChildByLocalName(IXmlElementSyntax element, string localName)
    {
        return DirectChildElements(element)
            .FirstOrDefault(child => string.Equals(child.NameNode.LocalName, localName, StringComparison.Ordinal));
    }

    public static string? GetAttributeValue(IXmlElementSyntax element, string name)
    {
        return FindAttribute(element, name)?.Value;
    }

    public static string? GetAttributeValueByLocalName(IXmlElementSyntax element, string localName)
    {
        return element.Attributes
            .FirstOrDefault(attribute =>
                string.Equals(GetAttributeLocalName(attribute.Name), localName, StringComparison.Ordinal))
            ?.Value;
    }

    public static XmlAttributeSyntax? FindAttribute(IXmlElementSyntax element, string name)
    {
        return element.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name, name, StringComparison.Ordinal));
    }

    public static string SetAttributeValue(
        string xaml,
        IXmlElementSyntax element,
        string name,
        string value)
    {
        var attribute = FindAttribute(element, name);
        if (attribute is not null)
        {
            var quote = attribute.ValueNode.StartQuoteToken.Start < xaml.Length
                ? xaml[attribute.ValueNode.StartQuoteToken.Start]
                : '"';
            return ReplaceRange(
                xaml,
                attribute.ValueNode.StartQuoteToken.End,
                attribute.ValueNode.EndQuoteToken.Start - attribute.ValueNode.StartQuoteToken.End,
                EscapeAttributeValue(value, quote));
        }

        var insertAt = GetStartTagCloseStart(xaml, element);
        return xaml.Insert(insertAt, CreateAttributeInsertionText(xaml, element, name, value));
    }

    public static string InsertChild(
        string xaml,
        IXmlElementSyntax parent,
        string childXaml,
        bool insertFirst = false)
    {
        var newLine = GetPreferredNewLine(xaml);
        var child = NormalizeBlock(childXaml);
        if (child.Length == 0)
        {
            return xaml;
        }

        var parentIndent = GetLineIndent(xaml, parent.AsNode.Span.Start);
        var childIndent = GetChildIndent(xaml, parent, parentIndent);
        var indentedChild = IndentBlock(child, childIndent, newLine);

        if (parent.AsNode is XmlEmptyElementSyntax emptyElement)
        {
            var replacement = $">{newLine}{indentedChild}{newLine}{parentIndent}</{emptyElement.Name}>";
            var closeStart = GetEmptyElementCloseStart(xaml, emptyElement);
            return ReplaceRange(
                xaml,
                closeStart,
                emptyElement.SlashGreaterThanToken.End - closeStart,
                replacement);
        }

        if (parent.AsNode is not XmlElementSyntax element)
        {
            return xaml;
        }

        var children = DirectChildElements(parent).ToArray();
        if (insertFirst && children.Length > 0)
        {
            var insertBefore = GetElementLineInsertionStart(xaml, children[0]);
            return xaml.Insert(insertBefore, $"{indentedChild}{newLine}");
        }

        if (children.Length == 0 && IsWhitespaceOnly(xaml, element.StartTag.GreaterThanToken.End, element.EndTag.Span.Start))
        {
            return ReplaceRange(
                xaml,
                element.StartTag.GreaterThanToken.End,
                element.EndTag.Span.Start - element.StartTag.GreaterThanToken.End,
                $"{newLine}{indentedChild}{newLine}{parentIndent}");
        }

        var endTagLineStart = GetLineStart(xaml, element.EndTag.Span.Start);
        if (endTagLineStart > element.StartTag.GreaterThanToken.End &&
            IsWhitespaceOnly(xaml, endTagLineStart, element.EndTag.Span.Start))
        {
            return xaml.Insert(endTagLineStart, $"{indentedChild}{newLine}");
        }

        return xaml.Insert(element.EndTag.Span.Start, $"{newLine}{indentedChild}{newLine}{parentIndent}");
    }

    public static string ReplaceElement(
        string xaml,
        IXmlElementSyntax element,
        string replacementXaml)
    {
        var replacement = NormalizeBlock(replacementXaml);
        if (replacement.Length == 0)
        {
            return xaml;
        }

        var (start, length, indent) = GetElementReplacementRange(xaml, element);
        var newLine = GetPreferredNewLine(xaml);
        return ReplaceRange(xaml, start, length, IndentBlock(replacement, indent, newLine));
    }

    public static string RemoveElement(string xaml, IXmlElementSyntax element)
    {
        var (start, length) = ExpandRemovalRangeToWholeLine(xaml, element.AsNode.Span.Start, element.AsNode.Span.Length);
        return ReplaceRange(xaml, start, length, string.Empty);
    }

    public static string GetPreferredNewLine(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (text.Contains('\n'))
        {
            return "\n";
        }

        if (text.Contains('\r'))
        {
            return "\r";
        }

        return Environment.NewLine;
    }

    public static string GetIndentUnit(string xaml, IXmlElementSyntax element)
    {
        var parentIndent = GetLineIndent(xaml, element.AsNode.Span.Start);
        foreach (var child in DirectChildElements(element))
        {
            var childIndent = GetLineIndent(xaml, child.AsNode.Span.Start);
            if (childIndent.Length > parentIndent.Length &&
                childIndent.StartsWith(parentIndent, StringComparison.Ordinal))
            {
                return childIndent[parentIndent.Length..];
            }
        }

        return DetectDocumentIndentUnit(xaml) ?? DefaultIndentUnit;
    }

    public static string IndentBlock(string text, string indent, string newLine)
    {
        var lines = NormalizeLineEndings(text).Split('\n');
        return string.Join(
            newLine,
            lines.Select(line => line.Length == 0 ? line : indent + line));
    }

    public static string NormalizeBlock(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var commonIndent = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(CountLeadingWhitespace)
            .DefaultIfEmpty(0)
            .Min();

        if (commonIndent > 0)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var remove = Math.Min(commonIndent, CountLeadingWhitespace(lines[i]));
                lines[i] = lines[i][remove..];
            }
        }

        return string.Join("\n", lines);
    }

    public static string EscapeAttributeValue(string value, char quote = '"')
    {
        var escaped = value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        return quote == '\''
            ? escaped.Replace("'", "&apos;", StringComparison.Ordinal)
            : escaped.Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    public static int GetLineNumber(string text, int position)
    {
        var line = 1;
        var end = Math.Clamp(position, 0, text.Length);
        for (var i = 0; i < end; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                if (i + 1 < end && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static IEnumerable<IXmlElementSyntax> GetVisualDescendantsAndSelf(IXmlElementSyntax root)
    {
        yield return root;

        foreach (var child in GetDirectVisualChildren(root))
        {
            foreach (var descendant in GetVisualDescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<IXmlElementSyntax> GetDirectVisualChildren(IXmlElementSyntax element)
    {
        foreach (var child in DirectChildElements(element))
        {
            if (!IsMemberElement(child))
            {
                yield return child;
                continue;
            }

            if (!IsVisualContentMemberElement(child))
            {
                continue;
            }

            foreach (var visualChild in GetDirectVisualChildren(child))
            {
                yield return visualChild;
            }
        }
    }

    private static bool IsMemberElement(IXmlElementSyntax element)
    {
        return element.Parent is not null &&
               element.NameNode.FullName.Contains('.', StringComparison.Ordinal);
    }

    private static string GetAttributeLocalName(string name)
    {
        var separator = name.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 && separator < name.Length - 1
            ? name[(separator + 1)..]
            : name;
    }

    private static bool IsVisualContentMemberElement(IXmlElementSyntax element)
    {
        var propertyName = GetMemberPropertyName(element);
        if (propertyName.Length == 0 ||
            IsNonVisualMemberProperty(propertyName))
        {
            return false;
        }

        return propertyName is "Child" or "Children" or "Content" or "Footer" or "Header" or "Icon" or "Items" or
                   "Pane" ||
               propertyName.EndsWith("Child", StringComparison.Ordinal) ||
               propertyName.EndsWith("Children", StringComparison.Ordinal) ||
               propertyName.EndsWith("Content", StringComparison.Ordinal);
    }

    private static bool IsNonVisualMemberProperty(string propertyName)
    {
        return propertyName.EndsWith("Bindings", StringComparison.Ordinal) ||
               propertyName.EndsWith("Definitions", StringComparison.Ordinal) ||
               propertyName.EndsWith("Dictionaries", StringComparison.Ordinal) ||
               propertyName.EndsWith("Resources", StringComparison.Ordinal) ||
               propertyName.EndsWith("Styles", StringComparison.Ordinal) ||
               propertyName.EndsWith("Template", StringComparison.Ordinal) ||
               propertyName.EndsWith("Templates", StringComparison.Ordinal) ||
               propertyName.EndsWith("Transitions", StringComparison.Ordinal) ||
               propertyName is "DataContext" or "RenderTransform" or "Resources" or "Styles" or "Transitions";
    }

    private static string GetMemberPropertyName(IXmlElementSyntax element)
    {
        var fullName = element.NameNode.FullName;
        var separator = fullName.LastIndexOf('.');
        return separator < 0 || separator == fullName.Length - 1
            ? string.Empty
            : fullName[(separator + 1)..];
    }

    private static string CreateAttributeInsertionText(
        string xaml,
        IXmlElementSyntax element,
        string name,
        string value)
    {
        var escaped = EscapeAttributeValue(value);
        if (!UsesMultilineAttributes(xaml, element))
        {
            return $" {name}=\"{escaped}\"";
        }

        var indent = GetAttributeIndent(xaml, element);
        return $"{GetPreferredNewLine(xaml)}{indent}{name}=\"{escaped}\"";
    }

    private static bool UsesMultilineAttributes(string xaml, IXmlElementSyntax element)
    {
        var start = element.NameNode.Span.End;
        var end = GetStartTagCloseStart(xaml, element);
        if (end <= start)
        {
            return false;
        }

        return xaml.AsSpan(start, end - start).IndexOfAny('\r', '\n') >= 0;
    }

    private static string GetAttributeIndent(string xaml, IXmlElementSyntax element)
    {
        var elementLineStart = GetLineStart(xaml, element.AsNode.Span.Start);
        var multilineAttribute = element.Attributes.FirstOrDefault(attribute =>
            GetLineStart(xaml, attribute.Span.Start) != elementLineStart);
        if (multilineAttribute is not null)
        {
            return GetLineIndent(xaml, multilineAttribute.Span.Start);
        }

        return GetLineIndent(xaml, element.AsNode.Span.Start) + DefaultIndentUnit;
    }

    private static int GetStartTagCloseStart(string xaml, IXmlElementSyntax element)
    {
        return element.AsNode switch
        {
            XmlElementSyntax normalElement => normalElement.StartTag.GreaterThanToken.Start,
            XmlEmptyElementSyntax emptyElement => GetEmptyElementCloseStart(xaml, emptyElement),
            _ => element.AsNode.End
        };
    }

    private static int GetEmptyElementCloseStart(string xaml, XmlEmptyElementSyntax emptyElement)
    {
        var closeStart = emptyElement.SlashGreaterThanToken.Start;
        while (closeStart > emptyElement.Start && xaml[closeStart - 1] is ' ' or '\t')
        {
            closeStart--;
        }

        return closeStart;
    }

    private static string GetChildIndent(
        string xaml,
        IXmlElementSyntax parent,
        string parentIndent)
    {
        foreach (var child in DirectChildElements(parent))
        {
            var childIndent = GetLineIndent(xaml, child.AsNode.Span.Start);
            if (childIndent.Length > 0)
            {
                return childIndent;
            }
        }

        return parentIndent + GetIndentUnit(xaml, parent);
    }

    private static int GetElementLineInsertionStart(string xaml, IXmlElementSyntax element)
    {
        var elementStart = element.AsNode.Span.Start;
        var lineStart = GetLineStart(xaml, elementStart);
        return IsWhitespaceOnly(xaml, lineStart, elementStart)
            ? lineStart
            : elementStart;
    }

    private static (int Start, int Length, string Indent) GetElementReplacementRange(
        string xaml,
        IXmlElementSyntax element)
    {
        var elementStart = element.AsNode.Span.Start;
        var elementLength = element.AsNode.Span.Length;
        var lineStart = GetLineStart(xaml, elementStart);
        if (IsWhitespaceOnly(xaml, lineStart, elementStart))
        {
            return (
                lineStart,
                elementStart + elementLength - lineStart,
                xaml[lineStart..elementStart]);
        }

        return (elementStart, elementLength, string.Empty);
    }

    private static (int Start, int Length) ExpandRemovalRangeToWholeLine(string text, int start, int length)
    {
        var end = start + length;
        var lineStart = GetLineStart(text, start);
        var lineEnd = GetLineEndIncludingNewLine(text, end);

        var before = text[lineStart..start];
        var after = text[end..lineEnd];
        if (before.All(char.IsWhiteSpace) && after.All(char.IsWhiteSpace))
        {
            return (lineStart, lineEnd - lineStart);
        }

        return (start, length);
    }

    private static string? DetectDocumentIndentUnit(string text)
    {
        var previousIndent = string.Empty;
        foreach (var rawLine in NormalizeLineEndings(text).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (!rawLine.TrimStart().StartsWith('<'))
            {
                continue;
            }

            var indent = rawLine[..CountLeadingWhitespace(rawLine)];
            if (indent.Length > previousIndent.Length &&
                indent.StartsWith(previousIndent, StringComparison.Ordinal))
            {
                return indent[previousIndent.Length..];
            }

            previousIndent = indent;
        }

        return null;
    }

    private static string GetLineIndent(string text, int position)
    {
        var lineStart = GetLineStart(text, position);

        var builder = new StringBuilder();
        for (var i = lineStart; i < position && i < text.Length; i++)
        {
            if (text[i] is ' ' or '\t')
            {
                builder.Append(text[i]);
                continue;
            }

            break;
        }

        return builder.ToString();
    }

    private static int GetLineStart(string text, int position)
    {
        if (position <= 0 || text.Length == 0)
        {
            return 0;
        }

        var index = Math.Min(Math.Max(position - 1, 0), Math.Max(text.Length - 1, 0));
        for (; index >= 0 && index < text.Length; index--)
        {
            if (text[index] is '\r' or '\n')
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static int GetLineEndIncludingNewLine(string text, int position)
    {
        for (var i = Math.Clamp(position, 0, text.Length); i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                return i + 1 < text.Length && text[i + 1] == '\n'
                    ? i + 2
                    : i + 1;
            }

            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static bool IsWhitespaceOnly(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        for (var i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountLeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] is ' ' or '\t')
        {
            count++;
        }

        return count;
    }

    private static string ReplaceRange(string text, int start, int length, string replacement)
    {
        return text.Remove(start, length).Insert(start, replacement);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string? GetFirstDiagnosticDescription(XmlDocumentSyntax document)
    {
        return document
            .DescendantNodesAndTokensAndSelf(static node => node.ContainsDiagnostics, descendIntoTrivia: true)
            .SelectMany(static node => node.GetDiagnostics())
            .Select(static diagnostic => diagnostic.GetDescription())
            .FirstOrDefault(static description => !string.IsNullOrWhiteSpace(description));
    }
}
