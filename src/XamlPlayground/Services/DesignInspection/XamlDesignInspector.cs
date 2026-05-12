using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Language.Xml;
using XamlPlayground.Services.Editing;

namespace XamlPlayground.Services.DesignInspection;

public sealed record XamlDesignDocument(
    string Path,
    string Text,
    bool IsResourceDictionary);

public sealed record XamlDesignInspection(
    IReadOnlyList<XamlStyleDefinition> Styles,
    IReadOnlyList<XamlBindingDefinition> Bindings,
    IReadOnlyList<XamlResourceDefinition> Resources,
    IReadOnlyList<XamlDesignDiagnostic> Diagnostics)
{
    public static XamlDesignInspection Empty { get; } = new(
        Array.Empty<XamlStyleDefinition>(),
        Array.Empty<XamlBindingDefinition>(),
        Array.Empty<XamlResourceDefinition>(),
        Array.Empty<XamlDesignDiagnostic>());
}

public sealed record XamlStyleDefinition(
    int Index,
    string Selector,
    string? Key,
    string TargetType,
    string FilePath,
    int Line,
    int Start,
    int Length,
    IReadOnlyList<XamlStyleSetterDefinition> Setters);

public sealed record XamlStyleSetterDefinition(
    string Property,
    string Value,
    bool IsComplex,
    string FilePath,
    int Line,
    int Start,
    int Length);

public sealed record XamlBindingDefinition(
    int Index,
    XamlBindingLocationKind LocationKind,
    string Kind,
    string OwnerType,
    string PropertyName,
    string RawValue,
    string Path,
    string Mode,
    string Source,
    string ElementName,
    string RelativeSource,
    string Converter,
    string StringFormat,
    string FallbackValue,
    string TargetNullValue,
    string FilePath,
    int Line,
    int Start,
    int Length,
    int EditStart,
    int EditLength,
    string EditText);

public enum XamlBindingLocationKind
{
    Attribute,
    ObjectElement
}

public sealed record XamlResourceDefinition(
    int Index,
    string Key,
    string ResourceType,
    string TargetType,
    string ValuePreview,
    string RawXaml,
    string FilePath,
    int Line,
    int Start,
    int Length);

public sealed record XamlDesignDiagnostic(
    string Message,
    string FilePath,
    int? Line);

public sealed class XamlDesignInspector
{
    private static readonly string[] BindingElementNames =
    {
        "Binding",
        "CompiledBinding",
        "TemplateBinding",
        "MultiBinding"
    };

