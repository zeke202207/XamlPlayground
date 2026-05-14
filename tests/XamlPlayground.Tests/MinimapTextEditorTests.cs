using System.Reflection;
using Avalonia;
using Avalonia.Media;
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

        Assert.NotNull(metricsType);
        Assert.NotNull(layoutMethod);

        var metrics = Activator.CreateInstance(
            metricsType,
            1000,
            20d,
            1d,
            100d,
            200d,
            20000d,
            0d,
            1,
            10,
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
}
