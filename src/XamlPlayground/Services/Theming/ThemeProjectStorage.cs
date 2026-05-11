using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlPlayground.Services.Theming;

public sealed class ThemeProjectDocument
{
    public int Version { get; set; } = ThemeProjectStorage.CurrentVersion;

    public string Format { get; set; } = ThemeProjectStorage.FormatName;

    public string Name { get; set; } = "ThemeProject";

    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<string> Variants { get; set; } = new();

    public List<ThemeProjectFile> Files { get; set; } = new();
}

public sealed class ThemeProjectFile
{
    public string Path { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Kind { get; set; } = ThemeProjectStorage.ResourceFileKind;

    public string Variant { get; set; } = ThemeProjectStorage.BaseVariant;
}

public static class ThemeProjectStorage
{
    public const int CurrentVersion = 2;
    public const string FormatName = "xamlplayground.theme";
    public const string ResourceFileKind = "resource-dictionary";
    public const string BaseVariant = "base";

    public static string Save(
        string name,
        IEnumerable<(string Path, string Text)> files)
    {
        var document = new ThemeProjectDocument
        {
            Version = CurrentVersion,
            Format = FormatName,
            Name = string.IsNullOrWhiteSpace(name) ? "ThemeProject" : name,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Files = files
                .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
                .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static file => new ThemeProjectFile
                {
                    Path = NormalizeProjectPath(file.Path),
                    Text = file.Text,
                    Kind = ResourceFileKind,
                    Variant = InferVariant(file.Path)
                })
                .ToList()
        };
        document.Variants = document.Files
            .Select(static file => file.Variant)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static variant => variant, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return JsonSerializer.Serialize(document, ThemeProjectJsonContext.Default.ThemeProjectDocument);
    }

    public static ThemeProjectDocument Load(string json)
    {
        var document = JsonSerializer.Deserialize(json, ThemeProjectJsonContext.Default.ThemeProjectDocument)
                       ?? throw new InvalidDataException("Theme project file is empty.");

        var sourceVersion = document.Version;
        if (sourceVersion < 1 || sourceVersion > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported theme project version {sourceVersion}.");
        }

        document.Version = CurrentVersion;
        document.Format = string.IsNullOrWhiteSpace(document.Format)
            ? FormatName
            : document.Format;
        document.Name = string.IsNullOrWhiteSpace(document.Name)
            ? "ThemeProject"
            : document.Name;
        document.Files = document.Files
            .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
            .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => new ThemeProjectFile
            {
                Path = NormalizeProjectPath(file.Path),
                Text = file.Text ?? string.Empty,
                Kind = string.IsNullOrWhiteSpace(file.Kind) ? ResourceFileKind : file.Kind,
                Variant = sourceVersion < 2 || string.IsNullOrWhiteSpace(file.Variant)
                    ? InferVariant(file.Path)
                    : file.Variant
            })
            .ToList();
        document.Variants = document.Files
            .Select(static file => file.Variant)
            .Concat(document.Variants ?? new List<string>())
            .Where(static variant => !string.IsNullOrWhiteSpace(variant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static variant => variant, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return document;
    }

    public static string NormalizeProjectPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    public static string InferVariant(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.EndsWith(".Light", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Light", StringComparison.OrdinalIgnoreCase))
        {
            return "light";
        }

        if (fileName.EndsWith(".Dark", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "dark";
        }

        return BaseVariant;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ThemeProjectDocument))]
internal sealed partial class ThemeProjectJsonContext : JsonSerializerContext;
