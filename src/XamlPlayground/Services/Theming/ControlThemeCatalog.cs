using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XamlPlayground.Services.Theming;

public sealed record FluentControlThemeTemplate(
    string Key,
    string TargetType,
    string SourcePath,
    string Xaml);

public sealed record ControlThemeDefinition(
    string Key,
    string TargetType,
    string FilePath);

public sealed class FluentControlThemeCatalog
{
    private const string AvaloniaFluentThemeRelativePath = "src/Avalonia.Themes.Fluent";
    private const string ExternalAvaloniaFluentThemeRelativePath = "external/Avalonia/src/Avalonia.Themes.Fluent";
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly object s_templateCacheLock = new();
    private static readonly Dictionary<string, IReadOnlyList<FluentControlThemeTemplate>> s_templateCache = new(StringComparer.Ordinal);

    private readonly Lazy<IReadOnlyList<FluentControlThemeTemplate>> _templates;

    public FluentControlThemeCatalog()
    {
        SourceRoot = ResolveSourceRoot();
        _templates = new Lazy<IReadOnlyList<FluentControlThemeTemplate>>(LoadTemplates);
    }

    public string? SourceRoot { get; }

    public IReadOnlyList<FluentControlThemeTemplate> Templates => _templates.Value;

    public FluentControlThemeTemplate? FindDefaultTemplate(string targetType)
    {
        var localTargetType = GetLocalName(targetType);
        return Templates.FirstOrDefault(template =>
                   string.Equals(GetLocalName(template.TargetType), localTargetType, StringComparison.Ordinal) &&
                   string.Equals(template.Key, $"{{x:Type {localTargetType}}}", StringComparison.Ordinal)) ??
               Templates.FirstOrDefault(template =>
                   string.Equals(GetLocalName(template.TargetType), localTargetType, StringComparison.Ordinal));
    }

    private IReadOnlyList<FluentControlThemeTemplate> LoadTemplates()
    {
        if (SourceRoot is null || !Directory.Exists(SourceRoot))
        {
            return Array.Empty<FluentControlThemeTemplate>();
        }

        lock (s_templateCacheLock)
        {
            if (s_templateCache.TryGetValue(SourceRoot, out var cachedTemplates))
            {
                return cachedTemplates;
            }
        }

        var templates = Directory
            .EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(LoadTemplatesFromFile)
            .ToArray();

        lock (s_templateCacheLock)
        {
            if (s_templateCache.TryGetValue(SourceRoot, out var cachedTemplates))
            {
                return cachedTemplates;
            }

            s_templateCache[SourceRoot] = templates;
        }

        return templates;
    }

    private IEnumerable<FluentControlThemeTemplate> LoadTemplatesFromFile(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var theme in document.Descendants().Where(static element => element.Name.LocalName == "ControlTheme"))
        {
            var targetType = theme.Attribute("TargetType")?.Value;
            if (string.IsNullOrWhiteSpace(targetType))
            {
                continue;
            }

            var key = theme.Attribute(XamlNamespace + "Key")?.Value ?? targetType;
            yield return new FluentControlThemeTemplate(
                key,
                targetType,
                NormalizeResourcePath(Path.GetRelativePath(SourceRoot!, path)),
                theme.ToString(SaveOptions.DisableFormatting));
        }
    }

    private static string NormalizeResourcePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string? ResolveSourceRoot()
    {
        var environmentOverride = Environment.GetEnvironmentVariable("XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH");
        if (IsThemeRoot(environmentOverride))
        {
            return environmentOverride;
        }

        foreach (var basePath in EnumerateBasePaths())
        {
            var externalCandidate = Path.GetFullPath(Path.Combine(basePath, ExternalAvaloniaFluentThemeRelativePath));
            if (IsThemeRoot(externalCandidate))
            {
                return externalCandidate;
            }

            var siblingCandidate = Path.GetFullPath(Path.Combine(basePath, "..", "Avalonia", AvaloniaFluentThemeRelativePath));
            if (IsThemeRoot(siblingCandidate))
            {
                return siblingCandidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBasePaths()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            yield return current.FullName;
        }
    }

    private static bool IsThemeRoot(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "FluentTheme.xaml")) &&
               Directory.Exists(Path.Combine(path, "Controls"));
    }

    internal static string GetLocalName(string typeName)
    {
        var namespaceIndex = typeName.IndexOf(':', StringComparison.Ordinal);
        if (namespaceIndex >= 0)
        {
            return typeName[(namespaceIndex + 1)..];
        }

        var typeIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);
        return typeIndex >= 0
            ? typeName[(typeIndex + 1)..]
            : typeName;
    }
}

