using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace XamlPlayground.Services;

public static class CompilerService
{
    private static PortableExecutableReference[]? s_references;
    private static Task? s_referenceLoadTask;
    private static IReadOnlyDictionary<string, string> s_browserReferenceAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static string? BaseUri { get; set; }

    public static void SetBrowserReferenceAssets(string? assets)
    {
        s_references = null;
        s_referenceLoadTask = null;

        if (string.IsNullOrWhiteSpace(assets))
        {
            s_browserReferenceAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var browserReferenceAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = asset.Split('|', 2, StringSplitOptions.TrimEntries);
            var virtualPath = parts[0];
            var name = parts.Length == 2 ? parts[1] : parts[0];
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var assemblyName = GetAssemblyName(virtualPath);
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                browserReferenceAssets[assemblyName] = name;
            }
        }

        s_browserReferenceAssets = browserReferenceAssets;
    }

    public static async Task<IReadOnlyList<PortableExecutableReference>> GetMetadataReferences()
    {
        if (s_references is null)
        {
            s_referenceLoadTask ??= LoadReferences();
            await s_referenceLoadTask;
        }

        return s_references ?? Array.Empty<PortableExecutableReference>();
    }

    private static async Task LoadReferences()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        if (Utilities.IsBrowser())
        {
            if (BaseUri is null)
            {
                return;
            }
            
            var appDomainReferences = new List<PortableExecutableReference>();
            var client = new HttpClient 
            {
                // BaseAddress = new Uri(BaseUri)
            };

            Console.WriteLine($"Loading references BaseUri: {BaseUri}");

            foreach(var name in assemblies
                        .Where(static assembly => !assembly.IsDynamic)
                        .Select(static assembly => assembly.GetName().Name)
                        .Where(static name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var metadataReference = await LoadBrowserReference(client, name);
                    if (metadataReference is null)
                    {
                        continue;
                    }

                    appDomainReferences.Add(metadataReference);
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }
            }

            s_references = appDomainReferences.ToArray();
            Console.WriteLine($"Loaded browser references: {s_references.Length}");
        }
        else
        {
            var appDomainReferences = new List<PortableExecutableReference>();

            foreach(var reference in assemblies.Where(x => !x.IsDynamic && !string.IsNullOrWhiteSpace(x.Location)))
            {
                appDomainReferences.Add(MetadataReference.CreateFromFile(reference.Location));
            }

            s_references = appDomainReferences.ToArray();
        }
    }

    private static async Task<PortableExecutableReference?> LoadBrowserReference(HttpClient client, string? name)
    {
        if (BaseUri is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        Exception? lastException = null;
        foreach (var requestUri in ResolveBrowserReferenceUris(name))
        {
            try
            {
                var bytes = await client.GetByteArrayAsync(requestUri);
                return MetadataReference.CreateFromImage(bytes);
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        if (lastException is { })
        {
            Console.WriteLine($"Failed to load browser reference '{name}': {lastException.Message}");
        }

        return null;
    }

    private static IEnumerable<string> ResolveBrowserReferenceUris(string name)
    {
        if (s_browserReferenceAssets.TryGetValue(name, out var asset))
        {
            yield return ResolveBrowserReferenceUri(asset);
            yield break;
        }

        if (s_browserReferenceAssets.Count > 0)
        {
            yield break;
        }

        yield return ResolveBrowserReferenceUri($"_framework/{name}.dll");
        yield return ResolveBrowserReferenceUri($"managed/{name}.dll");
    }

    private static string GetAssemblyName(string virtualPath)
    {
        var normalized = virtualPath.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        var name = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".dll".Length]
            : fileName;

        var hashSeparator = name.LastIndexOf('.');
        return hashSeparator > 0 && IsFingerprintSuffix(name[(hashSeparator + 1)..])
            ? name[..hashSeparator]
            : name;
    }

    private static bool IsFingerprintSuffix(string value)
    {
        if (value.Length != 10)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterLower(ch) && !char.IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveBrowserReferenceUri(string asset)
    {
        var normalized = asset.Replace('\\', '/');
        var relativePath = normalized.Contains('/')
            ? normalized
            : $"_framework/{normalized}";

        return new Uri(new Uri(BaseUri!, UriKind.Absolute), relativePath).ToString();
    }

    public static async Task<ScriptCompilationResult> GetScriptAssembly(string code)
    {
        return await GetProjectAssembly(
            Path.GetRandomFileName(),
            new[] { ("Script.cs", code) });
    }

    public static async Task<ScriptCompilationResult> GetProjectAssembly(
        string assemblyName,
        IEnumerable<(string Path, string Text)> codeFiles)
    {
        var references = await GetMetadataReferences();

        var parsedSyntaxTrees = codeFiles
            .Where(static file => !string.IsNullOrWhiteSpace(file.Text))
            .Select(static file => SyntaxFactory.ParseSyntaxTree(
                SourceText.From(file.Text, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                file.Path))
            .ToArray();

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release);
        var compilation = CSharpCompilation.Create(
            string.IsNullOrWhiteSpace(assemblyName) ? Path.GetRandomFileName() : assemblyName,
            parsedSyntaxTrees,
            references,
            compilationOptions);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        var errors = result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error);
        if (!result.Success)
        {
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }

            return new ScriptCompilationResult(null, null, result.Diagnostics);
        }

        ms.Seek(0, SeekOrigin.Begin);

        var context = new AssemblyLoadContext(name: Path.GetRandomFileName(), isCollectible: true);
        var assembly = context.LoadFromStream(ms);

        return new ScriptCompilationResult(assembly, context, result.Diagnostics);
    }
}

public sealed record ScriptCompilationResult(
    Assembly? Assembly,
    AssemblyLoadContext? Context,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Assembly is not null;
}
