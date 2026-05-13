using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace XamlPlayground.Workspace;

public sealed class WorkspaceAssemblyLoadContext : AssemblyLoadContext
{
    private readonly IReadOnlyDictionary<string, WorkspaceAssemblyReference> _referencesByName;
    private readonly HashSet<string> _privateAssemblyNames;
    private readonly AssemblyDependencyResolver? _dependencyResolver;
    private readonly string? _outputDirectory;

    public WorkspaceAssemblyLoadContext(
        string name,
        IEnumerable<WorkspaceAssemblyReference> references,
        string? outputDirectory = null,
        IEnumerable<string>? privateAssemblyNames = null,
        string? mainAssemblyPath = null)
        : base(name, isCollectible: true)
    {
        _referencesByName = references
            .Where(static reference => reference.IsRuntimeAssembly)
            .GroupBy(static reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        _outputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory;
        _privateAssemblyNames = privateAssemblyNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : privateAssemblyNames
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _dependencyResolver = !string.IsNullOrWhiteSpace(mainAssemblyPath) && File.Exists(mainAssemblyPath)
            ? new AssemblyDependencyResolver(mainAssemblyPath)
            : null;
    }

    public IReadOnlyList<Assembly> LoadRuntimeAssemblies(string? skipAssemblyName = null)
    {
        var assemblies = new List<Assembly>();
        foreach (var reference in _referencesByName.Values)
        {
            if (!string.IsNullOrWhiteSpace(skipAssemblyName) &&
                string.Equals(reference.Name, skipAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (LoadAssemblyReference(reference) is { } assembly)
            {
                assemblies.Add(assembly);
            }
        }

        return assemblies;
    }

    public Assembly? LoadAssemblyReference(WorkspaceAssemblyReference reference)
    {
        if (!reference.IsRuntimeAssembly)
        {
            return null;
        }

        if (TryFindLoadedAssembly(this, reference.Name) is { } loaded)
        {
            return loaded;
        }

        if (TryResolveSharedDefaultAssembly(reference.Name) is { } shared)
        {
            return shared;
        }

        return reference.LoadAssembly(this);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return ResolveAssembly(assemblyName);
    }

    private Assembly? ResolveAssembly(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (TryFindLoadedAssembly(this, name) is { } loaded)
        {
            return loaded;
        }

        if (TryResolveSharedDefaultAssembly(name) is { } shared)
        {
            return shared;
        }

        if (_referencesByName.TryGetValue(name, out var reference) &&
            reference.LoadAssembly(this) is { } referencedAssembly)
        {
            return referencedAssembly;
        }

        if (TryResolveFromDependencyResolver(assemblyName) is { } resolvedAssembly)
        {
            return resolvedAssembly;
        }

        return TryLoadFromOutputDirectory(name);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _dependencyResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is { } && File.Exists(path)
            ? LoadUnmanagedDllFromPath(path)
            : IntPtr.Zero;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads workspace assemblies selected by the user.")]
    private Assembly? TryResolveFromDependencyResolver(AssemblyName assemblyName)
    {
        var path = _dependencyResolver?.ResolveAssemblyToPath(assemblyName);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return LoadFromAssemblyPath(path);
        }
        catch
        {
            return null;
        }
    }

    private Assembly? TryResolveSharedDefaultAssembly(string name)
    {
        if (_privateAssemblyNames.Contains(name) ||
            !IsSharedDefaultAssemblyName(name))
        {
            return null;
        }

        return TryFindLoadedAssembly(Default, name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The playground runtime preview intentionally loads workspace assemblies selected by the user.")]
    private Assembly? TryLoadFromOutputDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            return null;
        }

        foreach (var extension in new[] { ".dll", ".exe" })
        {
            var candidate = Path.Combine(_outputDirectory, name + extension);
            if (!File.Exists(candidate) ||
                !WorkspaceAssemblyReference.IsAssemblyFile(candidate))
            {
                continue;
            }

            try
            {
                return LoadFromAssemblyPath(candidate);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static Assembly? TryFindLoadedAssembly(AssemblyLoadContext context, string assemblyName)
    {
        foreach (var assembly in context.Assemblies)
        {
            if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        return null;
    }

    private static bool IsSharedDefaultAssemblyName(string name)
    {
        // Keep the host visual tree and diagnostics type identities shared while allowing
        // package-style Avalonia assemblies to remain private workspace dependencies.
        return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" or
                   "Microsoft.VisualBasic" or "SkiaSharp" or "HarfBuzzSharp" or "XamlX" or
                   "Avalonia.Base" or "Avalonia.Controls" or "Avalonia.Diagnostics" or
                   "Avalonia.Markup" ||
               name.StartsWith("System.", StringComparison.Ordinal) ||
               name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal) ||
               name.StartsWith("XamlX.", StringComparison.Ordinal);
    }
}
