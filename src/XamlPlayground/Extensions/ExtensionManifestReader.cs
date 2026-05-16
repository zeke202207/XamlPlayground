using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlPlayground.Extensions;

public static class ExtensionManifestReader
{
    public const string ManifestFileName = "xamlplayground.extension.json";

    public static ExtensionManifest ReadJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The extension manifest JSON cannot be empty.", nameof(json));
        }

        var document = JsonSerializer.Deserialize(json, ExtensionManifestJsonContext.Default.ExtensionManifestDocument)
            ?? throw new InvalidDataException("The extension manifest is empty.");

        return ToManifest(document);
    }

    public static ExtensionManifest ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The extension manifest path cannot be empty.", nameof(path));
        }

        return ReadJson(File.ReadAllText(path));
    }

    private static ExtensionManifest ToManifest(ExtensionManifestDocument document)
    {
        var id = ResolveExtensionId(document);
        var displayName = document.DisplayName ?? document.Name ?? id;
        var version = Require(document.Version, "version");
        var contributes = document.Contributes is null
            ? ExtensionContributions.Empty
            : ToContributions(document.Contributes);
        var metadata = CopyMetadata(document);

        return new ExtensionManifest(
            new ExtensionIdentity(
                id,
                displayName,
                version,
                document.Publisher,
                document.Description),
            document.ActivationEvents,
            contributes,
            metadata);
    }

    private static string ResolveExtensionId(ExtensionManifestDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.Id))
        {
            return Require(document.Id, "id");
        }

        if (!string.IsNullOrWhiteSpace(document.Publisher) &&
            !string.IsNullOrWhiteSpace(document.Name))
        {
            return Require(document.Publisher, "publisher") + "." + Require(document.Name, "name");
        }

        return Require(document.Id, "id");
    }

    private static ExtensionContributions ToContributions(ExtensionContributesDocument document)
    {
        return new ExtensionContributions(
            commands: document.Commands?.Select(static command =>
                new CommandContribution(
                    Require(command.Command, "contributes.commands[].command"),
                    Require(command.Title, "contributes.commands[].title"),
                    command.Category,
                    command.Icon)),
            menus: FlattenMenus(document.Menus),
            views: FlattenViews(document.Views),
            perspectives: document.Perspectives?.Select(static perspective =>
                new PerspectiveContribution(
                    Require(perspective.Id, "contributes.perspectives[].id"),
                    Require(perspective.Title, "contributes.perspectives[].title"),
                    perspective.Views ?? Array.Empty<string>())),
            previewProviders: document.PreviewProviders?.Select(static provider =>
                new PreviewProviderContribution(
                    Require(provider.Id, "contributes.previewProviders[].id"),
                    Require(provider.Title, "contributes.previewProviders[].title"),
                    provider.Patterns ?? Array.Empty<string>(),
                    provider.Priority)),
            editorFeatures: document.EditorFeatures?.Select(static feature =>
                new EditorFeatureContribution(
                    Require(feature.Id, "contributes.editorFeatures[].id"),
                    Require(feature.Title, "contributes.editorFeatures[].title"),
                    Require(feature.Language, "contributes.editorFeatures[].language"),
                    Require(feature.FeatureKind, "contributes.editorFeatures[].featureKind"))),
            animationProviders: document.AnimationProviders?.Select(static provider =>
                new AnimationProviderContribution(
                    Require(provider.Id, "contributes.animationProviders[].id"),
                    Require(provider.Title, "contributes.animationProviders[].title"),
                    Require(provider.TargetKind, "contributes.animationProviders[].targetKind"))),
            themeResourceEditors: document.ThemeResourceEditors?.Select(static editor =>
                new ThemeResourceEditorContribution(
                    Require(editor.Id, "contributes.themeResourceEditors[].id"),
                    Require(editor.Title, "contributes.themeResourceEditors[].title"),
                    Require(editor.ResourceKind, "contributes.themeResourceEditors[].resourceKind"))),
            workspaceFeatures: document.WorkspaceFeatures?.Select(static feature =>
                new WorkspaceFeatureContribution(
                    Require(feature.Id, "contributes.workspaceFeatures[].id"),
                    Require(feature.Title, "contributes.workspaceFeatures[].title"),
                    Require(feature.FeatureKind, "contributes.workspaceFeatures[].featureKind"))),
            diagnosticProviders: document.DiagnosticProviders?.Select(static provider =>
                new DiagnosticProviderContribution(
                    Require(provider.Id, "contributes.diagnosticProviders[].id"),
                    Require(provider.Title, "contributes.diagnosticProviders[].title"),
                    Require(provider.DiagnosticKind, "contributes.diagnosticProviders[].diagnosticKind"))),
            visualEditorFeatures: document.VisualEditorFeatures?.Select(static feature =>
                new VisualEditorFeatureContribution(
                    Require(feature.Id, "contributes.visualEditorFeatures[].id"),
                    Require(feature.Title, "contributes.visualEditorFeatures[].title"),
                    Require(feature.FeatureKind, "contributes.visualEditorFeatures[].featureKind"))),
            toolboxItems: document.Toolbox?.Select(static item =>
                new ToolboxItemContribution(
                    Require(item.Id, "contributes.toolbox[].id"),
                    Require(item.DisplayName, "contributes.toolbox[].displayName"),
                    Require(item.Category, "contributes.toolbox[].category"),
                    Require(item.TypeName, "contributes.toolbox[].typeName"),
                    Require(item.XmlNamespace, "contributes.toolbox[].xmlNamespace"),
                    item.AssemblyName ?? string.Empty,
                    Require(item.DefaultXaml, "contributes.toolbox[].defaultXaml"),
                    item.Metadata)));
    }

    private static IEnumerable<MenuContribution>? FlattenMenus(Dictionary<string, ExtensionMenuItemDocument[]>? menus)
    {
        if (menus is null)
        {
            return null;
        }

        return menus.SelectMany(static pair => pair.Value.Select(item =>
            new MenuContribution(
                pair.Key,
                Require(item.Command, "contributes.menus[].command"),
                item.Group,
                item.When,
                item.Order)));
    }

    private static IEnumerable<ViewToolContribution>? FlattenViews(Dictionary<string, ExtensionViewDocument[]>? views)
    {
        if (views is null)
        {
            return null;
        }

        return views.SelectMany(static pair => pair.Value.Select(view =>
            new ViewToolContribution(
                Require(view.Id, "contributes.views[].id"),
                view.Name ?? Require(view.Title, "contributes.views[].title"),
                pair.Key,
                view.Icon,
                view.IsTool)));
    }

    private static string Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Extension manifest field '" + field + "' is required.");
        }

        return value.Trim();
    }

    private static IEnumerable<KeyValuePair<string, string>> CopyMetadata(ExtensionManifestDocument document)
    {
        var metadata = document.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal);

        AddIfPresent(metadata, "name", document.Name);
        AddIfPresent(metadata, "main", document.Main);
        AddIfPresent(metadata, "browser", document.Browser);
        AddIfPresent(metadata, "kind", document.Kind is null ? null : string.Join(",", document.Kind));

        return metadata;
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }
}

