using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Language.Xml;
using XamlPlayground.Services.Editing;

namespace XamlPlayground.Services.DesignInspection;

internal sealed record XamlBindingMarkupFields(
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

internal static class XamlBindingMarkup
{
    public static XamlBindingMarkupFields Parse(string value)
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

        return new XamlBindingMarkupFields(
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

    public static XamlBindingMarkupFields ParseObjectElement(IXmlElementSyntax element)
    {
        return new XamlBindingMarkupFields(
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
        var escaped = false;
        foreach (var character in value)
        {
            if (quote is { } currentQuote)
            {
                builder.Append(character);
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

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
        var escaped = false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (quote is { } currentQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

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
            ? UnescapeQuotedBindingValue(value[1..^1], value[0])
            : value;
    }

    private static string UnescapeQuotedBindingValue(string value, char quote)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' &&
                index + 1 < value.Length &&
                (value[index + 1] == quote || value[index + 1] == '\\'))
            {
                builder.Append(value[index + 1]);
                index++;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
