using System;
using Avalonia;
using AvaloniaEdit;

namespace XamlPlayground.Behaviors;

public sealed class TextEditorSourceSelection : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, bool>("IsEnabled");

    public static readonly AttachedProperty<int> SourceStartProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, int>("SourceStart");

    public static readonly AttachedProperty<int> SourceLengthProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, int>("SourceLength");

    public static readonly AttachedProperty<int> SourceVersionProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, int>("SourceVersion");

    public static readonly AttachedProperty<string?> SourcePathProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, string?>("SourcePath");

    public static readonly AttachedProperty<string?> TargetPathProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, string?>("TargetPath");

    private static readonly AttachedProperty<bool> IsSubscribedProperty =
        AvaloniaProperty.RegisterAttached<TextEditorSourceSelection, TextEditor, bool>("IsSubscribed");

    private TextEditorSourceSelection()
    {
    }

    static TextEditorSourceSelection()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextEditor>(OnSelectionPropertyChanged);
        SourceVersionProperty.Changed.AddClassHandler<TextEditor>(OnSelectionPropertyChanged);
        SourcePathProperty.Changed.AddClassHandler<TextEditor>(OnSelectionPropertyChanged);
        TargetPathProperty.Changed.AddClassHandler<TextEditor>(OnSelectionPropertyChanged);
    }

    public static bool GetIsEnabled(TextEditor editor)
    {
        return editor.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(TextEditor editor, bool value)
    {
        editor.SetValue(IsEnabledProperty, value);
    }

    public static int GetSourceStart(TextEditor editor)
    {
        return editor.GetValue(SourceStartProperty);
    }

    public static void SetSourceStart(TextEditor editor, int value)
    {
        editor.SetValue(SourceStartProperty, value);
    }

    public static int GetSourceLength(TextEditor editor)
    {
        return editor.GetValue(SourceLengthProperty);
    }

    public static void SetSourceLength(TextEditor editor, int value)
    {
        editor.SetValue(SourceLengthProperty, value);
    }

    public static int GetSourceVersion(TextEditor editor)
    {
        return editor.GetValue(SourceVersionProperty);
    }

    public static void SetSourceVersion(TextEditor editor, int value)
    {
        editor.SetValue(SourceVersionProperty, value);
    }

    public static string? GetSourcePath(TextEditor editor)
    {
        return editor.GetValue(SourcePathProperty);
    }

    public static void SetSourcePath(TextEditor editor, string? value)
    {
        editor.SetValue(SourcePathProperty, value);
    }

    public static string? GetTargetPath(TextEditor editor)
    {
        return editor.GetValue(TargetPathProperty);
    }

    public static void SetTargetPath(TextEditor editor, string? value)
    {
        editor.SetValue(TargetPathProperty, value);
    }

    private static bool GetIsSubscribed(TextEditor editor)
    {
        return editor.GetValue(IsSubscribedProperty);
    }

    private static void SetIsSubscribed(TextEditor editor, bool value)
    {
        editor.SetValue(IsSubscribedProperty, value);
    }

    private static void OnSelectionPropertyChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs _)
    {
        UpdateDocumentSubscription(editor);
        ApplySelection(editor);
    }

    private static void UpdateDocumentSubscription(TextEditor editor)
    {
        var shouldSubscribe = GetIsEnabled(editor);
        var isSubscribed = GetIsSubscribed(editor);

        if (shouldSubscribe == isSubscribed)
        {
            return;
        }

        if (shouldSubscribe)
        {
            editor.DocumentChanged += EditorOnDocumentChanged;
        }
        else
        {
            editor.DocumentChanged -= EditorOnDocumentChanged;
        }

        SetIsSubscribed(editor, shouldSubscribe);
    }

    private static void EditorOnDocumentChanged(object? sender, EventArgs e)
    {
        if (sender is TextEditor editor)
        {
            ApplySelection(editor);
        }
    }

    private static void ApplySelection(TextEditor editor)
    {
        if (!GetIsEnabled(editor) ||
            editor.Document is null ||
            !string.Equals(GetSourcePath(editor), GetTargetPath(editor), StringComparison.Ordinal))
        {
            ClearSelection(editor);
            return;
        }

        var documentLength = editor.Document.TextLength;
        var start = Math.Clamp(GetSourceStart(editor), 0, documentLength);
        var length = Math.Clamp(GetSourceLength(editor), 0, documentLength - start);
        if (length == 0)
        {
            ClearSelection(editor, start);
            return;
        }

        editor.Select(start, length);
        editor.CaretOffset = start + length;

        var line = editor.Document.GetLineByOffset(start);
        editor.ScrollToLine(line.LineNumber);
    }

    private static void ClearSelection(TextEditor editor, int? caretOffset = null)
    {
        if (editor.Document is null)
        {
            return;
        }

        var offset = Math.Clamp(caretOffset ?? editor.CaretOffset, 0, editor.Document.TextLength);
        editor.Select(offset, 0);
        editor.CaretOffset = offset;
    }
}
