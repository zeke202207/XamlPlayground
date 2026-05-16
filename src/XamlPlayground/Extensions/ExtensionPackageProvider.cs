using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace XamlPlayground.Extensions;

public sealed class ExtensionPackageProvider : IExtensionProvider
{
    private readonly string _rootDirectory;
    private readonly bool _loadTrustedAssemblies;
    private readonly List<ExtensionPackageLoadError> _loadErrors = new();

    public ExtensionPackageProvider(string rootDirectory, bool loadTrustedAssemblies = false)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("The extension root directory cannot be empty.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
        _loadTrustedAssemblies = loadTrustedAssemblies;
    }

    public IReadOnlyList<ExtensionPackageLoadError> LoadErrors => _loadErrors;

    public IEnumerable<ExtensionDescriptor> GetExtensions()
    {
        _loadErrors.Clear();

        if (!Directory.Exists(_rootDirectory))
        {
            yield break;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(
                     _rootDirectory,
                     ExtensionManifestReader.ManifestFileName,
                     SearchOption.AllDirectories)
                 .OrderBy(static path => path, StringComparer.Ordinal))
        {
            ExtensionDescriptor? descriptor = null;
            try
            {
                var manifest = ExtensionManifestReader.ReadFile(manifestPath);
                var packageDirectory = Path.GetDirectoryName(manifestPath) ?? _rootDirectory;
                descriptor = new ExtensionDescriptor(
                    manifest,
                    CreateFactory(packageDirectory, manifest),
                    isBuiltIn: false);
            }
            catch (Exception exception)
            {
                _loadErrors.Add(new ExtensionPackageLoadError(manifestPath, exception.Message));
            }

            if (descriptor is not null)
            {
                yield return descriptor;
            }
        }
    }

    private Func<IXamlPlaygroundExtension>? CreateFactory(string packageDirectory, ExtensionManifest manifest)
    {
        if (!_loadTrustedAssemblies ||
            !manifest.Metadata.TryGetValue("main", out var main) ||
            string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        return () => CreateExtensionInstance(packageDirectory, main);
    }

    private static IXamlPlaygroundExtension CreateExtensionInstance(string packageDirectory, string main)
    {
        var assemblyPath = Path.GetFullPath(Path.Combine(packageDirectory, main));
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The extension assembly was not found.", assemblyPath);
        }

        return CreateExtensionInstanceWithReflection(assemblyPath);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Trusted extension loading is explicitly runtime-discovery based and disabled unless opted in by the host.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Trusted extension loading requires public parameterless constructors discovered at runtime.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Trusted extension loading requires public parameterless constructors discovered at runtime.")]
    private static IXamlPlaygroundExtension CreateExtensionInstanceWithReflection(string assemblyPath)
    {
        var loadContext = new ExtensionAssemblyLoadContext(assemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var extensionType = assembly
                .GetTypes()
                .FirstOrDefault(static type =>
                    !type.IsAbstract &&
                    type.GetConstructor(Type.EmptyTypes) is not null &&
                    typeof(IXamlPlaygroundExtension).IsAssignableFrom(type));

            if (extensionType is null)
            {
                throw new InvalidOperationException(
                    "The extension assembly '" + assemblyPath + "' does not contain a public parameterless IXamlPlaygroundExtension implementation.");
            }

            var extension = (IXamlPlaygroundExtension)Activator.CreateInstance(extensionType)!;
            return new LoadedExtensionAssembly(extension, loadContext);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private sealed class ExtensionAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ExtensionAssemblyLoadContext(string mainAssemblyPath)
            : base("XamlPlayground.Extension." + Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "Trusted extension dependency loading is runtime-discovery based and only used when the host opts in to trusted assemblies.")]
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            if (sharedAssembly is not null)
            {
                return sharedAssembly;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath is null ? 0 : LoadUnmanagedDllFromPath(libraryPath);
        }
    }

    private sealed class LoadedExtensionAssembly : IXamlPlaygroundExtension, IAsyncDisposable
    {
        private readonly IXamlPlaygroundExtension _extension;
        private readonly AssemblyLoadContext _loadContext;
        private bool _disposed;

        public LoadedExtensionAssembly(IXamlPlaygroundExtension extension, AssemblyLoadContext loadContext)
        {
            _extension = extension;
            _loadContext = loadContext;
        }

        public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
        {
            return _extension.ActivateAsync(context, cancellationToken);
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            return _extension.DeactivateAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            switch (_extension)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }

            _loadContext.Unload();
        }
    }

}

public sealed record ExtensionPackageLoadError(string ManifestPath, string Message);
