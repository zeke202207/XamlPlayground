using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XamlPlayground.Controls;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels;

namespace XamlPlayground.Views;

public partial class PreviewView : UserControl
{
    private const double ResizeHandleSize = 14;
    private const double DragThreshold = 0.5;
    private const double AlignmentGuideSnapDistance = 4;
    private DesignerDragMode _designerDragMode;
    private Point _dragStartPoint;
    private Rect _dragStartBounds;
    private Rect _latestDragBounds;
    private bool _dragMoved;
    private bool _previewSelectionSyncQueued;
    private Point _selectionCyclePoint;
    private string _selectionCycleKey = string.Empty;
    private int _selectionCycleIndex = -1;
    private ToolboxDropPlacement? _lastToolboxDropPlacement;
    private string? _lastToolboxDropItemId;
    private string? _lastToolboxDropDocumentText;
    private LiveResizeState? _liveResizeState;
    private MainViewModel? _sourceSelectionViewModel;
    private readonly IVisualTreeSnapshotService _sourceSelectionSnapshotService = new AvaloniaVisualTreeSnapshotService();
    private readonly IXamlMutationEngine _sourceSelectionMutationEngine = new XamlMutationEngine();
    private readonly XamlVisualTreeMapper _sourceSelectionMapper = new();

    public PreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnPreviewDataContextChanged;
        LayoutUpdated += PreviewViewOnLayoutUpdated;
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetSourceSelectionViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPreviewDataContextChanged(object? sender, EventArgs e)
    {
        SetSourceSelectionViewModel(DataContext as MainViewModel);
    }

