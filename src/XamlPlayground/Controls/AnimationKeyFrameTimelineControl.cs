using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using XamlPlayground.ViewModels.VisualEditing;

namespace XamlPlayground.Controls;

public sealed class AnimationKeyFrameTimelineControl : Control
{
    public static readonly StyledProperty<IEnumerable<AnimationTimelineTrackViewModel>?> TracksProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IEnumerable<AnimationTimelineTrackViewModel>?>(
            nameof(Tracks));

    public static readonly StyledProperty<AnimationTimelineTrackViewModel?> SelectedTrackProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, AnimationTimelineTrackViewModel?>(
            nameof(SelectedTrack),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<AnimationTimelineKeyFrameViewModel?> SelectedKeyFrameProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, AnimationTimelineKeyFrameViewModel?>(
            nameof(SelectedKeyFrame),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> CurrentTimePercentProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, int>(
            nameof(CurrentTimePercent),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> FrameCountProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, int>(
            nameof(FrameCount),
            100);

    public static readonly StyledProperty<ICommand?> CommitKeyFrameCommandProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, ICommand?>(
            nameof(CommitKeyFrameCommand));

    public static readonly StyledProperty<IBrush?> PanelBackgroundProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(PanelBackground),
            Brushes.Transparent);

    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(HeaderBackground),
            Brushes.Transparent);

    public static readonly StyledProperty<IBrush?> TrackBackgroundProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(TrackBackground),
            Brushes.White);

