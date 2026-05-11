using Avalonia.Platform.Storage;

namespace XamlPlayground.Services;

internal static class StorageService
{
    public static FilePickerFileType All { get; } = new("All")
    {
        Patterns = new[] { "*.*" },
        MimeTypes = new[] { "*/*" }
    };

    public static FilePickerFileType Xaml { get; } = new("Xaml")
    {
        Patterns = new[] { "*.xaml" },
        AppleUniformTypeIdentifiers = new[] { "public.xaml" },
        MimeTypes = new[] { "application/xaml" }
    };

    public static FilePickerFileType Axaml { get; } = new("Axaml")
    {
        Patterns = new[] { "*.axaml" },
        AppleUniformTypeIdentifiers = new[] { "public.axaml" },
        MimeTypes = new[] { "application/axaml" }
    };

    public static FilePickerFileType ThemeProject { get; } = new("XamlPlayground Theme Project")
    {
        Patterns = new[] { "*.xamltheme" },
        AppleUniformTypeIdentifiers = new[] { "com.xamlplayground.theme-project" },
        MimeTypes = new[] { "application/json" }
    };

    public static FilePickerFileType Json { get; } = new("Json")
    {
        Patterns = new[] { "*.json" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    public static FilePickerFileType CSharp { get; } = new("C#")
    {
        Patterns = new[] { "*.cs" },
        AppleUniformTypeIdentifiers = new[] { "public.csharp-source" },
        MimeTypes = new[] { "text/plain" }
    };
}
