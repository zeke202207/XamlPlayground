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

    public static ThemeResourceEditResult RenameResourceKey(
        string xaml,
        string oldKey,
        string newKey,
        int? line = null)
    {
        if (!IsValidKey(oldKey) || !IsValidKey(newKey))
        {
            return new ThemeResourceEditResult(false, xaml, "Resource keys cannot be empty.");
        }

        return UpdateResourceElement(
            xaml,
            oldKey,
            line,
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

        return ResourceReferenceParser.ReplaceKeys(xaml, oldKey, newKey);
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
        string newKey,
        int? line = null)
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

        var source = FindTopLevelResource(root, sourceKey, line);
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
        string key,
        int? line = null)
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

        var element = FindTopLevelResource(root, key, line);
        if (element is null)
        {
            return new ThemeResourceDeleteResult(false, xaml, RemovedLastResource: false, $"Resource '{key}' was not found.");
        }

        element.Remove();
        RemoveDesignPreviewReferences(root, key);

        var resourceCount = EnumerateTopLevelResources(root).Count();
        if (resourceCount == 0)
        {
            return new ThemeResourceDeleteResult(true, Serialize(parse.Document), RemovedLastResource: true);
        }

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
        int? line,
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

        var element = FindTopLevelResource(root, key, line);
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
            return (XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo), null);
        }
        catch (XmlException exception)
        {
            return (new XDocument(), exception.Message);
        }
    }

    private static XElement? FindTopLevelResource(XElement root, string key, int? line = null)
    {
        return EnumerateTopLevelResources(root)
            .FirstOrDefault(element => string.Equals(
                element.Attribute(XamlNamespace + "Key")?.Value,
                key,
                StringComparison.Ordinal) &&
                (line is null || GetLineNumber(element) == line));
    }

    private static IEnumerable<XElement> EnumerateTopLevelResources(XElement root)
    {
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "Design.PreviewWith":
                case "ResourceDictionary.MergedDictionaries":
                    continue;

                case "ResourceDictionary.ThemeDictionaries":
                    foreach (var themeDictionary in element.Elements().Where(static child => child.Name.LocalName == "ResourceDictionary"))
                    {
                        foreach (var resource in EnumerateTopLevelResources(themeDictionary))
                        {
                            yield return resource;
                        }
                    }

                    continue;
            }

            if (element.Attribute(XamlNamespace + "Key") is not null)
            {
                yield return element;
            }
        }
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
        xaml = RemoveResourceReferenceSetters(xaml, key);

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

    private static string RemoveResourceReferenceSetters(string xaml, string key)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return xaml;
        }

        var setters = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Setter" && SetterReferencesResource(element, key))
            .ToArray();
        if (setters.Length == 0)
        {
            return xaml;
        }

        foreach (var setter in setters)
        {
            setter.Remove();
        }

        return Serialize(document);
    }

    private static bool SetterReferencesResource(XElement setter, string key)
    {
        var valueAttribute = setter
            .Attributes()
            .FirstOrDefault(static attribute => attribute.Name.LocalName == "Value");
        if (valueAttribute is not null &&
            IsExactResourceReference(valueAttribute.Value, key))
        {
            return true;
        }

        return setter
            .Elements()
            .Any(element => element.Name.LocalName == "Setter.Value" &&
                            SetterValueElementReferencesResource(element, key));
    }

    private static bool SetterValueElementReferencesResource(XElement setterValue, string key)
    {
        return IsExactResourceReference(setterValue.Value, key) ||
               setterValue
                   .Descendants()
                   .Any(element => IsResourceReferenceElement(element, key));
    }

    private static bool IsResourceReferenceElement(XElement element, string key)
    {
        if (element.Name.LocalName is not ("StaticResource" or "DynamicResource"))
        {
            return false;
        }

        var resourceKey = element
            .Attributes()
            .FirstOrDefault(static attribute => attribute.Name.LocalName == "ResourceKey")
            ?.Value;
        if (!string.IsNullOrWhiteSpace(resourceKey) &&
            (string.Equals(resourceKey, key, StringComparison.Ordinal) ||
             IsExactResourceReference(resourceKey, key)))
        {
            return true;
        }

        return string.Equals(element.Value.Trim(), key, StringComparison.Ordinal) ||
               IsExactResourceReference(element.Value, key);
    }

    private static bool IsExactResourceReference(string value, string key)
    {
        return ResourceReferenceParser.TryGetExactKey(value, out var referenceKey) &&
               string.Equals(referenceKey, key, StringComparison.Ordinal);
    }

    private static bool IsValidKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key);
    }

    private static int? GetLineNumber(XObject value)
    {
        return value is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : null;
    }

    private static string Serialize(XDocument document)
    {
        return document.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
    }
}
