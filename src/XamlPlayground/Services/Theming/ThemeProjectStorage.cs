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

    public string Name { get; set; } = "ThemeProject";

    public List<ThemeProjectFile> Files { get; set; } = new();
}

public sealed class ThemeProjectFile
{
    public string Path { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public static class ThemeProjectStorage
{
    public const int CurrentVersion = 1;

    public static string Save(
        string name,
        IEnumerable<(string Path, string Text)> files)
    {
        var document = new ThemeProjectDocument
        {
            Version = CurrentVersion,
            Name = string.IsNullOrWhiteSpace(name) ? "ThemeProject" : name,
            Files = files
                .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
                .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static file => new ThemeProjectFile
                {
                    Path = NormalizeProjectPath(file.Path),
                    Text = file.Text
                })
                .ToList()
        };

        return JsonSerializer.Serialize(document, ThemeProjectJsonContext.Default.ThemeProjectDocument);
    }

    public static ThemeProjectDocument Load(string json)
    {
        var document = JsonSerializer.Deserialize(json, ThemeProjectJsonContext.Default.ThemeProjectDocument)
                       ?? throw new InvalidDataException("Theme project file is empty.");

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported theme project version {document.Version}.");
        }

        document.Name = string.IsNullOrWhiteSpace(document.Name)
            ? "ThemeProject"
            : document.Name;
        document.Files = document.Files
            .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
            .Select(static file => new ThemeProjectFile
            {
                Path = NormalizeProjectPath(file.Path),
                Text = file.Text ?? string.Empty
            })
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
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ThemeProjectDocument))]
internal sealed partial class ThemeProjectJsonContext : JsonSerializerContext;
