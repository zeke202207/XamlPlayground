using Avalonia;
using AvaloniaEdit.Document;
using XamlPlayground.Behaviors;
using XamlPlayground.Editor.Minimap;

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
}