internal sealed class ExtensionManifestDocument
{
    public string? Id { get; set; }

    public string? Publisher { get; set; }

    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Version { get; set; }

    public string? Main { get; set; }

    public string? Browser { get; set; }

    public string[]? Kind { get; set; }

    public string[]? ActivationEvents { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    public ExtensionContributesDocument? Contributes { get; set; }
}

internal sealed class ExtensionContributesDocument
{
    public ExtensionCommandDocument[]? Commands { get; set; }

    public Dictionary<string, ExtensionMenuItemDocument[]>? Menus { get; set; }

    public Dictionary<string, ExtensionViewDocument[]>? Views { get; set; }

    public ExtensionPerspectiveDocument[]? Perspectives { get; set; }

    public ExtensionPreviewProviderDocument[]? PreviewProviders { get; set; }

    public ExtensionEditorFeatureDocument[]? EditorFeatures { get; set; }

    public ExtensionAnimationProviderDocument[]? AnimationProviders { get; set; }

    public ExtensionThemeResourceEditorDocument[]? ThemeResourceEditors { get; set; }

    public ExtensionWorkspaceFeatureDocument[]? WorkspaceFeatures { get; set; }

    public ExtensionDiagnosticProviderDocument[]? DiagnosticProviders { get; set; }

    public ExtensionVisualEditorFeatureDocument[]? VisualEditorFeatures { get; set; }

    public ExtensionToolboxItemDocument[]? Toolbox { get; set; }
}

internal sealed class ExtensionCommandDocument
{
    public string? Command { get; set; }

    public string? Title { get; set; }

    public string? Category { get; set; }

    public string? Icon { get; set; }
}

internal sealed class ExtensionMenuItemDocument
{
    public string? Command { get; set; }

    public string? Group { get; set; }

    public string? When { get; set; }

    public double Order { get; set; }
}

internal sealed class ExtensionViewDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Title { get; set; }

    public string? Icon { get; set; }

    public bool IsTool { get; set; } = true;
}

internal sealed class ExtensionPerspectiveDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string[]? Views { get; set; }
}

internal sealed class ExtensionPreviewProviderDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string[]? Patterns { get; set; }

    public int Priority { get; set; }
}

internal sealed class ExtensionEditorFeatureDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? Language { get; set; }

    public string? FeatureKind { get; set; }
}

internal sealed class ExtensionAnimationProviderDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? TargetKind { get; set; }
}

internal sealed class ExtensionThemeResourceEditorDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? ResourceKind { get; set; }
}

internal sealed class ExtensionWorkspaceFeatureDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? FeatureKind { get; set; }
}

internal sealed class ExtensionDiagnosticProviderDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? DiagnosticKind { get; set; }
}

internal sealed class ExtensionVisualEditorFeatureDocument
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? FeatureKind { get; set; }
}

internal sealed class ExtensionToolboxItemDocument
{
    public string? Id { get; set; }

    public string? DisplayName { get; set; }

    public string? Category { get; set; }

    public string? TypeName { get; set; }

    public string? XmlNamespace { get; set; }

    public string? AssemblyName { get; set; }

    public string? DefaultXaml { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExtensionManifestDocument))]
internal sealed partial class ExtensionManifestJsonContext : JsonSerializerContext;
