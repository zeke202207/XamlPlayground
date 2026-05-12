using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.DataGridDragDrop;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Input;
using Avalonia.VisualTree;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.VisualEditing;

namespace XamlPlayground.Views.Docking;

public partial class VisualStructureDockView : UserControl
{
    public VisualStructureDockView()
    {
        InitializeComponent();
        StructureGrid.RowDropHandler = new StructureRowDropHandler(this);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!ToolboxDragPayload.TryGetItemId(e.DataTransfer, out _))
        {
            return;
        }

        e.DragEffects = TryGetToolboxDropContext(
                e,
                out var viewModel,
                out var item,
                out var target,
                out var position) &&
            viewModel.Shell.CanInsertToolboxItemIntoStructure(item, target.Element, position)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!ToolboxDragPayload.TryGetItemId(e.DataTransfer, out _))
        {
            return;
        }

        if (!TryGetToolboxDropContext(
                e,
                out var viewModel,
                out var item,
                out var target,
                out var position))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        viewModel.Shell.SelectedVisualEditorToolboxItem = item;
        e.DragEffects = viewModel.Shell.InsertToolboxItemIntoStructure(item, target.Element, position)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private bool TryGetToolboxDropContext(
        DragEventArgs e,
        out VisualStructureDockViewModel viewModel,
        out ToolboxItemDescriptor item,
        out VisualEditorNodeViewModel target,
        out VisualEditorStructureDropPosition position)
    {
        viewModel = null!;
        item = null!;
        target = null!;
        position = VisualEditorStructureDropPosition.Inside;

        if (DataContext is not VisualStructureDockViewModel candidate ||
            !ToolboxDragPayload.TryGetItemId(e.DataTransfer, out var itemId))
        {
            return false;
        }

        var toolboxItem = candidate.Shell.VisualEditorToolboxItems.FirstOrDefault(toolboxItem =>
            string.Equals(toolboxItem.Id, itemId, System.StringComparison.Ordinal));
        if (toolboxItem is null)
        {
            return false;
        }

        if (!TryGetStructureDropTarget(e, out var targetNode, out var dropPosition))
        {
            return false;
        }

        viewModel = candidate;
        item = toolboxItem;
        target = targetNode;
        position = dropPosition;
        return true;
    }

    private bool TryGetStructureDropTarget(
        DragEventArgs e,
        out VisualEditorNodeViewModel target,
        out VisualEditorStructureDropPosition position)
    {
        target = null!;
        position = VisualEditorStructureDropPosition.Inside;

        var row = GetStructureRow(e);
        if (row is null ||
            !TryGetStructureNode(row.DataContext, out target))
        {
            return false;
        }

        position = GetStructureDropPosition(row, e.GetPosition(row));
        return true;
    }

    private DataGridRow? GetStructureRow(DragEventArgs e)
    {
        var row = (e.Source as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<DataGridRow>()
            .FirstOrDefault(IsStructureRow);
        if (row is not null)
        {
            return row;
        }

        return StructureGrid
            .GetVisualAt(e.GetPosition(StructureGrid))?
            .GetSelfAndVisualAncestors()
            .OfType<DataGridRow>()
            .FirstOrDefault(IsStructureRow);
    }

    private bool IsStructureRow(DataGridRow row)
    {
        return ReferenceEquals(row.OwningGrid, StructureGrid);
    }

    private static VisualEditorStructureDropPosition GetStructureDropPosition(
        DataGridRow row,
        Point position)
    {
        if (row.Bounds.Height <= 0)
        {
            return VisualEditorStructureDropPosition.Inside;
        }

        var ratio = position.Y / row.Bounds.Height;
        return ratio switch
        {
            < 0.33 => VisualEditorStructureDropPosition.Before,
            > 0.66 => VisualEditorStructureDropPosition.After,
            _ => VisualEditorStructureDropPosition.Inside
        };
    }

    private static bool TryGetStructureNode(
        object? dataContext,
        out VisualEditorNodeViewModel node)
    {
        node = dataContext switch
        {
            VisualEditorNodeViewModel direct => direct,
            HierarchicalNode { Item: VisualEditorNodeViewModel item } => item,
            _ => null!
        };

        return node is not null;
    }

    private sealed class StructureRowDropHandler : IDataGridRowDropHandler
    {
        private readonly VisualStructureDockView _owner;

        public StructureRowDropHandler(VisualStructureDockView owner)
        {
            _owner = owner;
        }

        public bool Validate(DataGridRowDropEventArgs args)
        {
            if (!args.IsSameGrid ||
                !args.RequestedEffect.HasFlag(DragDropEffects.Move) ||
                !TryGetDropContext(args, out var viewModel, out var source, out var target, out var position) ||
                !viewModel.Shell.CanMoveVisualEditorElementInStructure(source.Element, target.Element, position))
            {
                args.EffectiveEffect = DragDropEffects.None;
                return false;
            }

            args.EffectiveEffect = DragDropEffects.Move;
            return true;
        }

        public bool Execute(DataGridRowDropEventArgs args)
        {
            if (!Validate(args) ||
                !TryGetDropContext(args, out var viewModel, out var source, out var target, out var position))
            {
                return false;
            }

            return viewModel.Shell.MoveVisualEditorElementInStructure(source.Element, target.Element, position);
        }

        private bool TryGetDropContext(
            DataGridRowDropEventArgs args,
            out VisualStructureDockViewModel viewModel,
            out VisualEditorNodeViewModel source,
            out VisualEditorNodeViewModel target,
            out VisualEditorStructureDropPosition position)
        {
            viewModel = null!;
            source = null!;
            target = null!;
            position = VisualEditorStructureDropPosition.Inside;

            if (_owner.DataContext is not VisualStructureDockViewModel candidate ||
                args.Items.Count != 1 ||
                !TryGetStructureNode(args.Items[0], out var sourceNode) ||
                !TryGetStructureNode(args.TargetItem, out var targetNode))
            {
                return false;
            }

            viewModel = candidate;
            source = sourceNode;
            target = targetNode;
            position = args.Position switch
            {
                DataGridRowDropPosition.Before => VisualEditorStructureDropPosition.Before,
                DataGridRowDropPosition.After => VisualEditorStructureDropPosition.After,
                _ => VisualEditorStructureDropPosition.Inside
            };
            return true;
        }
    }
}
