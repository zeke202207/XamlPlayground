using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlPlayground.Services.Theming;

public sealed record ThemeResourceDocument(
    string Path,
    string Text,
    bool IsResourceDictionary);

public sealed record ThemeResourceAnalysis(
    IReadOnlyList<ThemeResourceDefinition> Resources,
    IReadOnlyList<ThemeResourceReference> References,
    IReadOnlyList<ThemeResourceDiagnostic> Diagnostics)
{
    public static ThemeResourceAnalysis Empty { get; } = new(
        Array.Empty<ThemeResourceDefinition>(),
        Array.Empty<ThemeResourceReference>(),
        Array.Empty<ThemeResourceDiagnostic>());
}

public sealed record ThemeResourceDefinition(
    string Key,
    string ResourceType,
    string? TargetType,
    string FilePath,
    int? Line)
{
    public string ThemeScope { get; init; } = ThemeProjectStorage.BaseVariant;
}

public sealed record ThemeResourceReference(
    string Key,
    ThemeResourceReferenceKind Kind,
    string FilePath,
    int Line,
    string Snippet);

public sealed record ThemeResourceDiagnostic(
    ThemeResourceDiagnosticSeverity Severity,
    string Message,
    string FilePath,
    int? Line);

public enum ThemeResourceReferenceKind
{
    StaticResource,
    DynamicResource
}

public enum ThemeResourceDiagnosticSeverity
{
    Warning,
    Error
}

public static class ResourceDictionaryAnalyzer
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex ResourceReferenceRegex = new(
        "\\{\\s*(?<kind>StaticResource|DynamicResource)\\s+(?:ResourceKey\\s*=\\s*)?(?<key>[^,}\\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ThemeResourceAnalysis Analyze(IEnumerable<ThemeResourceDocument> documents)
    {
        var resources = new List<ThemeResourceDefinition>();
        var references = new List<ThemeResourceReference>();
        var diagnostics = new List<ThemeResourceDiagnostic>();
        var documentArray = documents.ToArray();

        foreach (var document in documentArray)
        {
            references.AddRange(FindResourceReferences(document));
            if (document.IsResourceDictionary)
            {
                resources.AddRange(FindResourceDefinitions(document, diagnostics));
            }
        }

        diagnostics.AddRange(FindDuplicateDiagnostics(resources));
        diagnostics.AddRange(FindMissingReferenceDiagnostics(resources, references));

        return new ThemeResourceAnalysis(
            resources
                .OrderBy(static resource => resource.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static resource => resource.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            references
                .OrderBy(static reference => reference.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static reference => reference.Line)
                .ToArray(),
            diagnostics
                .OrderByDescending(static diagnostic => diagnostic.Severity)
                .ThenBy(static diagnostic => diagnostic.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static diagnostic => diagnostic.Line ?? 0)
                .ToArray());
    }

    private static IEnumerable<ThemeResourceDefinition> FindResourceDefinitions(
        ThemeResourceDocument document,
        List<ThemeResourceDiagnostic> diagnostics)
    {
        XDocument xaml;
        try
        {
            xaml = XDocument.Parse(document.Text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            diagnostics.Add(new ThemeResourceDiagnostic(
                ThemeResourceDiagnosticSeverity.Error,
                exception.Message,
                document.Path,
                exception.LineNumber > 0 ? exception.LineNumber : null));
            yield break;
        }

        if (xaml.Root is null)
        {
            yield break;
        }

        foreach (var resource in EnumerateResourceElements(xaml.Root, ThemeProjectStorage.BaseVariant))
        {
            var element = resource.Element;
            var key = element.Attribute(XamlNamespace + "Key")?.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            yield return new ThemeResourceDefinition(
                key,
                element.Name.LocalName,
                element.Attribute("TargetType")?.Value ?? element.Attribute("DataType")?.Value,
                document.Path,
                GetLineNumber(element))
            {
                ThemeScope = resource.ThemeScope
            };
        }
    }

    private static IEnumerable<(XElement Element, string ThemeScope)> EnumerateResourceElements(
        XElement dictionary,
        string themeScope)
    {
        foreach (var element in dictionary.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "Design.PreviewWith":
                case "ResourceDictionary.MergedDictionaries":
                    continue;

                case "ResourceDictionary.ThemeDictionaries":
                    foreach (var themeDictionary in element.Elements().Where(static child => child.Name.LocalName == "ResourceDictionary"))
                    {
                        var childScope = themeDictionary.Attribute(XamlNamespace + "Key")?.Value;
                        if (string.IsNullOrWhiteSpace(childScope))
                        {
                            childScope = themeScope;
                        }

                        foreach (var resource in EnumerateResourceElements(themeDictionary, childScope))
                        {
                            yield return resource;
                        }
                    }

                    continue;

                default:
                    yield return (element, themeScope);
                    break;
            }
        }
    }

    private static IEnumerable<ThemeResourceReference> FindResourceReferences(ThemeResourceDocument document)
    {
        var lines = document.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (Match match in ResourceReferenceRegex.Matches(line))
            {
                var key = match.Groups["key"].Value.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                yield return new ThemeResourceReference(
                    key,
                    Enum.Parse<ThemeResourceReferenceKind>(match.Groups["kind"].Value),
                    document.Path,
                    i + 1,
                    line.Trim());
            }
        }
    }

    private static IEnumerable<ThemeResourceDiagnostic> FindDuplicateDiagnostics(
        IEnumerable<ThemeResourceDefinition> resources)
    {
        foreach (var duplicateGroup in resources
                     .GroupBy(
                         static resource => (resource.Key, resource.ThemeScope),
                         new ThemeResourceDefinitionDuplicateScopeComparer())
                     .Where(static group => group.Count() > 1))
        {
            foreach (var resource in duplicateGroup)
            {
                yield return new ThemeResourceDiagnostic(
                    ThemeResourceDiagnosticSeverity.Warning,
                    $"Duplicate resource key '{resource.Key}'.",
                    resource.FilePath,
                    resource.Line);
            }
        }
    }

    private static IEnumerable<ThemeResourceDiagnostic> FindMissingReferenceDiagnostics(
        IEnumerable<ThemeResourceDefinition> resources,
        IEnumerable<ThemeResourceReference> references)
    {
        var keys = resources
            .Select(static resource => resource.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var reference in references.Where(reference => !keys.Contains(reference.Key)))
        {
            yield return new ThemeResourceDiagnostic(
                ThemeResourceDiagnosticSeverity.Warning,
                $"Resource '{reference.Key}' is referenced but not defined in the theme workspace.",
                reference.FilePath,
                reference.Line);
        }
    }

    private static int? GetLineNumber(XObject value)
    {
        return value is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : null;
    }

    private sealed class ThemeResourceDefinitionDuplicateScopeComparer :
        IEqualityComparer<(string Key, string ThemeScope)>
    {
        public bool Equals((string Key, string ThemeScope) x, (string Key, string ThemeScope) y)
        {
            return string.Equals(x.Key, y.Key, StringComparison.Ordinal) &&
                   string.Equals(x.ThemeScope, y.ThemeScope, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Key, string ThemeScope) obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Key),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ThemeScope));
        }
    }
}
