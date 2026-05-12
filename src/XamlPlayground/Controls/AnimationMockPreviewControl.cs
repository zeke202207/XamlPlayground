using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using XamlPlayground.ViewModels.VisualEditing;

namespace XamlPlayground.Controls;

public sealed class AnimationMockPreviewControl : Control
{
    public static readonly StyledProperty<IEnumerable<AnimationTimelineTrackViewModel>?> TracksProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IEnumerable<AnimationTimelineTrackViewModel>?>(
            nameof(Tracks));

    public static readonly StyledProperty<int> CurrentTimePercentProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, int>(
            nameof(CurrentTimePercent),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> StageBackgroundProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(StageBackground),
            Brushes.Transparent);

    public static readonly StyledProperty<IBrush?> GridLineBrushProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(GridLineBrush),
            Brushes.LightGray);

    public static readonly StyledProperty<IBrush?> PreviewFillBrushProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(PreviewFillBrush),
            Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush?> PreviewBorderBrushProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(PreviewBorderBrush),
            Brushes.Gray);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(TextBrush),
            Brushes.Black);

    public static readonly StyledProperty<IBrush?> MutedTextBrushProperty =
        AvaloniaProperty.Register<AnimationMockPreviewControl, IBrush?>(
            nameof(MutedTextBrush),
            Brushes.Gray);

    private readonly HashSet<AnimationTimelineTrackViewModel> _trackedTracks = new();
    private readonly HashSet<AnimationTimelineKeyFrameViewModel> _trackedKeyFrames = new();
    private INotifyCollectionChanged? _tracksCollection;

    public IEnumerable<AnimationTimelineTrackViewModel>? Tracks
    {
        get => GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    public int CurrentTimePercent
    {
        get => GetValue(CurrentTimePercentProperty);
        set => SetValue(CurrentTimePercentProperty, Math.Clamp(value, 0, 100));
    }

    public IBrush? StageBackground
    {
        get => GetValue(StageBackgroundProperty);
        set => SetValue(StageBackgroundProperty, value);
    }

    public IBrush? GridLineBrush
    {
        get => GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    public IBrush? PreviewFillBrush
    {
        get => GetValue(PreviewFillBrushProperty);
        set => SetValue(PreviewFillBrushProperty, value);
    }

    public IBrush? PreviewBorderBrush
    {
        get => GetValue(PreviewBorderBrushProperty);
        set => SetValue(PreviewBorderBrushProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? MutedTextBrush
    {
        get => GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        using (context.PushClip(bounds))
        {
            context.DrawRectangle(StageBackground ?? Brushes.Transparent, null, bounds);
            DrawGrid(context, bounds);

            var setters = ResolveFrameSetters();
            DrawMockTarget(context, bounds, setters);
            DrawStatus(context, bounds, setters);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResetSubscriptions(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TracksProperty)
        {
            ResetSubscriptions(change.GetNewValue<IEnumerable<AnimationTimelineTrackViewModel>?>());
            InvalidateVisual();
            return;
        }

        if (change.Property == CurrentTimePercentProperty)
        {
            var value = change.GetNewValue<int>();
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped != value)
            {
                CurrentTimePercent = clamped;
                return;
            }
        }

        if (change.Property == CurrentTimePercentProperty ||
            change.Property == StageBackgroundProperty ||
            change.Property == GridLineBrushProperty ||
            change.Property == PreviewFillBrushProperty ||
            change.Property == PreviewBorderBrushProperty ||
            change.Property == TextBrushProperty ||
            change.Property == MutedTextBrushProperty)
        {
            InvalidateVisual();
        }
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var pen = new Pen(GridLineBrush ?? Brushes.LightGray, 1);
        for (var x = 0.0; x <= bounds.Width; x += 24)
        {
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
        }

        for (var y = 0.0; y <= bounds.Height; y += 24)
        {
            context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawMockTarget(DrawingContext context, Rect bounds, IReadOnlyDictionary<string, string> setters)
    {
        var opacity = Math.Clamp(GetDouble(setters, "Opacity", 1), 0, 1);
        var width = Math.Clamp(GetDouble(setters, "Width", 118), 32, Math.Max(32, bounds.Width - 40));
        var height = Math.Clamp(GetDouble(setters, "Height", 46), 24, Math.Max(24, bounds.Height - 92));
        var translateX = GetDouble(setters, "TranslateTransform.X", 0);
        var translateY = GetDouble(setters, "TranslateTransform.Y", 0);
        var rotateAngle = GetDouble(setters, "RotateTransform.Angle", 0);
        var scaleX = Math.Clamp(GetDouble(setters, "ScaleTransform.ScaleX", 1), 0.1, 4);
        var scaleY = Math.Clamp(GetDouble(setters, "ScaleTransform.ScaleY", 1), 0.1, 4);
        var cornerRadius = Math.Clamp(GetDouble(setters, "CornerRadius", 6), 0, 32);
        var fill = GetBrush(setters, "Background") ??
                   GetBrush(setters, "Fill") ??
                   PreviewFillBrush ??
                   Brushes.DodgerBlue;
        var border = GetBrush(setters, "BorderBrush") ??
                     PreviewBorderBrush ??
                     Brushes.Gray;

        var center = new Point(bounds.Center.X + translateX, Math.Max(58, bounds.Center.Y - 10 + translateY));
        var transform =
            Matrix.CreateScale(scaleX, scaleY) *
            Matrix.CreateRotation(rotateAngle * Math.PI / 180.0) *
            Matrix.CreateTranslation(center.X, center.Y);

        using (context.PushOpacity(opacity))
        using (context.PushTransform(transform))
        {
            var rect = new Rect(-width / 2, -height / 2, width, height);
            context.DrawRectangle(fill, new Pen(border, 1.5), rect, cornerRadius, cornerRadius);
            DrawText(context, "Preview", new Point(rect.X + 14, rect.Y + height / 2 - 8), 13, Brushes.White, FontWeight.SemiBold);
        }
    }

    private void DrawStatus(DrawingContext context, Rect bounds, IReadOnlyDictionary<string, string> setters)
    {
        var textBrush = TextBrush ?? Brushes.Black;
        var mutedBrush = MutedTextBrush ?? textBrush;
        var y = Math.Max(4, bounds.Height - 42);
        DrawText(context, $"Frame {CurrentTimePercent}%", new Point(10, y), 12, textBrush, FontWeight.SemiBold);

        var summary = setters.Count == 0
            ? "No frame setters"
            : string.Join(", ", setters
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(3)
                .Select(static pair => $"{pair.Key}={pair.Value}"));
        DrawText(context, summary, new Point(10, y + 19), 10, mutedBrush);
    }

    private IReadOnlyDictionary<string, string> ResolveFrameSetters()
    {
        var cue = Math.Clamp(CurrentTimePercent, 0, 100);
        var setters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in GetTracks())
        {
            if (string.IsNullOrWhiteSpace(track.PropertyName))
            {
                continue;
            }

            var frames = track.KeyFrames
                .OrderBy(static keyFrame => keyFrame.CuePercent)
                .ToArray();
            if (frames.Length == 0)
            {
                continue;
            }

            var value = ResolveTrackValue(frames, cue);
            setters[track.PropertyName.Trim()] = value;
        }

        return setters;
    }

    private static string ResolveTrackValue(IReadOnlyList<AnimationTimelineKeyFrameViewModel> frames, int cue)
    {
        var first = frames[0];
        if (cue <= first.CuePercent)
        {
            return first.Value;
        }

        var last = frames[^1];
        if (cue >= last.CuePercent)
        {
            return last.Value;
        }

        var previous = frames.Last(keyFrame => keyFrame.CuePercent <= cue);
        var next = frames.First(keyFrame => keyFrame.CuePercent >= cue);
        if (ReferenceEquals(previous, next) ||
            previous.CuePercent == next.CuePercent)
        {
            return previous.Value;
        }

        var progress = (cue - previous.CuePercent) / (double)(next.CuePercent - previous.CuePercent);
        return TryInterpolateValue(previous.Value, next.Value, progress, out var interpolated)
            ? interpolated
            : previous.Value;
    }

    private IReadOnlyList<AnimationTimelineTrackViewModel> GetTracks()
    {
        return Tracks switch
        {
            null => Array.Empty<AnimationTimelineTrackViewModel>(),
            IReadOnlyList<AnimationTimelineTrackViewModel> list => list,
            _ => Tracks.ToArray()
        };
    }

    private void ResetSubscriptions(IEnumerable<AnimationTimelineTrackViewModel>? tracks)
    {
        if (_tracksCollection is not null)
        {
            _tracksCollection.CollectionChanged -= TracksCollectionOnCollectionChanged;
            _tracksCollection = null;
        }

        foreach (var track in _trackedTracks)
        {
            track.PropertyChanged -= TrackOnPropertyChanged;
            track.KeyFrames.CollectionChanged -= KeyFramesOnCollectionChanged;
        }

        foreach (var keyFrame in _trackedKeyFrames)
        {
            keyFrame.PropertyChanged -= KeyFrameOnPropertyChanged;
        }

        _trackedTracks.Clear();
        _trackedKeyFrames.Clear();

        if (tracks is INotifyCollectionChanged collection)
        {
            _tracksCollection = collection;
            collection.CollectionChanged += TracksCollectionOnCollectionChanged;
        }

        if (tracks is null)
        {
            return;
        }

        foreach (var track in tracks)
        {
            AddTrackSubscription(track);
        }
    }

    private void AddTrackSubscription(AnimationTimelineTrackViewModel track)
    {
        if (!_trackedTracks.Add(track))
        {
            return;
        }

        track.PropertyChanged += TrackOnPropertyChanged;
        track.KeyFrames.CollectionChanged += KeyFramesOnCollectionChanged;
        foreach (var keyFrame in track.KeyFrames)
        {
            AddKeyFrameSubscription(keyFrame);
        }
    }

    private void AddKeyFrameSubscription(AnimationTimelineKeyFrameViewModel keyFrame)
    {
        if (_trackedKeyFrames.Add(keyFrame))
        {
            keyFrame.PropertyChanged += KeyFrameOnPropertyChanged;
        }
    }

    private void TracksCollectionOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetSubscriptions(Tracks);
        InvalidateVisual();
    }

    private void KeyFramesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetSubscriptions(Tracks);
        InvalidateVisual();
    }

    private void TrackOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnimationTimelineTrackViewModel.PropertyName) or
            nameof(AnimationTimelineTrackViewModel.TargetSelector) or
            nameof(AnimationTimelineTrackViewModel.Title))
        {
            InvalidateVisual();
        }
    }

    private void KeyFrameOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnimationTimelineKeyFrameViewModel.CuePercent) or
            nameof(AnimationTimelineKeyFrameViewModel.Value) or
            nameof(AnimationTimelineKeyFrameViewModel.Title))
        {
            InvalidateVisual();
        }
    }

    private static bool TryInterpolateValue(string from, string to, double progress, out string value)
    {
        if (TryParseDouble(from, out var fromDouble) &&
            TryParseDouble(to, out var toDouble))
        {
            value = FormatDouble(fromDouble + ((toDouble - fromDouble) * progress));
            return true;
        }

        var fromValues = ParseDoubleList(from);
        var toValues = ParseDoubleList(to);
        if (fromValues.Length > 0 &&
            fromValues.Length == toValues.Length)
        {
            value = string.Join(
                ",",
                fromValues
                    .Zip(toValues, (fromValue, toValue) => FormatDouble(fromValue + ((toValue - fromValue) * progress))));
            return true;
        }

        value = from;
        return false;
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> setters, string propertyName, double fallback)
    {
        return setters.TryGetValue(propertyName, out var value) &&
               TryParseDouble(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static IBrush? GetBrush(IReadOnlyDictionary<string, string> setters, string propertyName)
    {
        if (!setters.TryGetValue(propertyName, out var value) ||
            string.IsNullOrWhiteSpace(value) ||
            value.Contains('{', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Brush.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static double[] ParseDoubleList(string value)
    {
        return value
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => TryParseDouble(part, out var parsed) ? parsed : double.NaN)
            .Where(static parsed => !double.IsNaN(parsed) && !double.IsInfinity(parsed))
            .ToArray();
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Point origin,
        double fontSize,
        IBrush brush,
        FontWeight fontWeight = default)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, fontWeight == default ? FontWeight.Normal : fontWeight),
            fontSize,
            brush);

        context.DrawText(formattedText, origin);
    }
}
