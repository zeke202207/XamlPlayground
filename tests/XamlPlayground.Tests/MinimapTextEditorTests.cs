using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using XamlPlayground.Behaviors;
using XamlPlayground.Editor.Minimap;
using XamlPlayground.Editor.Minimap.Inline;
using XamlPlayground.Services.Editing.InlineFeatures;
using XamlPlayground.ViewModels;
using XamlPlayground.Workspace;

namespace XamlPlayground.Tests;

public sealed class MinimapTextEditorTests
{
    [Fact]
    public void MinimapTextEditor_UsesVsCodeCompatibleDefaults()
    {
        var editor = new MinimapTextEditor();

        Assert.True(editor.MinimapEnabled);
        Assert.Equal(TextMinimapAutoHide.None, editor.MinimapAutoHide);
        Assert.Equal(TextMinimapSide.Right, editor.MinimapSide);
        Assert.Equal(TextMinimapSize.Proportional, editor.MinimapSize);
        Assert.Equal(TextMinimapSliderVisibility.MouseOver, editor.MinimapShowSlider);
        Assert.True(editor.MinimapRenderCharacters);
        Assert.Equal(120, editor.MinimapMaxColumn);
        Assert.Equal(1, editor.MinimapScale);
        Assert.True(editor.MinimapShowRegionSectionHeaders);
        Assert.True(editor.MinimapShowMarkSectionHeaders);
        Assert.Equal(@"\bMARK:\s*(?<separator>-?)\s*(?<label>.*)$", editor.MinimapMarkSectionHeaderRegex);
        Assert.Equal(9, editor.MinimapSectionHeaderFontSize);
        Assert.Equal(1, editor.MinimapSectionHeaderLetterSpacing);
        Assert.Null(editor.MinimapSliderActiveBrush);
        Assert.True(editor.InlineFeaturesEnabled);
        Assert.Empty(editor.InlineViewZones);
        Assert.Empty(editor.InlineControls);
        Assert.Empty(editor.InlineAnnotations);
        Assert.Empty(editor.InlineExtensions);
    }

    [Fact]
    public void EditorInlineLayout_MapsBeforeAndAfterLineToInsertionOffsets()
    {
        var document = new TextDocument("one\ntwo\nthree");

        Assert.Equal(
            document.GetLineByNumber(2).Offset,
            EditorInlineLayout.GetInsertionOffset(document, 2, EditorInlinePlacement.BeforeLine));
        Assert.Equal(
            document.GetLineByNumber(2).Offset,
            EditorInlineLayout.GetInsertionOffset(document, 1, EditorInlinePlacement.AfterLine));
        Assert.Equal(
            document.TextLength,
            EditorInlineLayout.GetInsertionOffset(document, 3, EditorInlinePlacement.AfterLine));
    }

    [Fact]
    public void MinimapTextEditor_ShowInlinePeek_ReplacesExistingPeekZone()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new MinimapTextEditor
        {
            Document = new TextDocument("class First { }\nclass Second { }")
        };

        var first = editor.ShowInlinePeek(1, "First.cs", "line 1", "class First { }", "csharp", 180);
        var second = editor.ShowInlinePeek(2, "Second.cs", "line 2", "class Second { }", "csharp", 180);

        var zone = Assert.Single(editor.InlineViewZones);
        Assert.Same(second, zone);
        Assert.DoesNotContain(first, editor.InlineViewZones);
        Assert.Equal(EditorInlineZoneKind.Peek, zone.Kind);
        Assert.Equal(EditorInlinePlacement.AfterLine, zone.Placement);

