using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XamlPlayground.Services;

public static class RuntimeXamlPreviewLoader
{
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

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            CreateSourceInfo = true,
            DiagnosticHandler = diagnostic =>
            {
                diagnostics.Add(diagnostic);
                return diagnostic.Severity;
            }
        };

        return AvaloniaRuntimeXamlLoader.Load(document, configuration) as Control;
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
