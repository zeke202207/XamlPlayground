using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels.Docking;

namespace XamlPlayground.Views.Docking;

public partial class VisualStructureDockView : UserControl
{
    public VisualStructureDockView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ToolboxDragPayload.TryGetItemId(e.DataTransfer, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VisualStructureDockViewModel viewModel ||
            !ToolboxDragPayload.TryGetItemId(e.DataTransfer, out var itemId))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var item = viewModel.Shell.VisualEditorToolboxItems.FirstOrDefault(toolboxItem =>
            string.Equals(toolboxItem.Id, itemId, System.StringComparison.Ordinal));
        if (item is null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        viewModel.Shell.SelectedVisualEditorToolboxItem = item;
        if (viewModel.Shell.InsertSelectedToolboxItemCommand.CanExecute(null))
        {
            viewModel.Shell.InsertSelectedToolboxItemCommand.Execute(null);
            e.DragEffects = DragDropEffects.Copy;
        }

        e.Handled = true;
    }
}
