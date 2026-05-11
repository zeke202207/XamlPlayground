using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XamlPlayground.Services.VisualEditing;

public sealed record ToolboxInsertionResult(
    XamlMutationResult Mutation,
    string InsertedXaml,
    string? NamespacePrefix)
{
    public bool Success => Mutation.Success;
}

public sealed class XamlToolboxInsertionService
{
    private readonly IXamlMutationEngine _mutationEngine;

    public XamlToolboxInsertionService(IXamlMutationEngine mutationEngine)
    {
        _mutationEngine = mutationEngine ?? throw new ArgumentNullException(nameof(mutationEngine));
    }

    public ToolboxInsertionResult Insert(
        string xaml,
        XamlElementSelector parentSelector,
        ToolboxItemDescriptor item,
        int? childIndex = null,
        IReadOnlyDictionary<string, string>? insertionProperties = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(parentSelector);
        ArgumentNullException.ThrowIfNull(item);

        var insertionXaml = item.DefaultXaml;
        if (insertionProperties is { Count: > 0 })
        {
            insertionXaml = ApplyInsertionProperties(insertionXaml, insertionProperties);
        }

        var namespacePrefix = default(string);
        var document = _mutationEngine.Analyze(xaml);
        if (document.Root is null)
        {
            var failed = new XamlMutationResult(xaml, document, document.Diagnostics);
            return new ToolboxInsertionResult(failed, insertionXaml, namespacePrefix);
        }

        if (!UsesDefaultNamespace(item))
        {
            var snippetPrefix = GetSnippetPrefix(insertionXaml);
            var namespaceResult = EnsureNamespace(xaml, document.Root, item.XmlNamespace, snippetPrefix ?? "local");
            if (!namespaceResult.Mutation.Success)
            {
                return new ToolboxInsertionResult(namespaceResult.Mutation, insertionXaml, namespacePrefix);
            }

            xaml = namespaceResult.Xaml;
            namespacePrefix = namespaceResult.Prefix;
            insertionXaml = RewriteSnippetPrefix(insertionXaml, snippetPrefix, namespacePrefix);
        }

        var mutation = _mutationEngine.InsertChild(xaml, parentSelector, insertionXaml, childIndex);
        return new ToolboxInsertionResult(mutation, insertionXaml, namespacePrefix);
    }

    private string ApplyInsertionProperties(
        string snippet,
        IReadOnlyDictionary<string, string> properties)
    {
        var updated = snippet;
        foreach (var property in properties)
        {
            var result = _mutationEngine.SetProperty(
                updated,
                XamlElementSelector.ByPath(),
                property.Key,
                property.Value);
            if (!result.Success)
            {
                return snippet;
            }

            updated = result.Text;
        }

        return updated;
    }

    private (string Xaml, string Prefix, XamlMutationResult Mutation) EnsureNamespace(
        string xaml,
        XamlElementSnapshot root,
        string xmlNamespace,
        string preferredPrefix)
    {
        var declarations = GetNamespaceDeclarations(root).ToArray();
        var existingForNamespace = declarations.FirstOrDefault(declaration =>
            string.Equals(declaration.XmlNamespace, xmlNamespace, StringComparison.Ordinal));
        if (existingForNamespace.Prefix is { Length: > 0 })
        {
            return (xaml, existingForNamespace.Prefix, CreateMutationResult(_mutationEngine.Analyze(xaml)));
        }

        var prefix = GetAvailablePrefix(preferredPrefix, declarations);
        var updated = _mutationEngine.SetProperty(
            xaml,
            root.Selector,
            $"xmlns:{prefix}",
            xmlNamespace);

        return (updated.Text, prefix, updated);
    }

    private static IEnumerable<(string? Prefix, string XmlNamespace)> GetNamespaceDeclarations(XamlElementSnapshot root)
    {
        foreach (var attribute in root.Attributes)
        {
            if (string.Equals(attribute.Key, "xmlns", StringComparison.Ordinal))
            {
                yield return (null, attribute.Value);
                continue;
            }

            const string prefix = "xmlns:";
            if (attribute.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return (attribute.Key[prefix.Length..], attribute.Value);
            }
        }
    }

