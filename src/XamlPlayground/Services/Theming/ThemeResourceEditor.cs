using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlPlayground.Services.Theming;

public sealed record ThemeResourceEditResult(
    bool Changed,
    string Text,
    string? Error = null);

public sealed record ThemeResourceDeleteResult(
    bool Changed,
    string Text,
    bool RemovedLastResource,
    string? Error = null);

public static class ThemeResourceEditor
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex ResourceReferenceRegex = new(
        "\\{\\s*(?<kind>StaticResource|DynamicResource)(?<leading>\\s+)(?:(?<name>ResourceKey)\\s*=\\s*)?(?<key>[^,}\\s]+)(?<tail>[^}]*)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ThemeResourceEditResult RenameResourceKey(
        string xaml,
        string oldKey,
        string newKey)
    {
        if (!IsValidKey(oldKey) || !IsValidKey(newKey))
        {
            return new ThemeResourceEditResult(false, xaml, "Resource keys cannot be empty.");
        }

        return UpdateResourceElement(
            xaml,
            oldKey,
            element => element.SetAttributeValue(XamlNamespace + "Key", newKey));
    }

    public static string RenameResourceReferences(
        string xaml,
        string oldKey,
        string newKey)
    {
        if (!IsValidKey(oldKey) || !IsValidKey(newKey) ||
            string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            return xaml;
        }

        return ResourceReferenceRegex.Replace(xaml, match =>
        {
            if (!string.Equals(match.Groups["key"].Value, oldKey, StringComparison.Ordinal))
            {
                return match.Value;
            }

            var prefix = match.Groups["name"].Success
                ? $"{match.Groups["name"].Value}="
                : string.Empty;
            return "{" +
                   match.Groups["kind"].Value +
                   match.Groups["leading"].Value +
                   prefix +
                   newKey +
                   match.Groups["tail"].Value +
                   "}";
        });
    }

    public static string RemoveResourceReferences(
        string xaml,
        string key)
    {
        if (!IsValidKey(key))
        {
            return xaml;
        }

        return RemoveResourceReferenceAttributes(xaml, key);
    }

    public static ThemeResourceEditResult DuplicateResource(
        string xaml,
        string sourceKey,
        string newKey)
    {
        if (!IsValidKey(sourceKey) || !IsValidKey(newKey))
        {
            return new ThemeResourceEditResult(false, xaml, "Resource keys cannot be empty.");
        }

        if (string.Equals(sourceKey, newKey, StringComparison.Ordinal))
        {
            return new ThemeResourceEditResult(false, xaml, "Duplicate resource key must be different.");
        }

        var parse = TryParseResourceDictionary(xaml);
        if (parse.Error is not null)
        {
            return new ThemeResourceEditResult(false, xaml, parse.Error);
        }

        var root = parse.Document.Root;
        if (root is null)
        {
            return new ThemeResourceEditResult(false, xaml, "Resource dictionary has no root element.");
        }

        if (FindTopLevelResource(root, newKey) is not null)
        {
            return new ThemeResourceEditResult(false, xaml, $"Resource '{newKey}' already exists in this dictionary.");
        }

        var source = FindTopLevelResource(root, sourceKey);
        if (source is null)
        {
            return new ThemeResourceEditResult(false, xaml, $"Resource '{sourceKey}' was not found.");
        }

        var duplicate = new XElement(source);
        duplicate.SetAttributeValue(XamlNamespace + "Key", newKey);
        source.AddAfterSelf(new XText(Environment.NewLine), duplicate);

        return new ThemeResourceEditResult(true, Serialize(parse.Document));
    }

    public static ThemeResourceDeleteResult DeleteResource(
        string xaml,
        string key)
    {
        if (!IsValidKey(key))
        {
            return new ThemeResourceDeleteResult(false, xaml, RemovedLastResource: false, "Resource key cannot be empty.");
        }

        var parse = TryParseResourceDictionary(xaml);
        if (parse.Error is not null)
        {
            return new ThemeResourceDeleteResult(false, xaml, RemovedLastResource: false, parse.Error);
        }

        var root = parse.Document.Root;
        if (root is null)
        {
            return new ThemeResourceDeleteResult(false, xaml, RemovedLastResource: false, "Resource dictionary has no root element.");
        }

        var element = FindTopLevelResource(root, key);
        if (element is null)
        {
            return new ThemeResourceDeleteResult(false, xaml, RemovedLastResource: false, $"Resource '{key}' was not found.");
        }

        element.Remove();
        var resourceCount = EnumerateTopLevelResources(root).Count();
        if (resourceCount == 0)
        {
            return new ThemeResourceDeleteResult(true, Serialize(parse.Document), RemovedLastResource: true);
        }

        RemoveDesignPreviewReferences(root, key);
        return new ThemeResourceDeleteResult(true, Serialize(parse.Document), RemovedLastResource: false);
    }

    public static bool ContainsResourceKey(string xaml, string key)
    {
        var parse = TryParseResourceDictionary(xaml);
        return parse.Error is null &&
               parse.Document.Root is { } root &&
               FindTopLevelResource(root, key) is not null;
    }

    private static ThemeResourceEditResult UpdateResourceElement(
        string xaml,
        string key,
        Action<XElement> update)
    {
        var parse = TryParseResourceDictionary(xaml);
        if (parse.Error is not null)
        {
            return new ThemeResourceEditResult(false, xaml, parse.Error);
        }

        var root = parse.Document.Root;
        if (root is null)
        {
            return new ThemeResourceEditResult(false, xaml, "Resource dictionary has no root element.");
        }

        var element = FindTopLevelResource(root, key);
        if (element is null)
        {
            return new ThemeResourceEditResult(false, xaml, $"Resource '{key}' was not found.");
        }

        update(element);
        return new ThemeResourceEditResult(true, Serialize(parse.Document));
    }

    private static (XDocument Document, string? Error) TryParseResourceDictionary(string xaml)
    {
        try
        {
            return (XDocument.Parse(xaml, LoadOptions.PreserveWhitespace), null);
        }
        catch (XmlException exception)
        {
            return (new XDocument(), exception.Message);
        }
    }

    private static XElement? FindTopLevelResource(XElement root, string key)
    {
        return EnumerateTopLevelResources(root)
            .FirstOrDefault(element => string.Equals(
                element.Attribute(XamlNamespace + "Key")?.Value,
                key,
                StringComparison.Ordinal));
    }

    private static IEnumerable<XElement> EnumerateTopLevelResources(XElement root)
    {
        return root.Elements()
            .Where(static element => element.Name.LocalName is not "Design.PreviewWith" and not "ResourceDictionary.MergedDictionaries")
            .Where(static element => element.Attribute(XamlNamespace + "Key") is not null);
    }

    private static void RemoveDesignPreviewReferences(XElement root, string key)
    {
        root.Elements()
            .Where(static element => element.Name.LocalName == "Design.PreviewWith")
            .Where(element => element.ToString(SaveOptions.DisableFormatting).Contains(key, StringComparison.Ordinal))
            .Remove();
    }

    private static string RemoveResourceReferenceAttributes(string xaml, string key)
    {
        var attributeRegex = new Regex(
            "\\s+[A-Za-z_][A-Za-z0-9_.:-]*\\s*=\\s*\"(?<value>[^\"]*)\"",
            RegexOptions.CultureInvariant);

        return attributeRegex.Replace(xaml, match =>
        {
            var value = match.Groups["value"].Value;
            return IsExactResourceReference(value, key)
                ? string.Empty
                : match.Value;
        });
    }

    private static bool IsExactResourceReference(string value, string key)
    {
        var match = ResourceReferenceRegex.Match(value.Trim());
        return match.Success &&
               match.Index == 0 &&
               match.Length == value.Trim().Length &&
               string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal);
    }

    private static bool IsValidKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key);
    }

    private static string Serialize(XDocument document)
    {
        return document.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
    }
}
