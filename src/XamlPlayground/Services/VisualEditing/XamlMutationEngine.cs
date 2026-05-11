using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Language.Xml;

namespace XamlPlayground.Services.VisualEditing;

public sealed class XamlMutationEngine : IXamlMutationEngine
{
    private const string XamlNameAttribute = "x:Name";
    private const string NameAttribute = "Name";
    private const string ChildIndent = "  ";
    private const string CanonicalNewLine = "\n";

    public XamlDocumentSnapshot Analyze(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        var diagnostics = new List<string>();
        XmlDocumentSyntax? document = null;

        try
        {
            document = Parser.ParseText(xaml);
        }
        catch (Exception exception)
        {
            diagnostics.Add(exception.Message);
        }

        var root = document?.RootSyntax;
        if (root is null)
        {
            diagnostics.Add("The XAML document does not contain a root element.");
            return new XamlDocumentSnapshot(xaml, null, Array.Empty<XamlElementSnapshot>(), diagnostics);
        }

        var elements = new List<XamlElementSnapshot>();
        AddElementSnapshot(root, Array.Empty<int>(), elements);

        return new XamlDocumentSnapshot(xaml, elements[0], elements, diagnostics);
    }

    public XamlMutationResult SetProperty(
        string xaml,
        XamlElementSelector selector,
        string propertyName,
        string value)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(value);

