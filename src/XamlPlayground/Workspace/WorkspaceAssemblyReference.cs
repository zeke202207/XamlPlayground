using System;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace XamlPlayground.Workspace;

public sealed class WorkspaceAssemblyReference
{
    private WorkspaceAssemblyReference(
        string name,
        string? filePath,
        byte[]? image,
        bool isReferenceAssembly,
        bool isRuntimeAssembly)
    {
        Name = name;
        FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        Image = image;
        IsReferenceAssembly = isReferenceAssembly;
        IsRuntimeAssembly = isRuntimeAssembly;
    }

    public string Name { get; }

    public string? FilePath { get; }

    public byte[]? Image { get; }

    public bool IsReferenceAssembly { get; }

    public bool IsRuntimeAssembly { get; }

    public static WorkspaceAssemblyReference? FromPath(string? filePath, bool isRuntimeAssembly)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            !File.Exists(filePath) ||
            !IsAssemblyFile(filePath) ||
            !IsManagedAssembly(filePath))
        {
            return null;
        }

        string? name;
        try
        {
            name = AssemblyName.GetAssemblyName(filePath).Name;
        }
        catch
        {
            return null;
        }

        var isReferenceAssembly = IsReferenceAssemblyPath(filePath);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new WorkspaceAssemblyReference(
                name,
                filePath,
                image: null,
                isReferenceAssembly,
                isRuntimeAssembly: isRuntimeAssembly && !isReferenceAssembly);
    }

    public static WorkspaceAssemblyReference? FromImage(string fileName, byte[] image, bool isRuntimeAssembly)
    {
        if (image.Length == 0 || !IsAssemblyFile(fileName) || !IsManagedAssembly(image))
        {
            return null;
        }

        var isReferenceAssembly = IsReferenceAssemblyPath(fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new WorkspaceAssemblyReference(
                name,
                fileName,
                image,
                isReferenceAssembly,
                isRuntimeAssembly: isRuntimeAssembly && !isReferenceAssembly);
    }

    public PortableExecutableReference? CreateMetadataReference()
    {
        if (Image is { Length: > 0 } image)
        {
            return MetadataReference.CreateFromImage(image);
        }

        return FilePath is { } filePath && File.Exists(filePath)
            ? MetadataReference.CreateFromFile(filePath)
            : null;
    }

    public Assembly? LoadAssembly(AssemblyLoadContext context)
    {
        if (!IsRuntimeAssembly)
        {
            return null;
        }

        if (FindLoadedAssembly(context, Name) is { } loaded)
        {
            return loaded;
        }

        try
        {
            if (Image is { Length: > 0 } image)
            {
                using var stream = new MemoryStream(image, writable: false);
                return context.LoadFromStream(stream);
            }

            return FilePath is { } filePath && File.Exists(filePath)
                ? context.LoadFromAssemblyPath(filePath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsReferenceAssemblyPath(string assemblyPath)
    {
        var normalized = assemblyPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var marker = Path.DirectorySeparatorChar.ToString();
        return normalized.Contains(marker + "ref" + marker, StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(marker + "refint" + marker, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAssemblyFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManagedAssembly(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var reader = new PEReader(stream);
            return reader.HasMetadata && reader.PEHeaders?.CorHeader is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsManagedAssembly(byte[] image)
    {
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var reader = new PEReader(stream);
            return reader.HasMetadata && reader.PEHeaders?.CorHeader is not null;
        }
        catch
        {
            return false;
        }
    }

    private static Assembly? FindLoadedAssembly(AssemblyLoadContext context, string assemblyName)
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
}