    private void SetSourceSelectionViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_sourceSelectionViewModel, viewModel))
        {
            return;
        }

        if (_sourceSelectionViewModel is not null)
        {
            _sourceSelectionViewModel.PropertyChanged -= SourceSelectionViewModelOnPropertyChanged;
        }

        _sourceSelectionViewModel = viewModel;

        if (_sourceSelectionViewModel is not null)
        {
            _sourceSelectionViewModel.PropertyChanged += SourceSelectionViewModelOnPropertyChanged;
            QueueSynchronizePreviewSelectionFromSource();
        }
    }

    private void SourceSelectionViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedVisualEditorNode) or
            nameof(MainViewModel.VisualEditorSourceSelectionVersion) or
            nameof(MainViewModel.VisualEditorCurrentContainerTitle) or
            nameof(MainViewModel.Control))
        {
            QueueSynchronizePreviewSelectionFromSource();
        }
    }

    private void QueueSynchronizePreviewSelectionFromSource()
    {
        if (_previewSelectionSyncQueued)
        {
            return;
        }

        _previewSelectionSyncQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _previewSelectionSyncQueued = false;
                SynchronizePreviewSelectionFromSource();
            },
            DispatcherPriority.Loaded);
    }

    private void PreviewViewOnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_designerDragMode != DesignerDragMode.None ||
            _sourceSelectionViewModel is not { Control: not null, SelectedVisualEditorNode: not null })
        {
            return;
        }

        QueueSynchronizePreviewSelectionFromSource();
    }

    private void SynchronizePreviewSelectionFromSource()
    {
        if (_sourceSelectionViewModel is not { } viewModel ||
            viewModel.Control is not { } previewRoot ||
            viewModel.ActiveXamlFile is not { } xamlFile ||
            viewModel.SelectedVisualEditorNode?.Element is not { } selected)
        {
            return;
        }

        var document = _sourceSelectionMutationEngine.Analyze(xamlFile.Text);
        var control = FindPreviewControlForXamlElement(previewRoot, document, selected);
        if (control is null || GetSelectionBounds(control) is not { } bounds)
        {
            return;
        }

        if (viewModel.GetVisualEditorCurrentContainerElement() is { } container &&
            FindPreviewControlForXamlElement(previewRoot, document, container) is { } containerControl &&
            GetSelectionBounds(containerControl) is { } containerBounds)
        {
            viewModel.UpdateVisualEditorPreviewCurrentContainerBounds(containerBounds);
        }
        else
        {
            viewModel.UpdateVisualEditorPreviewCurrentContainerBounds(null);
        }

        if (GetCurrentSelectionBounds(viewModel) is { } currentBounds &&
            SameBounds(currentBounds, bounds))
        {
            return;
        }

        viewModel.UpdateVisualEditorPreviewSelectionBounds(bounds);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (DataContext is not MainViewModel viewModel ||
            viewModel.Control is not { } previewRoot ||
            (!properties.IsLeftButtonPressed && properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed))
        {
            return;
        }

        if (!viewModel.VisualEditorDesignerMode)
        {
            return;
        }

        HandleDesignerPointerPressed(viewModel, previewRoot, e);
    }

    private void HandleDesignerPointerPressed(
        MainViewModel viewModel,
        Control previewRoot,
        PointerPressedEventArgs e)
    {
        var point = e.GetPosition(PreviewSurface);
        var currentSelection = GetCurrentSelectionBounds(viewModel);
        if (currentSelection is { } sourceBounds &&
            TryGetResizeDragMode(e.Source, out var sourceResizeMode))
        {
            StartDesignerDrag(
                sourceResizeMode,
                point,
                sourceBounds,
                e,
                FindLiveResizePreviewControl(viewModel, previewRoot, sourceBounds));
            return;
        }

        if (currentSelection is { } geometryBounds &&
            TryGetResizeDragMode(geometryBounds, point, out var resizeMode))
        {
            StartDesignerDrag(
                resizeMode,
                point,
                geometryBounds,
                e,
                FindLiveResizePreviewControl(viewModel, previewRoot, geometryBounds));
            return;
        }

        if (TrySelectPreviewControlAt(
                viewModel,
                point,
                previewRoot,
                e.KeyModifiers,
                e.ClickCount,
                out var selectionBounds))
        {
            if (e.ClickCount > 1 && viewModel.EnterVisualEditorCurrentContainer())
            {
                DesignerOverlay.Focus();
                e.Handled = true;
                return;
            }

            StartDesignerDrag(DesignerDragMode.Move, point, selectionBounds, e);
            return;
        }

        if (currentSelection is { } moveBounds && moveBounds.Contains(point))
        {
            StartDesignerDrag(
                DesignerDragMode.Move,
                point,
                moveBounds,
                e);
        }
        else
        {
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel ||
            !viewModel.VisualEditorDesignerMode)
        {
            if (DataContext is MainViewModel inactiveViewModel)
            {
                inactiveViewModel.ClearVisualEditorPreviewDropFeedback();
            }

            return;
        }

        var point = e.GetPosition(PreviewSurface);
        var delta = point - _dragStartPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragMoved = true;
        var bounds = IsResizeMode(_designerDragMode)
            ? ResizeBounds(_dragStartBounds, delta, _designerDragMode)
            : MoveBounds(_dragStartBounds, delta);

        _latestDragBounds = bounds;
        viewModel.UpdateVisualEditorPreviewSelectionBounds(bounds);
        if (IsResizeMode(_designerDragMode))
        {
            ApplyLiveResizeBounds(bounds);
            if (viewModel.Control is { } previewRoot)
            {
                UpdateAlignmentGuides(viewModel, previewRoot, bounds);
            }
        }
        else if (_designerDragMode == DesignerDragMode.Move)
        {
            UpdateMoveDropFeedback(viewModel, point, bounds);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel ||
            !viewModel.VisualEditorDesignerMode)
        {
            _designerDragMode = DesignerDragMode.None;
            _dragMoved = false;
            _liveResizeState = null;
            e.Pointer.Capture(null);
            if (DataContext is MainViewModel inactiveViewModel)
            {
                inactiveViewModel.ClearVisualEditorPreviewDropFeedback();
            }

            return;
        }

        var mode = _designerDragMode;
        _designerDragMode = DesignerDragMode.None;
        e.Pointer.Capture(null);

        try
        {
            if (_dragMoved)
            {
                ResetSelectionCycle();
                var point = e.GetPosition(PreviewSurface);
                var delta = point - _dragStartPoint;
                if (IsResizeMode(mode))
                {
                    viewModel.ResizeVisualEditorSelectionToBounds(
                        _dragStartBounds,
                        ResizeBounds(_dragStartBounds, delta, mode));
                }
                else if (TryMoveSelectionNearDropSibling(viewModel, point))
                {
                    viewModel.UpdateVisualEditorPreviewSelectionBounds(_dragStartBounds);
                }
                else if (!TryMoveSelectionIntoDropContainer(viewModel, point))
                {
                    viewModel.MoveVisualEditorSelectionBy(delta.X, delta.Y);
                }
                else
                {
                    viewModel.UpdateVisualEditorPreviewSelectionBounds(_dragStartBounds);
                }
            }
        }
        finally
        {
            _dragMoved = false;
            _liveResizeState = null;
            viewModel.ClearVisualEditorPreviewDropFeedback();
        }

        e.Handled = true;
    }

    private void StartDesignerDrag(
        DesignerDragMode mode,
        Point point,
        Rect bounds,
        PointerEventArgs e,
        Control? liveResizeControl = null)
    {
        _designerDragMode = mode;
        _dragStartPoint = point;
        _dragStartBounds = bounds;
        _latestDragBounds = bounds;
        _dragMoved = false;
        _liveResizeState = IsResizeMode(mode) && liveResizeControl is not null
            ? CreateLiveResizeState(liveResizeControl)
            : null;
        e.Pointer.Capture(DesignerOverlay);
        DesignerOverlay.Focus();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            !viewModel.VisualEditorDesignerMode)
        {
            return;
        }

        if (HandleDesignerKeyDown(viewModel, e.Key, e.KeyModifiers))
        {
            DesignerOverlay.Focus();
            e.Handled = true;
        }
    }

    private bool HandleDesignerKeyDown(
        MainViewModel viewModel,
        Key key,
        KeyModifiers modifiers)
    {
        if (_designerDragMode != DesignerDragMode.None)
        {
            return true;
        }

        var hasCommandModifier = HasCommandModifier(modifiers);
        var hasAlt = modifiers.HasFlag(KeyModifiers.Alt);
        var hasShift = modifiers.HasFlag(KeyModifiers.Shift);

        if (key is Key.Delete or Key.Back)
        {
            return ExecuteCommand(viewModel.DeleteVisualEditorElementCommand);
        }

        if (hasCommandModifier && key == Key.D)
        {
            return ExecuteCommand(viewModel.DuplicateVisualEditorElementCommand);
        }

        if (key == Key.Tab)
        {
            return viewModel.SelectAdjacentVisualEditorElement(hasShift);
        }

        if (key == Key.Enter)
        {
            return hasShift
                ? viewModel.SelectVisualEditorParentElement()
                : viewModel.EnterVisualEditorCurrentContainer();
        }

        if (key == Key.Escape)
        {
            return viewModel.SelectVisualEditorParentElement();
        }

        if (!IsArrowKey(key))
        {
            return false;
        }

        if (hasAlt && hasShift)
        {
            return ResizeSelectionByKeyboard(viewModel, key, 10);
        }

        if (hasAlt)
        {
            return TryMoveSelectionToDirectionalContainer(viewModel, key);
        }

        if (hasCommandModifier)
        {
            return ExecuteCommand(key is Key.Left or Key.Up
                ? viewModel.MoveVisualEditorElementUpCommand
                : viewModel.MoveVisualEditorElementDownCommand);
        }

        return MoveSelectionByKeyboard(viewModel, key, hasShift ? 10 : 1);
    }

    private static bool ExecuteCommand(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private bool MoveSelectionByKeyboard(MainViewModel viewModel, Key key, double step)
    {
        if (GetCurrentSelectionBounds(viewModel) is not { } bounds)
        {
            return false;
        }

        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default
        };
        if (delta == default)
        {
            return false;
        }

        var nextBounds = MoveBounds(bounds, delta);
        viewModel.UpdateVisualEditorPreviewSelectionBounds(nextBounds);
        if (viewModel.Control is { } previewRoot)
        {
            UpdateAlignmentGuides(viewModel, previewRoot, nextBounds);
        }

        var moved = viewModel.MoveVisualEditorSelectionBy(delta.X, delta.Y);
        if (!moved)
        {
            viewModel.UpdateVisualEditorPreviewSelectionBounds(bounds);
        }

        return moved;
    }

    private bool ResizeSelectionByKeyboard(MainViewModel viewModel, Key key, double step)
    {
        if (GetCurrentSelectionBounds(viewModel) is not { } bounds)
        {
            return false;
        }

        var widthDelta = key switch
        {
            Key.Left => -step,
            Key.Right => step,
            _ => 0
        };
        var heightDelta = key switch
        {
            Key.Up => -step,
            Key.Down => step,
            _ => 0
        };

        if (Math.Abs(widthDelta) < 0.1 && Math.Abs(heightDelta) < 0.1)
        {
            return false;
        }

        var nextBounds = new Rect(
            bounds.Position,
            new Size(
                Math.Max(1, bounds.Width + widthDelta),
                Math.Max(1, bounds.Height + heightDelta)));
        viewModel.UpdateVisualEditorPreviewSelectionBounds(nextBounds);
        if (viewModel.Control is { } previewRoot)
        {
            UpdateAlignmentGuides(viewModel, previewRoot, nextBounds);
        }

        var resized = viewModel.ResizeVisualEditorSelectionToBounds(bounds, nextBounds);
        if (!resized)
        {
            viewModel.UpdateVisualEditorPreviewSelectionBounds(bounds);
        }

        return resized;
    }

    private bool TryMoveSelectionToDirectionalContainer(MainViewModel viewModel, Key key)
    {
        if (viewModel.Control is not { } previewRoot ||
            GetCurrentSelectionBounds(viewModel) is not { } currentSelectionBounds)
        {
            return false;
        }

        var selectedControl = FindCurrentSelectedPreviewControl(viewModel, previewRoot);
        var sourceBounds = selectedControl is not null && GetSelectionBounds(selectedControl) is { } selectedBounds
            ? selectedBounds
            : currentSelectionBounds;
        var selectedParent = selectedControl?.GetVisualParent() as Control;
        var target = previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Where(control =>
                control.TemplatedParent is null &&
                IsDropContainer(control) &&
                IsPreviewDescendantOrRoot(control, previewRoot) &&
                !ReferenceEquals(control, selectedControl) &&
                !ReferenceEquals(control, selectedParent) &&
                (selectedControl is null ||
                 (!control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, selectedControl)) &&
                  !selectedControl.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, control)))) &&
                GetSelectionBounds(control) is { } bounds &&
                !ContainsBounds(bounds, sourceBounds) &&
                TryGetDirectionalContainerScore(sourceBounds, bounds, key, out _))
            .Select(control => new
            {
                Control = control,
                Bounds = GetSelectionBounds(control)!.Value
            })
            .Select(item => new
            {
                item.Control,
                Score = TryGetDirectionalContainerScore(sourceBounds, item.Bounds, key, out var score)
                    ? score
                    : double.MaxValue
            })
            .OrderBy(item => item.Score)
            .FirstOrDefault()
            ?.Control;

        if (target is null)
        {
            return false;
        }

        viewModel.ClearVisualEditorPreviewDropFeedback();
        return viewModel.MoveVisualEditorSelectionIntoPreviewControl(target);
    }

    private static bool HasCommandModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) ||
               modifiers.HasFlag(KeyModifiers.Meta);
    }

    private static bool IsArrowKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down;
    }

    private static bool ContainsBounds(Rect targetBounds, Rect sourceBounds)
    {
        return targetBounds.Contains(sourceBounds.TopLeft) &&
               targetBounds.Contains(sourceBounds.BottomRight);
    }

    private static bool TryGetDirectionalContainerScore(
        Rect sourceBounds,
        Rect targetBounds,
        Key key,
        out double score)
    {
        var sourceCenter = sourceBounds.Center;
        var targetCenter = targetBounds.Center;
        double primary;
        double secondary;

        switch (key)
        {
            case Key.Left when targetCenter.X < sourceCenter.X:
                primary = Math.Max(0, sourceBounds.Left - targetBounds.Right);
                secondary = Math.Abs(targetCenter.Y - sourceCenter.Y);
                break;
            case Key.Right when targetCenter.X > sourceCenter.X:
                primary = Math.Max(0, targetBounds.Left - sourceBounds.Right);
                secondary = Math.Abs(targetCenter.Y - sourceCenter.Y);
                break;
            case Key.Up when targetCenter.Y < sourceCenter.Y:
                primary = Math.Max(0, sourceBounds.Top - targetBounds.Bottom);
                secondary = Math.Abs(targetCenter.X - sourceCenter.X);
                break;
            case Key.Down when targetCenter.Y > sourceCenter.Y:
                primary = Math.Max(0, targetBounds.Top - sourceBounds.Bottom);
                secondary = Math.Abs(targetCenter.X - sourceCenter.X);
                break;
            default:
                score = 0;
                return false;
        }

        score = primary * 1000 + secondary;
        return true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var viewModel = DataContext as MainViewModel;
        if (viewModel is null || !viewModel.VisualEditorDesignerMode)
        {
            return;
        }

        if (!TryGetDraggedToolboxItem(viewModel, e, out var item) ||
            !TryCreateToolboxDropPlacement(viewModel, item, e.GetPosition(PreviewSurface), out var placement))
        {
            e.DragEffects = DragDropEffects.None;
            viewModel?.ClearVisualEditorPreviewDropFeedback();
            ClearToolboxDropPlacement();
            e.Handled = true;
            return;
        }

        viewModel.UpdateVisualEditorPreviewDropFeedback(
            placement.TargetBounds,
            placement.InsertionBounds,
            placement.PlaceholderBounds);
        UpdateToolboxDropGuides(viewModel, placement);
        StoreToolboxDropPlacement(viewModel, item, placement);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            !viewModel.VisualEditorDesignerMode)
        {
            return;
        }

        var point = e.GetPosition(PreviewSurface);
        var bounds = new Rect(0, 0, PreviewSurface.Bounds.Width, PreviewSurface.Bounds.Height);
        if (!bounds.Contains(point))
        {
            viewModel.ClearVisualEditorPreviewDropFeedback();
            ClearToolboxDropPlacement();
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var viewModel = DataContext as MainViewModel;
        if (viewModel is null || !viewModel.VisualEditorDesignerMode)
        {
            return;
        }

        if (!TryGetDraggedToolboxItem(viewModel, e, out var item) ||
            !TryGetCurrentToolboxDropPlacement(viewModel, item, e.GetPosition(PreviewSurface), out var placement))
        {
            e.DragEffects = DragDropEffects.None;
            viewModel?.ClearVisualEditorPreviewDropFeedback();
            ClearToolboxDropPlacement();
            e.Handled = true;
            return;
        }

        viewModel.SelectedVisualEditorToolboxItem = item;
        if (viewModel.InsertToolboxItemIntoPreviewControl(
                item,
                placement.Target,
                placement.ChildIndex,
                placement.CanvasPosition,
                placement.TargetElement))
        {
            e.DragEffects = DragDropEffects.Copy;
            viewModel.UpdateVisualEditorPreviewSelectionBounds(placement.PlaceholderBounds);
            if (viewModel.RunCommand.CanExecute(null))
            {
                viewModel.RunCommand.Execute(null);
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        viewModel.ClearVisualEditorPreviewDropFeedback();
        ClearToolboxDropPlacement();
        QueueSynchronizePreviewSelectionFromSource();
        e.Handled = true;
    }

    private bool TryGetCurrentToolboxDropPlacement(
        MainViewModel viewModel,
        ToolboxItemDescriptor item,
        Point point,
        out ToolboxDropPlacement placement)
    {
        if (_lastToolboxDropPlacement is { } cached &&
            string.Equals(_lastToolboxDropItemId, item.Id, StringComparison.Ordinal) &&
            string.Equals(_lastToolboxDropDocumentText, viewModel.ActiveXamlFile?.Text, StringComparison.Ordinal) &&
            cached.TargetBounds.Contains(point))
        {
            placement = cached;
            return true;
        }

        return TryCreateToolboxDropPlacement(viewModel, item, point, out placement);
    }

    private void StoreToolboxDropPlacement(
        MainViewModel viewModel,
        ToolboxItemDescriptor item,
        ToolboxDropPlacement placement)
    {
        _lastToolboxDropPlacement = placement;
        _lastToolboxDropItemId = item.Id;
        _lastToolboxDropDocumentText = viewModel.ActiveXamlFile?.Text;
    }

    private void ClearToolboxDropPlacement()
    {
        _lastToolboxDropPlacement = null;
        _lastToolboxDropItemId = null;
        _lastToolboxDropDocumentText = null;
    }

    private static bool TryGetDraggedToolboxItem(
        MainViewModel viewModel,
        DragEventArgs e,
        out ToolboxItemDescriptor item)
    {
        item = null!;
        if (!ToolboxDragPayload.TryGetItemId(e.DataTransfer, out var itemId))
        {
            return false;
        }

        var candidate = viewModel.VisualEditorToolboxItems.FirstOrDefault(toolboxItem =>
            string.Equals(toolboxItem.Id, itemId, StringComparison.Ordinal));
        if (candidate is null)
        {
            return false;
        }

        item = candidate;
        return true;
    }

    private bool TryCreateToolboxDropPlacement(
        MainViewModel viewModel,
        ToolboxItemDescriptor item,
        Point point,
        out ToolboxDropPlacement placement)
    {
        placement = default;
        if (viewModel.Control is not { } previewRoot)
        {
            return false;
        }

        var target = FindDropContainerAt(point, previewRoot);
        if (target is null || GetSelectionBounds(target) is not { } targetBounds)
        {
            return false;
        }

        if (!TryResolvePreviewControl(
                viewModel,
                previewRoot,
                target,
                out _,
                out var targetElement,
                out _))
        {
            return false;
        }

        if (target is Panel panel)
        {
            placement = CreatePanelDropPlacement(panel, targetElement, item, point, targetBounds);
            return true;
        }

        placement = new ToolboxDropPlacement(
            target,
            targetElement,
            ChildIndex: 0,
            CanvasPosition: null,
            TargetBounds: targetBounds,
            InsertionBounds: null,
            PlaceholderBounds: DeflateForPlaceholder(targetBounds));
        return true;
    }

    private ToolboxDropPlacement CreatePanelDropPlacement(
        Panel panel,
        XamlElementSnapshot targetElement,
        ToolboxItemDescriptor item,
        Point point,
        Rect targetBounds)
    {
        if (panel is Canvas)
        {
            var size = EstimateToolboxPlaceholderSize(item);
            var placeholder = ClampRectToBounds(
                new Rect(point, size),
                targetBounds);
            var canvasPosition = new Point(
                Math.Max(0, point.X - targetBounds.X),
                Math.Max(0, point.Y - targetBounds.Y));

            return new ToolboxDropPlacement(
                panel,
                targetElement,
                ChildIndex: null,
                CanvasPosition: canvasPosition,
                TargetBounds: targetBounds,
                InsertionBounds: null,
                PlaceholderBounds: placeholder);
        }

        var children = GetDirectPanelChildren(panel)
            .Select(child => (Control: child, Bounds: GetSelectionBounds(child)))
            .Where(static child => child.Bounds is not null)
            .Select(static child => new ChildPlacement(child.Control, child.Bounds!.Value))
            .ToArray();

        if (children.Length == 0)
        {
            var placeholder = CreateEmptyPanelPlaceholder(targetBounds, item, point, GetPanelInsertionOrientation(panel));
            return new ToolboxDropPlacement(
                panel,
                targetElement,
                ChildIndex: 0,
                CanvasPosition: null,
                TargetBounds: targetBounds,
                InsertionBounds: null,
                PlaceholderBounds: placeholder);
        }

        var orientation = GetPanelInsertionOrientation(panel);
        var childIndex = GetPanelChildInsertionIndex(children, point, orientation);
        var insertion = CreatePanelInsertionBounds(children, childIndex, orientation);
        var placeholderBounds = CreatePanelPlaceholderBounds(
            targetBounds,
            children,
            childIndex,
            orientation,
            EstimateToolboxPlaceholderSize(item));

        return new ToolboxDropPlacement(
            panel,
            targetElement,
            childIndex,
            CanvasPosition: null,
            TargetBounds: targetBounds,
            InsertionBounds: insertion,
            PlaceholderBounds: placeholderBounds);
    }

    private Control? FindDropContainerAt(Point point, Control previewRoot)
    {
        var hit = FindSelectablePreviewControlAt(point, previewRoot);
        var candidates = hit is null
            ? new[] { previewRoot }
            : new[] { hit }.Concat(hit.GetVisualAncestors().OfType<Control>());

        foreach (var candidate in candidates)
        {
            if (!IsPreviewDescendantOrRoot(candidate, previewRoot))
            {
                continue;
            }

            if (IsDropContainer(candidate) &&
                GetSelectionBounds(candidate) is { } bounds &&
                bounds.Contains(point))
            {
                return candidate;
            }

            if (ReferenceEquals(candidate, previewRoot))
            {
                break;
            }
        }

        return null;
    }

    private static bool IsDropContainer(Control control)
    {
        return control switch
        {
            Panel => true,
            Decorator decorator => decorator.Child is null,
            ContentControl contentControl => contentControl.Content is null,
            _ => false
        };
    }

    private static bool IsEmptyDropContainer(Control control)
    {
        return control switch
        {
            Panel panel => !GetDirectPanelChildren(panel).Any(),
            Decorator { Child: null } => true,
            ContentControl { Content: null } => true,
            _ => false
        };
    }

    private Rect? GetSelectionBounds(Control control)
    {
        var topLeft = control.TranslatePoint(default, PreviewSurface);
        if (topLeft is null || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return null;
        }

        return new Rect(topLeft.Value, control.Bounds.Size);
    }

    private Control? FindSelectablePreviewControlAt(Point point, Control previewRoot)
    {
        return EnumerateSelectablePreviewControlsAt(point, previewRoot)
            .FirstOrDefault();
    }

    private Control? FindPreviewControlForXamlElement(
        Control previewRoot,
        XamlDocumentSnapshot document,
        XamlElementSnapshot selected)
    {
        return previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Where(control =>
                control.TemplatedParent is null &&
                IsPreviewDescendantOrRoot(control, previewRoot))
            .Select(control => new
            {
                Control = control,
                Element = ResolvePreviewControlElement(previewRoot, control, document),
                Depth = control.GetVisualAncestors().Count()
            })
            .Where(item => item.Element is not null && SameXamlElement(item.Element, selected))
            .OrderByDescending(item => item.Depth)
            .FirstOrDefault()
            ?.Control;
    }

    private Control? FindCurrentSelectedPreviewControl(MainViewModel viewModel, Control previewRoot)
    {
        if (viewModel.ActiveXamlFile is not { } xamlFile ||
            viewModel.SelectedVisualEditorNode?.Element is not { } selected)
        {
            return null;
        }

        var document = _sourceSelectionMutationEngine.Analyze(xamlFile.Text);
        return FindPreviewControlForXamlElement(previewRoot, document, selected);
    }

    private Control? FindLiveResizePreviewControl(
        MainViewModel viewModel,
        Control previewRoot,
        Rect selectionBounds)
    {
        return FindCurrentSelectedPreviewControl(viewModel, previewRoot) ??
               FindPreviewControlBySelectionBounds(previewRoot, selectionBounds) ??
               FindPreviewControlNearSelectionBounds(
                   previewRoot,
                   selectionBounds,
                   viewModel.SelectedVisualEditorNode?.Element.TypeName);
    }

    private Control? FindPreviewControlBySelectionBounds(Control previewRoot, Rect selectionBounds)
    {
        return previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Where(control =>
                control.TemplatedParent is null &&
                IsPreviewDescendantOrRoot(control, previewRoot) &&
                GetSelectionBounds(control) is { } bounds &&
                SameBounds(bounds, selectionBounds))
            .OrderByDescending(control => control.GetVisualAncestors().Count())
            .FirstOrDefault();
    }

    private Control? FindPreviewControlNearSelectionBounds(
        Control previewRoot,
        Rect selectionBounds,
        string? selectedTypeName)
    {
        var candidates = previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Where(control =>
                control.TemplatedParent is null &&
                IsPreviewDescendantOrRoot(control, previewRoot) &&
                GetSelectionBounds(control) is { })
            .Select(control =>
            {
                var bounds = GetSelectionBounds(control)!.Value;
                var typeMatches = MatchesXamlType(control, selectedTypeName);
                var overlapArea = GetOverlapArea(bounds, selectionBounds);
                return new LiveResizeCandidate(
                    control,
                    typeMatches,
                    overlapArea,
                    GetCenterDistanceSquared(bounds, selectionBounds),
                    Math.Abs(bounds.Width - selectionBounds.Width) + Math.Abs(bounds.Height - selectionBounds.Height),
                    control.GetVisualAncestors().Count());
            })
            .Where(candidate => candidate.TypeMatches || candidate.OverlapArea > 0)
            .OrderByDescending(candidate => candidate.TypeMatches)
            .ThenByDescending(candidate => candidate.OverlapArea)
            .ThenBy(candidate => candidate.CenterDistanceSquared)
            .ThenBy(candidate => candidate.SizeDifference)
            .ThenByDescending(candidate => candidate.Depth)
            .ToArray();

        return candidates.Length == 0
            ? null
            : candidates[0].Control;
    }

    private static bool SameXamlElement(XamlElementSnapshot? left, XamlElementSnapshot right)
    {
        return left is not null &&
               left.Path.Count == right.Path.Count &&
               left.Path.SequenceEqual(right.Path);
    }

    private bool TryMoveSelectionIntoDropContainer(MainViewModel viewModel, Point point)
    {
        if (viewModel.Control is not { } previewRoot)
        {
            return false;
        }

        var target = FindDropContainerAt(point, previewRoot);

        if (target is null)
        {
            return false;
        }

        var resolvedTarget = TryResolvePreviewControl(
            viewModel,
            previewRoot,
            target,
            out _,
            out var targetElement,
            out _)
                ? targetElement
                : null;

        return viewModel.MoveVisualEditorSelectionIntoPreviewControl(target, resolvedTarget);
    }

    private bool TryMoveSelectionNearDropSibling(MainViewModel viewModel, Point point)
    {
        if (viewModel.Control is not { } previewRoot)
        {
            return false;
        }

        var target = FindDropSiblingAt(point, previewRoot);
        if (target is null)
        {
            return false;
        }

        var bounds = GetSelectionBounds(target);
        if (bounds is null)
        {
            return false;
        }

        var after = IsAfterPlacement(target, point, bounds.Value);
        var resolvedTarget = TryResolvePreviewControl(
            viewModel,
            previewRoot,
            target,
            out _,
            out var targetElement,
            out _)
                ? targetElement
                : null;

        return viewModel.MoveVisualEditorSelectionNearPreviewControl(target, after, resolvedTarget);
    }

    private void UpdateMoveDropFeedback(MainViewModel viewModel, Point point, Rect activeBounds)
    {
        if (viewModel.Control is not { } previewRoot)
        {
            viewModel.ClearVisualEditorPreviewDropFeedback();
            return;
        }

        if (TryCreateMoveDropPlacement(previewRoot, point, activeBounds, out var placement))
        {
            viewModel.UpdateVisualEditorPreviewDropFeedback(
                placement.TargetBounds,
                placement.InsertionBounds,
                placement.PlaceholderBounds);
            UpdateAlignmentGuides(viewModel, previewRoot, activeBounds);
            return;
        }

        UpdateAlignmentGuides(viewModel, previewRoot, activeBounds);
        viewModel.UpdateVisualEditorPreviewDropFeedback(null, null);
    }

    private bool TryCreateMoveDropPlacement(
        Control previewRoot,
        Point point,
        Rect activeBounds,
        out MoveDropPlacement placement)
    {
        placement = default;
        var sibling = FindDropSiblingAt(point, previewRoot);
        if (sibling is not null && GetSelectionBounds(sibling) is { } siblingBounds)
        {
            var after = IsAfterPlacement(sibling, point, siblingBounds);
            var parentBounds = FindDropParentBounds(sibling, previewRoot) ?? siblingBounds;
            placement = new MoveDropPlacement(
                parentBounds,
                CreateInsertionBounds(sibling, siblingBounds, after),
                CreateMovePlaceholderBounds(
                    parentBounds,
                    siblingBounds,
                    after,
                    GetInsertionOrientation(sibling),
                    activeBounds.Size));
            return true;
        }

        var target = FindDropContainerAt(point, previewRoot);
        if (target is not null && GetSelectionBounds(target) is { } targetBounds)
        {
            placement = new MoveDropPlacement(
                targetBounds,
                InsertionBounds: null,
                PlaceholderBounds: CreateInsideDropPlaceholder(target, targetBounds, activeBounds.Size, point));
            return true;
        }

        return false;
    }

    private Rect? FindDropParentBounds(Control control, Control previewRoot)
    {
        for (var parent = control.GetVisualParent() as Control; parent is not null; parent = parent.GetVisualParent() as Control)
        {
            if (!IsPreviewDescendantOrRoot(parent, previewRoot))
            {
                continue;
            }

            if (parent is Panel or Decorator or ContentControl)
            {
                return GetSelectionBounds(parent);
            }

            if (ReferenceEquals(parent, previewRoot))
            {
                break;
            }
        }

        return GetSelectionBounds(previewRoot);
    }

    private Control? FindDropSiblingAt(Point point, Control previewRoot)
    {
        if (FindDropContainerAt(point, previewRoot) is { } container &&
            IsEmptyDropContainer(container))
        {
            return null;
        }

        return FindSelectablePreviewControlAt(
            point,
            previewRoot,
            control => !ReferenceEquals(control, previewRoot) &&
                       GetSelectionBounds(control) is { } bounds &&
                       !SameBounds(bounds, _dragStartBounds));
    }

    private Control? FindSelectablePreviewControlAt(
        Point point,
        Control previewRoot,
        Func<Control, bool> predicate)
    {
        return EnumerateSelectablePreviewControlsAt(point, previewRoot, predicate)
            .FirstOrDefault();
    }

    private bool TrySelectPreviewControlAt(
        MainViewModel viewModel,
        Point point,
        Control previewRoot,
        KeyModifiers modifiers,
        int clickCount,
        out Rect selectionBounds)
    {
        var candidates = CreateSelectionCandidatesAt(viewModel, point, previewRoot);
        if (candidates.Length == 0)
        {
            selectionBounds = default;
            return false;
        }

        var scopedCandidates = candidates
            .Where(candidate => viewModel.IsVisualEditorCandidateInCurrentContainerSubtree(candidate.Element))
            .ToArray();
        if (scopedCandidates.Length > 0)
        {
            candidates = scopedCandidates;
        }

        var deepSelect = HasCommandModifier(modifiers);
        var defaultCandidate = deepSelect
            ? candidates[0]
            : candidates.FirstOrDefault(candidate => viewModel.IsVisualEditorCandidateInCurrentContainer(candidate.Element)) ??
              candidates[0];
        var cycleCandidates = CreateSelectionCycleOrder(candidates, defaultCandidate);
        var useCycle = deepSelect ||
                       clickCount > 1 ||
                       IsSameSelectionCycle(point, cycleCandidates);
        var selected = useCycle
            ? GetNextSelectionCycleCandidate(point, cycleCandidates, defaultCandidate)
            : SetSelectionCycleCandidate(point, cycleCandidates, defaultCandidate);

        selectionBounds = selected.Bounds;
        return viewModel.SelectVisualEditorPreviewElement(
            selected.Document,
            selected.Element,
            selected.Bounds,
            useCycle ? "from preview cycle" : "from preview");
    }

    private PreviewSelectionCandidate[] CreateSelectionCandidatesAt(
        MainViewModel viewModel,
        Point point,
        Control previewRoot)
    {
        var candidates = new List<PreviewSelectionCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var control in EnumerateSelectablePreviewControlsAt(point, previewRoot))
        {
            if (GetSelectionBounds(control) is not { } bounds ||
                !TryResolvePreviewControl(
                    viewModel,
                    previewRoot,
                    control,
                    out var document,
                    out var element,
                    out _))
            {
                continue;
            }

            var key = GetPathKey(element.Path);
            if (!seen.Add(key))
            {
                continue;
            }

            candidates.Add(new PreviewSelectionCandidate(control, bounds, document, element));
        }

        return candidates.ToArray();
    }

    private bool TryResolvePreviewControl(
        MainViewModel viewModel,
        Control previewRoot,
        Control control,
        out XamlDocumentSnapshot document,
        out XamlElementSnapshot element,
        out IReadOnlyList<string> diagnostics)
    {
        if (viewModel.TryResolveVisualEditorPreviewControl(control, out var mappedDocument, out var mappedElement, out diagnostics))
        {
            document = mappedDocument;
            element = mappedElement;
            return true;
        }

        document = null!;
        element = null!;
        if (viewModel.ActiveXamlFile is not { } xamlFile)
        {
            diagnostics = new[] { "No XAML document selected." };
            return false;
        }

        if (!TryGetXamlStructuralPath(previewRoot, control, out var path))
        {
            diagnostics = new[] { "The selected visual could not be mapped to a XAML source element." };
            return false;
        }

        document = _sourceSelectionMutationEngine.Analyze(xamlFile.Text);
        if (FindDocumentElementByPath(document, path) is not { } structuralElement)
        {
            diagnostics = new[] { "The selected visual structural path could not be mapped to XAML." };
            return false;
        }

        element = structuralElement;
        diagnostics = Array.Empty<string>();
        return true;
    }

    private XamlElementSnapshot? ResolvePreviewControlElement(
        Control previewRoot,
        Control control,
        XamlDocumentSnapshot document)
    {
        var visualNode = _sourceSelectionSnapshotService.Snapshot(control);
        if (_sourceSelectionMapper.FindXamlElement(visualNode, document) is { } mapped)
        {
            return mapped;
        }

        return TryGetXamlStructuralPath(previewRoot, control, out var path)
            ? FindDocumentElementByPath(document, path)
            : null;
    }

    private static XamlElementSnapshot? FindDocumentElementByPath(
        XamlDocumentSnapshot document,
        IReadOnlyList<int> path)
    {
        return document.Elements.FirstOrDefault(element =>
            element.Path.Count == path.Count &&
            element.Path.SequenceEqual(path));
    }

    private static bool TryGetXamlStructuralPath(
        Control previewRoot,
        Control target,
        out int[] path)
    {
        var segments = new List<int>();
        if (TryGetXamlStructuralPath(previewRoot, target, segments))
        {
            path = segments.ToArray();
            return true;
        }

        path = Array.Empty<int>();
        return false;
    }

    private static bool TryGetXamlStructuralPath(
        Control current,
        Control target,
        List<int> path)
    {
        if (ReferenceEquals(current, target))
        {
            return true;
        }

        var children = GetXamlStructuralChildren(current).ToArray();
        for (var i = 0; i < children.Length; i++)
        {
            path.Add(i);
            if (TryGetXamlStructuralPath(children[i], target, path))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static IEnumerable<Control> GetXamlStructuralChildren(Control control)
    {
        return control switch
        {
            Panel panel => GetDirectPanelChildren(panel),
            Decorator { Child: Control child } when child.TemplatedParent is null => new[] { child },
            ContentControl { Content: Control child } when child.TemplatedParent is null => new[] { child },
            _ => Array.Empty<Control>()
        };
    }

    private static PreviewSelectionCandidate[] CreateSelectionCycleOrder(
        IReadOnlyList<PreviewSelectionCandidate> candidates,
        PreviewSelectionCandidate defaultCandidate)
    {
        return candidates
            .OrderBy(candidate => ReferenceEquals(candidate, defaultCandidate) ? 0 : 1)
            .ThenByDescending(candidate => candidate.Element.Path.Count > defaultCandidate.Element.Path.Count)
            .ThenByDescending(candidate => candidate.Element.Path.Count)
            .ToArray();
    }

    private bool IsSameSelectionCycle(
        Point point,
        IReadOnlyList<PreviewSelectionCandidate> candidates)
    {
        return _selectionCycleIndex >= 0 &&
               Distance(point, _selectionCyclePoint) <= 3 &&
               string.Equals(_selectionCycleKey, CreateSelectionCycleKey(candidates), StringComparison.Ordinal);
    }

    private PreviewSelectionCandidate GetNextSelectionCycleCandidate(
        Point point,
        IReadOnlyList<PreviewSelectionCandidate> candidates,
        PreviewSelectionCandidate fallback)
    {
        if (candidates.Count == 0)
        {
            return fallback;
        }

        var key = CreateSelectionCycleKey(candidates);
        if (!string.Equals(_selectionCycleKey, key, StringComparison.Ordinal) ||
            Distance(point, _selectionCyclePoint) > 3)
        {
            return SetSelectionCycleCandidate(point, candidates, fallback);
        }

        _selectionCycleIndex = (_selectionCycleIndex + 1) % candidates.Count;
        _selectionCyclePoint = point;
        return candidates[_selectionCycleIndex];
    }

    private PreviewSelectionCandidate SetSelectionCycleCandidate(
        Point point,
        IReadOnlyList<PreviewSelectionCandidate> candidates,
        PreviewSelectionCandidate selected)
    {
        _selectionCycleKey = CreateSelectionCycleKey(candidates);
        _selectionCyclePoint = point;
        _selectionCycleIndex = Math.Max(0, IndexOfCandidate(candidates, selected));
        return selected;
    }

    private static int IndexOfCandidate(
        IReadOnlyList<PreviewSelectionCandidate> candidates,
        PreviewSelectionCandidate selected)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (ReferenceEquals(candidates[index], selected))
            {
                return index;
            }
        }

        return -1;
    }

    private void ResetSelectionCycle()
    {
        _selectionCycleKey = string.Empty;
        _selectionCycleIndex = -1;
    }

    private static string CreateSelectionCycleKey(IEnumerable<PreviewSelectionCandidate> candidates)
    {
        return string.Join("|", candidates.Select(candidate => GetPathKey(candidate.Element.Path)));
    }

    private static string GetPathKey(IEnumerable<int> path)
    {
        return string.Join("/", path);
    }

    private static double Distance(Point left, Point right)
    {
        var delta = left - right;
        return Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
    }

    private IEnumerable<Control> EnumerateSelectablePreviewControlsAt(
        Point point,
        Control previewRoot,
        Func<Control, bool>? predicate = null)
    {
        return previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Select((control, index) => new
            {
                Control = control,
                Index = index,
                Depth = control.GetVisualAncestors().Count()
            })
            .Where(item =>
                (predicate?.Invoke(item.Control) ?? true) &&
                item.Control.TemplatedParent is null &&
                IsPreviewDescendantOrRoot(item.Control, previewRoot) &&
                GetSelectionBounds(item.Control) is { } bounds &&
                bounds.Contains(point))
            .OrderByDescending(item => item.Depth)
            .ThenByDescending(item => item.Index)
            .Select(static item => item.Control);
    }

    private void UpdateToolboxDropGuides(MainViewModel viewModel, ToolboxDropPlacement placement)
    {
        if (viewModel.Control is { } previewRoot)
        {
            UpdateAlignmentGuides(viewModel, previewRoot, placement.PlaceholderBounds);
        }
    }

    private void UpdateAlignmentGuides(MainViewModel viewModel, Control previewRoot, Rect activeBounds)
    {
        var candidates = previewRoot
            .GetVisualDescendants()
            .OfType<Control>()
            .Append(previewRoot)
            .Where(control =>
                control.TemplatedParent is null &&
                IsPreviewDescendantOrRoot(control, previewRoot) &&
                GetSelectionBounds(control) is { } bounds &&
                !SameBounds(bounds, _dragStartBounds) &&
                !SameBounds(bounds, activeBounds))
            .Select(control => new GuideCandidate(control, GetSelectionBounds(control)!.Value))
            .ToArray();

        var vertical = FindNearestVerticalGuide(activeBounds, candidates);
        var horizontal = FindNearestHorizontalGuide(activeBounds, candidates);
        var measurementPoint = ClampPointToPreview(
            new Point(activeBounds.Right + 8, activeBounds.Bottom + 8),
            new Size(PreviewSurface.Bounds.Width, PreviewSurface.Bounds.Height));

        viewModel.UpdateVisualEditorPreviewGuides(
            vertical?.GuideBounds,
            horizontal?.GuideBounds,
            measurementPoint,
            $"{Math.Round(activeBounds.Width)} x {Math.Round(activeBounds.Height)}  X {Math.Round(activeBounds.X)}  Y {Math.Round(activeBounds.Y)}");
    }

    private static GuideMatch? FindNearestVerticalGuide(Rect activeBounds, IReadOnlyList<GuideCandidate> candidates)
    {
        var activeEdges = new[] { activeBounds.Left, activeBounds.Center.X, activeBounds.Right };
        var best = default(GuideMatch?);

        foreach (var candidate in candidates)
        {
            var candidateEdges = new[] { candidate.Bounds.Left, candidate.Bounds.Center.X, candidate.Bounds.Right };
            foreach (var activeEdge in activeEdges)
            {
                foreach (var candidateEdge in candidateEdges)
                {
                    var distance = Math.Abs(activeEdge - candidateEdge);
                    if (distance > AlignmentGuideSnapDistance ||
                        best is { } currentBest && currentBest.Distance <= distance)
                    {
                        continue;
                    }

                    var top = Math.Min(activeBounds.Top, candidate.Bounds.Top);
                    var bottom = Math.Max(activeBounds.Bottom, candidate.Bounds.Bottom);
                    best = new GuideMatch(
                        distance,
                        new Rect(candidateEdge, top, 1, Math.Max(1, bottom - top)));
                }
            }
        }

        return best;
    }

    private static GuideMatch? FindNearestHorizontalGuide(Rect activeBounds, IReadOnlyList<GuideCandidate> candidates)
    {
        var activeEdges = new[] { activeBounds.Top, activeBounds.Center.Y, activeBounds.Bottom };
        var best = default(GuideMatch?);

        foreach (var candidate in candidates)
        {
            var candidateEdges = new[] { candidate.Bounds.Top, candidate.Bounds.Center.Y, candidate.Bounds.Bottom };
            foreach (var activeEdge in activeEdges)
            {
                foreach (var candidateEdge in candidateEdges)
                {
                    var distance = Math.Abs(activeEdge - candidateEdge);
                    if (distance > AlignmentGuideSnapDistance ||
                        best is { } currentBest && currentBest.Distance <= distance)
                    {
                        continue;
                    }

                    var left = Math.Min(activeBounds.Left, candidate.Bounds.Left);
                    var right = Math.Max(activeBounds.Right, candidate.Bounds.Right);
                    best = new GuideMatch(
                        distance,
                        new Rect(left, candidateEdge, Math.Max(1, right - left), 1));
                }
            }
        }

        return best;
    }

    private static Rect CreateMovePlaceholderBounds(
        Rect parentBounds,
        Rect siblingBounds,
        bool after,
        Orientation orientation,
        Size size)
    {
        Rect placeholder;
        if (orientation == Orientation.Horizontal)
        {
            var width = Math.Max(24, Math.Min(size.Width, Math.Max(24, parentBounds.Width - 8)));
            var height = Math.Max(16, Math.Min(parentBounds.Height - 8, Math.Max(siblingBounds.Height, size.Height)));
            var x = after ? siblingBounds.Right - width / 2 : siblingBounds.Left - width / 2;
            var y = siblingBounds.Center.Y - height / 2;
            placeholder = new Rect(x, y, width, height);
        }
        else
        {
            var width = Math.Max(24, Math.Min(parentBounds.Width - 8, Math.Max(siblingBounds.Width, size.Width)));
            var height = Math.Max(16, Math.Min(size.Height, Math.Max(16, parentBounds.Height - 8)));
            var x = parentBounds.Left + 4;
            var y = after ? siblingBounds.Bottom - height / 2 : siblingBounds.Top - height / 2;
            placeholder = new Rect(x, y, width, height);
        }

        return ClampRectToBounds(placeholder, parentBounds);
    }

    private static Rect CreateInsideDropPlaceholder(Control target, Rect targetBounds, Size size, Point point)
    {
        if (target is Panel panel)
        {
            return CreateEmptyPanelPlaceholder(targetBounds, size, point, GetPanelInsertionOrientation(panel));
        }

        return DeflateForPlaceholder(targetBounds);
    }

    private static Rect CreateEmptyPanelPlaceholder(
        Rect targetBounds,
        Size size,
        Point point,
        Orientation orientation)
    {
        var placeholder = orientation == Orientation.Horizontal
            ? new Rect(point.X, targetBounds.Top + 4, Math.Max(24, size.Width), Math.Max(16, targetBounds.Height - 8))
            : new Rect(targetBounds.Left + 4, point.Y, Math.Max(24, targetBounds.Width - 8), Math.Max(16, size.Height));

        return ClampRectToBounds(placeholder, targetBounds);
    }

    private static Point ClampPointToPreview(Point point, Size previewSize)
    {
        if (previewSize.Width <= 0 || previewSize.Height <= 0)
        {
            return point;
        }

        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, previewSize.Width - 96)),
            Math.Clamp(point.Y, 0, Math.Max(0, previewSize.Height - 24)));
    }

    private static Rect? GetCurrentSelectionBounds(MainViewModel viewModel)
    {
        return viewModel.VisualEditorPreviewSelectionVisible
            ? new Rect(
                viewModel.VisualEditorPreviewSelectionLeft,
                viewModel.VisualEditorPreviewSelectionTop,
                viewModel.VisualEditorPreviewSelectionWidth,
                viewModel.VisualEditorPreviewSelectionHeight)
            : null;
    }

    private static bool TryGetResizeDragMode(Rect bounds, Point point, out DesignerDragMode mode)
    {
        var handles = new[]
        {
            (Mode: DesignerDragMode.ResizeNorthWest, Bounds: ThumbBounds(bounds.Left, bounds.Top)),
            (Mode: DesignerDragMode.ResizeNorth, Bounds: ThumbBounds(bounds.Center.X, bounds.Top)),
            (Mode: DesignerDragMode.ResizeNorthEast, Bounds: ThumbBounds(bounds.Right, bounds.Top)),
            (Mode: DesignerDragMode.ResizeWest, Bounds: ThumbBounds(bounds.Left, bounds.Center.Y)),
            (Mode: DesignerDragMode.ResizeEast, Bounds: ThumbBounds(bounds.Right, bounds.Center.Y)),
            (Mode: DesignerDragMode.ResizeSouthWest, Bounds: ThumbBounds(bounds.Left, bounds.Bottom)),
            (Mode: DesignerDragMode.ResizeSouth, Bounds: ThumbBounds(bounds.Center.X, bounds.Bottom)),
            (Mode: DesignerDragMode.ResizeSouthEast, Bounds: ThumbBounds(bounds.Right, bounds.Bottom))
        };

        foreach (var handle in handles)
        {
            if (handle.Bounds.Contains(point))
            {
                mode = handle.Mode;
                return true;
            }
        }

        mode = DesignerDragMode.None;
        return false;
    }

    private static bool TryGetResizeDragMode(object? source, out DesignerDragMode mode)
    {
        for (var current = source as Control; current is not null; current = current.GetVisualParent() as Control)
        {
            if (TryGetResizeDragMode(current.Name, out mode))
            {
                return true;
            }

            if (string.Equals(current.Name, "DesignerOverlay", StringComparison.Ordinal))
            {
                break;
            }
        }

        mode = DesignerDragMode.None;
        return false;
    }

    private static bool TryGetResizeDragMode(string? name, out DesignerDragMode mode)
    {
        mode = name switch
        {
            "ResizeNorthWestThumb" => DesignerDragMode.ResizeNorthWest,
            "ResizeNorthThumb" => DesignerDragMode.ResizeNorth,
            "ResizeNorthEastThumb" => DesignerDragMode.ResizeNorthEast,
            "ResizeWestThumb" => DesignerDragMode.ResizeWest,
            "ResizeEastThumb" => DesignerDragMode.ResizeEast,
            "ResizeSouthWestThumb" => DesignerDragMode.ResizeSouthWest,
            "ResizeSouthThumb" => DesignerDragMode.ResizeSouth,
            "ResizeSouthEastThumb" => DesignerDragMode.ResizeSouthEast,
            _ => DesignerDragMode.None
        };

        return IsResizeMode(mode);
    }

    private static Rect MoveBounds(Rect bounds, Vector delta)
    {
        return new Rect(bounds.Position + delta, bounds.Size);
    }

    private static LiveResizeState CreateLiveResizeState(Control control)
    {
        return new LiveResizeState(
            control,
            GetCanvasCoordinate(Canvas.GetLeft(control)),
            GetCanvasCoordinate(Canvas.GetTop(control)),
            control.Margin);
    }

    private void ApplyLiveResizeBounds(Rect bounds)
    {
        if (_liveResizeState is null &&
            DataContext is MainViewModel viewModel &&
            viewModel.Control is { } previewRoot &&
            FindLiveResizePreviewControl(viewModel, previewRoot, _dragStartBounds) is { } liveResizeControl)
        {
            _liveResizeState = CreateLiveResizeState(liveResizeControl);
        }

        if (_liveResizeState is not { } state)
        {
            return;
        }

        var control = state.Control;
        var positionDelta = bounds.Position - _dragStartBounds.Position;
        control.Width = Math.Max(1, bounds.Width);
        control.Height = Math.Max(1, bounds.Height);

        if (control.GetVisualParent() is Canvas)
        {
            Canvas.SetLeft(control, state.CanvasLeft + positionDelta.X);
            Canvas.SetTop(control, state.CanvasTop + positionDelta.Y);
        }
        else if (Math.Abs(positionDelta.X) >= 0.1 || Math.Abs(positionDelta.Y) >= 0.1)
        {
            control.Margin = new Thickness(
                state.Margin.Left + positionDelta.X,
                state.Margin.Top + positionDelta.Y,
                state.Margin.Right,
                state.Margin.Bottom);
        }

        control.InvalidateMeasure();
        control.InvalidateArrange();
        if (control.GetVisualParent() is Layoutable parent)
        {
            parent.InvalidateMeasure();
            parent.InvalidateArrange();
        }
    }

    private static double GetCanvasCoordinate(double value)
    {
        return double.IsNaN(value) ? 0 : value;
    }

    private static Rect ResizeBounds(Rect bounds, Vector delta, DesignerDragMode mode)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (mode is DesignerDragMode.ResizeNorthWest or DesignerDragMode.ResizeWest or DesignerDragMode.ResizeSouthWest)
        {
            left = Math.Min(right - 1, left + delta.X);
        }

        if (mode is DesignerDragMode.ResizeNorthWest or DesignerDragMode.ResizeNorth or DesignerDragMode.ResizeNorthEast)
        {
            top = Math.Min(bottom - 1, top + delta.Y);
        }

        if (mode is DesignerDragMode.ResizeNorthEast or DesignerDragMode.ResizeEast or DesignerDragMode.ResizeSouthEast)
        {
            right = Math.Max(left + 1, right + delta.X);
        }

        if (mode is DesignerDragMode.ResizeSouthWest or DesignerDragMode.ResizeSouth or DesignerDragMode.ResizeSouthEast)
        {
            bottom = Math.Max(top + 1, bottom + delta.Y);
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private static Rect ThumbBounds(double centerX, double centerY)
    {
        return new Rect(
            centerX - ResizeHandleSize / 2,
            centerY - ResizeHandleSize / 2,
            ResizeHandleSize,
            ResizeHandleSize);
    }

    private static bool IsResizeMode(DesignerDragMode mode)
    {
        return mode is DesignerDragMode.ResizeNorthWest
            or DesignerDragMode.ResizeNorth
            or DesignerDragMode.ResizeNorthEast
            or DesignerDragMode.ResizeWest
            or DesignerDragMode.ResizeEast
            or DesignerDragMode.ResizeSouthWest
            or DesignerDragMode.ResizeSouth
            or DesignerDragMode.ResizeSouthEast;
    }

    private static bool SameBounds(Rect left, Rect right)
    {
        return Math.Abs(left.X - right.X) < 0.5 &&
               Math.Abs(left.Y - right.Y) < 0.5 &&
               Math.Abs(left.Width - right.Width) < 0.5 &&
               Math.Abs(left.Height - right.Height) < 0.5;
    }

    private static bool MatchesXamlType(Control control, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var localTypeName = GetLocalXamlTypeName(typeName);
        return string.Equals(control.GetType().Name, typeName, StringComparison.Ordinal) ||
               string.Equals(control.GetType().Name, localTypeName, StringComparison.Ordinal);
    }

    private static string GetLocalXamlTypeName(string typeName)
    {
        var namespaceIndex = typeName.IndexOf(':', StringComparison.Ordinal);
        if (namespaceIndex >= 0)
        {
            return typeName[(namespaceIndex + 1)..];
        }

        var typeIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);
        return typeIndex >= 0
            ? typeName[(typeIndex + 1)..]
            : typeName;
    }

    private static double GetOverlapArea(Rect left, Rect right)
    {
        var x = Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
        var y = Math.Max(0, Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Top, right.Top));
        return x * y;
    }

    private static double GetCenterDistanceSquared(Rect left, Rect right)
    {
        var delta = left.Center - right.Center;
        return delta.X * delta.X + delta.Y * delta.Y;
    }

    private static bool IsAfterPlacement(Control target, Point point, Rect bounds)
    {
        return GetInsertionOrientation(target) == Orientation.Horizontal
            ? point.X >= bounds.Center.X
            : point.Y >= bounds.Center.Y;
    }

    private static Rect CreateInsertionBounds(Control target, Rect bounds, bool after)
    {
        const double thickness = 3;
        if (GetInsertionOrientation(target) == Orientation.Horizontal)
        {
            var x = after ? bounds.Right - thickness / 2 : bounds.Left - thickness / 2;
            return new Rect(x, bounds.Top, thickness, bounds.Height);
        }

        var y = after ? bounds.Bottom - thickness / 2 : bounds.Top - thickness / 2;
        return new Rect(bounds.Left, y, bounds.Width, thickness);
    }

    private static IEnumerable<Control> GetDirectPanelChildren(Panel panel)
    {
        return panel.Children
            .OfType<Control>()
            .Where(static child => child.TemplatedParent is null);
    }

    private static int GetPanelChildInsertionIndex(
        IReadOnlyList<ChildPlacement> children,
        Point point,
        Orientation orientation)
    {
        var coordinate = orientation == Orientation.Horizontal ? point.X : point.Y;
        for (var i = 0; i < children.Count; i++)
        {
            var bounds = children[i].Bounds;
            var center = orientation == Orientation.Horizontal ? bounds.Center.X : bounds.Center.Y;
            if (coordinate < center)
            {
                return i;
            }
        }

        return children.Count;
    }

    private static Rect CreatePanelInsertionBounds(
        IReadOnlyList<ChildPlacement> children,
        int childIndex,
        Orientation orientation)
    {
        const double thickness = 3;
        var clampedIndex = Math.Clamp(childIndex, 0, children.Count);
        var reference = clampedIndex < children.Count
            ? children[clampedIndex].Bounds
            : children[^1].Bounds;
        var after = clampedIndex >= children.Count;

        if (orientation == Orientation.Horizontal)
        {
            var x = after ? reference.Right - thickness / 2 : reference.Left - thickness / 2;
            return new Rect(x, reference.Top, thickness, reference.Height);
        }

        var y = after ? reference.Bottom - thickness / 2 : reference.Top - thickness / 2;
        return new Rect(reference.Left, y, reference.Width, thickness);
    }

    private static Rect CreatePanelPlaceholderBounds(
        Rect targetBounds,
        IReadOnlyList<ChildPlacement> children,
        int childIndex,
        Orientation orientation,
        Size size)
    {
        var clampedIndex = Math.Clamp(childIndex, 0, children.Count);
        var reference = clampedIndex < children.Count
            ? children[clampedIndex].Bounds
            : children[^1].Bounds;
        var after = clampedIndex >= children.Count;

        Rect placeholder;
        if (orientation == Orientation.Horizontal)
        {
            var width = Math.Max(24, size.Width);
            var height = Math.Max(16, Math.Min(targetBounds.Height - 8, Math.Max(reference.Height, size.Height)));
            var x = after ? reference.Right - width / 2 : reference.Left - width / 2;
            var y = reference.Center.Y - height / 2;
            placeholder = new Rect(x, y, width, height);
        }
        else
        {
            var width = Math.Max(24, targetBounds.Width - 8);
            var height = Math.Max(16, size.Height);
            var x = targetBounds.Left + 4;
            var y = after ? reference.Bottom - height / 2 : reference.Top - height / 2;
            placeholder = new Rect(x, y, width, height);
        }

        return ClampRectToBounds(placeholder, targetBounds);
    }

    private static Rect CreateEmptyPanelPlaceholder(
        Rect targetBounds,
        ToolboxItemDescriptor item,
        Point point,
        Orientation orientation)
    {
        var size = EstimateToolboxPlaceholderSize(item);
        var placeholder = orientation == Orientation.Horizontal
            ? new Rect(point.X, targetBounds.Top + 4, size.Width, Math.Max(16, targetBounds.Height - 8))
            : new Rect(targetBounds.Left + 4, point.Y, Math.Max(24, targetBounds.Width - 8), size.Height);

        return ClampRectToBounds(placeholder, targetBounds);
    }

    private static Rect DeflateForPlaceholder(Rect bounds)
    {
        return bounds.Width <= 8 || bounds.Height <= 8
            ? bounds
            : bounds.Deflate(4);
    }

    private static Rect ClampRectToBounds(Rect rect, Rect bounds)
    {
        var width = bounds.Width > 0 ? Math.Min(rect.Width, bounds.Width) : rect.Width;
        var height = bounds.Height > 0 ? Math.Min(rect.Height, bounds.Height) : rect.Height;
        var maxX = bounds.Right - width;
        var maxY = bounds.Bottom - height;
        var x = maxX >= bounds.Left ? Math.Clamp(rect.X, bounds.Left, maxX) : bounds.Left;
        var y = maxY >= bounds.Top ? Math.Clamp(rect.Y, bounds.Top, maxY) : bounds.Top;
        return new Rect(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    private static Size EstimateToolboxPlaceholderSize(ToolboxItemDescriptor item)
    {
        var typeName = item.TypeName;
        if (typeName.Contains("Panel", StringComparison.Ordinal) ||
            typeName is "Border" or "Grid" or "Canvas" or "DockPanel" or "WrapPanel")
        {
            return new Size(160, 96);
        }

        if (typeName.Contains("TextBox", StringComparison.Ordinal) ||
            typeName.Contains("ComboBox", StringComparison.Ordinal) ||
            typeName.Contains("Picker", StringComparison.Ordinal))
        {
            return new Size(160, 32);
        }

        if (typeName.Contains("Button", StringComparison.Ordinal) ||
            typeName.Contains("CheckBox", StringComparison.Ordinal) ||
            typeName.Contains("RadioButton", StringComparison.Ordinal) ||
            typeName.Contains("Toggle", StringComparison.Ordinal))
        {
            return new Size(112, 32);
        }

        if (typeName is "Slider" or "ProgressBar")
        {
            return new Size(160, 24);
        }

        if (typeName is "TextBlock")
        {
            return new Size(140, 24);
        }

        return new Size(120, 32);
    }

    private static Orientation GetInsertionOrientation(Control target)
    {
        var parent = target.GetVisualParent();
        while (parent is not null)
        {
            if (parent is StackPanel stackPanel)
            {
                return stackPanel.Orientation;
            }

            if (parent is WrapPanel wrapPanel)
            {
                return wrapPanel.Orientation;
            }

            if (parent is Panel)
            {
                return Orientation.Vertical;
            }

            parent = parent.GetVisualParent();
        }

        return Orientation.Vertical;
    }

    private static Orientation GetPanelInsertionOrientation(Panel panel)
    {
        return panel switch
        {
            StackPanel stackPanel => stackPanel.Orientation,
            WrapPanel wrapPanel => wrapPanel.Orientation,
            _ => Orientation.Vertical
        };
    }

    private static IEnumerable<Control> EnumerateSelectablePreviewControls(Control source, Control previewRoot)
    {
        var controls = new[] { source }.Concat(source.GetVisualAncestors().OfType<Control>());
        foreach (var control in controls)
        {
            if (control is PreviewView or ScrollViewer or ExclusiveContentControl)
            {
                yield break;
            }

            if (!IsPreviewDescendantOrRoot(control, previewRoot))
            {
                continue;
            }

            if (control.TemplatedParent is null)
            {
                yield return control;
            }

            if (ReferenceEquals(control, previewRoot))
            {
                yield break;
            }
        }
    }

    private static bool IsPreviewDescendantOrRoot(Control control, Control previewRoot)
    {
        return ReferenceEquals(control, previewRoot) ||
               control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, previewRoot));
    }

    private readonly record struct ChildPlacement(Control Control, Rect Bounds);

    private readonly record struct LiveResizeState(
        Control Control,
        double CanvasLeft,
        double CanvasTop,
        Thickness Margin);

    private readonly record struct ToolboxDropPlacement(
        Control Target,
        XamlElementSnapshot TargetElement,
        int? ChildIndex,
        Point? CanvasPosition,
        Rect TargetBounds,
        Rect? InsertionBounds,
        Rect PlaceholderBounds);

    private readonly record struct MoveDropPlacement(
        Rect TargetBounds,
        Rect? InsertionBounds,
        Rect PlaceholderBounds);

    private readonly record struct GuideCandidate(Control Control, Rect Bounds);

    private readonly record struct GuideMatch(double Distance, Rect GuideBounds);

    private readonly record struct LiveResizeCandidate(
        Control? Control,
        bool TypeMatches,
        double OverlapArea,
        double CenterDistanceSquared,
        double SizeDifference,
        int Depth);

    private sealed record PreviewSelectionCandidate(
        Control Control,
        Rect Bounds,
        XamlDocumentSnapshot Document,
        XamlElementSnapshot Element);

    private enum DesignerDragMode
    {
        None,
        Move,
        ResizeNorthWest,
        ResizeNorth,
        ResizeNorthEast,
        ResizeWest,
        ResizeEast,
        ResizeSouthWest,
        ResizeSouth,
        ResizeSouthEast
    }
}
