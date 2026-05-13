using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Language.Xml;
using XamlPlayground.Services.Editing;
using XamlPlayground.Services.Theming;

namespace XamlPlayground.Services.DesignInspection;

public sealed record XamlDesignEditResult(
    bool Changed,
    string Text,
    string? Error = null);

public sealed class XamlDesignEditor
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public XamlDesignEditResult SetStyleSelector(
        string xaml,
        XamlStyleDefinition style,
        string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new XamlDesignEditResult(false, xaml, "Style selector is required.");
        }

        if (!TryFindStyle(xaml, style, out var element, out var error))
        {
            return new XamlDesignEditResult(false, xaml, error);
        }

        return new XamlDesignEditResult(
            true,
            XamlTextEditor.SetAttributeValue(xaml, element, "Selector", selector.Trim()));
    }

    public XamlDesignEditResult SetStyleSetter(
        string xaml,
        XamlStyleDefinition style,
        string selector,
        string propertyName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return new XamlDesignEditResult(false, xaml, "Setter property is required.");
        }

        if (!TryFindStyle(xaml, style, out var element, out var error))
        {
            return new XamlDesignEditResult(false, xaml, error);
        }

        var edited = xaml;
        if (!string.Equals(XamlTextEditor.GetAttributeValue(element, "Selector"), selector, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(selector))
        {
            edited = XamlTextEditor.SetAttributeValue(edited, element, "Selector", selector.Trim());
            if (!TryFindStyle(edited, style with { Selector = selector.Trim() }, out element, out error))
            {
                return new XamlDesignEditResult(false, xaml, error);
            }
        }

        var property = propertyName.Trim();
        var setter = XamlTextEditor.DirectChildElements(element)
            .FirstOrDefault(child =>
                string.Equals(child.NameNode.LocalName, "Setter", StringComparison.Ordinal) &&
                string.Equals(XamlTextEditor.GetAttributeValue(child, "Property"), property, StringComparison.Ordinal));
        var setterText = CreateSetterText(property, value ?? string.Empty);

        if (setter is null)
        {
            return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(edited, element, setterText));
        }

        return new XamlDesignEditResult(true, XamlTextEditor.ReplaceElement(edited, setter, setterText));
    }

    public XamlDesignEditResult AddStyleToDocument(
        string xaml,
        string selector,
        IEnumerable<(string PropertyName, string Value)> setters)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new XamlDesignEditResult(false, xaml, "Style selector is required.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var error) ||
            document.RootSyntax is null)
        {
            return new XamlDesignEditResult(false, xaml, error ?? "The XAML document does not contain a root element.");
        }

        var setterText = string.Join(
            XamlTextEditor.GetPreferredNewLine(xaml),
            setters
                .Where(static setter => !string.IsNullOrWhiteSpace(setter.PropertyName))
                .Select(static setter => CreateSetterText(setter.PropertyName.Trim(), setter.Value ?? string.Empty)));
        if (string.IsNullOrWhiteSpace(setterText))
        {
            setterText = CreateSetterText("Opacity", "1");
        }

        var styleText = CreateStyleText(xaml, selector.Trim(), setterText);
        if (string.Equals(document.RootSyntax.NameNode.LocalName, "ResourceDictionary", StringComparison.Ordinal) ||
            string.Equals(document.RootSyntax.NameNode.LocalName, "Styles", StringComparison.Ordinal))
        {
            return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, document.RootSyntax, styleText));
        }

        var stylesMember = FindRootMember(document.RootSyntax, "Styles");
        if (stylesMember is not null)
        {
            return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, stylesMember, styleText));
        }

        var memberName = $"{document.RootSyntax.NameNode.FullName}.Styles";
        var memberText =
            $"<{memberName}>{XamlTextEditor.GetPreferredNewLine(xaml)}" +
            XamlTextEditor.IndentBlock(styleText, XamlTextEditor.DefaultIndentUnit, XamlTextEditor.GetPreferredNewLine(xaml)) +
            $"{XamlTextEditor.GetPreferredNewLine(xaml)}</{memberName}>";
        return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, document.RootSyntax, memberText, insertFirst: true));
    }

    public XamlDesignEditResult ReplaceBinding(
        string xaml,
        XamlBindingDefinition binding,
        string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new XamlDesignEditResult(false, xaml, "Binding value is required.");
        }

        var replacement = binding.LocationKind == XamlBindingLocationKind.Attribute
            ? XamlTextEditor.EscapeAttributeValue(rawValue.Trim())
            : XamlTextEditor.NormalizeBlock(rawValue);
        return ReplaceTrackedRange(xaml, binding.FilePath, binding.EditStart, binding.EditLength, replacement, binding.EditText);
    }

    public XamlDesignEditResult ReplaceResource(
        string xaml,
        XamlResourceDefinition resource,
        string rawXaml)
    {
        if (string.IsNullOrWhiteSpace(rawXaml))
        {
            return new XamlDesignEditResult(false, xaml, "Resource XAML is required.");
        }

        var resourceText = XamlTextEditor.NormalizeBlock(rawXaml);
        var preparedXaml = xaml;
        var start = resource.Start;
        if (!TryIsDocumentRootResource(xaml, resource, out var isRootResource, out var error))
        {
            return new XamlDesignEditResult(false, xaml, error);
        }

        if (isRootResource)
        {
            if (!TryEnsureRootXamlNamespacePrefix(resourceText, out resourceText, out error))
            {
                return new XamlDesignEditResult(false, xaml, error);
            }
        }
        else if (!TryEnsureXamlNamespacePrefix(xaml, resourceText, out preparedXaml, out var addedLength, out error))
        {
            return new XamlDesignEditResult(false, xaml, error);
        }
        else if (addedLength > 0)
        {
            start += addedLength;
        }

        return ReplaceTrackedRange(
            preparedXaml,
            resource.FilePath,
            start,
            resource.Length,
            resourceText,
            resource.RawXaml);
    }

    public XamlDesignEditResult AddResourceToDocument(
        string xaml,
        string rawXaml)
    {
        if (string.IsNullOrWhiteSpace(rawXaml))
        {
            return new XamlDesignEditResult(false, xaml, "Resource XAML is required.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var error) ||
            document.RootSyntax is null)
        {
            return new XamlDesignEditResult(false, xaml, error ?? "The XAML document does not contain a root element.");
        }

        var resourceText = XamlTextEditor.NormalizeBlock(rawXaml);
        if (!TryEnsureXamlNamespacePrefix(xaml, resourceText, out xaml, out _, out error) ||
            !XamlTextEditor.TryParse(xaml, out document, out error) ||
            document.RootSyntax is null)
        {
            return new XamlDesignEditResult(false, xaml, error ?? "The XAML document does not contain a root element.");
        }

        if (string.Equals(document.RootSyntax.NameNode.LocalName, "ResourceDictionary", StringComparison.Ordinal))
        {
            return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, document.RootSyntax, resourceText));
        }

        var resourcesMember = FindRootMember(document.RootSyntax, "Resources");
        if (resourcesMember is not null)
        {
            return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, resourcesMember, resourceText));
        }

        var memberName = $"{document.RootSyntax.NameNode.FullName}.Resources";
        var memberText =
            $"<{memberName}>{XamlTextEditor.GetPreferredNewLine(xaml)}" +
            XamlTextEditor.IndentBlock(resourceText, XamlTextEditor.DefaultIndentUnit, XamlTextEditor.GetPreferredNewLine(xaml)) +
            $"{XamlTextEditor.GetPreferredNewLine(xaml)}</{memberName}>";
        return new XamlDesignEditResult(true, XamlTextEditor.InsertChild(xaml, document.RootSyntax, memberText, insertFirst: true));
    }

    public string CreateStylePreviewXaml(
        string selector,
        IEnumerable<(string PropertyName, string Value)> setters)
    {
        var selectorText = string.IsNullOrWhiteSpace(selector) ? "Button.preview" : selector.Trim();
        var targetType = XamlDesignInspector.InferStyleTargetType(selectorText);
        if (string.IsNullOrWhiteSpace(targetType) || targetType == "^")
        {
            targetType = "Button";
        }

        var className = ExtractSelectorClass(selectorText) ?? "preview";
        var elementText = CreatePreviewElement(targetType, className, ExtractSelectorName(selectorText));
        var setterText = string.Join(
            Environment.NewLine,
            setters
                .Where(static setter => !string.IsNullOrWhiteSpace(setter.PropertyName))
                .Select(static setter => CreateSetterText(setter.PropertyName.Trim(), setter.Value ?? string.Empty)));
        if (string.IsNullOrWhiteSpace(setterText))
        {
            setterText = CreateSetterText("Padding", "12,6");
        }

        var previewSelector = CreatePreviewSelector(selectorText, targetType, className);

        return
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <UserControl.Styles>\n" +
            $"    <Style Selector=\"{XamlTextEditor.EscapeAttributeValue(previewSelector)}\">\n" +
            XamlTextEditor.IndentBlock(setterText, "      ", "\n") + "\n" +
            "    </Style>\n" +
            "  </UserControl.Styles>\n" +
            "  <Border Padding=\"18\">\n" +
            $"    {elementText}\n" +
            "  </Border>\n" +
            "</UserControl>";
    }

    public static string BuildBindingMarkup(
        string kind,
        string path,
        string mode,
        string source,
        string elementName,
        string relativeSource,
        string converter,
        string stringFormat,
        string fallbackValue,
        string targetNullValue)
    {
        kind = string.IsNullOrWhiteSpace(kind) ? "Binding" : kind.Trim();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            parts.Add(path.Trim());
        }

        Add("Mode", mode);
        Add("Source", source);
        Add("ElementName", elementName);
        Add("RelativeSource", relativeSource);
        Add("Converter", converter);
        Add("StringFormat", stringFormat);
        Add("FallbackValue", fallbackValue);
        Add("TargetNullValue", targetNullValue);

        return parts.Count == 0
            ? $"{{{kind}}}"
            : $"{{{kind} {string.Join(", ", parts)}}}";

        void Add(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={QuoteBindingMarkupValue(value.Trim())}");
            }
        }
    }

    public static string BuildBindingObjectElement(
        string kind,
        string path,
        string mode,
        string source,
        string elementName,
        string relativeSource,
        string converter,
        string stringFormat,
        string fallbackValue,
        string targetNullValue)
    {
        kind = string.IsNullOrWhiteSpace(kind) ? "Binding" : kind.Trim();
        var attributes = new List<(string Name, string Value)>();
        Add("Path", path);
        Add("Mode", mode);
        Add("Source", source);
        Add("ElementName", elementName);
        Add("RelativeSource", relativeSource);
        Add("Converter", converter);
        Add("StringFormat", stringFormat);
        Add("FallbackValue", fallbackValue);
        Add("TargetNullValue", targetNullValue);

        if (attributes.Count == 0)
        {
            return $"<{kind} />";
        }

        return $"<{kind} {string.Join(" ", attributes.Select(static attribute => $"{attribute.Name}=\"{XamlTextEditor.EscapeAttributeValue(attribute.Value)}\""))} />";

        void Add(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes.Add((name, value.Trim()));
            }
        }
    }

    private static bool TryIsDocumentRootResource(
        string xaml,
        XamlResourceDefinition resource,
        out bool isRootResource,
        out string? error)
    {
        isRootResource = false;
        if (!XamlTextEditor.TryParse(xaml, out var document, out error) ||
            document.RootSyntax is null)
        {
            error ??= "The XAML document does not contain a root element.";
            return false;
        }

        isRootResource =
            document.RootSyntax.AsNode.Span.Start == resource.Start &&
            document.RootSyntax.AsNode.Span.Length == resource.Length;
        return true;
    }

    private static string QuoteBindingMarkupValue(string value)
    {
        if (!RequiresBindingMarkupQuote(value) || IsQuoted(value))
        {
            return value;
        }

        if (!value.Contains('\'', StringComparison.Ordinal))
        {
            return $"'{value}'";
        }

        if (!value.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{value}\"";
        }

        return $"'{value.Replace("'", "&apos;", StringComparison.Ordinal)}'";
    }

    private static bool RequiresBindingMarkupQuote(string value)
    {
        return value.Contains(',', StringComparison.Ordinal);
    }

    private static bool IsQuoted(string value)
    {
        return value.Length >= 2 &&
               ((value[0] == '\'' && value[^1] == '\'') ||
                (value[0] == '"' && value[^1] == '"'));
    }

    private static bool TryFindStyle(
        string xaml,
        XamlStyleDefinition style,
        out IXmlElementSyntax element,
        out string? error)
    {
        element = null!;
        if (!XamlTextEditor.TryParse(xaml, out var document, out error) ||
            document.RootSyntax is null)
        {
            error ??= "The XAML document does not contain a root element.";
            return false;
        }

        var styles = XamlTextEditor.DescendantsAndSelf(document.RootSyntax)
            .Where(static candidate => string.Equals(candidate.NameNode.LocalName, "Style", StringComparison.Ordinal))
            .ToArray();

        var match = styles.FirstOrDefault(candidate =>
            XamlTextEditor.GetLineNumber(xaml, candidate.AsNode.Span.Start) == style.Line &&
            string.Equals(XamlTextEditor.GetAttributeValue(candidate, "Selector"), style.Selector, StringComparison.Ordinal)) ??
                    styles.ElementAtOrDefault(style.Index) ??
                    styles.FirstOrDefault(candidate =>
                        string.Equals(XamlTextEditor.GetAttributeValue(candidate, "Selector"), style.Selector, StringComparison.Ordinal));

        if (match is null)
        {
            error = $"Style '{style.Selector}' was not found.";
            return false;
        }

        element = match;
        return true;
    }

    private static IXmlElementSyntax? FindRootMember(IXmlElementSyntax root, string memberName)
    {
        return XamlTextEditor.DirectChildElements(root)
            .FirstOrDefault(child =>
                child.NameNode.FullName.EndsWith("." + memberName, StringComparison.Ordinal) ||
                string.Equals(child.NameNode.LocalName, memberName, StringComparison.Ordinal));
    }

    private static bool TryEnsureXamlNamespacePrefix(
        string xaml,
        string insertedText,
        out string preparedXaml,
        out int addedLength,
        out string? error)
    {
        preparedXaml = xaml;
        addedLength = 0;
        error = null;
        if (!insertedText.Contains("x:", StringComparison.Ordinal))
        {
            return true;
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out error) ||
            document.RootSyntax is null)
        {
            error ??= "The XAML document does not contain a root element.";
            return false;
        }

        if (document.RootSyntax.Attributes.Any(static attribute =>
                string.Equals(attribute.Name, "xmlns:x", StringComparison.Ordinal)))
        {
            return true;
        }

        preparedXaml = XamlTextEditor.SetAttributeValue(xaml, document.RootSyntax, "xmlns:x", XamlNamespace);
        addedLength = preparedXaml.Length - xaml.Length;
        return true;
    }

    private static bool TryEnsureRootXamlNamespacePrefix(
        string insertedText,
        out string preparedText,
        out string? error)
    {
        preparedText = insertedText;
        error = null;
        if (!insertedText.Contains("x:", StringComparison.Ordinal))
        {
            return true;
        }

        var tagStart = insertedText.IndexOf('<');
        if (tagStart < 0)
        {
            error = "The resource XAML does not contain a root element.";
            return false;
        }

        var tagEnd = FindStartTagEnd(insertedText, tagStart);
        if (tagEnd < 0)
        {
            error = "The resource XAML root start tag is incomplete.";
            return false;
        }

        var startTag = insertedText.Substring(tagStart, tagEnd - tagStart + 1);
        if (startTag.Contains("xmlns:x", StringComparison.Ordinal))
        {
            return true;
        }

        var insertAt = tagEnd;
        if (insertAt > tagStart && insertedText[insertAt - 1] == '/')
        {
            insertAt--;
        }

        preparedText = insertedText.Insert(insertAt, $" xmlns:x=\"{XamlNamespace}\"");
        return true;
    }

    private static int FindStartTagEnd(string xaml, int start)
    {
        var quote = '\0';
        for (var i = start + 1; i < xaml.Length; i++)
        {
            var current = xaml[i];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static XamlDesignEditResult ReplaceTrackedRange(
        string xaml,
        string filePath,
        int start,
        int length,
        string replacement,
        string? expectedText = null)
    {
        if (start < 0 || length < 0 || start + length > xaml.Length)
        {
            return new XamlDesignEditResult(false, xaml, $"The tracked source range in {filePath} is no longer valid.");
        }

        if (expectedText is not null &&
            !string.Equals(xaml.Substring(start, length), expectedText, StringComparison.Ordinal))
        {
            return new XamlDesignEditResult(false, xaml, $"The tracked source range in {filePath} changed. Refresh the inspector and try again.");
        }

        return new XamlDesignEditResult(true, xaml.Remove(start, length).Insert(start, replacement));
    }

    private static string CreateStyleText(string xaml, string selector, string setterText)
    {
        var newLine = XamlTextEditor.GetPreferredNewLine(xaml);
        return $"<Style Selector=\"{XamlTextEditor.EscapeAttributeValue(selector)}\">{newLine}" +
               XamlTextEditor.IndentBlock(setterText, XamlTextEditor.DefaultIndentUnit, newLine) +
               $"{newLine}</Style>";
    }

    private static string CreateSetterText(string propertyName, string value)
    {
        if (value.TrimStart().StartsWith('<'))
        {
            return
                $"<Setter Property=\"{XamlTextEditor.EscapeAttributeValue(propertyName)}\">\n" +
                "  <Setter.Value>\n" +
                XamlTextEditor.IndentBlock(XamlTextEditor.NormalizeBlock(value), "    ", "\n") + "\n" +
                "  </Setter.Value>\n" +
                "</Setter>";
        }

        return $"<Setter Property=\"{XamlTextEditor.EscapeAttributeValue(propertyName)}\" Value=\"{XamlTextEditor.EscapeAttributeValue(value)}\" />";
    }

    private static string CreatePreviewElement(string targetType, string className, string? name)
    {
        var nameText = string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : $" Name=\"{XamlTextEditor.EscapeAttributeValue(name)}\"";
        var classText = string.IsNullOrWhiteSpace(className)
            ? string.Empty
            : $" Classes=\"{XamlTextEditor.EscapeAttributeValue(className)}\"";

        return targetType switch
        {
            "TextBlock" => $"<TextBlock{nameText}{classText} Text=\"Preview text\" />",
            "TextBox" => $"<TextBox{nameText}{classText} Text=\"Preview text\" Width=\"180\" />",
            "Border" => $"<Border{nameText}{classText} Width=\"160\" Height=\"64\" />",
            "Panel" or "StackPanel" => $"<StackPanel{nameText}{classText}><TextBlock Text=\"Preview content\" /></StackPanel>",
            _ => $"<{targetType}{nameText}{classText} Content=\"Preview\" />"
        };
    }

    private static string CreatePreviewSelector(string selector, string targetType, string className)
    {
        var firstSelector = selector.Split(" /template/ ", StringSplitOptions.None)[0].Trim();
        if (firstSelector.Contains(' ', StringComparison.Ordinal) ||
            firstSelector.Contains('>', StringComparison.Ordinal) ||
            firstSelector.Contains('+', StringComparison.Ordinal) ||
            firstSelector.Contains('~', StringComparison.Ordinal))
        {
            return $"{targetType}.{className}";
        }

        var withoutState = RemovePseudoClasses(firstSelector);
        return withoutState.Contains('.', StringComparison.Ordinal) ||
               withoutState.Contains('#', StringComparison.Ordinal)
            ? withoutState
            : $"{targetType}.{className}";
    }

    private static string RemovePseudoClasses(string selector)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < selector.Length; i++)
        {
            if (selector[i] != ':')
            {
                result.Append(selector[i]);
                continue;
            }

            i++;
            while (i < selector.Length && IsSelectorIdentifierCharacter(selector[i]))
            {
                i++;
            }

            i--;
        }

        return result.ToString();
    }

    private static string? ExtractSelectorClass(string selector)
    {
        var dot = selector.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || dot == selector.Length - 1)
        {
            return null;
        }

        var end = dot + 1;
        while (end < selector.Length && IsSelectorIdentifierCharacter(selector[end]))
        {
            end++;
        }

        return selector[(dot + 1)..end];
    }

    private static string? ExtractSelectorName(string selector)
    {
        var hash = selector.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || hash == selector.Length - 1)
        {
            return null;
        }

        var end = hash + 1;
        while (end < selector.Length && IsSelectorIdentifierCharacter(selector[end]))
        {
            end++;
        }

        return selector[(hash + 1)..end];
    }

    private static bool IsSelectorIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }
}