    private static string GetAvailablePrefix(
        string preferredPrefix,
        IReadOnlyCollection<(string? Prefix, string XmlNamespace)> declarations)
    {
        var normalized = string.IsNullOrWhiteSpace(preferredPrefix) ? "local" : preferredPrefix.Trim();
        var used = declarations
            .Select(static declaration => declaration.Prefix)
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(normalized))
        {
            return normalized;
        }

        var index = 1;
        while (used.Contains($"{normalized}{index}"))
        {
            index++;
        }

        return $"{normalized}{index}";
    }

    private static bool UsesDefaultNamespace(ToolboxItemDescriptor item)
    {
        return string.Equals(item.XmlNamespace, "https://github.com/avaloniaui", StringComparison.Ordinal);
    }

    private static string? GetSnippetPrefix(string snippet)
    {
        var elementName = GetFirstElementName(snippet);
        var colon = elementName?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return colon < 0 ? null : elementName![..colon];
    }

    private static string RewriteSnippetPrefix(string snippet, string? oldPrefix, string newPrefix)
    {
        if (!string.IsNullOrWhiteSpace(oldPrefix))
        {
            return snippet
                .Replace($"<{oldPrefix}:", $"<{newPrefix}:", StringComparison.Ordinal)
                .Replace($"</{oldPrefix}:", $"</{newPrefix}:", StringComparison.Ordinal);
        }

        var elementName = GetFirstElementName(snippet);
        if (string.IsNullOrWhiteSpace(elementName) || elementName.Contains(':', StringComparison.Ordinal))
        {
            return snippet;
        }

        return PrefixUnqualifiedElementTags(snippet, newPrefix);
    }

    private static string PrefixUnqualifiedElementTags(string snippet, string newPrefix)
    {
        var builder = new StringBuilder(snippet.Length + newPrefix.Length * 2);
        var offset = 0;
        while (offset < snippet.Length)
        {
            var tagStart = snippet.IndexOf('<', offset);
            if (tagStart < 0)
            {
                builder.Append(snippet, offset, snippet.Length - offset);
                break;
            }

            builder.Append(snippet, offset, tagStart - offset);
            if (tagStart + 1 >= snippet.Length)
            {
                builder.Append('<');
                break;
            }

            var marker = snippet[tagStart + 1];
            if (marker is '!' or '?')
            {
                var tagEnd = snippet.IndexOf('>', tagStart + 2);
                if (tagEnd < 0)
                {
                    builder.Append(snippet, tagStart, snippet.Length - tagStart);
                    break;
                }

                builder.Append(snippet, tagStart, tagEnd - tagStart + 1);
                offset = tagEnd + 1;
                continue;
            }

            var nameStart = marker == '/' ? tagStart + 2 : tagStart + 1;
            var nameEnd = nameStart;
            while (nameEnd < snippet.Length &&
                   snippet[nameEnd] is not ' ' and not '\t' and not '\r' and not '\n' and not '/' and not '>')
            {
                nameEnd++;
            }

            if (nameEnd == nameStart)
            {
                builder.Append('<');
                offset = tagStart + 1;
                continue;
            }

            var name = snippet[nameStart..nameEnd];
            builder.Append(snippet, tagStart, nameStart - tagStart);
            if (!name.Contains(':', StringComparison.Ordinal))
            {
                builder.Append(newPrefix);
                builder.Append(':');
            }

            builder.Append(name);
            offset = nameEnd;
        }

        return builder.ToString();
    }

    private static string? GetFirstElementName(string snippet)
    {
        var start = snippet.IndexOf('<', StringComparison.Ordinal);
        if (start < 0 || start + 1 >= snippet.Length)
        {
            return null;
        }

        var nameStart = snippet[start + 1] == '/' ? start + 2 : start + 1;
        if (nameStart >= snippet.Length)
        {
            return null;
        }

        var nameEnd = nameStart;
        while (nameEnd < snippet.Length && snippet[nameEnd] is not ' ' and not '\t' and not '\r' and not '\n' and not '/' and not '>')
        {
            nameEnd++;
        }

        return nameEnd > nameStart ? snippet[nameStart..nameEnd] : null;
    }

    private static XamlMutationResult CreateMutationResult(XamlDocumentSnapshot snapshot)
    {
        return new XamlMutationResult(snapshot.Text, snapshot, snapshot.Diagnostics);
    }
}
