using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace XamlPlayground.Behaviors;

public sealed class TextEditorDocument : AvaloniaObject
{
    public static readonly AttachedProperty<TextDocument?> DocumentProperty =
        AvaloniaProperty.RegisterAttached<TextEditorDocument, TextEditor, TextDocument?>("Document");

    private TextEditorDocument()
    {
    }

    public static TextDocument? GetDocument(TextEditor editor)
    {
        return editor.GetValue(DocumentProperty);
    }

    public static void SetDocument(TextEditor editor, TextDocument? document)
    {
        editor.SetValue(DocumentProperty, document);
    }

    static TextEditorDocument()
    {
        DocumentProperty.Changed.AddClassHandler<TextEditor>(OnDocumentChanged);
    }

    private static void OnDocumentChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is TextDocument document)
        {
            if (ReferenceEquals(editor.Document, document))
            {
                return;
            }

            TextEditorBehavior.PrepareForDocumentReplacement(editor);
            editor.Document = document;
            return;
        }

        editor.Document ??= new TextDocument();
    }
}