    public static readonly StyledProperty<IBrush?> SelectedTrackBackgroundProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(SelectedTrackBackground),
            Brushes.LightBlue);

    public static readonly StyledProperty<IBrush?> TimelineBorderBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(TimelineBorderBrush),
            Brushes.Gray);

    public static readonly StyledProperty<IBrush?> GridLineBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(GridLineBrush),
            Brushes.LightGray);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(TextBrush),
            Brushes.Black);

    public static readonly StyledProperty<IBrush?> MutedTextBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(MutedTextBrush),
            Brushes.Gray);

    public static readonly StyledProperty<IBrush?> KeyFrameBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(KeyFrameBrush),
            Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush?> KeyFrameBorderBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(KeyFrameBorderBrush),
            Brushes.White);

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty =
        AvaloniaProperty.Register<AnimationKeyFrameTimelineControl, IBrush?>(
            nameof(PlayheadBrush),
            Brushes.Red);

    private const double HeaderHeight = 26.0;
    private const double RulerHeight = 24.0;
    private const double RowHeight = 42.0;
    private const double GutterWidth = 178.0;
    private const double KeyFrameRadius = 5.0;
    private const double MinimumTimelineWidth = 560.0;
    private const double MinimumTimelineHeight = 180.0;

    private readonly HashSet<AnimationTimelineTrackViewModel> _trackedTracks = new();
    private readonly HashSet<AnimationTimelineKeyFrameViewModel> _trackedKeyFrames = new();
    private INotifyCollectionChanged? _tracksCollection;
    private AnimationTimelineKeyFrameViewModel? _draggingKeyFrame;
    private bool _draggingPlayhead;

    public AnimationKeyFrameTimelineControl()
    {
        Focusable = true;
    }

    public IEnumerable<AnimationTimelineTrackViewModel>? Tracks
    {
        get => GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    public AnimationTimelineTrackViewModel? SelectedTrack
    {
        get => GetValue(SelectedTrackProperty);
        set => SetValue(SelectedTrackProperty, value);
    }

    public AnimationTimelineKeyFrameViewModel? SelectedKeyFrame
    {
        get => GetValue(SelectedKeyFrameProperty);
        set => SetValue(SelectedKeyFrameProperty, value);
    }

    public int CurrentTimePercent
    {
        get => GetValue(CurrentTimePercentProperty);
        set => SetValue(CurrentTimePercentProperty, Math.Clamp(value, 0, FrameCount));
    }

    public int FrameCount
    {
        get => GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, Math.Max(1, value));
    }

    public ICommand? CommitKeyFrameCommand
    {
        get => GetValue(CommitKeyFrameCommandProperty);
        set => SetValue(CommitKeyFrameCommandProperty, value);
    }

    public IBrush? PanelBackground
    {
        get => GetValue(PanelBackgroundProperty);
        set => SetValue(PanelBackgroundProperty, value);
    }

    public IBrush? HeaderBackground
    {
        get => GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public IBrush? TrackBackground
    {
        get => GetValue(TrackBackgroundProperty);
        set => SetValue(TrackBackgroundProperty, value);
    }

    public IBrush? SelectedTrackBackground
    {
        get => GetValue(SelectedTrackBackgroundProperty);
        set => SetValue(SelectedTrackBackgroundProperty, value);
    }

    public IBrush? TimelineBorderBrush
    {
        get => GetValue(TimelineBorderBrushProperty);
        set => SetValue(TimelineBorderBrushProperty, value);
    }

    public IBrush? GridLineBrush
    {
        get => GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
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

    public IBrush? KeyFrameBrush
    {
        get => GetValue(KeyFrameBrushProperty);
        set => SetValue(KeyFrameBrushProperty, value);
    }

    public IBrush? KeyFrameBorderBrush
    {
        get => GetValue(KeyFrameBorderBrushProperty);
        set => SetValue(KeyFrameBorderBrushProperty, value);
    }

    public IBrush? PlayheadBrush
    {
        get => GetValue(PlayheadBrushProperty);
        set => SetValue(PlayheadBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(PanelBackground ?? Brushes.Transparent, null, bounds);

        var tracks = GetTracks();
        var borderPen = new Pen(TimelineBorderBrush ?? Brushes.Gray, 1);
        var gridPen = new Pen(GridLineBrush ?? Brushes.LightGray, 1);
        var textBrush = TextBrush ?? Brushes.Black;
        var mutedTextBrush = MutedTextBrush ?? textBrush;
        var timelineRect = GetTimelineRect(bounds.Width);
        var contentHeight = HeaderHeight + RulerHeight + Math.Max(1, tracks.Count) * RowHeight;

        context.DrawRectangle(HeaderBackground ?? Brushes.Transparent, null, new Rect(0, 0, bounds.Width, HeaderHeight + RulerHeight));
        context.DrawLine(borderPen, new Point(0, HeaderHeight), new Point(bounds.Width, HeaderHeight));
        context.DrawLine(borderPen, new Point(0, HeaderHeight + RulerHeight), new Point(bounds.Width, HeaderHeight + RulerHeight));
        context.DrawLine(borderPen, new Point(GutterWidth, 0), new Point(GutterWidth, Math.Max(contentHeight, bounds.Height)));

        DrawText(context, "Layers", new Point(12, 6), 11, mutedTextBrush, FontWeight.SemiBold);
        DrawText(context, "Frames", new Point(GutterWidth + 10, 6), 11, mutedTextBrush, FontWeight.SemiBold);
        DrawRuler(context, timelineRect, gridPen, mutedTextBrush);

        if (tracks.Count == 0)
        {
            DrawEmptyState(context, bounds, textBrush, mutedTextBrush, borderPen);
        }
        else
        {
            DrawTracks(context, tracks, timelineRect, borderPen, gridPen, textBrush, mutedTextBrush);
        }

        DrawPlayhead(context, timelineRect, Math.Max(contentHeight, bounds.Height), borderPen);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var trackCount = GetTracks().Count;
        var desiredWidth = double.IsInfinity(availableSize.Width)
            ? GutterWidth + MinimumTimelineWidth
            : availableSize.Width;
        var desiredHeight = HeaderHeight + RulerHeight + Math.Max(3, trackCount) * RowHeight;

        return new Size(
            Math.Max(GutterWidth + MinimumTimelineWidth, desiredWidth),
            Math.Max(MinimumTimelineHeight, desiredHeight));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var position = e.GetPosition(this);
        var hit = HitTestTimeline(position);
        if (hit.Track is not null)
        {
            SelectedTrack = hit.Track;
        }

        if (hit.KeyFrame is not null)
        {
            SelectedKeyFrame = hit.KeyFrame;
            CurrentTimePercent = hit.KeyFrame.CuePercent;
            _draggingKeyFrame = hit.KeyFrame;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (hit.Track is not null || position.X >= GutterWidth)
        {
            CurrentTimePercent = PositionToCue(position.X);
            _draggingPlayhead = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingKeyFrame is null && !_draggingPlayhead)
        {
            return;
        }

        var cue = PositionToCue(e.GetPosition(this).X);
        CurrentTimePercent = cue;
        if (_draggingKeyFrame is not null)
        {
            _draggingKeyFrame.CuePercent = cue;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingKeyFrame is not null)
        {
            SelectedKeyFrame = null;
            SelectedKeyFrame = _draggingKeyFrame;
            if (CommitKeyFrameCommand?.CanExecute(null) == true)
            {
                CommitKeyFrameCommand.Execute(null);
            }
        }

        _draggingKeyFrame = null;
        _draggingPlayhead = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
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
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        if (change.Property == CurrentTimePercentProperty)
        {
            var value = change.GetNewValue<int>();
            var clamped = Math.Clamp(value, 0, FrameCount);
            if (clamped != value)
            {
                CurrentTimePercent = clamped;
                return;
            }
        }

        if (change.Property == SelectedTrackProperty ||
            change.Property == SelectedKeyFrameProperty ||
            change.Property == CurrentTimePercentProperty ||
            change.Property == FrameCountProperty ||
            change.Property == PanelBackgroundProperty ||
            change.Property == HeaderBackgroundProperty ||
            change.Property == TrackBackgroundProperty ||
            change.Property == SelectedTrackBackgroundProperty ||
            change.Property == TimelineBorderBrushProperty ||
            change.Property == GridLineBrushProperty ||
            change.Property == TextBrushProperty ||
            change.Property == MutedTextBrushProperty ||
            change.Property == KeyFrameBrushProperty ||
            change.Property == KeyFrameBorderBrushProperty ||
            change.Property == PlayheadBrushProperty)
        {
            InvalidateVisual();
        }
    }

    private void DrawRuler(DrawingContext context, Rect timelineRect, Pen gridPen, IBrush mutedTextBrush)
    {
        var frameCount = FrameCount;
        var majorStep = frameCount <= 60 ? 5 : 10;
        var minorStep = frameCount <= 60 ? 1 : 5;

        for (var frame = 0; frame <= frameCount; frame += minorStep)
        {
            var x = CueToX(frame, timelineRect);
            var isMajor = frame % majorStep == 0 || frame == frameCount;
            var tickTop = isMajor ? HeaderHeight + 5 : HeaderHeight + 13;
            context.DrawLine(gridPen, new Point(x, tickTop), new Point(x, HeaderHeight + RulerHeight));

            if (isMajor)
            {
                DrawText(context, frame.ToString(CultureInfo.InvariantCulture), new Point(x + 3, HeaderHeight + 4), 10, mutedTextBrush);
            }
        }
    }

    private void DrawTracks(
        DrawingContext context,
        IReadOnlyList<AnimationTimelineTrackViewModel> tracks,
        Rect timelineRect,
        Pen borderPen,
        Pen gridPen,
        IBrush textBrush,
        IBrush mutedTextBrush)
    {
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var rowTop = HeaderHeight + RulerHeight + index * RowHeight;
            var rowRect = new Rect(0, rowTop, Bounds.Width, RowHeight);
            var trackBrush = ReferenceEquals(track, SelectedTrack)
                ? SelectedTrackBackground ?? TrackBackground
                : TrackBackground;

            context.DrawRectangle(trackBrush, null, rowRect);
            context.DrawLine(borderPen, new Point(0, rowTop + RowHeight), new Point(Bounds.Width, rowTop + RowHeight));
            DrawGridForRow(context, timelineRect, rowTop, gridPen);
            DrawLayerLabel(context, track, rowRect, textBrush, mutedTextBrush);
            DrawTrackKeys(context, track, timelineRect, rowTop, borderPen);
        }
    }

    private void DrawGridForRow(DrawingContext context, Rect timelineRect, double rowTop, Pen gridPen)
    {
        var frameCount = FrameCount;
        var step = frameCount <= 60 ? 5 : 10;
        for (var frame = 0; frame <= frameCount; frame += step)
        {
            var x = CueToX(frame, timelineRect);
            context.DrawLine(gridPen, new Point(x, rowTop), new Point(x, rowTop + RowHeight));
        }
    }

    private void DrawLayerLabel(
        DrawingContext context,
        AnimationTimelineTrackViewModel track,
        Rect rowRect,
        IBrush textBrush,
        IBrush mutedTextBrush)
    {
        var clip = new Rect(8, rowRect.Y + 2, GutterWidth - 16, RowHeight - 4);
        using (context.PushClip(clip))
        {
            DrawText(context, track.PropertyName, new Point(12, rowRect.Y + 7), 12, textBrush, FontWeight.SemiBold);
            DrawText(context, track.TargetSelector, new Point(12, rowRect.Y + 24), 10, mutedTextBrush);
        }
    }

    private void DrawTrackKeys(
        DrawingContext context,
        AnimationTimelineTrackViewModel track,
        Rect timelineRect,
        double rowTop,
        Pen borderPen)
    {
        var laneRect = new Rect(timelineRect.X, rowTop + 12, timelineRect.Width, 18);
        context.DrawRectangle(null, borderPen, laneRect, 3, 3);

        foreach (var keyFrame in track.KeyFrames.OrderBy(static keyFrame => keyFrame.CuePercent))
        {
            var x = CueToX(keyFrame.CuePercent, timelineRect);
            var center = new Point(x, rowTop + RowHeight / 2);
            var isSelected = ReferenceEquals(keyFrame, SelectedKeyFrame);
            var radius = isSelected ? KeyFrameRadius + 2 : KeyFrameRadius;
            var keyBrush = KeyFrameBrush ?? Brushes.DodgerBlue;
            var keyBorder = new Pen(isSelected ? PlayheadBrush ?? keyBrush : KeyFrameBorderBrush ?? Brushes.White, isSelected ? 2 : 1);

            context.DrawEllipse(keyBrush, keyBorder, center, radius, radius);
        }
    }

    private void DrawPlayhead(DrawingContext context, Rect timelineRect, double contentHeight, Pen borderPen)
    {
        var cue = Math.Clamp(CurrentTimePercent, 0, FrameCount);
        var x = CueToX(cue, timelineRect);
        var playheadBrush = PlayheadBrush ?? Brushes.Red;
        var playheadPen = new Pen(playheadBrush, 2);
        var triangle = new StreamGeometry();

        using (var geometryContext = triangle.Open())
        {
            geometryContext.BeginFigure(new Point(x, HeaderHeight + 1), isFilled: true);
            geometryContext.LineTo(new Point(x - 6, HeaderHeight + 10));
            geometryContext.LineTo(new Point(x + 6, HeaderHeight + 10));
            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(playheadBrush, null, triangle);
        context.DrawLine(playheadPen, new Point(x, HeaderHeight + 10), new Point(x, contentHeight));
        context.DrawLine(borderPen, new Point(timelineRect.X, contentHeight), new Point(timelineRect.Right, contentHeight));
    }

    private void DrawEmptyState(DrawingContext context, Rect bounds, IBrush textBrush, IBrush mutedTextBrush, Pen borderPen)
    {
        var emptyRect = new Rect(8, HeaderHeight + RulerHeight + 10, Math.Max(0, bounds.Width - 16), 74);
        context.DrawRectangle(TrackBackground, borderPen, emptyRect, 4, 4);
        DrawText(context, "No animation tracks", new Point(emptyRect.X + 12, emptyRect.Y + 14), 13, textBrush, FontWeight.SemiBold);
        DrawText(context, "Choose a target and add or capture keyframes to populate the timeline.", new Point(emptyRect.X + 12, emptyRect.Y + 38), 11, mutedTextBrush);
    }

    private TimelineHit HitTestTimeline(Point position)
    {
        var tracks = GetTracks();
        var rowIndex = (int)Math.Floor((position.Y - HeaderHeight - RulerHeight) / RowHeight);
        if (rowIndex < 0 || rowIndex >= tracks.Count)
        {
            return default;
        }

        var track = tracks[rowIndex];
        var rowTop = HeaderHeight + RulerHeight + rowIndex * RowHeight;
        var timelineRect = GetTimelineRect(Bounds.Width);
        var nearestKeyFrame = track.KeyFrames
            .Select(keyFrame => new
            {
                KeyFrame = keyFrame,
                X = CueToX(keyFrame.CuePercent, timelineRect)
            })
            .Where(candidate =>
                Math.Abs(candidate.X - position.X) <= KeyFrameRadius + 4 &&
                Math.Abs(rowTop + RowHeight / 2 - position.Y) <= KeyFrameRadius + 8)
            .OrderBy(candidate => Math.Abs(candidate.X - position.X))
            .FirstOrDefault()
            ?.KeyFrame;

        return new TimelineHit(track, nearestKeyFrame);
    }

    private int PositionToCue(double x)
    {
        var timelineRect = GetTimelineRect(Bounds.Width);
        if (timelineRect.Width <= 0)
        {
            return 0;
        }

        var ratio = Math.Clamp((x - timelineRect.X) / timelineRect.Width, 0, 1);
        return (int)Math.Round(ratio * FrameCount, MidpointRounding.AwayFromZero);
    }

    private double CueToX(int cue, Rect timelineRect)
    {
        var ratio = Math.Clamp(cue, 0, FrameCount) / (double)FrameCount;
        return timelineRect.X + timelineRect.Width * ratio;
    }

    private Rect GetTimelineRect(double width)
    {
        return new Rect(GutterWidth, HeaderHeight, Math.Max(1, width - GutterWidth - 8), Math.Max(1, Bounds.Height - HeaderHeight));
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
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void KeyFramesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetSubscriptions(Tracks);
        InvalidateMeasure();
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

    private readonly record struct TimelineHit(
        AnimationTimelineTrackViewModel? Track,
        AnimationTimelineKeyFrameViewModel? KeyFrame);
}
