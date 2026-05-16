using System.Collections.Generic;
using System.Linq;
using XamlPlayground.Extensions;

namespace XamlPlayground.Services.VisualEditing;

public sealed class ExtensionToolboxContributor : IToolboxContributor
{
    private readonly IReadOnlyList<ToolboxItemContribution> _items;

    public ExtensionToolboxContributor(IEnumerable<ToolboxItemContribution> items)
    {
        _items = items.ToArray();
    }

    public IEnumerable<ToolboxItemDescriptor> GetItems(ToolboxContext context)
    {
        return _items.Select(static item => new ToolboxItemDescriptor(
            item.Id,
            item.DisplayName,
            item.Category,
            item.TypeName,
            item.XmlNamespace,
            item.AssemblyName,
            item.DefaultXaml,
            item.Metadata));
    }
}
