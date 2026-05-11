using System;
using Avalonia;
using Avalonia.Controls;
using XamlPlayground.Behaviors;
using XamlPlayground.ViewModels.Docking;

namespace XamlPlayground.Views.Docking;

public partial class WorkspaceFileEditorDockView : UserControl
{
    private WorkspaceFileDocumentDockViewModel? _dockable;
    private bool _applyingVisualEditorSourceSelection;

    public WorkspaceFileEditorDockView()
    {
        InitializeComponent();
        Editor.TextArea.Caret.PositionChanged += EditorOnCaretPositionChanged;
        Editor.TextArea.SelectionChanged += EditorOnTextSelectionChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_dockable is not null)
        {
            _dockable.PropertyChanged -= DockableOnPropertyChanged;
        }

        _dockable = DataContext as WorkspaceFileDocumentDockViewModel;
        if (_dockable is not null)
        {
            _dockable.PropertyChanged += DockableOnPropertyChanged;
        }

        ApplyVisualEditorSourceSelection();
        base.OnDataContextChanged(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Editor.TextArea.Caret.PositionChanged -= EditorOnCaretPositionChanged;
        Editor.TextArea.SelectionChanged -= EditorOnTextSelectionChanged;

        if (_dockable is not null)
        {
            _dockable.PropertyChanged -= DockableOnPropertyChanged;
            _dockable = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void DockableOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceFileDocumentDockViewModel.VisualEditorSourceSelectionFilePath) or
            nameof(WorkspaceFileDocumentDockViewModel.VisualEditorSourceSelectionStart) or
            nameof(WorkspaceFileDocumentDockViewModel.VisualEditorSourceSelectionLength) or
            nameof(WorkspaceFileDocumentDockViewModel.VisualEditorSourceSelectionVersion))
        {
            ApplyVisualEditorSourceSelection();
        }
    }

    private void ApplyVisualEditorSourceSelection()
    {
        if (_dockable is null)
        {
            TextEditorSourceSelection.SetIsEnabled(Editor, false);
            return;
        }

        try
        {
            _applyingVisualEditorSourceSelection = true;
            TextEditorSourceSelection.SetIsEnabled(Editor, _dockable.File.IsXaml);
            TextEditorSourceSelection.SetSourcePath(Editor, _dockable.VisualEditorSourceSelectionFilePath);
            TextEditorSourceSelection.SetTargetPath(Editor, _dockable.File.Path);
            TextEditorSourceSelection.SetSourceStart(Editor, _dockable.VisualEditorSourceSelectionStart);
            TextEditorSourceSelection.SetSourceLength(Editor, _dockable.VisualEditorSourceSelectionLength);
            TextEditorSourceSelection.SetSourceVersion(Editor, _dockable.VisualEditorSourceSelectionVersion);
        }
        finally
        {
            _applyingVisualEditorSourceSelection = false;
        }
    }

    private void EditorOnCaretPositionChanged(object? sender, EventArgs e)
    {
        SelectVisualEditorElementFromEditor(useSelectionRange: false);
    }

    private void EditorOnTextSelectionChanged(object? sender, EventArgs e)
    {
        SelectVisualEditorElementFromEditor(useSelectionRange: Editor.SelectionLength > 0);
    }

    private void SelectVisualEditorElementFromEditor(bool useSelectionRange)
    {
        if (_applyingVisualEditorSourceSelection ||
            _dockable is not { File.IsXaml: true } dockable ||
            Editor.Document is null)
        {
            return;
        }

        dockable.Shell.SelectVisualEditorSourceRange(
            dockable.File.Path,
            useSelectionRange ? Editor.SelectionStart : Editor.CaretOffset,
            useSelectionRange ? Editor.SelectionLength : 0,
            Editor.CaretOffset);
    }
}
