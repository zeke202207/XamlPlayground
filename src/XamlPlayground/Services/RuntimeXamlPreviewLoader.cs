using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XamlPlayground.Services;

public static class RuntimeXamlPreviewLoader
{
    private static readonly object s_resourceLock = new();
    private static readonly List<IResourceProvider> s_projectPreviewResources = new();

    public static void ApplyProjectResources(
        IEnumerable<(string Path, string Text)> resourceFiles,
        Assembly? localAssembly,
        ICollection<RuntimeXamlDiagnostic> diagnostics)
    {
        lock (s_resourceLock)
        {
            ClearProjectResources();

            if (Application.Current?.Resources is not ResourceDictionary resources)
            {
                return;
            }

            foreach (var resourceFile in resourceFiles)
            {
                if (string.IsNullOrWhiteSpace(resourceFile.Text))
                {
                    continue;
                }

                var document = new RuntimeXamlLoaderDocument(RemoveDesignPreviewContent(resourceFile.Text))
                {
                    Document = resourceFile.Path
                };
                var configuration = CreateConfiguration(localAssembly, diagnostics);
                if (AvaloniaRuntimeXamlLoader.Load(document, configuration) is not IResourceProvider resourceProvider)
                {
                    continue;
                }

                resources.MergedDictionaries.Add(resourceProvider);
                s_projectPreviewResources.Add(resourceProvider);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML and dynamically compiled user code.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Fallback root types come from dynamically compiled user assemblies and are not statically trim-analyzable.")]
    public static Control? LoadControl(
        string xaml,
        Assembly? localAssembly,
        string? fallbackRootTypeName,
        string documentName,
        ICollection<RuntimeXamlDiagnostic> diagnostics)
    {
        object? rootInstance = null;

        if (localAssembly is { } && !HasXamlClass(xaml))
        {
            var rootType = ResolveFallbackRootType(localAssembly, fallbackRootTypeName);
            if (rootType is { })
            {
                rootInstance = Activator.CreateInstance(rootType);
            }
        }

        var document = new RuntimeXamlLoaderDocument(rootInstance, xaml)
        {
            Document = string.IsNullOrWhiteSpace(documentName) ? "Main.axaml" : documentName
        };

        var configuration = CreateConfiguration(localAssembly, diagnostics);

        return AvaloniaRuntimeXamlLoader.Load(document, configuration) as Control;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML and dynamically compiled user code.")]
    public static Control? LoadResourceDictionaryPreview(
        string xaml,
        Assembly? localAssembly,
        string documentName,
        ICollection<RuntimeXamlDiagnostic> diagnostics)
    {
        var previewContent = ExtractDesignPreviewContent(xaml);
        var previewXaml = string.IsNullOrWhiteSpace(previewContent)
            ? CreateEmptyResourcePreview(documentName)
            : WrapPreviewContent(previewContent);

        return LoadControl(
            previewXaml,
            localAssembly,
            fallbackRootTypeName: null,
            documentName: documentName,
            diagnostics: diagnostics);
    }

    private static RuntimeXamlLoaderConfiguration CreateConfiguration(
        Assembly? localAssembly,
        ICollection<RuntimeXamlDiagnostic> diagnostics)
    {
        return new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            CreateSourceInfo = true,
            DiagnosticHandler = diagnostic =>
            {
                diagnostics.Add(diagnostic);
                return diagnostic.Severity;
            }
        };
    }

    private static void ClearProjectResources()
    {
        if (Application.Current?.Resources is not ResourceDictionary resources)
        {
            s_projectPreviewResources.Clear();
            return;
        }

        foreach (var resource in s_projectPreviewResources)
        {
            resources.MergedDictionaries.Remove(resource);
        }

        s_projectPreviewResources.Clear();
    }

    private static string? ExtractDesignPreviewContent(string xaml)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            var preview = root?
                .Elements()
                .FirstOrDefault(static element => element.Name.LocalName == "Design.PreviewWith");
            var content = preview?.Elements().FirstOrDefault();
            if (root is null || content is null)
            {
                return null;
            }

            var previewContent = new XElement(content);
            AddInheritedNamespaceDeclarations(previewContent, root);
            return previewContent.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return null;
        }
    }

    private static string RemoveDesignPreviewContent(string xaml)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            document.Root?
                .Elements()
                .Where(static element => element.Name.LocalName == "Design.PreviewWith")
                .Remove();
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return xaml;
        }
    }

    private static string WrapPreviewContent(string previewContent)
    {
        var namespaces = CreateNamespaceDeclarations(ExtractNamespaceDeclarations(previewContent));
        return
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
            namespaces +
            ">\n" +
            RemoveXmlnsDeclarations(previewContent) +
            "\n</UserControl>";
    }

    private static void AddInheritedNamespaceDeclarations(XElement content, XElement root)
    {
        var existing = content
            .Attributes()
            .Where(static attribute => attribute.IsNamespaceDeclaration)
            .Select(static attribute => attribute.Name)
            .ToHashSet();

        foreach (var attribute in root.Attributes().Where(static attribute => attribute.IsNamespaceDeclaration))
        {
            if (existing.Add(attribute.Name))
            {
                content.Add(new XAttribute(attribute.Name, attribute.Value));
            }
        }
    }

    private static string CreateEmptyResourcePreview(string documentName)
    {
        return
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Border Padding=\"24\">\n" +
            $"    <TextBlock Text=\"{EscapeAttributeValue(documentName)} has no Design.PreviewWith content.\" />\n" +
            "  </Border>\n" +
            "</UserControl>";
    }

    private static string EscapeAttributeValue(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static IEnumerable<(string Prefix, string Uri)> ExtractNamespaceDeclarations(string xaml)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     xaml,
                     "\\s+xmlns(?::(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*))?=\"(?<uri>[^\"]*)\"",
                     RegexOptions.CultureInvariant))
        {
            var prefix = match.Groups["prefix"].Value;
            if (string.IsNullOrEmpty(prefix) ||
                string.Equals(prefix, "x", StringComparison.Ordinal) ||
                !seen.Add(prefix))
            {
                continue;
            }

            yield return (prefix, match.Groups["uri"].Value);
        }
    }

    private static string CreateNamespaceDeclarations(IEnumerable<(string Prefix, string Uri)> declarations)
    {
        return string.Concat(declarations.Select(declaration =>
            "\n             xmlns:" + declaration.Prefix + "=\"" + EscapeAttributeValue(declaration.Uri) + "\""));
    }

    private static string RemoveXmlnsDeclarations(string xaml)
    {
        return Regex.Replace(
            xaml,
            "\\s+xmlns(?::[A-Za-z_][A-Za-z0-9_.-]*)?=\"[^\"]*\"",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Fallback root types are discovered from dynamically compiled user assemblies.")]
    private static Type? ResolveFallbackRootType(Assembly assembly, string? fallbackRootTypeName)
    {
        var types = assembly.GetTypes();
        return types.FirstOrDefault(static type => type.Name == "SampleView")
               ?? (!string.IsNullOrWhiteSpace(fallbackRootTypeName)
                   ? types.FirstOrDefault(type => type.Name == fallbackRootTypeName)
                   : null);
    }

    private static bool HasXamlClass(string xaml)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
            return document.Root?.Attributes()
                .Any(static attribute =>
                    attribute.Name.LocalName == "Class" &&
                    attribute.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml") == true;
        }
        catch
        {
            return false;
        }
    }
}
