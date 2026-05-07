using AvaloniaEdit.Document;
using XamlPlayground.Services.Editing;

namespace XamlPlayground.Tests;

public sealed class XamlEditorTypingServiceTests
{
    [Fact]
    public void Equals_InAttribute_InsertsQuotes()
    {
        var document = new TextDocument { Text = "<Button Content=" };
        var caretOffset = document.TextLength;

        var handled = XamlEditorTypingService.TryHandleTextEntered(document, caretOffset, "=", out var newCaretOffset);

        Assert.True(handled);
        Assert.Equal("<Button Content=\"\"", document.Text);
        Assert.Equal("<Button Content=\"".Length, newCaretOffset);
    }

    [Fact]
    public void GreaterThan_AfterOpeningElement_InsertsEndTag()
    {
        var document = new TextDocument { Text = "<Grid>" };
        var caretOffset = document.TextLength;

        var handled = XamlEditorTypingService.TryHandleTextEntered(document, caretOffset, ">", out var newCaretOffset);

        Assert.True(handled);
        Assert.Equal("<Grid></Grid>", document.Text);
        Assert.Equal("<Grid>".Length, newCaretOffset);
    }

    [Fact]
    public void GreaterThan_AfterSelfClosingElement_DoesNotInsertEndTag()
    {
        var document = new TextDocument { Text = "<Button />" };
        var caretOffset = document.TextLength;

        var handled = XamlEditorTypingService.TryHandleTextEntered(document, caretOffset, ">", out var newCaretOffset);

        Assert.False(handled);
        Assert.Equal("<Button />", document.Text);
        Assert.Equal(caretOffset, newCaretOffset);
    }

    [Fact]
    public void Slash_AfterLessThan_CompletesParentEndTag()
    {
        var document = new TextDocument { Text = "<Grid>\n  </" };
        var caretOffset = document.TextLength;

        var handled = XamlEditorTypingService.TryHandleTextEntered(document, caretOffset, "/", out var newCaretOffset);

        Assert.True(handled);
        Assert.Equal("<Grid>\n  </Grid>", document.Text);
        Assert.Equal(document.TextLength, newCaretOffset);
    }

    [Fact]
    public void Enter_BetweenOpeningAndClosingTags_InsertsIndentedElementBreak()
    {
        var document = new TextDocument { Text = "<Grid></Grid>" };
        var caretOffset = "<Grid>".Length;

        var handled = XamlEditorTypingService.TryInsertElementBreak(document, caretOffset, out var newCaretOffset);

        Assert.True(handled);
        Assert.Equal("<Grid>\n  \n</Grid>", document.Text);
        Assert.Equal("<Grid>\n  ".Length, newCaretOffset);
    }

    [Fact]
    public void Rename_OpeningTag_UpdatesClosingTag()
    {
        var document = new TextDocument { Text = "<Grid></Grid>" };
        document.Replace(1, "Grid".Length, "StackPanel");
        var caretOffset = "<StackPanel".Length;

        var handled = XamlEditorTypingService.TrySynchronizeTagRename(document, caretOffset, ref caretOffset);

        Assert.True(handled);
        Assert.Equal("<StackPanel></StackPanel>", document.Text);
        Assert.Equal("<StackPanel".Length, caretOffset);
    }

    [Fact]
    public void Rename_ClosingTag_UpdatesOpeningTag()
    {
        var document = new TextDocument { Text = "<Grid></Grid>" };
        var closingNameOffset = "<Grid></".Length;
        document.Replace(closingNameOffset, "Grid".Length, "StackPanel");
        var caretOffset = "<Grid></StackPanel".Length;

        var handled = XamlEditorTypingService.TrySynchronizeTagRename(document, caretOffset, ref caretOffset);

        Assert.True(handled);
        Assert.Equal("<StackPanel></StackPanel>", document.Text);
        Assert.Equal("<StackPanel></StackPanel".Length, caretOffset);
    }

    [Fact]
    public void Rename_NestedOpeningTag_UpdatesMatchingNestedClosingTag()
    {
        var document = new TextDocument { Text = "<Grid><Border></Border></Grid>" };
        var borderNameOffset = "<Grid><".Length;
        document.Replace(borderNameOffset, "Border".Length, "Button");
        var caretOffset = "<Grid><Button".Length;

        var handled = XamlEditorTypingService.TrySynchronizeTagRename(document, caretOffset, ref caretOffset);

        Assert.True(handled);
        Assert.Equal("<Grid><Button></Button></Grid>", document.Text);
        Assert.Equal("<Grid><Button".Length, caretOffset);
    }

    [Fact]
    public void Rename_OpeningTagAfterDeletion_UpdatesClosingTag()
    {
        var document = new TextDocument { Text = "<Grid></Grid>" };
        var deletedNameCharOffset = "<Gri".Length;
        document.Remove(deletedNameCharOffset, 1);
        var caretOffset = deletedNameCharOffset;

        var handled = XamlEditorTypingService.TrySynchronizeTagRename(document, deletedNameCharOffset, ref caretOffset);

        Assert.True(handled);
        Assert.Equal("<Gri></Gri>", document.Text);
        Assert.Equal(deletedNameCharOffset, caretOffset);
    }

    [Fact]
    public void Rename_ClosingTagAfterDeletion_UpdatesOpeningTag()
    {
        var document = new TextDocument { Text = "<Grid></Grid>" };
        var deletedNameCharOffset = "<Grid></Gri".Length;
        document.Remove(deletedNameCharOffset, 1);
        var caretOffset = deletedNameCharOffset;

        var handled = XamlEditorTypingService.TrySynchronizeTagRename(document, deletedNameCharOffset, ref caretOffset);

        Assert.True(handled);
        Assert.Equal("<Gri></Gri>", document.Text);
        Assert.Equal("<Gri></Gri".Length, caretOffset);
    }
}
