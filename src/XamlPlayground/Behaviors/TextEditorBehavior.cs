using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

namespace XamlPlayground.Behaviors;

public class TextEditorBehavior : Behavior<TextEditor>
{
    public static readonly StyledProperty<string?> ExtensionProperty = 
        AvaloniaProperty.Register<TextEditorBehavior, string?>(nameof(Extension));

    private TextEditor? _textEditor;

    public string? Extension
    {
        get => GetValue(ExtensionProperty);
        set => SetValue(ExtensionProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is not { } textEditor)
        {
            return;
        }

        _textEditor = textEditor;

        _textEditor.TextArea.SelectionCornerRadius = 0;
        _textEditor.TextArea.KeyDown += TextAreaOnKeyDown;

        _textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Extension);
    }

    protected override void OnDetaching()
    {
        if (_textEditor is { } textEditor)
        {
            textEditor.TextArea.KeyDown -= TextAreaOnKeyDown;
        }

        _textEditor = null;

        base.OnDetaching();
    }

    private void TextAreaOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textEditor is null || e.Key != Key.A)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var isSelectAllGesture = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        var hasOtherModifiers = (modifiers & ~(KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (!isSelectAllGesture || hasOtherModifiers)
        {
            return;
        }

        _textEditor.SelectAll();
        e.Handled = true;
    }
}
