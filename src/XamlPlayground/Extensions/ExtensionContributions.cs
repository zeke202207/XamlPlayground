using System.Collections.Generic;

namespace XamlPlayground.Extensions;

public sealed class ExtensionContributions
{
    public ExtensionContributions(
        IEnumerable<CommandContribution>? commands = null,
        IEnumerable<MenuContribution>? menus = null,
        IEnumerable<ViewToolContribution>? views = null,
        IEnumerable<PerspectiveContribution>? perspectives = null,
        IEnumerable<PreviewProviderContribution>? previewProviders = null,
        IEnumerable<EditorFeatureContribution>? editorFeatures = null,
        IEnumerable<AnimationProviderContribution>? animationProviders = null,
        IEnumerable<ThemeResourceEditorContribution>? themeResourceEditors = null,
        IEnumerable<WorkspaceFeatureContribution>? workspaceFeatures = null,
        IEnumerable<DiagnosticProviderContribution>? diagnosticProviders = null,
        IEnumerable<VisualEditorFeatureContribution>? visualEditorFeatures = null,
        IEnumerable<ToolboxItemContribution>? toolboxItems = null)
    {
        Commands = ExtensionCollections.CopyList(commands);
        Menus = ExtensionCollections.CopyList(menus);
        Views = ExtensionCollections.CopyList(views);
        Perspectives = ExtensionCollections.CopyList(perspectives);
        PreviewProviders = ExtensionCollections.CopyList(previewProviders);
        EditorFeatures = ExtensionCollections.CopyList(editorFeatures);
        AnimationProviders = ExtensionCollections.CopyList(animationProviders);
        ThemeResourceEditors = ExtensionCollections.CopyList(themeResourceEditors);
        WorkspaceFeatures = ExtensionCollections.CopyList(workspaceFeatures);
        DiagnosticProviders = ExtensionCollections.CopyList(diagnosticProviders);
        VisualEditorFeatures = ExtensionCollections.CopyList(visualEditorFeatures);
        ToolboxItems = ExtensionCollections.CopyList(toolboxItems);
    }

    public static ExtensionContributions Empty { get; } = new();

    public IReadOnlyList<CommandContribution> Commands { get; }

    public IReadOnlyList<MenuContribution> Menus { get; }

    public IReadOnlyList<ViewToolContribution> Views { get; }

    public IReadOnlyList<PerspectiveContribution> Perspectives { get; }

    public IReadOnlyList<PreviewProviderContribution> PreviewProviders { get; }

    public IReadOnlyList<EditorFeatureContribution> EditorFeatures { get; }

    public IReadOnlyList<AnimationProviderContribution> AnimationProviders { get; }

    public IReadOnlyList<ThemeResourceEditorContribution> ThemeResourceEditors { get; }

    public IReadOnlyList<WorkspaceFeatureContribution> WorkspaceFeatures { get; }

    public IReadOnlyList<DiagnosticProviderContribution> DiagnosticProviders { get; }

    public IReadOnlyList<VisualEditorFeatureContribution> VisualEditorFeatures { get; }

    public IReadOnlyList<ToolboxItemContribution> ToolboxItems { get; }
}

public sealed class CommandContribution
{
    public CommandContribution(string id, string title, string? category = null, string? icon = null)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        Category = category;
        Icon = icon;
    }

    public string Id { get; }

    public string Title { get; }

    public string? Category { get; }

    public string? Icon { get; }
}

public sealed class MenuContribution
{
    public MenuContribution(string menuId, string commandId, string? group = null, string? when = null, double order = 0)
    {
        MenuId = ExtensionCollections.RequireIdentifier(menuId, nameof(menuId));
        CommandId = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));
        Group = group;
        When = when;
        Order = order;
    }

    public string MenuId { get; }

    public string CommandId { get; }

    public string? Group { get; }

    public string? When { get; }

    public double Order { get; }
}

public sealed class ViewToolContribution
{
    public ViewToolContribution(string id, string title, string location, string? icon = null, bool isTool = true)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        Location = ExtensionCollections.RequireIdentifier(location, nameof(location));
        Icon = icon;
        IsTool = isTool;
    }

    public string Id { get; }

    public string Title { get; }

    public string Location { get; }

    public string? Icon { get; }

    public bool IsTool { get; }
}

public sealed class WorkspaceFeatureContribution
{
    public WorkspaceFeatureContribution(string id, string title, string featureKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        FeatureKind = ExtensionCollections.RequireIdentifier(featureKind, nameof(featureKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string FeatureKind { get; }
}

public sealed class DiagnosticProviderContribution
{
    public DiagnosticProviderContribution(string id, string title, string diagnosticKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        DiagnosticKind = ExtensionCollections.RequireIdentifier(diagnosticKind, nameof(diagnosticKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string DiagnosticKind { get; }
}

public sealed class VisualEditorFeatureContribution
{
    public VisualEditorFeatureContribution(string id, string title, string featureKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        FeatureKind = ExtensionCollections.RequireIdentifier(featureKind, nameof(featureKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string FeatureKind { get; }
}

public sealed class ToolboxItemContribution
{
    public ToolboxItemContribution(
        string id,
        string displayName,
        string category,
        string typeName,
        string xmlNamespace,
        string assemblyName,
        string defaultXaml,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        DisplayName = ExtensionCollections.RequireText(displayName, nameof(displayName));
        Category = ExtensionCollections.RequireText(category, nameof(category));
        TypeName = ExtensionCollections.RequireIdentifier(typeName, nameof(typeName));
        XmlNamespace = ExtensionCollections.RequireText(xmlNamespace, nameof(xmlNamespace));
        AssemblyName = assemblyName ?? string.Empty;
        DefaultXaml = ExtensionCollections.RequireText(defaultXaml, nameof(defaultXaml));
        Metadata = ExtensionCollections.CopyDictionary(metadata);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public string TypeName { get; }

    public string XmlNamespace { get; }

    public string AssemblyName { get; }

    public string DefaultXaml { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class PerspectiveContribution
{
    public PerspectiveContribution(string id, string title, IEnumerable<string> viewIds)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        ViewIds = ExtensionCollections.CopyList(viewIds);
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<string> ViewIds { get; }
}

public sealed class PreviewProviderContribution
{
    public PreviewProviderContribution(string id, string title, IEnumerable<string> supportedFilePatterns, int priority = 0)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        SupportedFilePatterns = ExtensionCollections.CopyList(supportedFilePatterns);
        Priority = priority;
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<string> SupportedFilePatterns { get; }

    public int Priority { get; }
}

public sealed class EditorFeatureContribution
{
    public EditorFeatureContribution(string id, string title, string language, string featureKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        Language = ExtensionCollections.RequireIdentifier(language, nameof(language));
        FeatureKind = ExtensionCollections.RequireIdentifier(featureKind, nameof(featureKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string Language { get; }

    public string FeatureKind { get; }
}

public sealed class AnimationProviderContribution
{
    public AnimationProviderContribution(string id, string title, string targetKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        TargetKind = ExtensionCollections.RequireIdentifier(targetKind, nameof(targetKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string TargetKind { get; }
}

public sealed class ThemeResourceEditorContribution
{
    public ThemeResourceEditorContribution(string id, string title, string resourceKind)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = ExtensionCollections.RequireText(title, nameof(title));
        ResourceKind = ExtensionCollections.RequireIdentifier(resourceKind, nameof(resourceKind));
    }

    public string Id { get; }

    public string Title { get; }

    public string ResourceKind { get; }
}
