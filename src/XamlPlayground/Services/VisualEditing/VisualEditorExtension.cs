using System;

namespace XamlPlayground.Services.VisualEditing;

public interface IVisualEditorExtension
{
    void Configure(VisualEditorExtensionContext context);
}

public sealed class VisualEditorExtensionContext
{
    public VisualEditorExtensionContext(
        IXamlMutationEngine mutationEngine,
        ControlEditorRegistry editorRegistry,
        ToolboxCatalogBuilder toolboxCatalogBuilder)
    {
        MutationEngine = mutationEngine ?? throw new ArgumentNullException(nameof(mutationEngine));
        EditorRegistry = editorRegistry ?? throw new ArgumentNullException(nameof(editorRegistry));
        ToolboxCatalogBuilder = toolboxCatalogBuilder ?? throw new ArgumentNullException(nameof(toolboxCatalogBuilder));
    }

    public IXamlMutationEngine MutationEngine { get; }

    public ControlEditorRegistry EditorRegistry { get; }

    public ToolboxCatalogBuilder ToolboxCatalogBuilder { get; }
}
