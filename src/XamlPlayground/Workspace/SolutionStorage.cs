using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlPlayground.Workspace;

public sealed class SolutionDocument
{
    public int Version { get; set; } = SolutionStorage.CurrentVersion;

    public string Format { get; set; } = SolutionStorage.FormatName;

    public string Name { get; set; } = "Solution";

    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<SolutionProjectDocument> Projects { get; set; } = new();
}

public sealed class SolutionProjectDocument
{
    public string Name { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = string.Empty;

    public string TemplateShortName { get; set; } = string.Empty;

    public List<SolutionProjectFileDocument> Files { get; set; } = new();
}

public sealed class SolutionProjectFileDocument
{
    public string Path { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Kind { get; set; } = ProjectFileKind.Text.ToString();

    public bool IncludeInRuntimePreview { get; set; } = true;
}

public static class SolutionStorage
{
    public const int CurrentVersion = 1;
    public const string FormatName = "xamlplayground.solution";

    public static string Save(InMemorySolution solution)
    {
        var document = CreateDocument(solution);
        return JsonSerializer.Serialize(document, SolutionJsonContext.Default.SolutionDocument);
    }

    public static SolutionDocument CreateDocument(InMemorySolution solution)
    {
        return new SolutionDocument
        {
            Version = CurrentVersion,
            Format = FormatName,
            Name = string.IsNullOrWhiteSpace(solution.Name) ? "Solution" : solution.Name,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Projects = solution.Projects
                .Where(static project => !string.IsNullOrWhiteSpace(project.Name))
                .Select(static project => new SolutionProjectDocument
                {
                    Name = project.Name,
                    RootNamespace = string.IsNullOrWhiteSpace(project.RootNamespace) ? project.Name : project.RootNamespace,
                    TemplateShortName = project.TemplateShortName,
                    Files = project.Files
                        .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
                        .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                        .Select(static file => new SolutionProjectFileDocument
                        {
                            Path = NormalizeProjectPath(file.Path),
                            Text = file.Text,
                            Kind = file.Kind.ToString(),
                            IncludeInRuntimePreview = file.IncludeInRuntimePreview
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public static InMemorySolution Load(string json, Action<InMemoryProjectFile>? fileChanged = null)
    {
        var document = JsonSerializer.Deserialize(json, SolutionJsonContext.Default.SolutionDocument)
                       ?? throw new InvalidDataException("Solution file is empty.");

        var sourceVersion = document.Version;
        if (sourceVersion < 1 || sourceVersion > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported solution version {sourceVersion}.");
        }

        if (!string.Equals(document.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected file is not a XamlPlayground solution.");
        }

        var solutionName = string.IsNullOrWhiteSpace(document.Name)
            ? "Solution"
            : document.Name.Trim();
        var solution = new InMemorySolution(solutionName);

        foreach (var projectDocument in document.Projects ?? new List<SolutionProjectDocument>())
        {
            if (string.IsNullOrWhiteSpace(projectDocument.Name))
            {
                continue;
            }

            var projectName = projectDocument.Name.Trim();
            var rootNamespace = string.IsNullOrWhiteSpace(projectDocument.RootNamespace)
                ? projectName
                : projectDocument.RootNamespace.Trim();
            var project = new InMemoryProject(
                projectName,
                rootNamespace,
                projectDocument.TemplateShortName ?? string.Empty);
            solution.Projects.Add(project);

            foreach (var fileDocument in projectDocument.Files ?? new List<SolutionProjectFileDocument>())
            {
                if (string.IsNullOrWhiteSpace(fileDocument.Path))
                {
                    continue;
                }

                project.AddFile(new InMemoryProjectFile(
                    NormalizeProjectPath(fileDocument.Path),
                    fileDocument.Text ?? string.Empty,
                    ParseFileKind(fileDocument.Kind),
                    fileChanged,
                    includeInRuntimePreview: fileDocument.IncludeInRuntimePreview));
            }
        }

        if (solution.Projects.Count == 0)
        {
            throw new InvalidDataException("Solution file does not contain any projects.");
        }

        return solution;
    }

    public static string NormalizeProjectPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Equals("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
        {
            return "File.txt";
        }

        return normalized;
    }

    private static ProjectFileKind ParseFileKind(string? kind)
    {
        return Enum.TryParse<ProjectFileKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : ProjectFileKind.Text;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SolutionDocument))]
internal sealed partial class SolutionJsonContext : JsonSerializerContext;
