using AvaloniaEdit;
using AvaloniaEdit.Document;
using XamlPlayground.Behaviors;

namespace XamlPlayground.Tests;

public sealed class TextEditorDocumentTests
{
    [Fact]
    public void AttachedDocument_DoesNotClearTextEditorDocument_WhenBindingSuppliesNull()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new TextEditor();
        var fallbackDocument = editor.Document;

        TextEditorDocument.SetDocument(editor, null);

        Assert.NotNull(editor.Document);
        Assert.Same(fallbackDocument, editor.Document);
    }

    [Fact]
    public void AttachedDocument_AcceptsEmptyTextDocument()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new TextEditor();
        var emptyDocument = new TextDocument { Text = string.Empty };

        TextEditorDocument.SetDocument(editor, emptyDocument);

        Assert.Same(emptyDocument, editor.Document);
        Assert.Equal(string.Empty, editor.Document.Text);
    }
}