    public XamlDesignInspection Analyze(IEnumerable<XamlDesignDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var styles = new List<XamlStyleDefinition>();
        var bindings = new List<XamlBindingDefinition>();
        var resources = new List<XamlResourceDefinition>();
        var diagnostics = new List<XamlDesignDiagnostic>();

        foreach (var document in documents)
        {
            AnalyzeDocument(document, styles, bindings, resources, diagnostics);
        }

        return new XamlDesignInspection(
            styles
                .OrderBy(static style => style.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static style => style.Line)
                .ThenBy(static style => style.Selector, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            bindings
                .OrderBy(static binding => binding.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static binding => binding.Line)
                .ThenBy(static binding => binding.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            resources
                .OrderBy(static resource => resource.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static resource => resource.Line)
                .ThenBy(static resource => resource.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics
                .OrderBy(static diagnostic => diagnostic.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static diagnostic => diagnostic.Line ?? 0)
                .ToArray());
    }

    private static void AnalyzeDocument(
        XamlDesignDocument document,
        ICollection<XamlStyleDefinition> styles,
        ICollection<XamlBindingDefinition> bindings,
        ICollection<XamlResourceDefinition> resources,
        ICollection<XamlDesignDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(document.Text))
        {
            return;
        }

        if (!XamlTextEditor.TryParse(document.Text, out var parsed, out var error) ||
            parsed.RootSyntax is null)
        {
            diagnostics.Add(new XamlDesignDiagnostic(error ?? "XAML could not be parsed.", document.Path, null));
            return;
        }

        var elements = XamlTextEditor.DescendantsAndSelf(parsed.RootSyntax).ToArray();
        var styleIndex = 0;
        var bindingIndex = 0;
        var resourceIndex = 0;
        foreach (var element in elements)
        {
            if (IsStyleElement(element))
            {
                styles.Add(CreateStyle(document, element, styleIndex));
                styleIndex++;
            }

            if (TryGetResourceKey(element, out var resourceKey))
            {
                resources.Add(CreateResource(document, element, resourceIndex, resourceKey));
                resourceIndex++;
            }

            foreach (var binding in CreateBindings(document, element, bindingIndex))
            {
                bindings.Add(binding);
                bindingIndex++;
            }
        }
    }

    private static XamlStyleDefinition CreateStyle(
        XamlDesignDocument document,
        IXmlElementSyntax style,
        int index)
    {
        var selector = XamlTextEditor.GetAttributeValue(style, "Selector") ?? string.Empty;
        var key = GetXamlKey(style);
        var setters = XamlTextEditor.DirectChildElements(style)
            .Where(static child => string.Equals(child.NameNode.LocalName, "Setter", StringComparison.Ordinal))
            .Select(child => CreateSetter(document, child))
            .ToArray();

        return new XamlStyleDefinition(
            index,
            selector,
            key,
            InferStyleTargetType(selector),
            document.Path,
            GetLine(document.Text, style.AsNode.Span.Start),
            style.AsNode.Span.Start,
            style.AsNode.Span.Length,
            setters);
    }

    private static XamlStyleSetterDefinition CreateSetter(
        XamlDesignDocument document,
        IXmlElementSyntax setter)
    {
        var property = XamlTextEditor.GetAttributeValue(setter, "Property") ?? string.Empty;
        var valueAttribute = XamlTextEditor.FindAttribute(setter, "Value");
        var isComplex = valueAttribute is null;
        var value = valueAttribute?.Value ?? GetComplexSetterValue(document.Text, setter);

        return new XamlStyleSetterDefinition(
            property,
            value,
            isComplex,
            document.Path,
            GetLine(document.Text, setter.AsNode.Span.Start),
            setter.AsNode.Span.Start,
            setter.AsNode.Span.Length);
    }

    private static XamlResourceDefinition CreateResource(
        XamlDesignDocument document,
        IXmlElementSyntax resource,
        int index,
        string key)
    {
        var targetType =
            XamlTextEditor.GetAttributeValue(resource, "TargetType") ??
            XamlTextEditor.GetAttributeValue(resource, "DataType") ??
            string.Empty;
        var valuePreview =
            XamlTextEditor.GetAttributeValue(resource, "Value") ??
            XamlTextEditor.GetAttributeValue(resource, "Color") ??
            GetElementTextPreview(document.Text, resource);
        var rawXaml = document.Text.Substring(resource.AsNode.Span.Start, resource.AsNode.Span.Length);

        return new XamlResourceDefinition(
            index,
            key,
            resource.NameNode.FullName,
            targetType,
            valuePreview,
            rawXaml,
            document.Path,
            GetLine(document.Text, resource.AsNode.Span.Start),
            resource.AsNode.Span.Start,
            resource.AsNode.Span.Length);
    }

    private static IEnumerable<XamlBindingDefinition> CreateBindings(
        XamlDesignDocument document,
        IXmlElementSyntax element,
        int startIndex)
    {
        var index = startIndex;
        foreach (var attribute in element.Attributes)
        {
            if (!TryCreateAttributeBinding(document, element, attribute, index, out var binding))
            {
                continue;
            }

            index++;
            yield return binding;
        }

        if (IsBindingElement(element))
        {
            yield return CreateObjectElementBinding(document, element, index);
        }
    }

    private static bool TryCreateAttributeBinding(
        XamlDesignDocument document,
        IXmlElementSyntax element,
        XmlAttributeSyntax attribute,
        int index,
        out XamlBindingDefinition binding)
    {
        binding = null!;
        if (!ContainsBindingMarkup(attribute.Value))
        {
            return false;
        }

        var rawValue = attribute.Value;
        var parsed = XamlBindingMarkup.Parse(rawValue);
        var propertyName = attribute.Name;
        var ownerType = element.NameNode.FullName;
        var editStart = attribute.ValueNode.StartQuoteToken.End;
        var editLength = attribute.ValueNode.EndQuoteToken.Start - attribute.ValueNode.StartQuoteToken.End;
        var editText = document.Text.Substring(editStart, editLength);
        if (string.Equals(element.NameNode.LocalName, "Setter", StringComparison.Ordinal))
        {
            propertyName = XamlTextEditor.GetAttributeValue(element, "Property") ?? propertyName;
            ownerType = "Setter";
        }

        binding = new XamlBindingDefinition(
            index,
            XamlBindingLocationKind.Attribute,
            parsed.Kind,
            ownerType,
            propertyName,
            rawValue,
            parsed.Path,
            parsed.Mode,
            parsed.Source,
            parsed.ElementName,
            parsed.RelativeSource,
            parsed.Converter,
            parsed.StringFormat,
            parsed.FallbackValue,
            parsed.TargetNullValue,
            document.Path,
            GetLine(document.Text, attribute.Span.Start),
            attribute.Span.Start,
            attribute.Span.Length,
            editStart,
            editLength,
            editText);
        return true;
    }

    private static XamlBindingDefinition CreateObjectElementBinding(
        XamlDesignDocument document,
        IXmlElementSyntax element,
        int index)
    {
        var rawValue = document.Text.Substring(element.AsNode.Span.Start, element.AsNode.Span.Length);
        var parsed = XamlBindingMarkup.ParseObjectElement(element);
        var propertyName = ResolveObjectElementPropertyName(element);

        return new XamlBindingDefinition(
            index,
            XamlBindingLocationKind.ObjectElement,
            parsed.Kind,
            element.Parent?.NameNode.FullName ?? element.NameNode.FullName,
            propertyName,
            rawValue,
            parsed.Path,
            parsed.Mode,
            parsed.Source,
            parsed.ElementName,
            parsed.RelativeSource,
            parsed.Converter,
            parsed.StringFormat,
            parsed.FallbackValue,
            parsed.TargetNullValue,
            document.Path,
            GetLine(document.Text, element.AsNode.Span.Start),
            element.AsNode.Span.Start,
            element.AsNode.Span.Length,
            element.AsNode.Span.Start,
            element.AsNode.Span.Length,
            rawValue);
    }

    private static bool IsStyleElement(IXmlElementSyntax element)
    {
        return string.Equals(element.NameNode.LocalName, "Style", StringComparison.Ordinal) &&
               XamlTextEditor.GetAttributeValue(element, "Selector") is not null;
    }

    private static bool IsBindingElement(IXmlElementSyntax element)
    {
        return BindingElementNames.Contains(element.NameNode.LocalName, StringComparer.Ordinal);
    }

    private static bool ContainsBindingMarkup(string value)
    {
        if (value.TrimStart().StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Contains("{Binding", StringComparison.Ordinal) ||
               value.Contains("{CompiledBinding", StringComparison.Ordinal) ||
               value.Contains("{TemplateBinding", StringComparison.Ordinal) ||
               value.Contains("{MultiBinding", StringComparison.Ordinal);
    }

    private static string ResolveObjectElementPropertyName(IXmlElementSyntax element)
    {
        var parent = element.Parent;
        if (parent is null)
        {
            return string.Empty;
        }

        if (parent.NameNode.FullName.Contains('.', StringComparison.Ordinal))
        {
            var index = parent.NameNode.FullName.LastIndexOf('.');
            return index >= 0 && index < parent.NameNode.FullName.Length - 1
                ? parent.NameNode.FullName[(index + 1)..]
                : parent.NameNode.FullName;
        }

        return parent.NameNode.FullName;
    }

    private static bool TryGetResourceKey(IXmlElementSyntax element, out string key)
    {
        key = GetXamlKey(element) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (string.Equals(element.NameNode.LocalName, "ResourceDictionary", StringComparison.Ordinal) &&
            element.Parent is { NameNode.FullName: "ResourceDictionary.ThemeDictionaries" })
        {
            return false;
        }

        return true;
    }

    private static string? GetXamlKey(IXmlElementSyntax element)
    {
        return XamlTextEditor.GetAttributeValue(element, "x:Key") ??
               XamlTextEditor.GetAttributeValueByLocalName(element, "Key");
    }

    private static string GetComplexSetterValue(string xaml, IXmlElementSyntax setter)
    {
        var valueElement = XamlTextEditor.DirectChildElements(setter)
            .FirstOrDefault(static child => string.Equals(child.NameNode.FullName, "Setter.Value", StringComparison.Ordinal));
        if (valueElement is null)
        {
            return string.Empty;
        }

        var valueChildren = XamlTextEditor.DirectChildElements(valueElement).ToArray();
        if (valueChildren.Length == 0)
        {
            return GetElementTextPreview(xaml, valueElement);
        }

        return string.Join(
            Environment.NewLine,
            valueChildren.Select(child => xaml.Substring(child.AsNode.Span.Start, child.AsNode.Span.Length).Trim()));
    }

    private static string GetElementTextPreview(string xaml, IXmlElementSyntax element)
    {
        if (element.AsNode is not XmlElementSyntax normalElement)
        {
            return string.Empty;
        }

        var start = normalElement.StartTag.GreaterThanToken.End;
        var end = normalElement.EndTag.Span.Start;
        if (start < 0 || end <= start || end > xaml.Length)
        {
            return string.Empty;
        }

        var value = xaml[start..end].Trim();
        if (value.Length <= 240)
        {
            return value;
        }

        return value[..240] + "...";
    }

    private static int GetLine(string text, int start)
    {
        return XamlTextEditor.GetLineNumber(text, start);
    }

    public static string InferStyleTargetType(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return string.Empty;
        }

        var token = selector.Trim()
            .Split(new[] { ' ', '>', '+', '~', '/', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        if (token.Length == 0)
        {
            return string.Empty;
        }

        var idIndex = token.IndexOf('#', StringComparison.Ordinal);
        if (idIndex >= 0)
        {
            token = token[..idIndex];
        }

        var classIndex = token.IndexOf('.', StringComparison.Ordinal);
        if (classIndex >= 0)
        {
            token = token[..classIndex];
        }

        var pipeIndex = token.IndexOf('|', StringComparison.Ordinal);
        if (pipeIndex >= 0 && pipeIndex < token.Length - 1)
        {
            token = token[(pipeIndex + 1)..];
        }

        return token.Trim('^', '*');
    }

    private sealed record BindingMarkupFields(
        string Kind,
        string Path,
        string Mode,
        string Source,
        string ElementName,
        string RelativeSource,
        string Converter,
        string StringFormat,
        string FallbackValue,
        string TargetNullValue);

    private static class XamlBindingMarkup
    {
        public static BindingMarkupFields Parse(string value)
        {
            var kind = "Binding";
            var path = string.Empty;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var content = value.Trim();

            if (content.StartsWith('{') && content.EndsWith('}') && content.Length > 2)
            {
                content = content[1..^1].Trim();
            }

            var firstSpace = IndexOfWhitespace(content);
            if (firstSpace >= 0)
            {
                kind = content[..firstSpace].Trim();
                content = content[(firstSpace + 1)..].Trim();
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                kind = content.Trim();
                content = string.Empty;
            }

            foreach (var part in SplitArguments(content))
            {
                var equal = IndexOfTopLevelEquals(part);
                if (equal < 0)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        path = TrimBindingValue(part);
                    }

                    continue;
                }

                var name = part[..equal].Trim();
                var fieldValue = TrimBindingValue(part[(equal + 1)..]);
                values[name] = fieldValue;
                if (string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase))
                {
                    path = fieldValue;
                }
            }

            return new BindingMarkupFields(
                string.IsNullOrWhiteSpace(kind) ? "Binding" : kind,
                path,
                Get(values, "Mode"),
                Get(values, "Source"),
                Get(values, "ElementName"),
                Get(values, "RelativeSource"),
                Get(values, "Converter"),
                Get(values, "StringFormat"),
                Get(values, "FallbackValue"),
                Get(values, "TargetNullValue"));
        }

        public static BindingMarkupFields ParseObjectElement(IXmlElementSyntax element)
        {
            return new BindingMarkupFields(
                element.NameNode.LocalName,
                XamlTextEditor.GetAttributeValue(element, "Path") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "Mode") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "Source") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "ElementName") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "RelativeSource") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "Converter") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "StringFormat") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "FallbackValue") ?? string.Empty,
                XamlTextEditor.GetAttributeValue(element, "TargetNullValue") ?? string.Empty);
        }

        private static string Get(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static IEnumerable<string> SplitArguments(string value)
        {
            var builder = new StringBuilder();
            var braceDepth = 0;
            char? quote = null;
            foreach (var character in value)
            {
                if (quote is { } currentQuote)
                {
                    builder.Append(character);
                    if (character == currentQuote)
                    {
                        quote = null;
                    }

                    continue;
                }

                if (character is '\'' or '"')
                {
                    quote = character;
                    builder.Append(character);
                    continue;
                }

                if (character == '{')
                {
                    braceDepth++;
                    builder.Append(character);
                    continue;
                }

                if (character == '}')
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                    builder.Append(character);
                    continue;
                }

                if (character == ',' && braceDepth == 0)
                {
                    var part = builder.ToString().Trim();
                    if (part.Length > 0)
                    {
                        yield return part;
                    }

                    builder.Clear();
                    continue;
                }

                builder.Append(character);
            }

            var last = builder.ToString().Trim();
            if (last.Length > 0)
            {
                yield return last;
            }
        }

        private static int IndexOfWhitespace(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfTopLevelEquals(string value)
        {
            var braceDepth = 0;
            char? quote = null;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (quote is { } currentQuote)
                {
                    if (character == currentQuote)
                    {
                        quote = null;
                    }

                    continue;
                }

                if (character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }

                if (character == '{')
                {
                    braceDepth++;
                    continue;
                }

                if (character == '}')
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                    continue;
                }

                if (character == '=' && braceDepth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string TrimBindingValue(string value)
        {
            value = value.Trim();
            return value.Length >= 2 &&
                   ((value[0] == '\'' && value[^1] == '\'') ||
                    (value[0] == '"' && value[^1] == '"'))
                ? value[1..^1]
                : value;
        }
    }
}