        editor.CloseInlinePeek();
        Assert.Empty(editor.InlineViewZones);
    }

    [Fact]
    public void EditorInlinePeekControl_RendersPreviewTextAndCloseButton()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new TextEditor();
        var peek = new EditorInlinePeekControl(
            editor,
            "MySliderTheme1",
            "Themes/MySliderTheme1.axaml:13",
            "  13: <ControlTheme x:Key=\"MySliderTheme1\" TargetType=\"Slider\">",
            "xaml",
            () => { });

        var grid = Assert.IsType<Grid>(peek.Child);
        var body = Assert.IsType<Grid>(grid.Children[1]);
        Assert.Equal(2, body.Children.Count);
        var previewEditor = Assert.IsType<TextEditor>(body.Children[0]);
        Assert.True(previewEditor.IsReadOnly);
        Assert.True(previewEditor.ShowLineNumbers);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Visible, previewEditor.VerticalScrollBarVisibility);
        Assert.Contains("<ControlTheme", previewEditor.Document.Text);

        var header = Assert.IsType<Border>(grid.Children[0]);
        var headerGrid = Assert.IsType<Grid>(header.Child);
        var closeButton = Assert.IsType<Border>(headerGrid.Children[1]);
        var closeText = Assert.IsType<TextBlock>(closeButton.Child);
        Assert.Equal("x", closeText.Text);
    }

    [Fact]
    public void EditorInlineOverlayPanel_DelegatesHitTestingToChildLayers()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var overlay = new EditorInlineOverlayPanel();
        var child = new TestHitLayer { ShouldHit = false };
        overlay.Children.Add(child);
        overlay.Measure(new Size(100, 100));
        overlay.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(0, 0, 100, 100), child.Bounds);

        Assert.False(((ICustomHitTest)overlay).HitTest(new Point(20, 20)));

        child.ShouldHit = true;

        Assert.True(((ICustomHitTest)overlay).HitTest(new Point(20, 20)));
    }

    [Fact]
    public void EditorInlineAnnotations_StartAtTextViewOrigin()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 220,
                ShowLineNumbers = true,
                Document = new TextDocument("root\n  <ControlTheme x:Key=\"MySliderTheme1\" />\nend")
            };
            editor.AddInlineAnnotation(2, "ControlTheme: MySliderTheme1");
            var window = new Window
            {
                Width = 600,
                Height = 260,
                Content = editor
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var overlay = editor.GetVisualDescendants().OfType<EditorInlineOverlayPanel>().Single();
                var textViewOrigin = editor.TextArea.TextView.TranslatePoint(default, overlay);
                var annotation = overlay.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(textBlock => textBlock.Text == "ControlTheme: MySliderTheme1");
                var annotationOrigin = annotation.TranslatePoint(default, overlay);

                Assert.NotNull(textViewOrigin);
                Assert.NotNull(annotationOrigin);
                Assert.True(annotationOrigin.Value.X >= textViewOrigin.Value.X - 0.5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlineAnnotations_AlignWithIndentedDeclaration()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 220,
                ShowLineNumbers = true,
                Document = new TextDocument("namespace Demo\n{\n    public class SampleView\n    {\n    }\n}")
            };
            editor.AddInlineAnnotation(3, "author: wieslaw");
            var window = new Window
            {
                Width = 600,
                Height = 260,
                Content = editor
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var overlay = editor.GetVisualDescendants().OfType<EditorInlineOverlayPanel>().Single();
                var annotation = overlay.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(textBlock => textBlock.Text == "author: wieslaw");
                var annotationOrigin = annotation.TranslatePoint(default, overlay);
                var expectedInTextView = editor.TextArea.TextView.GetVisualPosition(
                    new TextViewPosition(3, 5),
                    VisualYPosition.TextTop);
                var expectedOrigin = editor.TextArea.TextView.TranslatePoint(
                    new Point(expectedInTextView.X - editor.TextArea.TextView.ScrollOffset.X, 0),
                    overlay);

                Assert.NotNull(annotationOrigin);
                Assert.NotNull(expectedOrigin);
                Assert.True(
                    Math.Abs(expectedOrigin.Value.X - annotationOrigin.Value.X) <= 1,
                    $"Expected annotation X {annotationOrigin.Value.X} to be within one pixel of indentation X {expectedOrigin.Value.X}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlineControls_DoNotChangeVisualLineHeight()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var document = new TextDocument("one\ntwoX\nthree");
            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 220,
                ShowLineNumbers = true,
                Document = document
            };
            var window = new Window
            {
                Width = 600,
                Height = 260,
                Content = editor
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var before = editor.TextArea.TextView.GetVisualTopByDocumentLine(3) -
                             editor.TextArea.TextView.GetVisualTopByDocumentLine(2);
                var line = document.GetLineByNumber(2);
                var offsetBeforeX = line.Offset + 3;
                var offsetAfterX = line.Offset + 4;
                var beforeX = editor.TextArea.TextView.GetVisualPosition(
                    new TextViewPosition(document.GetLocation(offsetAfterX)),
                    VisualYPosition.TextMiddle);
                editor.AddInlineControl(offsetBeforeX, new Border
                {
                    Width = 40,
                    Height = 12,
                    Child = new TextBlock { Text = "Peek" }
                });
                PumpLayout(window);

                var after = editor.TextArea.TextView.GetVisualTopByDocumentLine(3) -
                            editor.TextArea.TextView.GetVisualTopByDocumentLine(2);
                var afterX = editor.TextArea.TextView.GetVisualPosition(
                    new TextViewPosition(document.GetLocation(offsetAfterX)),
                    VisualYPosition.TextMiddle);
                var overlay = editor.GetVisualDescendants().OfType<EditorInlineOverlayPanel>().Single();
                var link = overlay.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Child is TextBlock { Text: "Peek" });
                var linkOrigin = link.TranslatePoint(default, overlay);
                var afterXOrigin = editor.TextArea.TextView.TranslatePoint(
                    new Point(afterX.X - editor.TextArea.TextView.ScrollOffset.X, 0),
                    overlay);

                Assert.NotNull(linkOrigin);
                Assert.NotNull(afterXOrigin);
                Assert.True(
                    afterX.X >= beforeX.X + 30,
                    $"Expected inline control to reserve horizontal text space. Before X={beforeX.X}, after X={afterX.X}.");
                Assert.True(
                    linkOrigin.Value.X + link.Bounds.Width <= afterXOrigin.Value.X + 1,
                    $"Expected inline control to be arranged inside its reserved space. Link right={linkOrigin.Value.X + link.Bounds.Width}, text after={afterXOrigin.Value.X}.");
                Assert.Equal(before, after, precision: 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlineAnnotations_HideWhenTargetLineIsFolded()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 260,
                ShowLineNumbers = true,
                Document = new TextDocument("namespace Demo\n{\n    public class SampleView\n    {\n    }\n}\nend")
            };
            editor.AddInlineAnnotation(3, "author: wieslaw");
            var window = new Window
            {
                Width = 600,
                Height = 320,
                Content = editor
            };
            FoldingManager? foldingManager = null;

            try
            {
                window.Show();
                PumpLayout(window);
                Assert.Contains(
                    editor.GetVisualDescendants().OfType<TextBlock>(),
                    static textBlock => textBlock.Text == "author: wieslaw" && IsEffectivelyVisible(textBlock));

                foldingManager = FoldLines(editor, 2, 6);
                PumpLayout(window);

                Assert.DoesNotContain(
                    editor.GetVisualDescendants().OfType<TextBlock>(),
                    static textBlock => textBlock.Text == "author: wieslaw" && IsEffectivelyVisible(textBlock));
            }
            finally
            {
                if (foldingManager is not null)
                {
                    FoldingManager.Uninstall(foldingManager);
                }

                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlineControls_HideWhenAnchorLineIsFolded()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var document = new TextDocument("namespace Demo\n{\n    Value=\"{StaticResource AccentBrush}\"\n}\nend");
            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 260,
                ShowLineNumbers = true,
                Document = document
            };
            var window = new Window
            {
                Width = 600,
                Height = 320,
                Content = editor
            };
            FoldingManager? foldingManager = null;

            try
            {
                window.Show();
                PumpLayout(window);

                var anchorOffset = document.GetLineByNumber(3).EndOffset;
                editor.AddInlineControl(anchorOffset, new Border
                {
                    Width = 40,
                    Height = 12,
                    Child = new TextBlock { Text = "Peek" }
                });
                PumpLayout(window);
                Assert.Contains(
                    editor.GetVisualDescendants().OfType<TextBlock>(),
                    static textBlock => textBlock.Text == "Peek" && IsEffectivelyVisible(textBlock));

                foldingManager = FoldLines(editor, 2, 4);
                PumpLayout(window);

                Assert.DoesNotContain(
                    editor.GetVisualDescendants().OfType<TextBlock>(),
                    static textBlock => textBlock.Text == "Peek" && IsEffectivelyVisible(textBlock));
            }
            finally
            {
                if (foldingManager is not null)
                {
                    FoldingManager.Uninstall(foldingManager);
                }

                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlinePeek_HidesWhenAnchorLineIsFolded()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 280,
                ShowLineNumbers = true,
                Document = new TextDocument("namespace Demo\n{\n    public class SampleView\n    {\n    }\n}\nend")
            };
            var window = new Window
            {
                Width = 600,
                Height = 360,
                Content = editor
            };
            FoldingManager? foldingManager = null;

            try
            {
                window.Show();
                PumpLayout(window);
                editor.ShowInlinePeek(3, "SampleView", "Main.axaml.cs:3", "public class SampleView", "csharp", 140);
                PumpLayout(window);
                Assert.Contains(
                    editor.GetVisualDescendants().OfType<EditorInlinePeekControl>(),
                    static peek => IsEffectivelyVisible(peek));

                foldingManager = FoldLines(editor, 2, 6);
                PumpLayout(window);

                Assert.DoesNotContain(
                    editor.GetVisualDescendants().OfType<EditorInlinePeekControl>(),
                    static peek => IsEffectivelyVisible(peek));
            }
            finally
            {
                if (foldingManager is not null)
                {
                    FoldingManager.Uninstall(foldingManager);
                }

                window.Close();
            }
        });
    }

    [Fact]
    public void EditorInlinePeek_AfterFoldHeaderReservesSpaceBeforeNextVisibleLine()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureMinimapEditorStyles();

            var editor = new MinimapTextEditor
            {
                Width = 520,
                Height = 360,
                ShowLineNumbers = true,
                Document = new TextDocument("namespace Demo\n{\n    public class SampleView\n    {\n    }\n}\nend")
            };
            var window = new Window
            {
                Width = 600,
                Height = 420,
                Content = editor
            };
            FoldingManager? foldingManager = null;

            try
            {
                window.Show();
                foldingManager = FoldLines(editor, 2, 6);
                PumpLayout(window);

                editor.ShowInlinePeek(2, "namespace Demo", "Main.axaml.cs:2", "namespace Demo", "csharp", 140);
                PumpLayout(window);
                var zone = GetInlineZoneSnapshots(editor).Single();

                Assert.Contains(
                    editor.GetVisualDescendants().OfType<EditorInlinePeekControl>(),
                    static peek => IsEffectivelyVisible(peek));
                Assert.Equal(2, GetSnapshotInt32(zone, "AnchorLineNumber"));
                Assert.Equal(7, GetSnapshotInt32(zone, "InsertionLineNumber"));
                Assert.Equal(editor.Document.GetLineByNumber(7).Offset, GetSnapshotInt32(zone, "InsertionOffset"));
            }
            finally
            {
                if (foldingManager is not null)
                {
                    FoldingManager.Uninstall(foldingManager);
                }

                window.Close();
            }
        });
    }

    [Fact]
    public void PlaygroundInlineFeatureHelpers_CreatesHyperlinkStyleInlineControl()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var helpersType = typeof(PlaygroundInlineFeatures).Assembly.GetType(
            "XamlPlayground.Services.Editing.InlineFeatures.PlaygroundInlineFeatureHelpers");
        Assert.NotNull(helpersType);
        var createMethod = helpersType.GetMethod("CreateInlineTextButton", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);

        var control = Assert.IsAssignableFrom<Control>(
            createMethod.Invoke(null, [new TextEditor(), "Peek", () => { }]));

        var link = Assert.IsType<Border>(control);
        Assert.Equal(Brushes.Transparent, link.Background);
        Assert.Equal(default, link.BorderThickness);

        var text = Assert.IsType<TextBlock>(link.Child);
        Assert.Equal("Peek", text.Text);
        Assert.Same(TextDecorations.Underline, text.TextDecorations);
    }

    [Fact]
    public void PlaygroundXamlInlineExtension_PlacesPeekLinkAfterResourceExpressionQuote()
    {
        var extensionType = typeof(PlaygroundInlineFeatures).Assembly.GetType(
            "XamlPlayground.Services.Editing.InlineFeatures.PlaygroundXamlInlineExtension");
        Assert.NotNull(extensionType);
        var method = extensionType.GetMethod(
            "GetReferenceAnchorOffset",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var parserType = typeof(PlaygroundInlineFeatures).Assembly.GetType(
            "XamlPlayground.Services.Theming.ResourceReferenceParser");
        Assert.NotNull(parserType);
        var findMethod = parserType.GetMethod("Find", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(findMethod);

        const string text = "<Button Theme=\"{StaticResource MyButtonTheme1}\" />";
        var matches = Assert.IsAssignableFrom<IEnumerable>(findMethod.Invoke(null, [text]));
        var match = matches.Cast<object>().Single();

        var offset = Assert.IsType<int>(method.Invoke(null, [text, match]));

        Assert.Equal(text.IndexOf("\" />", StringComparison.Ordinal) + 1, offset);
    }

    [Fact]
    public void MinimapTextEditor_AddInlineHelpers_PopulateCollections()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new MinimapTextEditor();
        var control = new Button { Content = "Run" };
        var zoneContent = new Border();

        var inlineControl = editor.AddInlineControl(-10, control);
        var annotation = editor.AddInlineAnnotation(2, "author: wieslaw", 10);
        var zone = editor.AddInlineViewZone(1, EditorInlinePlacement.AfterLine, 120, zoneContent);

        Assert.Equal(0, inlineControl.Offset);
        Assert.Same(control, inlineControl.Control);
        Assert.Equal(2, annotation.LineNumber);
        Assert.Equal("author: wieslaw", annotation.Text);
        Assert.Equal(1, zone.LineNumber);
        Assert.Same(zoneContent, zone.Content);
        Assert.Single(editor.InlineControls);
        Assert.Single(editor.InlineAnnotations);
        Assert.Single(editor.InlineViewZones);
    }

    [Fact]
    public void MinimapTextEditor_InlineExtension_AttachesAndDisposes()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new MinimapTextEditor();
        var extension = new TestInlineExtension();

        editor.InlineExtensions.Add(extension);

        Assert.Equal(1, extension.AttachCount);
        Assert.Single(editor.InlineAnnotations);

        editor.InlineExtensions.Remove(extension);

        Assert.Equal(1, extension.DisposeCount);
        Assert.Empty(editor.InlineAnnotations);
    }

    [Fact]
    public void MinimapTextEditor_WorksWithExistingTextEditorDocumentAttachedProperty()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new MinimapTextEditor();
        var document = new TextDocument
        {
            Text = "public class Sample { }"
        };

        TextEditorDocument.SetDocument(editor, document);

        Assert.Same(document, editor.Document);
    }

    [Fact]
    public void PlaygroundInlineFeatures_AttachesSampleProviderFromShell()
    {
        TestApplication.EnsureAvaloniaInitialized();

        using var viewModel = new MainViewModel(null);
        var sample = viewModel.CurrentSample;
        Assert.NotNull(sample);
        var editor = new MinimapTextEditor();

        TextEditorDocument.SetDocument(editor, sample.Xaml);
        PlaygroundInlineFeatures.SetMode(editor, PlaygroundInlineFeatureMode.SampleXaml);
        PlaygroundInlineFeatures.SetShell(editor, viewModel);

        Assert.Single(editor.InlineExtensions);

        PlaygroundInlineFeatures.SetIsEnabled(editor, false);

        Assert.Empty(editor.InlineExtensions);
        Assert.Empty(editor.InlineAnnotations);
    }

    [Fact]
    public void PlaygroundInlineFeatures_AddsVisibleThemePeekControlsNearResourceKey()
    {
        TestApplication.EnsureAvaloniaInitialized();

        using var viewModel = new MainViewModel(null);
        var sample = Assert.Single(viewModel.Samples, static sample => sample.Name == "Binding");
        viewModel.CurrentSample = sample;
        sample.Xaml.Replace(
            0,
            sample.Xaml.TextLength,
            sample.Xaml.Text.Replace(
                "<Slider Name=\"AmountSlider\" Minimum=\"0\" Maximum=\"100\" Value=\"42\" />",
                "<Slider Name=\"AmountSlider\" Minimum=\"0\" Maximum=\"100\" Value=\"42\" Theme=\"{StaticResource MySliderTheme1}\" />",
                StringComparison.Ordinal));
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        project.AddFile(new InMemoryProjectFile(
            "Themes/MySliderTheme1.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTheme x:Key="MySliderTheme1" TargetType="Slider" />
            </ResourceDictionary>
            """,
            ProjectFileKind.Resource));
        var editor = new MinimapTextEditor();

        TextEditorDocument.SetDocument(editor, sample.Xaml);
        PlaygroundInlineFeatures.SetMode(editor, PlaygroundInlineFeatureMode.SampleXaml);
        PlaygroundInlineFeatures.SetShell(editor, viewModel);

        var inlineControl = Assert.Single(editor.InlineControls);
        Assert.NotNull(inlineControl.ControlFactory);
        Assert.Equal(GetOffsetAfterResourceAttributeQuote(sample.Xaml.Text, "MySliderTheme1"), inlineControl.Offset);
    }

    [Fact]
    public void PlaygroundInlineFeatures_KeepsPeekControlVisibleForUnresolvedResourceReference()
    {
        TestApplication.EnsureAvaloniaInitialized();

        using var viewModel = new MainViewModel(null);
        var sample = Assert.Single(viewModel.Samples, static sample => sample.Name == "Binding");
        viewModel.CurrentSample = sample;
        sample.Xaml.Replace(
            0,
            sample.Xaml.TextLength,
            sample.Xaml.Text.Replace(
                "<Slider Name=\"AmountSlider\" Minimum=\"0\" Maximum=\"100\" Value=\"42\" />",
                "<Slider Name=\"AmountSlider\" Minimum=\"0\" Maximum=\"100\" Value=\"42\" Theme=\"{StaticResource MissingSliderTheme}\" />",
                StringComparison.Ordinal));
        var editor = new MinimapTextEditor();

        TextEditorDocument.SetDocument(editor, sample.Xaml);
        PlaygroundInlineFeatures.SetMode(editor, PlaygroundInlineFeatureMode.SampleXaml);
        PlaygroundInlineFeatures.SetShell(editor, viewModel);

        var inlineControl = Assert.Single(editor.InlineControls);
        Assert.NotNull(inlineControl.ControlFactory);
        Assert.Equal(GetOffsetAfterResourceAttributeQuote(sample.Xaml.Text, "MissingSliderTheme"), inlineControl.Offset);
    }

    [Fact]
    public void PlaygroundInlineFeatures_WiresWorkspaceResourceAnnotations()
    {
        TestApplication.EnsureAvaloniaInitialized();

        using var viewModel = new MainViewModel(null);
        var project = viewModel.ActiveProject;
        Assert.NotNull(project);
        var resourceFile = Assert.Single(project.Files, static file => file.Path == "Styles/Resources.axaml");
        var editor = new MinimapTextEditor();

        TextEditorDocument.SetDocument(editor, resourceFile.Document);
        PlaygroundInlineFeatures.SetMode(editor, PlaygroundInlineFeatureMode.WorkspaceFile);
        PlaygroundInlineFeatures.SetFile(editor, resourceFile);
        PlaygroundInlineFeatures.SetShell(editor, viewModel);

        Assert.Single(editor.InlineExtensions);
        Assert.Contains(editor.InlineAnnotations, static annotation => annotation.Text == "SolidColorBrush: AccentBrush");
    }

    [Fact]
    public void TextEditorMinimap_UsesVsCodeRemainingWidthSizing()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var minimap = new TextEditorMinimap
        {
            MaxColumn = 120,
            Scale = 1,
            RenderCharacters = true
        };

        minimap.Measure(new Size(800, 400));
        Assert.Equal(96, minimap.DesiredSize.Width);

        minimap.Measure(new Size(400, 400));
        Assert.Equal(52, minimap.DesiredSize.Width);

        minimap.Measure(new Size(2000, 400));
        Assert.Equal(120, minimap.DesiredSize.Width);
    }

    [Fact]
    public void TextEditorMinimap_DefaultsToTransparentInputSurface()
    {
        var minimap = new TextEditorMinimap();

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(minimap.Background);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void TextEditorMinimap_MouseOverSliderOnlyShowsForMinimapPointerOrDrag()
    {
        var minimap = new TextEditorMinimap();
        var shouldDrawSlider = typeof(TextEditorMinimap).GetMethod("ShouldDrawSlider", BindingFlags.Instance | BindingFlags.NonPublic);
        var pointerOverField = typeof(TextEditorMinimap).GetField("_isPointerOver", BindingFlags.Instance | BindingFlags.NonPublic);
        var draggingField = typeof(TextEditorMinimap).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic);
        var scrollRevealedField = typeof(TextEditorMinimap).GetField("_isScrollRevealed", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(shouldDrawSlider);
        Assert.NotNull(pointerOverField);
        Assert.NotNull(draggingField);
        Assert.NotNull(scrollRevealedField);

        Assert.Equal(false, shouldDrawSlider.Invoke(minimap, []));

        scrollRevealedField.SetValue(minimap, true);
        Assert.Equal(false, shouldDrawSlider.Invoke(minimap, []));

        pointerOverField.SetValue(minimap, true);
        Assert.Equal(true, shouldDrawSlider.Invoke(minimap, []));

        pointerOverField.SetValue(minimap, false);
        draggingField.SetValue(minimap, true);
        Assert.Equal(true, shouldDrawSlider.Invoke(minimap, []));
    }

    [Fact]
    public void TextEditorMinimap_FillSamplingLayoutCoversFullDocument()
    {
        var minimap = new TextEditorMinimap();
        var metricsType = typeof(TextEditorMinimap).GetNestedType("MinimapMetrics", BindingFlags.NonPublic);
        var layoutMethod = typeof(TextEditorMinimap).GetMethod("CreateContainedLayout", BindingFlags.Instance | BindingFlags.NonPublic);
        var foldedLineMap = CreateFoldedLineMap(1000);

        Assert.NotNull(metricsType);
        Assert.NotNull(layoutMethod);

        var metrics = Activator.CreateInstance(
            metricsType,
            1000,
            1000,
            foldedLineMap,
            20d,
            1d,
            100d,
            200d,
            20000d,
            0d,
            1,
            10,
            0,
            9,
            0d,
            0d,
            true);

        Assert.NotNull(metrics);

        var layout = layoutMethod.Invoke(minimap, [metrics]);
        Assert.NotNull(layout);

        var layoutType = layout.GetType();
        Assert.Equal(true, layoutType.GetProperty("IsSampling")?.GetValue(layout));
        Assert.Equal(1, layoutType.GetProperty("StartLineNumber")?.GetValue(layout));
        Assert.Equal(1000, layoutType.GetProperty("EndLineNumber")?.GetValue(layout));
        Assert.Equal(100, layoutType.GetProperty("SampleRowCount")?.GetValue(layout));

        var getLineNumberForSampleRow = layoutType.GetMethod("GetLineNumberForSampleRow");
        Assert.NotNull(getLineNumberForSampleRow);
        Assert.Equal(1, getLineNumberForSampleRow.Invoke(layout, [0]));
        Assert.Equal(1000, getLineNumberForSampleRow.Invoke(layout, [99]));
    }

    [Fact]
    public void TextEditorMinimap_FoldedLineMapRemovesHiddenLines()
    {
        var foldedLineMap = CreateFoldedLineMap(10, (4, 6));
        var mapType = foldedLineMap.GetType();
        var getVisibleIndex = mapType.GetMethod("GetVisibleIndex");
        var getVisibleIndexOrNearest = mapType.GetMethod("GetVisibleIndexOrNearest");
        var getLineNumberAtVisibleIndex = mapType.GetMethod("GetLineNumberAtVisibleIndex");

        Assert.NotNull(getVisibleIndex);
        Assert.NotNull(getVisibleIndexOrNearest);
        Assert.NotNull(getLineNumberAtVisibleIndex);

        Assert.Equal(7, mapType.GetProperty("VisibleLineCount")?.GetValue(foldedLineMap));
        Assert.Equal(2, getVisibleIndex.Invoke(foldedLineMap, [3]));
        Assert.Equal(-1, getVisibleIndex.Invoke(foldedLineMap, [4]));
        Assert.Equal(-1, getVisibleIndex.Invoke(foldedLineMap, [6]));
        Assert.Equal(3, getVisibleIndex.Invoke(foldedLineMap, [7]));
        Assert.Equal(2, getVisibleIndexOrNearest.Invoke(foldedLineMap, [6]));
        Assert.Equal(7, getLineNumberAtVisibleIndex.Invoke(foldedLineMap, [3]));
    }

    [Fact]
    public void TextEditorMinimap_FoldedLineMapReadsAvaloniaEditFoldState()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var document = new TextDocument
            {
                Text = string.Join(Environment.NewLine, Enumerable.Range(1, 10))
            };
            var editor = new TextEditor
            {
                Document = document
            };
            var foldingManager = FoldingManager.Install(editor.TextArea);

            try
            {
                var startOffset = document.GetLineByNumber(3).Offset;
                var endLine = document.GetLineByNumber(7);
                var endOffset = endLine.Offset + endLine.Length;

                foldingManager.UpdateFoldings([new NewFolding(startOffset, endOffset) { Name = "..." }], -1);
                var folding = Assert.Single(foldingManager.AllFoldings);
                folding.IsFolded = true;

                var foldedLineMap = CreateFoldedLineMap(editor, document);
                var mapType = foldedLineMap.GetType();
                var getVisibleIndex = mapType.GetMethod("GetVisibleIndex");

                Assert.NotNull(getVisibleIndex);
                Assert.Equal(6, mapType.GetProperty("VisibleLineCount")?.GetValue(foldedLineMap));
                Assert.Equal(2, getVisibleIndex.Invoke(foldedLineMap, [3]));
                Assert.Equal(-1, getVisibleIndex.Invoke(foldedLineMap, [4]));
                Assert.Equal(-1, getVisibleIndex.Invoke(foldedLineMap, [7]));
                Assert.Equal(3, getVisibleIndex.Invoke(foldedLineMap, [8]));
            }
            finally
            {
                FoldingManager.Uninstall(foldingManager);
            }
        });
    }

    [Fact]
    public void TextEditorMinimap_FoldedSamplingLayoutSkipsHiddenLines()
    {
        var minimap = new TextEditorMinimap();
        var metricsType = typeof(TextEditorMinimap).GetNestedType("MinimapMetrics", BindingFlags.NonPublic);
        var layoutMethod = typeof(TextEditorMinimap).GetMethod("CreateContainedLayout", BindingFlags.Instance | BindingFlags.NonPublic);
        var foldedLineMap = CreateFoldedLineMap(10, (4, 6));

        Assert.NotNull(metricsType);
        Assert.NotNull(layoutMethod);

        var metrics = Activator.CreateInstance(
            metricsType,
            10,
            7,
            foldedLineMap,
            20d,
            1d,
            4d,
            40d,
            200d,
            0d,
            1,
            2,
            0,
            1,
            0d,
            0d,
            true);

        Assert.NotNull(metrics);

        var layout = layoutMethod.Invoke(minimap, [metrics]);
        Assert.NotNull(layout);

        var layoutType = layout.GetType();
        Assert.Equal(true, layoutType.GetProperty("IsSampling")?.GetValue(layout));
        Assert.Equal(1, layoutType.GetProperty("StartLineNumber")?.GetValue(layout));
        Assert.Equal(10, layoutType.GetProperty("EndLineNumber")?.GetValue(layout));
        Assert.Equal(4, layoutType.GetProperty("SampleRowCount")?.GetValue(layout));

        var getLineNumberForSampleRow = layoutType.GetMethod("GetLineNumberForSampleRow");
        Assert.NotNull(getLineNumberForSampleRow);
        Assert.Equal(1, getLineNumberForSampleRow.Invoke(layout, [0]));
        Assert.Equal(3, getLineNumberForSampleRow.Invoke(layout, [1]));
        Assert.Equal(8, getLineNumberForSampleRow.Invoke(layout, [2]));
        Assert.Equal(10, getLineNumberForSampleRow.Invoke(layout, [3]));
    }

    private static object CreateFoldedLineMap(int lineCount, params (int StartLineNumber, int EndLineNumber)[] hiddenRanges)
    {
        var minimapType = typeof(TextEditorMinimap);
        var hiddenLineRangeType = minimapType.GetNestedType("HiddenLineRange", BindingFlags.NonPublic);

        Assert.NotNull(hiddenLineRangeType);

        var hiddenLineRangeArray = Array.CreateInstance(hiddenLineRangeType, hiddenRanges.Length);
        for (var i = 0; i < hiddenRanges.Length; i++)
        {
            var hiddenLineRange = Activator.CreateInstance(
                hiddenLineRangeType,
                hiddenRanges[i].StartLineNumber,
                hiddenRanges[i].EndLineNumber);

            hiddenLineRangeArray.SetValue(hiddenLineRange, i);
        }

        var createFoldedLineMap = typeof(TextEditorMinimap)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "CreateFoldedLineMap" &&
                       parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(int);
            });

        Assert.NotNull(createFoldedLineMap);

        var foldedLineMap = createFoldedLineMap.Invoke(null, [lineCount, hiddenLineRangeArray]);

        Assert.NotNull(foldedLineMap);
        return foldedLineMap;
    }

    private static object CreateFoldedLineMap(TextEditor editor, TextDocument document)
    {
        var createFoldedLineMap = typeof(TextEditorMinimap)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "CreateFoldedLineMap" &&
                       parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(TextEditor);
            });

        Assert.NotNull(createFoldedLineMap);

        var foldedLineMap = createFoldedLineMap.Invoke(null, [editor, document]);

        Assert.NotNull(foldedLineMap);
        return foldedLineMap;
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }

    private static int GetOffsetAfterResourceAttributeQuote(string text, string key)
    {
        var keyIndex = text.IndexOf(key, StringComparison.Ordinal);
        Assert.True(keyIndex >= 0);
        var closingBraceIndex = text.IndexOf('}', keyIndex + key.Length);
        Assert.True(closingBraceIndex >= 0);
        var closingQuoteIndex = text.IndexOf('"', closingBraceIndex + 1);
        Assert.True(closingQuoteIndex >= 0);
        return closingQuoteIndex + 1;
    }

    private static FoldingManager FoldLines(TextEditor editor, int startLineNumber, int endLineNumber)
    {
        var document = editor.Document;
        var startOffset = document.GetLineByNumber(startLineNumber).Offset;
        var endLine = document.GetLineByNumber(endLineNumber);
        var endOffset = endLine.Offset + endLine.Length;
        var foldingManager = FoldingManager.Install(editor.TextArea);
        foldingManager.UpdateFoldings([new NewFolding(startOffset, endOffset) { Name = "..." }], -1);
        Assert.Single(foldingManager.AllFoldings).IsFolded = true;
        return foldingManager;
    }

    private static object[] GetInlineZoneSnapshots(MinimapTextEditor editor)
    {
        var hostField = typeof(MinimapTextEditor).GetField("_inlineFeatureHost", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hostField);
        var host = hostField.GetValue(editor);
        Assert.NotNull(host);
        var zones = host.GetType().GetProperty("Zones")?.GetValue(host);
        Assert.NotNull(zones);
        return Assert.IsAssignableFrom<IEnumerable>(zones).Cast<object>().ToArray();
    }

    private static int GetSnapshotInt32(object snapshot, string propertyName)
    {
        var property = snapshot.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(snapshot));
    }

    private static bool IsEffectivelyVisible(Control control)
    {
        return control.IsVisible &&
               control.GetVisualAncestors()
                   .OfType<Control>()
                   .All(static ancestor => ancestor.IsVisible);
    }

    private static void EnsureMinimapEditorStyles()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        AddStyleIfMissing(app, "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml");
        AddStyleIfMissing(app, "avares://XamlPlayground.AvaloniaEdit.Minimap/Themes/Generic.axaml");
    }

    private static void AddStyleIfMissing(Application app, string source)
    {
        var uri = new Uri(source);
        if (app.Styles.OfType<StyleInclude>().Any(style => style.Source == uri))
        {
            return;
        }

        app.Styles.Add(new StyleInclude(new Uri("avares://XamlPlayground.Tests/"))
        {
            Source = uri
        });
    }

    private sealed class TestInlineExtension : IEditorInlineExtension
    {
        private EditorCodeAnnotation? _annotation;
        private EditorInlineExtensionContext? _context;

        public int AttachCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IDisposable Attach(EditorInlineExtensionContext context)
        {
            AttachCount++;
            _context = context;
            _annotation = new EditorCodeAnnotation
            {
                LineNumber = 1,
                Text = "author: test"
            };
            context.Annotations.Add(_annotation);

            return new TestDisposable(() =>
            {
                DisposeCount++;
                if (_annotation is not null)
                {
                    _context?.Annotations.Remove(_annotation);
                    _annotation = null;
                }
            });
        }
    }

    private sealed class TestDisposable : IDisposable
    {
        private Action? _dispose;

        public TestDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            var dispose = _dispose;
            _dispose = null;
            dispose?.Invoke();
        }
    }

    private sealed class TestHitLayer : Control, ICustomHitTest
    {
        public bool ShouldHit { get; set; }

        public bool HitTest(Point point)
        {
            return ShouldHit;
        }
    }
}
