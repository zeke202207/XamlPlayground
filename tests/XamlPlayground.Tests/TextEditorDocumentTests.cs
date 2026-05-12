using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
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

    [Fact]
    public void AttachedDocument_SwapsFoldedEditorDocumentWithoutStaleFoldingGenerator()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var firstDocument = new TextDocument
            {
                Text = """
                       <Grid>
                         <StackPanel>
                           <Button />
                         </StackPanel>
                       </Grid>
                       """
            };
            var secondDocument = new TextDocument
            {
                Text = """
                       <Canvas>
                         <Border />
                       </Canvas>
                       """
            };
            var editor = new TextEditor
            {
                Width = 320,
                Height = 240,
                ShowLineNumbers = true
            };
            Interaction.GetBehaviors(editor).Add(new TextEditorBehavior { Extension = ".xml" });
            var window = new Window
            {
                Width = 360,
                Height = 280,
                Content = editor
            };

            try
            {
                TextEditorDocument.SetDocument(editor, firstDocument);
                window.Show();
                PumpLayout(window);

                TextEditorDocument.SetDocument(editor, secondDocument);
                PumpLayout(window);

                Assert.Same(secondDocument, editor.Document);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }
}
