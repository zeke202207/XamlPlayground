using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using XamlPlayground.Services.Theming;

namespace XamlPlayground.Services;

public static class RuntimeXamlPreviewLoader
{
    private const string PreviewAssemblyName = "XamlPlayground.Preview";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly object s_resourceLock = new();
    private static readonly List<IResourceProvider> s_projectPreviewResources = new();

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML and dynamically compiled user code.")]
    public static void ApplyProjectResources(
        IEnumerable<(string Path, string Text)> resourceFiles,
        Assembly? localAssembly,
        ICollection<RuntimeXamlDiagnostic> diagnostics,
        string? documentAssemblyName = null)
    {
        lock (s_resourceLock)
        {
            ClearProjectResources();

            if (Application.Current?.Resources is not ResourceDictionary resources)
            {
                return;
            }

            var documents = CreateResourceDocuments(resourceFiles, localAssembly, documentAssemblyName);
            if (documents.Count == 0)
            {
                return;
            }

            var configuration = CreateConfiguration(localAssembly, diagnostics);
            using var contextualReflection = EnterContextualReflection(localAssembly);
            foreach (var resourceProvider in AvaloniaRuntimeXamlLoader.LoadGroup(documents, configuration)
                         .OfType<IResourceProvider>())
            {
                resources.MergedDictionaries.Add(resourceProvider);
                s_projectPreviewResources.Add(resourceProvider);
            }
        }
    }