        return Mutate(xaml, selector, element =>
        {
            var attribute = FindAttribute(element, propertyName);
            if (attribute is not null)
            {
                var valueNode = attribute.ValueNode;
                var start = valueNode.StartQuoteToken.End;
                var end = valueNode.EndQuoteToken.Start;
                return ReplaceRange(xaml, start, end - start, EscapeAttributeValue(value));
            }

            var insertAt = GetStartTagCloseStart(xaml, element);
            return xaml.Insert(insertAt, CreateAttributeInsertionText(xaml, element, propertyName, value));
        });
    }

    public XamlMutationResult RemoveProperty(
        string xaml,
        XamlElementSelector selector,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return Mutate(xaml, selector, element =>
        {
            var attribute = FindAttribute(element, propertyName);
            if (attribute is null)
            {
                return xaml;
            }

            var (start, length) = GetAttributeRemovalRange(xaml, attribute);
            return ReplaceRange(xaml, start, length, string.Empty);
        });
    }

    public XamlMutationResult InsertChild(
        string xaml,
        XamlElementSelector parentSelector,
        string childXaml,
        int? childIndex = null)
    {
        ArgumentNullException.ThrowIfNull(childXaml);

        var newLine = GetPreferredNewLine(xaml);
        var child = NormalizeChildXaml(childXaml);
        if (child.Length == 0)
        {
            return CreateResult(xaml, "Cannot insert an empty child element.");
        }

        var childDocument = Parser.ParseText(child);
        if (childDocument.RootSyntax is null)
        {
            return CreateResult(xaml, "The child XAML snippet does not contain a root element.");
        }

        return Mutate(xaml, parentSelector, parent =>
        {
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

            var childElements = GetDirectChildElements(parent).ToArray();
            if (childIndex is { } index)
            {
                if (index >= 0 && index < childElements.Length)
                {
                    var insertBefore = GetElementLineInsertionStart(xaml, childElements[index]);
                    return xaml.Insert(insertBefore, $"{indentedChild}{newLine}");
                }
            }

            if (childElements.Length == 0 && IsWhitespaceOnly(xaml, element.StartTag.GreaterThanToken.End, element.EndTag.Span.Start))
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
        });
    }

    public XamlMutationResult RemoveElement(
        string xaml,
        XamlElementSelector selector)
    {
        return Mutate(xaml, selector, element =>
        {
            if (element.Parent is null)
            {
                throw new XamlMutationException("The root element cannot be removed.");
            }

            var (start, length) = ExpandRemovalRangeToWholeLine(xaml, element.AsNode.Span.Start, element.AsNode.Span.Length);
            return ReplaceRange(xaml, start, length, string.Empty);
        });
    }

    public XamlMutationResult RenameElement(
        string xaml,
        XamlElementSelector selector,
        string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return CreateResult(xaml, "Element type name cannot be empty.");
        }

        return Mutate(xaml, selector, element => RenameElementCore(xaml, element, typeName.Trim()));
    }

    public XamlMutationResult ReplaceElement(
        string xaml,
        XamlElementSelector selector,
        string replacementXaml)
    {
        ArgumentNullException.ThrowIfNull(replacementXaml);

        var replacement = NormalizeBlock(replacementXaml);
        if (replacement.Length == 0)
        {
            return CreateResult(xaml, "Replacement XAML cannot be empty.");
        }

        var replacementDocument = Parser.ParseText(replacement);
        if (replacementDocument.RootSyntax is null)
        {
            return CreateResult(xaml, "Replacement XAML does not contain a root element.");
        }

        return Mutate(xaml, selector, element =>
        {
            var indent = GetLineIndent(xaml, element.AsNode.Span.Start);
            var (start, length, replacementIndent) = GetElementReplacementRange(xaml, element);
            return ReplaceRange(
                xaml,
                start,
                length,
                IndentBlock(replacement, replacementIndent.Length > 0 ? replacementIndent : indent, GetPreferredNewLine(xaml)));
        });
    }

    public XamlMutationResult DuplicateElement(
        string xaml,
        XamlElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(selector);

        try
        {
            var document = Parser.ParseText(xaml);
            if (document.RootSyntax is null)
            {
                return CreateResult(xaml, "The XAML document does not contain a root element.");
            }

            var target = FindElement(document.RootSyntax, selector);
            if (target?.Parent is not { } parent)
            {
                return CreateResult(xaml, "The root element cannot be duplicated.");
            }

            var siblings = GetDirectChildElements(parent).ToArray();
            var index = Array.FindIndex(siblings, sibling =>
                sibling.AsNode.Span.Start == target.AsNode.Span.Start &&
                sibling.AsNode.Span.Length == target.AsNode.Span.Length);
            if (index < 0)
            {
                return CreateResult(xaml, "The element to duplicate could not be located in its parent.");
            }

            var targetBlock = RemoveNameAttributes(NormalizeElementBlock(xaml, target));

            return InsertChild(
                xaml,
                XamlElementSelector.ByPath(GetElementPath(parent).ToArray()),
                targetBlock,
                index + 1);
        }
        catch (Exception exception)
        {
            return CreateResult(xaml, $"XAML duplicate failed: {exception.Message}");
        }
    }

    public XamlMutationResult MoveElement(
        string xaml,
        XamlElementSelector selector,
        XamlElementSelector targetParentSelector,
        int? childIndex = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(targetParentSelector);

        try
        {
            var document = Parser.ParseText(xaml);
            if (document.RootSyntax is null)
            {
                return CreateResult(xaml, "The XAML document does not contain a root element.");
            }

            var target = FindElement(document.RootSyntax, selector);
            if (target is null)
            {
                return CreateResult(xaml, "The element to move could not be found.");
            }

            if (target.Parent is null)
            {
                return CreateResult(xaml, "The root element cannot be moved.");
            }

            var targetParent = FindElement(document.RootSyntax, targetParentSelector);
            if (targetParent is null)
            {
                return CreateResult(xaml, "The target parent element could not be found.");
            }

            if (IsDescendantOrSelf(targetParent, target))
            {
                return CreateResult(xaml, "An element cannot be moved into itself or one of its descendants.");
            }

            var targetBlock = NormalizeElementBlock(xaml, target);
            var (removeStart, removeLength) = ExpandRemovalRangeToWholeLine(xaml, target.AsNode.Span.Start, target.AsNode.Span.Length);
            var removed = ReplaceRange(xaml, removeStart, removeLength, string.Empty);

            return InsertChild(removed, targetParentSelector, targetBlock, childIndex);
        }
        catch (Exception exception)
        {
            return CreateResult(xaml, $"XAML move failed: {exception.Message}");
        }
    }

    public XamlMutationResult ReorderElement(
        string xaml,
        XamlElementSelector selector,
        int childIndex)
    {
        if (childIndex < 0)
        {
            return CreateResult(xaml, "Child index cannot be negative.");
        }

        try
        {
            var document = Parser.ParseText(xaml);
            if (document.RootSyntax is null)
            {
                return CreateResult(xaml, "The XAML document does not contain a root element.");
            }

            var target = FindElement(document.RootSyntax, selector);
            if (target?.Parent is not { } parent)
            {
                return CreateResult(xaml, "The root element cannot be reordered.");
            }

            var parentPath = GetElementPath(parent);
            return MoveElement(xaml, selector, XamlElementSelector.ByPath(parentPath.ToArray()), childIndex);
        }
        catch (Exception exception)
        {
            return CreateResult(xaml, $"XAML reorder failed: {exception.Message}");
        }
    }

    public XamlMutationResult WrapElement(
        string xaml,
        XamlElementSelector selector,
        string wrapperXaml)
    {
        ArgumentNullException.ThrowIfNull(wrapperXaml);

        var wrapper = NormalizeBlock(wrapperXaml);
        if (wrapper.Length == 0)
        {
            return CreateResult(xaml, "Wrapper XAML cannot be empty.");
        }

        var wrapperDocument = Parser.ParseText(wrapper);
        if (wrapperDocument.RootSyntax is null)
        {
            return CreateResult(xaml, "Wrapper XAML does not contain a root element.");
        }

        return Mutate(xaml, selector, element =>
        {
            var targetBlock = NormalizeElementBlock(xaml, element);
            var wrapped = InsertChild(wrapper, XamlElementSelector.ByPath(), targetBlock);
            if (!wrapped.Success)
            {
                throw new XamlMutationException(string.Join(Environment.NewLine, wrapped.Diagnostics));
            }

            var indent = GetLineIndent(xaml, element.AsNode.Span.Start);
            var (start, length, replacementIndent) = GetElementReplacementRange(xaml, element);
            return ReplaceRange(
                xaml,
                start,
                length,
                IndentBlock(wrapped.Text, replacementIndent.Length > 0 ? replacementIndent : indent, GetPreferredNewLine(xaml)));
        });
    }

    public XamlMutationResult UnwrapElement(
        string xaml,
        XamlElementSelector selector)
    {
        return Mutate(xaml, selector, element =>
        {
            if (element.Parent is null)
            {
                throw new XamlMutationException("The root element cannot be unwrapped.");
            }

            if (element.AsNode is not XmlElementSyntax normalElement)
            {
                throw new XamlMutationException("Empty elements cannot be unwrapped.");
            }

            var start = normalElement.StartTag.GreaterThanToken.End;
            var end = normalElement.EndTag.Span.Start;
            var content = NormalizeBlock(xaml.Substring(start, end - start));
            var indent = GetLineIndent(xaml, element.AsNode.Span.Start);
            var (replaceStart, replaceLength, replacementIndent) = GetElementReplacementRange(xaml, element);

            return ReplaceRange(
                xaml,
                replaceStart,
                replaceLength,
                IndentBlock(content, replacementIndent.Length > 0 ? replacementIndent : indent, GetPreferredNewLine(xaml)));
        });
    }

    public XamlMutationResult SetMemberElement(
        string xaml,
        XamlElementSelector selector,
        string propertyName,
        string valueXaml)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(valueXaml);

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return CreateResult(xaml, "Member property name cannot be empty.");
        }

        var value = NormalizeBlock(valueXaml);
        if (value.Length == 0)
        {
            return CreateResult(xaml, "Member element value cannot be empty.");
        }

        return Mutate(xaml, selector, element =>
        {
            var newLine = GetPreferredNewLine(xaml);
            var memberName = GetMemberElementName(element, propertyName);
            var memberText = CreateMemberElementText(memberName, value, newLine);
            var existing = FindDirectChildElement(element, memberName);
            if (existing is not null)
            {
                var indent = GetLineIndent(xaml, existing.AsNode.Span.Start);
                var (start, length, replacementIndent) = GetElementReplacementRange(xaml, existing);
                return ReplaceRange(
                    xaml,
                    start,
                    length,
                    IndentBlock(memberText, replacementIndent.Length > 0 ? replacementIndent : indent, GetPreferredNewLine(xaml)));
            }

            return InsertChild(xaml, selector, memberText, 0).Text;
        });
    }

    public XamlMutationResult RemoveMemberElement(
        string xaml,
        XamlElementSelector selector,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return Mutate(xaml, selector, element =>
        {
            var existing = FindDirectChildElement(element, GetMemberElementName(element, propertyName));
            if (existing is null)
            {
                return xaml;
            }

            var (start, length) = ExpandRemovalRangeToWholeLine(xaml, existing.AsNode.Span.Start, existing.AsNode.Span.Length);
            return ReplaceRange(xaml, start, length, string.Empty);
        });
    }

    public XamlMutationResult Batch(
        string xaml,
        IEnumerable<XamlMutationRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var text = xaml;
        foreach (var request in requests)
        {
            XamlMutationResult result;
            try
            {
                result = ApplyRequest(text, request);
            }
            catch (XamlMutationException exception)
            {
                return CreateResult(text, exception.Message);
            }

            if (!result.Success)
            {
                return result;
            }

            text = result.Text;
        }

        return CreateResult(text);
    }

    private XamlMutationResult Mutate(
        string xaml,
        XamlElementSelector selector,
        Func<IXmlElementSyntax, string> mutation)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(mutation);

        try
        {
            var document = Parser.ParseText(xaml);
            if (document.RootSyntax is null)
            {
                return CreateResult(xaml, "The XAML document does not contain a root element.");
            }

            var target = FindElement(document.RootSyntax, selector);
            if (target is null)
            {
                return CreateResult(xaml, "The requested XAML element could not be found.");
            }

            var text = mutation(target);
            return CreateResult(text);
        }
        catch (XamlMutationException exception)
        {
            return CreateResult(xaml, exception.Message);
        }
        catch (Exception exception)
        {
            return CreateResult(xaml, $"XAML mutation failed: {exception.Message}");
        }
    }

    private XamlMutationResult ApplyRequest(string xaml, XamlMutationRequest request)
    {
        return request.Kind switch
        {
            XamlMutationKind.SetProperty => SetProperty(
                xaml,
                request.Selector,
                Require(request.PropertyName, request.Kind, nameof(request.PropertyName)),
                Require(request.Value, request.Kind, nameof(request.Value))),

            XamlMutationKind.RemoveProperty => RemoveProperty(
                xaml,
                request.Selector,
                Require(request.PropertyName, request.Kind, nameof(request.PropertyName))),

            XamlMutationKind.InsertChild => InsertChild(
                xaml,
                request.Selector,
                Require(request.Xaml, request.Kind, nameof(request.Xaml)),
                request.ChildIndex),

            XamlMutationKind.RemoveElement => RemoveElement(xaml, request.Selector),

            XamlMutationKind.RenameElement => RenameElement(
                xaml,
                request.Selector,
                Require(request.TypeName, request.Kind, nameof(request.TypeName))),

            XamlMutationKind.ReplaceElement => ReplaceElement(
                xaml,
                request.Selector,
                Require(request.Xaml, request.Kind, nameof(request.Xaml))),

            XamlMutationKind.DuplicateElement => DuplicateElement(xaml, request.Selector),

            XamlMutationKind.MoveElement => MoveElement(
                xaml,
                request.Selector,
                request.TargetParentSelector ?? throw new XamlMutationException($"{request.Kind} requires {nameof(request.TargetParentSelector)}."),
                request.ChildIndex),

            XamlMutationKind.ReorderElement => ReorderElement(
                xaml,
                request.Selector,
                request.ChildIndex ?? throw new XamlMutationException($"{request.Kind} requires {nameof(request.ChildIndex)}.")),

            XamlMutationKind.WrapElement => WrapElement(
                xaml,
                request.Selector,
                Require(request.Xaml, request.Kind, nameof(request.Xaml))),

            XamlMutationKind.UnwrapElement => UnwrapElement(xaml, request.Selector),

            XamlMutationKind.SetMemberElement => SetMemberElement(
                xaml,
                request.Selector,
                Require(request.PropertyName, request.Kind, nameof(request.PropertyName)),
                Require(request.Xaml, request.Kind, nameof(request.Xaml))),

            XamlMutationKind.RemoveMemberElement => RemoveMemberElement(
                xaml,
                request.Selector,
                Require(request.PropertyName, request.Kind, nameof(request.PropertyName))),

            _ => CreateResult(xaml, $"Unsupported mutation kind: {request.Kind}.")
        };
    }

    private XamlMutationResult CreateResult(string xaml, params string[] diagnostics)
    {
        var snapshot = Analyze(xaml);
        var combinedDiagnostics = diagnostics
            .Concat(snapshot.Diagnostics)
            .Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new XamlMutationResult(xaml, snapshot with { Diagnostics = combinedDiagnostics }, combinedDiagnostics);
    }

    private static void AddElementSnapshot(
        IXmlElementSyntax element,
        IReadOnlyList<int> path,
        ICollection<XamlElementSnapshot> snapshots)
    {
        var attributes = element.Attributes
            .GroupBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.Ordinal);
        var name = TryGetName(element);
        var selector = name is { Length: > 0 }
            ? XamlElementSelector.ByName(name)
            : XamlElementSelector.ByPath(path.ToArray());
        var children = GetDirectChildElements(element).ToArray();

        snapshots.Add(new XamlElementSnapshot(
            selector,
            element.NameNode.FullName,
            name,
            path.ToArray(),
            element.AsNode.Span.Start,
            element.AsNode.Span.Length,
            attributes,
            children.Length));

        for (var i = 0; i < children.Length; i++)
        {
            AddElementSnapshot(children[i], path.Concat(new[] { i }).ToArray(), snapshots);
        }
    }

    private static IXmlElementSyntax? FindElement(IXmlElementSyntax root, XamlElementSelector selector)
    {
        var elements = root.DescendantsAndSelf().ToArray();

        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            return elements.FirstOrDefault(element =>
                string.Equals(TryGetName(element), selector.Name, StringComparison.Ordinal));
        }

        if (selector.Path is { } path)
        {
            return elements.FirstOrDefault(element => PathEquals(GetElementPath(element), path));
        }

        if (!string.IsNullOrWhiteSpace(selector.TypeName))
        {
            return elements.FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, selector.TypeName, StringComparison.Ordinal) ||
                string.Equals(element.NameNode.FullName, selector.TypeName, StringComparison.Ordinal));
        }

        return null;
    }

    private static IReadOnlyList<int> GetElementPath(IXmlElementSyntax element)
    {
        var path = new Stack<int>();
        var current = element;
        while (current.Parent is { } parent)
        {
            var siblings = GetDirectChildElements(parent).ToArray();
            var currentNode = current.AsNode;
            var index = Array.FindIndex(siblings, sibling =>
                sibling.AsNode.Span.Start == currentNode.Span.Start &&
                sibling.AsNode.Span.Length == currentNode.Span.Length);
            if (index < 0)
            {
                break;
            }

            path.Push(index);
            current = parent;
        }

        return path.ToArray();
    }

    private static bool PathEquals(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<IXmlElementSyntax> GetDirectChildElements(IXmlElementSyntax element)
    {
        return element.Content
            .OfType<IXmlElementSyntax>();
    }

    private static IXmlElementSyntax? FindDirectChildElement(IXmlElementSyntax element, string elementName)
    {
        return GetDirectChildElements(element).FirstOrDefault(child =>
            string.Equals(child.NameNode.FullName, elementName, StringComparison.Ordinal));
    }

    private static XmlAttributeSyntax? FindAttribute(IXmlElementSyntax element, string propertyName)
    {
        return element.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name, propertyName, StringComparison.Ordinal));
    }

    private static string? TryGetName(IXmlElementSyntax element)
    {
        return FindAttribute(element, XamlNameAttribute)?.Value ??
               FindAttribute(element, NameAttribute)?.Value;
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

    private static string CreateAttributeInsertionText(
        string xaml,
        IXmlElementSyntax element,
        string propertyName,
        string value)
    {
        var escaped = EscapeAttributeValue(value);
        if (!UsesMultilineAttributes(xaml, element))
        {
            return $" {propertyName}=\"{escaped}\"";
        }

        var indent = GetAttributeIndent(xaml, element);
        return $"{GetPreferredNewLine(xaml)}{indent}{propertyName}=\"{escaped}\"";
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

        return GetLineIndent(xaml, element.AsNode.Span.Start) + ChildIndent;
    }

    private static (int Start, int Length) GetAttributeRemovalRange(
        string xaml,
        XmlAttributeSyntax attribute)
    {
        var lineStart = GetLineStart(xaml, attribute.Span.Start);
        var lineEnd = GetLineEndIncludingNewLine(xaml, attribute.Span.End);
        if (IsWhitespaceOnly(xaml, lineStart, attribute.Span.Start) &&
            IsWhitespaceOnly(xaml, attribute.Span.End, lineEnd))
        {
            return (lineStart, lineEnd - lineStart);
        }

        var start = attribute.Span.Start;
        while (start > 0 && xaml[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return (start, attribute.Span.End - start);
    }

    private static string EscapeAttributeValue(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string ReplaceRange(string text, int start, int length, string replacement)
    {
        return text.Remove(start, length).Insert(start, replacement);
    }

    private static string NormalizeChildXaml(string childXaml)
    {
        return NormalizeBlock(childXaml);
    }

    private static string NormalizeElementBlock(string xaml, IXmlElementSyntax element)
    {
        return NormalizeBlock(xaml.Substring(element.AsNode.Span.Start, element.AsNode.Span.Length));
    }

    private static string RemoveNameAttributes(string xaml)
    {
        var document = Parser.ParseText(xaml);
        var root = document.RootSyntax;
        if (root is null)
        {
            return xaml;
        }

        var text = xaml;
        var attributes = root.Attributes
            .Where(static attribute =>
                string.Equals(attribute.Name, XamlNameAttribute, StringComparison.Ordinal) ||
                string.Equals(attribute.Name, NameAttribute, StringComparison.Ordinal))
            .OrderByDescending(static attribute => attribute.Span.Start);

        foreach (var attribute in attributes)
        {
            var (start, length) = GetAttributeRemovalRange(text, attribute);
            text = ReplaceRange(text, start, length, string.Empty);
        }

        return text;
    }

    private static string NormalizeBlock(string text)
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

        return string.Join(CanonicalNewLine, lines);
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

    private static string IndentBlock(string text, string indent, string newLine)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(
            newLine,
            lines.Select(line => line.Length == 0 ? line : indent + line));
    }

    private static string GetPreferredNewLine(string text)
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

    private static int GetLineStart(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return lineStart < 0 ? 0 : lineStart + 1;
    }

    private static int GetLineEndIncludingNewLine(string text, int position)
    {
        var lineEnd = text.IndexOf('\n', Math.Min(position, text.Length));
        return lineEnd >= 0 ? lineEnd + 1 : text.Length;
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

    private static string GetChildIndent(
        string xaml,
        IXmlElementSyntax parent,
        string parentIndent)
    {
        foreach (var child in GetDirectChildElements(parent))
        {
            var childIndent = GetLineIndent(xaml, child.AsNode.Span.Start);
            if (childIndent.Length > 0)
            {
                return childIndent;
            }
        }

        return parentIndent + ChildIndent;
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

        return (elementStart, elementLength, GetLineIndent(xaml, elementStart));
    }

    private static (int Start, int Length) ExpandRemovalRangeToWholeLine(string text, int start, int length)
    {
        var end = start + length;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var lineEnd = text.IndexOf('\n', end);
        var includesNewLine = lineEnd >= 0;
        lineEnd = includesNewLine ? lineEnd + 1 : text.Length;

        var before = text[lineStart..start];
        var after = text[end..lineEnd];
        if (before.All(char.IsWhiteSpace) && after.All(char.IsWhiteSpace))
        {
            return (lineStart, lineEnd - lineStart);
        }

        return (start, length);
    }

    private static string RenameElementCore(string xaml, IXmlElementSyntax element, string typeName)
    {
        return element.AsNode switch
        {
            XmlElementSyntax normalElement => ApplyReplacements(
                xaml,
                new[]
                {
                    (normalElement.EndTag.NameNode.Span.Start, normalElement.EndTag.NameNode.Span.Length, typeName),
                    (normalElement.StartTag.NameNode.Span.Start, normalElement.StartTag.NameNode.Span.Length, typeName)
                }),
            XmlEmptyElementSyntax emptyElement => ReplaceRange(
                xaml,
                emptyElement.NameNode.Span.Start,
                emptyElement.NameNode.Span.Length,
                typeName),
            _ => xaml
        };
    }

    private static string ApplyReplacements(
        string text,
        IEnumerable<(int Start, int Length, string Replacement)> replacements)
    {
        var result = text;
        foreach (var replacement in replacements.OrderByDescending(static replacement => replacement.Start))
        {
            result = ReplaceRange(result, replacement.Start, replacement.Length, replacement.Replacement);
        }

        return result;
    }

    private static bool IsDescendantOrSelf(IXmlElementSyntax candidate, IXmlElementSyntax ancestor)
    {
        for (var current = candidate; current is not null; current = current.Parent)
        {
            if (current.AsNode.Span.Start == ancestor.AsNode.Span.Start &&
                current.AsNode.Span.Length == ancestor.AsNode.Span.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetMemberElementName(IXmlElementSyntax element, string propertyName)
    {
        var trimmed = propertyName.Trim();
        return trimmed.Contains('.', StringComparison.Ordinal)
            ? trimmed
            : $"{element.NameNode.FullName}.{trimmed}";
    }

    private static string CreateMemberElementText(string memberName, string valueXaml, string newLine)
    {
        return $"<{memberName}>{newLine}{IndentBlock(valueXaml, ChildIndent, newLine)}{newLine}</{memberName}>";
    }

    private static string Require(string? value, XamlMutationKind kind, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new XamlMutationException($"{kind} requires {propertyName}.");
        }

        return value;
    }
}
