using System;
using System.Collections.Generic;
using System.Linq;
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
    string Snippet)
{
    public string ThemeScope { get; init; } = ThemeProjectStorage.BaseVariant;
}

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

    public static ThemeResourceAnalysis Analyze(IEnumerable<ThemeResourceDocument> documents)
    {
        var resources = new List<ThemeResourceDefinition>();
        var references = new List<ThemeResourceReference>();
        var diagnostics = new List<ThemeResourceDiagnostic>();
        var declaredThemeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentArray = documents.ToArray();

        foreach (var document in documentArray)
        {
            references.AddRange(FindResourceReferences(document));
            if (document.IsResourceDictionary)
            {
                declaredThemeScopes.UnionWith(FindDeclaredThemeScopes(document));
                resources.AddRange(FindResourceDefinitions(document, diagnostics));
            }
        }

        diagnostics.AddRange(FindDuplicateDiagnostics(resources));
        diagnostics.AddRange(FindMissingReferenceDiagnostics(resources, references, declaredThemeScopes));

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

    private static IEnumerable<string> FindDeclaredThemeScopes(ThemeResourceDocument document)
    {
        if (TryParseDocument(document.Text) is not { Root: { } root })
        {
            yield break;
        }

        foreach (var scope in EnumerateDeclaredThemeScopes(root, ThemeProjectStorage.BaseVariant))
        {
            yield return scope;
        }
    }

    private static IEnumerable<string> EnumerateDeclaredThemeScopes(
        XElement element,
        string themeScope)
    {
        if (element.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
        {
            foreach (var themeDictionary in element.Elements().Where(static child => child.Name.LocalName == "ResourceDictionary"))
            {
                var childScope = themeDictionary.Attribute(XamlNamespace + "Key")?.Value;
                if (string.IsNullOrWhiteSpace(childScope))
                {
                    childScope = themeScope;
                }

                if (!string.Equals(childScope, ThemeProjectStorage.BaseVariant, StringComparison.OrdinalIgnoreCase))
                {
                    yield return childScope;
                }

                foreach (var nestedScope in EnumerateDeclaredThemeScopes(themeDictionary, childScope))
                {
                    yield return nestedScope;
                }
            }

            yield break;
        }

        foreach (var child in element.Elements())
        {
            foreach (var nestedScope in EnumerateDeclaredThemeScopes(child, themeScope))
            {
                yield return nestedScope;
            }
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
        if (TryParseDocument(document.Text) is { } xaml && xaml.Root is { } root)
        {
            foreach (var scopedAttribute in EnumerateReferenceAttributes(root, ThemeProjectStorage.BaseVariant))
            {
                foreach (var match in ResourceReferenceParser.Find(scopedAttribute.Attribute.Value))
                {
                    var line = GetLineNumber(scopedAttribute.Attribute) ??
                               (scopedAttribute.Attribute.Parent is { } parent
                                   ? GetLineNumber(parent)
                                   : null) ??
                               1;
                    yield return new ThemeResourceReference(
                        match.Key,
                        Enum.Parse<ThemeResourceReferenceKind>(match.Kind),
                        document.Path,
                        line,
                        GetSnippet(lines, line))
                    {
                        ThemeScope = scopedAttribute.ThemeScope
                    };
                }
            }

            foreach (var scopedElement in EnumerateReferenceElements(root, ThemeProjectStorage.BaseVariant))
            {
                if (!TryGetObjectElementResourceReference(scopedElement.Element, out var key, out var kind))
                {
                    continue;
                }

                var line = GetLineNumber(scopedElement.Element) ?? 1;
                yield return new ThemeResourceReference(
                    key,
                    kind,
                    document.Path,
                    line,
                    GetSnippet(lines, line))
                {
                    ThemeScope = scopedElement.ThemeScope
                };
            }

            yield break;
        }

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            foreach (var match in ResourceReferenceParser.Find(line))
            {
                yield return new ThemeResourceReference(
                    match.Key,
                    Enum.Parse<ThemeResourceReferenceKind>(match.Kind),
                    document.Path,
                    lineIndex + 1,
                    line.Trim());
            }
        }
    }

    private static bool TryGetObjectElementResourceReference(
        XElement element,
        out string key,
        out ThemeResourceReferenceKind kind)
    {
        key = string.Empty;
        kind = default;

        switch (element.Name.LocalName)
        {
            case "StaticResource":
                kind = ThemeResourceReferenceKind.StaticResource;
                break;

            case "DynamicResource":
                kind = ThemeResourceReferenceKind.DynamicResource;
                break;

            default:
                return false;
        }

        key = element
            .Attributes()
            .FirstOrDefault(static attribute => attribute.Name.LocalName == "ResourceKey")
            ?.Value
            .Trim() ?? element.Value.Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private static XDocument? TryParseDocument(string text)
    {
        try
        {
            return XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<(XElement Element, string ThemeScope)> EnumerateReferenceElements(
        XElement element,
        string themeScope)
    {
        var currentScope = GetElementThemeScope(element, themeScope);
        yield return (element, currentScope);

        foreach (var child in element.Elements())
        {
            foreach (var scopedElement in EnumerateReferenceElements(child, currentScope))
            {
                yield return scopedElement;
            }
        }
    }

    private static IEnumerable<(XAttribute Attribute, string ThemeScope)> EnumerateReferenceAttributes(
        XElement element,
        string themeScope)
    {
        var currentScope = GetElementThemeScope(element, themeScope);

        foreach (var attribute in element.Attributes())
        {
            yield return (attribute, currentScope);
        }

        foreach (var child in element.Elements())
        {
            foreach (var attribute in EnumerateReferenceAttributes(child, currentScope))
            {
                yield return attribute;
            }
        }
    }

    private static string GetElementThemeScope(XElement element, string fallbackScope)
    {
        return element.Parent?.Name.LocalName == "ResourceDictionary.ThemeDictionaries" &&
               element.Name.LocalName == "ResourceDictionary"
            ? element.Attribute(XamlNamespace + "Key")?.Value ?? fallbackScope
            : fallbackScope;
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
        IEnumerable<ThemeResourceReference> references,
        IEnumerable<string> declaredThemeScopes)
    {
        var resourceArray = resources.ToArray();
        var themeScopes = resourceArray
            .Select(static resource => resource.ThemeScope)
            .Concat(declaredThemeScopes)
            .Where(static scope => !string.Equals(scope, ThemeProjectStorage.BaseVariant, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var reference in references.Where(reference => !HasResolvableResource(resourceArray, themeScopes, reference)))
        {
            yield return new ThemeResourceDiagnostic(
                ThemeResourceDiagnosticSeverity.Warning,
                $"Resource '{reference.Key}' is referenced but not defined in the theme workspace.",
                reference.FilePath,
                reference.Line);
        }
    }

    private static bool HasResolvableResource(
        IReadOnlyCollection<ThemeResourceDefinition> resources,
        IReadOnlyCollection<string> themeScopes,
        ThemeResourceReference reference)
    {
        var matches = resources
            .Where(resource => string.Equals(resource.Key, reference.Key, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return false;
        }

        if (matches.Any(static resource => string.Equals(resource.ThemeScope, ThemeProjectStorage.BaseVariant, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.Equals(reference.ThemeScope, ThemeProjectStorage.BaseVariant, StringComparison.OrdinalIgnoreCase))
        {
            return matches.Any(resource => string.Equals(resource.ThemeScope, reference.ThemeScope, StringComparison.OrdinalIgnoreCase));
        }

        return themeScopes.Count > 0 &&
               themeScopes.All(scope => matches.Any(resource => string.Equals(resource.ThemeScope, scope, StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetSnippet(IReadOnlyList<string> lines, int line)
    {
        return line > 0 && line <= lines.Count
            ? lines[line - 1].Trim()
            : string.Empty;
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