public static class ControlThemeResourceBuilder
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static string CreateResourceDictionary(
        FluentControlThemeTemplate template,
        string themeKey)
    {
        var targetType = FluentControlThemeCatalog.GetLocalName(template.TargetType);
        var preview = CreatePreviewXaml(targetType, themeKey);
        var themeXaml = CreateKeyedThemeXaml(template.Xaml, themeKey);
        var namespaces = CreateNamespaceDeclarations(ExtractNamespaceDeclarations(template.Xaml));

        return
            "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
            namespaces +
            ">\n" +
            preview +
            "\n\n" +
            Indent(themeXaml, "  ") +
            "\n</ResourceDictionary>\n";
    }

    public static string CreateVariantPreviewXaml(
        string targetType,
        string themeKey)
    {
        var samples = CreatePreviewSamples(targetType, themeKey);
        return
            "<Design.PreviewWith xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Border Padding=\"24\">\n" +
            "    <StackPanel Orientation=\"Horizontal\" Spacing=\"16\">\n" +
            CreateVariantPreviewColumn("Light", "Light", samples) +
            "\n" +
            CreateVariantPreviewColumn("Dark", "Dark", samples) +
            "\n" +
            "    </StackPanel>\n" +
            "  </Border>\n" +
            "</Design.PreviewWith>";
    }

    public static IReadOnlyList<ControlThemeDefinition> FindCustomThemes(
        IEnumerable<(string Path, string Text)> resourceFiles)
    {
        var themes = new List<ControlThemeDefinition>();

        foreach (var file in resourceFiles)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(file.Text, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            foreach (var theme in document.Descendants().Where(static element => element.Name.LocalName == "ControlTheme"))
            {
                var key = theme.Attribute(XamlNamespace + "Key")?.Value;
                var targetType = theme.Attribute("TargetType")?.Value;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(targetType))
                {
                    continue;
                }

                if (key.StartsWith("{x:Type ", StringComparison.Ordinal))
                {
                    continue;
                }

                themes.Add(new ControlThemeDefinition(
                    key,
                    FluentControlThemeCatalog.GetLocalName(targetType),
                    file.Path));
            }
        }

        return themes
            .OrderBy(static theme => theme.TargetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static theme => theme.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreatePreviewXaml(string targetType, string themeKey)
    {
        var samples = CreatePreviewSamples(targetType, themeKey);

        return
            "  <Design.PreviewWith>\n" +
            "    <Border Padding=\"24\">\n" +
            "      <StackPanel Spacing=\"12\">\n" +
            string.Join("\n", samples.Select(sample => "        " + sample)) + "\n" +
            "      </StackPanel>\n" +
            "    </Border>\n" +
            "  </Design.PreviewWith>";
    }

    private static string[] CreatePreviewSamples(string targetType, string themeKey)
    {
        return targetType switch
        {
            "TextBox" => new[]
            {
                "<TextBox Theme=\"{StaticResource " + themeKey + "}\" Text=\"Editable text\" Width=\"220\" />",
                "<TextBox Theme=\"{StaticResource " + themeKey + "}\" Text=\"Disabled\" Width=\"220\" IsEnabled=\"False\" />"
            },
            "Slider" => new[]
            {
                "<Slider Theme=\"{StaticResource " + themeKey + "}\" Width=\"220\" Value=\"45\" />",
                "<Slider Theme=\"{StaticResource " + themeKey + "}\" Width=\"220\" Value=\"45\" IsEnabled=\"False\" />"
            },
            "ProgressBar" => new[]
            {
                "<ProgressBar Theme=\"{StaticResource " + themeKey + "}\" Width=\"220\" Value=\"45\" />",
                "<ProgressBar Theme=\"{StaticResource " + themeKey + "}\" Width=\"220\" Value=\"45\" IsEnabled=\"False\" />"
            },
            "Button" or "RepeatButton" => CreateContentControlPreview(targetType, themeKey, targetType, "Disabled"),
            "ToggleButton" => CreateContentControlPreview(targetType, themeKey, "Toggle", "Disabled"),
            "CheckBox" => CreateContentControlPreview(targetType, themeKey, "Check option", "Disabled"),
            "RadioButton" => CreateContentControlPreview(targetType, themeKey, "Radio option", "Disabled"),
            _ => CreatePlainControlPreview(targetType, themeKey)
        };
    }

    private static string CreateVariantPreviewColumn(
        string title,
        string requestedThemeVariant,
        IEnumerable<string> samples)
    {
        return
            "      <Border RequestedThemeVariant=\"" + requestedThemeVariant + "\" Padding=\"16\" BorderThickness=\"1\" BorderBrush=\"Gray\">\n" +
            "        <StackPanel Spacing=\"12\">\n" +
            "          <TextBlock Text=\"" + title + "\" FontWeight=\"SemiBold\" />\n" +
            string.Join("\n", samples.Select(sample => "          " + sample)) + "\n" +
            "        </StackPanel>\n" +
            "      </Border>";
    }

    private static string[] CreateContentControlPreview(
        string targetType,
        string themeKey,
        string normalContent,
        string disabledContent)
    {
        return new[]
        {
            "<" + targetType + " Theme=\"{StaticResource " + themeKey + "}\" Content=\"" + normalContent + "\" />",
            "<" + targetType + " Theme=\"{StaticResource " + themeKey + "}\" Content=\"" + disabledContent + "\" IsEnabled=\"False\" />"
        };
    }

    private static string CreateKeyedThemeXaml(string templateXaml, string themeKey)
    {
        var xaml = RemoveXmlnsDeclarations(templateXaml);
        var openingTagEnd = xaml.IndexOf('>', StringComparison.Ordinal);
        if (openingTagEnd < 0)
        {
            return xaml;
        }

        var openingTag = xaml[..openingTagEnd];
        var remainder = xaml[openingTagEnd..];
        var keyRegex = new Regex("\\s+x:Key=\"[^\"]*\"", RegexOptions.CultureInvariant);
        if (keyRegex.IsMatch(openingTag))
        {
            return keyRegex.Replace(
                openingTag,
                $" x:Key=\"{EscapeAttributeValue(themeKey)}\"",
                1) + remainder;
        }

        return InsertAttributeIntoOpeningTag(
            openingTag,
            $" x:Key=\"{EscapeAttributeValue(themeKey)}\"") + remainder;
    }

    private static string[] CreatePlainControlPreview(string targetType, string themeKey)
    {
        return new[]
        {
            "<" + targetType + " Theme=\"{StaticResource " + themeKey + "}\" />",
            "<" + targetType + " Theme=\"{StaticResource " + themeKey + "}\" IsEnabled=\"False\" />"
        };
    }

    private static string InsertAttributeIntoOpeningTag(string openingTag, string attribute)
    {
        var insertIndex = openingTag.Length;
        while (insertIndex > 0 && char.IsWhiteSpace(openingTag[insertIndex - 1]))
        {
            insertIndex--;
        }

        if (insertIndex > 0 && openingTag[insertIndex - 1] == '/')
        {
            insertIndex--;
        }

        return openingTag.Insert(insertIndex, attribute);
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
            "\n                    xmlns:" + declaration.Prefix + "=\"" + EscapeAttributeValue(declaration.Uri) + "\""));
    }

    private static string RemoveXmlnsDeclarations(string xaml)
    {
        return Regex.Replace(
            xaml,
            "\\s+xmlns(?::[A-Za-z_][A-Za-z0-9_.-]*)?=\"[^\"]*\"",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static string EscapeAttributeValue(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string Indent(string text, string indentation)
    {
        return string.Join(
            "\n",
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => indentation + line));
    }
}