    private static IReadOnlyList<(string Path, string Text)> OrderResourceFilesForLoading(
        IEnumerable<(string Path, string Text)> resourceFiles)
    {
        var files = resourceFiles.ToArray();
        if (files.Length < 2)
        {
            return files;
        }

        var analysis = ResourceDictionaryAnalyzer.Analyze(files.Select(static file =>
            new ThemeResourceDocument(
                file.Path,
                RemoveDesignPreviewContent(file.Text),
                IsResourceDictionary: true)));
        var originalIndex = files
            .Select(static (file, index) => (file.Path, Index: index))
            .ToDictionary(static item => item.Path, static item => item.Index, StringComparer.OrdinalIgnoreCase);
        var ownersByKey = analysis.Resources
            .GroupBy(static resource => resource.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(static resource => resource.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(originalIndex.ContainsKey)
                    .OrderBy(path => originalIndex[path])
                    .ToArray(),
                StringComparer.Ordinal);
        var dependenciesByPath = files.ToDictionary(
            static file => file.Path,
            static _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var reference in analysis.References)
        {
            if (!dependenciesByPath.TryGetValue(reference.FilePath, out var dependencies) ||
                !ownersByKey.TryGetValue(reference.Key, out var owners))
            {
                continue;
            }

            foreach (var owner in owners)
            {
                if (!string.Equals(owner, reference.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    dependencies.Add(owner);
                }
            }
        }

        var ordered = new List<(string Path, string Text)>();
        var stateByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            Visit(file.Path);
        }

        return ordered;

        void Visit(string path)
        {
            if (!originalIndex.ContainsKey(path))
            {
                return;
            }

            if (stateByPath.TryGetValue(path, out var state))
            {
                if (state == 2)
                {
                    return;
                }

                return;
            }

            stateByPath[path] = 1;
            foreach (var dependency in dependenciesByPath[path].OrderBy(dependency => originalIndex[dependency]))
            {
                Visit(dependency);
            }

            stateByPath[path] = 2;
            ordered.Add(files[originalIndex[path]]);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML and dynamically compiled user code.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Fallback root types come from dynamically compiled user assemblies and are not statically trim-analyzable.")]
    public static Control? LoadControl(
        string xaml,
        Assembly? localAssembly,
        string? fallbackRootTypeName,
        string documentName,
        ICollection<RuntimeXamlDiagnostic> diagnostics,
        IEnumerable<(string Path, string Text)>? resourceFiles = null,
        string? documentAssemblyName = null,
        bool usePreviewRootForXClass = false)
    {
        var hasXamlClass = HasXamlClass(xaml);
        var previewDocumentXaml = usePreviewRootForXClass && hasXamlClass
            ? RemoveXamlClass(xaml)
            : xaml;
        object? rootInstance = null;

        if (localAssembly is { } && !hasXamlClass)
        {
            var rootType = ResolveFallbackRootType(localAssembly, fallbackRootTypeName);
            if (rootType is { })
            {
                rootInstance = Activator.CreateInstance(rootType);
            }
        }

        var document = CreateDocument(previewDocumentXaml, documentName, localAssembly, documentAssemblyName, rootInstance);

        var configuration = CreateConfiguration(localAssembly, diagnostics);
        using var contextualReflection = EnterContextualReflection(localAssembly);
        Control? control;
        if (resourceFiles is not null)
        {
            var documents = CreateResourceDocuments(resourceFiles, localAssembly, documentAssemblyName, document.Document);
            if (documents.Count > 0)
            {
                documents.Add(document);
                control = AvaloniaRuntimeXamlLoader.LoadGroup(documents, configuration).LastOrDefault() as Control;
                ApplyPreviewDataContext(control, previewDocumentXaml, localAssembly, usePreviewRootForXClass && hasXamlClass);
                return control;
            }
        }

        control = AvaloniaRuntimeXamlLoader.Load(document, configuration) as Control;
        ApplyPreviewDataContext(control, previewDocumentXaml, localAssembly, usePreviewRootForXClass && hasXamlClass);
        return control;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML and dynamically compiled user code.")]
    public static Control? LoadResourceDictionaryPreview(
        string xaml,
        Assembly? localAssembly,
        string documentName,
        ICollection<RuntimeXamlDiagnostic> diagnostics,
        IEnumerable<(string Path, string Text)>? resourceFiles = null,
        string? documentAssemblyName = null)
    {
        var previewContent = ExtractDesignPreviewContent(xaml);
        var previewXaml = string.IsNullOrWhiteSpace(previewContent)
            ? CreateEmptyResourcePreview(documentName)
            : WrapPreviewContent(previewContent);

        return LoadControl(
            previewXaml,
            localAssembly,
            fallbackRootTypeName: null,
            documentName: CreatePreviewDocumentName(documentName),
            diagnostics: diagnostics,
            resourceFiles: resourceFiles,
            documentAssemblyName: documentAssemblyName);
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

    private static IDisposable? EnterContextualReflection(Assembly? localAssembly)
    {
        if (localAssembly is null)
        {
            return null;
        }

        var context = AssemblyLoadContext.GetLoadContext(localAssembly);
        return context is null || ReferenceEquals(context, AssemblyLoadContext.Default)
            ? null
            : context.EnterContextualReflection();
    }

    private static List<RuntimeXamlLoaderDocument> CreateResourceDocuments(
        IEnumerable<(string Path, string Text)> resourceFiles,
        Assembly? localAssembly,
        string? documentAssemblyName,
        string? excludeDocumentName = null)
    {
        var excludePath = string.IsNullOrWhiteSpace(excludeDocumentName)
            ? null
            : NormalizeDocumentPath(excludeDocumentName);
        return OrderResourceFilesForLoading(resourceFiles)
            .Where(static file => !string.IsNullOrWhiteSpace(file.Text))
            .Where(file => excludePath is null ||
                           !string.Equals(NormalizeDocumentPath(file.Path), excludePath, StringComparison.OrdinalIgnoreCase))
            .Select(file => CreateDocument(
                RemoveDesignPreviewContent(file.Text),
                file.Path,
                localAssembly,
                documentAssemblyName))
            .ToList();
    }

    private static RuntimeXamlLoaderDocument CreateDocument(
        string xaml,
        string documentName,
        Assembly? localAssembly,
        string? documentAssemblyName,
        object? rootInstance = null)
    {
        var normalizedDocumentName = NormalizeDocumentPath(documentName);
        var document = new RuntimeXamlLoaderDocument(
            CreateDocumentUri(normalizedDocumentName, localAssembly, documentAssemblyName),
            rootInstance,
            xaml)
        {
            Document = normalizedDocumentName
        };
        return document;
    }

    private static Uri CreateDocumentUri(
        string documentName,
        Assembly? localAssembly,
        string? documentAssemblyName)
    {
        var assemblyName = string.IsNullOrWhiteSpace(documentAssemblyName)
            ? localAssembly?.GetName().Name
            : documentAssemblyName;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            assemblyName = PreviewAssemblyName;
        }

        return new Uri(
            $"avares://{Uri.EscapeDataString(assemblyName)}/{EscapeDocumentPath(documentName)}",
            UriKind.Absolute);
    }

    private static string NormalizeDocumentPath(string documentName)
    {
        var path = documentName.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(path) ? "Main.axaml" : path;
    }

    private static string EscapeDocumentPath(string documentName)
    {
        return string.Join(
            "/",
            NormalizeDocumentPath(documentName)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
    }

    private static string CreatePreviewDocumentName(string documentName)
    {
        var path = NormalizeDocumentPath(documentName);
        var separator = path.LastIndexOf('/');
        var folder = separator >= 0 ? path[..(separator + 1)] : string.Empty;
        var name = separator >= 0 ? path[(separator + 1)..] : path;
        return folder + name + ".preview.axaml";
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

    private static string RemoveXamlClass(string xaml)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            document.Root?
                .Attribute(XName.Get("Class", XamlNamespace))
                ?.Remove();
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return xaml;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Preview data contexts are discovered from dynamically loaded workspace assemblies.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Preview data contexts are created from dynamically loaded workspace assemblies.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Preview data contexts are created from dynamically loaded workspace assemblies.")]
    private static void ApplyPreviewDataContext(
        Control? control,
        string xaml,
        Assembly? localAssembly,
        bool enabled)
    {
        if (!enabled ||
            control is null ||
            control.DataContext is not null ||
            localAssembly is null)
        {
            return;
        }

        var dataContextType = ResolveRootDataType(xaml, localAssembly);
        if (dataContextType is null ||
            dataContextType.IsAbstract ||
            dataContextType.GetConstructor(Type.EmptyTypes) is null)
        {
            return;
        }

        try
        {
            control.DataContext = Activator.CreateInstance(dataContextType);
        }
        catch
        {
            // Design data is best-effort; preview loading should not fail because a view model constructor failed.
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Preview data context types are discovered from dynamically loaded workspace assemblies.")]
    private static Type? ResolveRootDataType(string xaml, Assembly localAssembly)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.None);
            var root = document.Root;
            var dataTypeValue = root?
                .Attribute(XName.Get("DataType", XamlNamespace))
                ?.Value;
            if (root is null || string.IsNullOrWhiteSpace(dataTypeValue))
            {
                return null;
            }

            dataTypeValue = NormalizeDataTypeValue(dataTypeValue);
            var separator = dataTypeValue.IndexOf(':');
            if (separator <= 0 || separator == dataTypeValue.Length - 1)
            {
                return null;
            }

            var prefix = dataTypeValue[..separator];
            var typeName = dataTypeValue[(separator + 1)..];
            var xamlNamespace = root.GetNamespaceOfPrefix(prefix);
            if (xamlNamespace is null)
            {
                return null;
            }

            var namespaceUri = xamlNamespace.NamespaceName;
            if (!TryParseClrNamespace(namespaceUri, out var clrNamespace, out var assemblyName))
            {
                return null;
            }

            var assembly = ResolveAssembly(localAssembly, assemblyName);
            return assembly?.GetType(clrNamespace + "." + typeName, throwOnError: false, ignoreCase: false);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDataTypeValue(string value)
    {
        value = value.Trim();
        const string xTypePrefix = "{x:Type ";
        if (value.StartsWith(xTypePrefix, StringComparison.Ordinal) &&
            value.EndsWith("}", StringComparison.Ordinal))
        {
            return value[xTypePrefix.Length..^1].Trim();
        }

        return value;
    }

    private static bool TryParseClrNamespace(
        string namespaceUri,
        [NotNullWhen(true)] out string? clrNamespace,
        out string? assemblyName)
    {
        clrNamespace = null;
        assemblyName = null;
        foreach (var part in namespaceUri.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                clrNamespace = part["clr-namespace:".Length..];
            }
            else if (part.StartsWith("assembly=", StringComparison.Ordinal))
            {
                assemblyName = part["assembly=".Length..];
            }
        }

        return !string.IsNullOrWhiteSpace(clrNamespace);
    }

    private static Assembly? ResolveAssembly(Assembly localAssembly, string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            string.Equals(localAssembly.GetName().Name, assemblyName, StringComparison.Ordinal))
        {
            return localAssembly;
        }

        var context = AssemblyLoadContext.GetLoadContext(localAssembly);
        var assembly = (context?.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
            .FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
        if (assembly is { })
        {
            return assembly;
        }

        try
        {
            return context?.LoadFromAssemblyName(new AssemblyName(assemblyName))
                   ?? AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
        }
        catch
        {
            return null;
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
                    attribute.Name.NamespaceName == XamlNamespace) == true;
        }
        catch
        {
            return false;
        }
    }
}
