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

    public static FilePickerFileType SolutionProject { get; } = new("XamlPlayground Solution")
    {
        Patterns = new[] { "*.xamlsln" },
        AppleUniformTypeIdentifiers = new[] { "com.xamlplayground.solution" },
        MimeTypes = new[] { "application/json" }
    };

    public static FilePickerFileType VisualStudioSolution { get; } = new("Visual Studio Solution")
    {
        Patterns = new[] { "*.sln" },
        AppleUniformTypeIdentifiers = new[] { "com.microsoft.visual-studio-solution" },
        MimeTypes = new[] { "text/plain" }
    };

    public static FilePickerFileType VisualStudioXmlSolution { get; } = new("Visual Studio XML Solution")
    {
        Patterns = new[] { "*.slnx" },
        AppleUniformTypeIdentifiers = new[] { "com.microsoft.visual-studio-xml-solution" },
        MimeTypes = new[] { "application/xml", "text/xml" }
    };

    public static FilePickerFileType MSBuildProject { get; } = new("MSBuild Project")
    {
        Patterns = new[] { "*.csproj", "*.fsproj", "*.vbproj" },
        AppleUniformTypeIdentifiers = new[] { "public.xml" },
        MimeTypes = new[] { "application/xml", "text/xml" }
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
