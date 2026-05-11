using System;
using Avalonia.Input;

namespace XamlPlayground.Services.VisualEditing;

public static class ToolboxDragPayload
{
    private const string Prefix = "xamlplayground-toolbox:";
    private static readonly DataFormat<string> ItemIdFormat =
        DataFormat.CreateStringApplicationFormat("xamlplayground.toolbox.item-id");

    public static string Create(ToolboxItemDescriptor item)
    {
        return Prefix + item.Id;
    }

    public static DataTransfer CreateDataTransfer(ToolboxItemDescriptor item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ItemIdFormat, Create(item)));
        data.Add(DataTransferItem.CreateText(item.DefaultXaml));
        return data;
    }

    public static bool TryGetItemId(IDataTransfer dataTransfer, out string itemId)
    {
        ArgumentNullException.ThrowIfNull(dataTransfer);

        if (TryGetItemId(dataTransfer.TryGetValue(ItemIdFormat), out itemId))
        {
            return true;
        }

        return TryGetItemId(dataTransfer.TryGetText(), out itemId);
    }

    public static bool TryGetItemId(string? payload, out string itemId)
    {
        itemId = string.Empty;

        if (string.IsNullOrWhiteSpace(payload) ||
            !payload.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        itemId = payload[Prefix.Length..];
        return !string.IsNullOrWhiteSpace(itemId);
    }
}
