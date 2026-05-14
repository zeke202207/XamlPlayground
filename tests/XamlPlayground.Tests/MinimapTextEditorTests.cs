using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
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
}
