using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
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
            foreach (var resourceProvider in LoadRuntimeXamlGroup(documents, configuration, localAssembly)
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
        string? documentAssemblyName = null)
    {
        var hasXamlClass = HasXamlClass(xaml);
        object? rootInstance = null;

        if (localAssembly is { } && !hasXamlClass)
        {
            var rootType = ResolveFallbackRootType(localAssembly, fallbackRootTypeName);
            if (rootType is { })
            {
                rootInstance = Activator.CreateInstance(rootType);
            }
        }

        var document = CreateDocument(xaml, documentName, localAssembly, documentAssemblyName, rootInstance);
        var configuration = CreateConfiguration(localAssembly, diagnostics);
        using var contextualReflection = EnterContextualReflection(localAssembly);
        Control? control;
        if (resourceFiles is not null)
        {
            var documents = CreateResourceDocuments(resourceFiles, localAssembly, documentAssemblyName, document.Document);
            if (documents.Count > 0)
            {
                documents.Add(document);
                control = LoadRuntimeXamlGroup(documents, configuration, localAssembly).LastOrDefault() as Control;
                ApplyPreviewDataContext(control, xaml, localAssembly, !hasXamlClass);
                return control;
            }
        }

        control = LoadRuntimeXaml(document, configuration, localAssembly) as Control;
        ApplyPreviewDataContext(control, xaml, localAssembly, !hasXamlClass);
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
            DesignMode = true,
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML in workspace assembly contexts.")]
    private static object? LoadRuntimeXaml(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        Assembly? localAssembly)
    {
        if (TryResolveWorkspaceRuntimeXamlAssembly(localAssembly, out var runtimeAssembly))
        {
            return InvokeWorkspaceRuntimeXamlLoad(runtimeAssembly, document, configuration, localAssembly);
        }

        return AvaloniaRuntimeXamlLoader.Load(document, configuration);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads user XAML in workspace assembly contexts.")]
    private static IReadOnlyList<object?> LoadRuntimeXamlGroup(
        IReadOnlyList<RuntimeXamlLoaderDocument> documents,
        RuntimeXamlLoaderConfiguration configuration,
        Assembly? localAssembly)
    {
        if (TryResolveWorkspaceRuntimeXamlAssembly(localAssembly, out var runtimeAssembly))
        {
            return InvokeWorkspaceRuntimeXamlLoadGroup(runtimeAssembly, documents, configuration, localAssembly);
        }

        return AvaloniaRuntimeXamlLoader.LoadGroup(documents, configuration);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Workspace runtime XAML assemblies are discovered dynamically for user-selected projects.")]
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Workspace assemblies are loaded from an external build output directory.")]
    private static bool TryResolveWorkspaceRuntimeXamlAssembly(
        Assembly? localAssembly,
        [NotNullWhen(true)] out Assembly? runtimeAssembly)
    {
        runtimeAssembly = null;
        if (localAssembly is null)
        {
            return false;
        }

        var context = AssemblyLoadContext.GetLoadContext(localAssembly);
        if (context is null || ReferenceEquals(context, AssemblyLoadContext.Default))
        {
            return false;
        }

        var hostRuntimeAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
        foreach (var assembly in context.Assemblies)
        {
            if (ReferenceEquals(assembly, hostRuntimeAssembly))
            {
                continue;
            }

            if (assembly.GetType("Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader", throwOnError: false) is { })
            {
                runtimeAssembly = assembly;
                return true;
            }
        }

        var outputDirectory = string.IsNullOrWhiteSpace(localAssembly.Location)
            ? null
            : Path.GetDirectoryName(localAssembly.Location);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return false;
        }

        foreach (var assemblyName in new[] { "Avalonia.Markup.Xaml", "Avalonia.Markup.Xaml.Loader" })
        {
            var path = Path.Combine(outputDirectory, assemblyName + ".dll");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var assembly = context.LoadFromAssemblyPath(path);
                if (!ReferenceEquals(assembly, hostRuntimeAssembly) &&
                    assembly.GetType("Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader", throwOnError: false) is { })
                {
                    runtimeAssembly = assembly;
                    return true;
                }
            }
            catch
            {
                // If the assembly is already loaded or incompatible, fall back to the host loader.
            }
        }

        if (!string.IsNullOrWhiteSpace(hostRuntimeAssembly.Location) &&
            File.Exists(hostRuntimeAssembly.Location))
        {
            try
            {
                var assembly = context.LoadFromAssemblyPath(hostRuntimeAssembly.Location);
                if (!ReferenceEquals(assembly, hostRuntimeAssembly) &&
                    assembly.GetType("Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader", throwOnError: false) is { })
                {
                    runtimeAssembly = assembly;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Workspace runtime XAML parameter types are discovered dynamically from the selected loader method.")]
    private static object? InvokeWorkspaceRuntimeXamlLoad(
        Assembly runtimeAssembly,
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        Assembly? localAssembly)
    {
        PrepareWorkspaceRuntimeCompiler(runtimeAssembly, localAssembly);
        var loaderType = ResolveRuntimeType(runtimeAssembly, "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader");
        var loadMethod = FindRuntimeLoaderMethod(loaderType, "Load");
        var parameters = loadMethod.GetParameters();
        var privateDocument = CreateWorkspaceRuntimeDocument(parameters[0].ParameterType, document);
        var privateConfiguration = CreateWorkspaceRuntimeConfiguration(parameters[1].ParameterType, configuration, localAssembly);
        return loadMethod.Invoke(null, new[] { privateDocument, privateConfiguration });
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Workspace runtime XAML loading uses reflection over user-selected assembly contexts.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Workspace runtime XAML parameter types are discovered dynamically from the selected loader method.")]
    private static IReadOnlyList<object?> InvokeWorkspaceRuntimeXamlLoadGroup(
        Assembly runtimeAssembly,
        IReadOnlyList<RuntimeXamlLoaderDocument> documents,
        RuntimeXamlLoaderConfiguration configuration,
        Assembly? localAssembly)
    {
        PrepareWorkspaceRuntimeCompiler(runtimeAssembly, localAssembly);
        var loaderType = ResolveRuntimeType(runtimeAssembly, "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader");
        var loadMethod = FindRuntimeLoaderMethod(loaderType, "LoadGroup");
        var parameters = loadMethod.GetParameters();
        var documentType = parameters[0].ParameterType.GetGenericArguments().FirstOrDefault()
            ?? ResolveRuntimeType(runtimeAssembly, "Avalonia.Markup.Xaml.RuntimeXamlLoaderDocument");
        var privateDocuments = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(documentType))!;
        foreach (var document in documents)
        {
            privateDocuments.Add(CreateWorkspaceRuntimeDocument(documentType, document));
        }

        var privateConfiguration = CreateWorkspaceRuntimeConfiguration(parameters[1].ParameterType, configuration, localAssembly);
        var result = loadMethod.Invoke(null, new object?[] { privateDocuments, privateConfiguration });
        return result is IEnumerable enumerable
            ? enumerable.Cast<object?>().ToArray()
            : Array.Empty<object?>();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Workspace runtime compiler internals are configured for user-selected assemblies.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Workspace runtime compiler internals are configured dynamically.")]
    private static void PrepareWorkspaceRuntimeCompiler(Assembly runtimeAssembly, Assembly? localAssembly)
    {
        if (localAssembly is null)
        {
            return;
        }

        var context = AssemblyLoadContext.GetLoadContext(localAssembly);
        if (context is null || ReferenceEquals(context, AssemblyLoadContext.Default))
        {
            return;
        }

        var compilerType = FindTypeInAssemblyContext(
            runtimeAssembly,
            "Avalonia.Markup.Xaml.XamlIl.AvaloniaXamlIlRuntimeCompiler");
        if (compilerType is null)
        {
            return;
        }

        var initializeMethod = compilerType.GetMethod(
            "InitializeSre",
            BindingFlags.Static | BindingFlags.NonPublic);
        var emitAccessMethod = compilerType.GetMethod(
            "EmitIgnoresAccessCheckToAttribute",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(AssemblyName) },
            modifiers: null);
        if (emitAccessMethod is null)
        {
            return;
        }

        try
        {
            initializeMethod?.Invoke(null, null);
        }
        catch
        {
            return;
        }

        foreach (var assembly in EnumerateWorkspaceAccessAssemblies(context, localAssembly))
        {
            try
            {
                emitAccessMethod.Invoke(null, new object[] { assembly.GetName() });
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<Assembly> EnumerateWorkspaceAccessAssemblies(
        AssemblyLoadContext context,
        Assembly localAssembly)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in context.Assemblies.Prepend(localAssembly))
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name) ||
                IsSharedRuntimeAssembly(name) ||
                !seen.Add(name))
            {
                continue;
            }

            yield return assembly;
        }
    }

    private static bool IsSharedRuntimeAssembly(string name)
    {
        return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" or "Microsoft.VisualBasic" ||
               name.StartsWith("System.", StringComparison.Ordinal) ||
               name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Workspace runtime XAML document types are discovered dynamically.")]
    private static object CreateWorkspaceRuntimeDocument(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type documentType,
        RuntimeXamlLoaderDocument document)
    {
        var xaml = ReadDocumentXaml(document);
        var runtimeDocument = Activator.CreateInstance(
            documentType,
            document.BaseUri,
            document.RootInstance,
            xaml)
            ?? throw new InvalidOperationException("Unable to create workspace runtime XAML document.");
        SetProperty(runtimeDocument, "Document", document.Document);
        return runtimeDocument;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Workspace runtime XAML configuration types are discovered dynamically.")]
    private static object CreateWorkspaceRuntimeConfiguration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type configurationType,
        RuntimeXamlLoaderConfiguration configuration,
        Assembly? localAssembly)
    {
        var runtimeConfiguration = Activator.CreateInstance(configurationType)
            ?? throw new InvalidOperationException("Unable to create workspace runtime XAML configuration.");
        SetProperty(runtimeConfiguration, "LocalAssembly", localAssembly);
        SetProperty(runtimeConfiguration, "UseCompiledBindingsByDefault", configuration.UseCompiledBindingsByDefault);
        SetProperty(runtimeConfiguration, "DesignMode", configuration.DesignMode);
        SetProperty(runtimeConfiguration, "CreateSourceInfo", configuration.CreateSourceInfo);
        return runtimeConfiguration;
    }

    private static Type ResolveRuntimeType(Assembly runtimeAssembly, string typeName)
    {
        if (FindTypeInAssemblyContext(runtimeAssembly, typeName) is { } type)
        {
            return type;
        }

        throw new InvalidOperationException($"Unable to locate {typeName} in workspace runtime XAML context.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Workspace runtime XAML types are discovered dynamically.")]
    private static Type? FindTypeInAssemblyContext(Assembly assembly, string typeName)
    {
        if (assembly.GetType(typeName, throwOnError: false) is { } type)
        {
            return type;
        }

        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context is null)
        {
            return null;
        }

        foreach (var loadedAssembly in context.Assemblies)
        {
            if (loadedAssembly.GetType(typeName, throwOnError: false) is { } loadedType)
            {
                return loadedType;
            }
        }

        foreach (var loadedAssembly in LoadRuntimeXamlSupportAssemblies(context, assembly))
        {
            if (loadedAssembly.GetType(typeName, throwOnError: false) is { } loadedType)
            {
                return loadedType;
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Workspace runtime XAML support assemblies are loaded from the selected project output.")]
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Workspace runtime XAML support assemblies are external project output files.")]
    private static IEnumerable<Assembly> LoadRuntimeXamlSupportAssemblies(
        AssemblyLoadContext context,
        Assembly runtimeAssembly)
    {
        var directory = string.IsNullOrWhiteSpace(runtimeAssembly.Location)
            ? null
            : Path.GetDirectoryName(runtimeAssembly.Location);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<Assembly>();
        }

        var assemblies = new List<Assembly>();
        foreach (var assemblyName in new[] { "Avalonia.Markup.Xaml", "Avalonia.Markup.Xaml.Loader" })
        {
            var path = Path.Combine(directory, assemblyName + ".dll");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                assemblies.Add(context.LoadFromAssemblyPath(path));
            }
            catch
            {
            }
        }

        return assemblies;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Workspace runtime XAML loader methods are discovered dynamically.")]
    private static MethodInfo FindRuntimeLoaderMethod(Type loaderType, string name)
    {
        return loaderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == name && method.GetParameters().Length == 2)
            ?? throw new InvalidOperationException($"Unable to locate AvaloniaRuntimeXamlLoader.{name} method.");
    }

    private static string ReadDocumentXaml(RuntimeXamlLoaderDocument document)
    {
        var stream = document.XamlStream;
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var xaml = reader.ReadToEnd();
        if (stream.CanSeek)
        {
            stream.Position = originalPosition;
        }

        return xaml;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Workspace runtime XAML objects are configured dynamically.")]
    private static void SetProperty(object instance, string name, object? value)
    {
        var property = instance.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is { CanWrite: true })
        {
            property.SetValue(instance, value);
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
