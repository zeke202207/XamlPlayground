using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using XamlPlayground.Behaviors;
using XamlPlayground.Services.IntelliSense;
using XamlPlayground.ViewModels;
using XamlPlayground.Workspace;

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
    public void TextEditorBehavior_InstallsEditorContextMenu()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new TextEditor
        {
            Document = new TextDocument("<Grid />")
        };

        Interaction.GetBehaviors(editor).Add(new TextEditorBehavior { Extension = ".axaml" });

        Assert.NotNull(editor.ContextMenu);
    }

    [Fact]
    public void TextEditorBehavior_ContextMenuIncludesVsCodeStyleEditorActions()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new TextEditor
        {
            Document = new TextDocument("<Grid />")
        };
        var behavior = new TextEditorBehavior { Extension = ".axaml" };

        Interaction.GetBehaviors(editor).Add(behavior);

        var method = typeof(TextEditorBehavior).GetMethod(
            "EditorContextMenuOnOpening",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(behavior, [editor.ContextMenu, new CancelEventArgs()]);

        var headers = editor.ContextMenu!.Items
            .OfType<MenuItem>()
            .Select(static item => item.Header?.ToString())
            .ToArray();

        Assert.Contains("Cut", headers);
        Assert.Contains("Copy", headers);
        Assert.Contains("Paste", headers);
        Assert.Contains("Peek Definition", headers);
        Assert.Contains("Go to Definition", headers);
        Assert.Contains("Peek References", headers);
        Assert.Contains("Show Suggestions", headers);
        Assert.Contains("Quick Info", headers);
        Assert.Contains("Format Document", headers);
        Assert.Contains("Toggle Line Comment", headers);
        Assert.Contains("Fold All", headers);
        Assert.Contains("Select All", headers);
    }

    [Fact]
    public void TextEditorBehavior_GoToDefinitionOpensCrossFileWorkspaceDocument()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var solution = new InMemorySolution("Workspace");
        var project = new InMemoryProject("Demo", "Demo", "msbuild");
        const string definitionText = "namespace Demo; public sealed class Customer { }";
        var definitionFile = project.AddFile(new InMemoryProjectFile(
            "Models/Customer.cs",
            definitionText,
            ProjectFileKind.CSharp));
        var currentFile = project.AddFile(new InMemoryProjectFile(
            "Views/MainViewModel.cs",
            "namespace Demo; public sealed class MainViewModel { Customer? Current; }",
            ProjectFileKind.CSharp));
        solution.Projects.Add(project);
        var viewModel = new MainViewModel(null);
        LoadSolution(viewModel, solution);
        var editor = new TextEditor
        {
            Document = new TextDocument(currentFile.Text)
        };
        var behavior = new TextEditorBehavior
        {
            Extension = ".cs",
            Project = project,
            File = currentFile,
            Shell = viewModel
        };
        Interaction.GetBehaviors(editor).Add(behavior);
        var start = definitionText.IndexOf("Customer", StringComparison.Ordinal);
        var location = new EditorLocation(
            definitionFile.Path,
            start,
            start + "Customer".Length,
            1,
            start + 1,
            definitionText);

        var method = typeof(TextEditorBehavior).GetMethod(
            "TryNavigateToLocation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = Assert.IsType<bool>(method.Invoke(behavior, [location]));

        Assert.True(result);
        Assert.Same(definitionFile, viewModel.ActiveWorkspaceFile);
        Assert.Same(definitionFile, viewModel.ActiveCodeFile);
        Assert.Equal(definitionFile.Path, viewModel.WorkspaceEditorNavigationFilePath);
        Assert.Equal(start, viewModel.WorkspaceEditorNavigationStart);
        Assert.Equal("Customer".Length, viewModel.WorkspaceEditorNavigationLength);
        Assert.Equal(1, viewModel.WorkspaceEditorNavigationVersion);
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

    private static void LoadSolution(MainViewModel viewModel, InMemorySolution solution)
    {
        var method = typeof(MainViewModel).GetMethod(
            "LoadSolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [solution]);
    }
}
