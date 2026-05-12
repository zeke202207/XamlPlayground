using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.VisualEditing;

namespace XamlPlayground.Views.Docking;

public partial class AnimationTimelineDockView : UserControl
{
    private AnimationTimelineKeyFrameViewModel? _dragKeyFrame;
    private AnimationTimelineTrackViewModel? _dragTrack;
    private Control? _dragCapture;
    private Canvas? _dragCanvas;

    public AnimationTimelineDockView()
    {
        InitializeComponent();
        AddHandler(PointerCaptureLostEvent, OnKeyFramePointerCaptureLost, handledEventsToo: true);
    }

    private MainViewModel? Shell => DataContext switch
    {
        VisualAnimationsDockViewModel visualAnimations => visualAnimations.Shell,
        ControlThemePanelDockViewModel controlThemePanel => controlThemePanel.Shell,
        _ => null
    };

    private void OnKeyFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control marker ||
            marker.DataContext is not AnimationTimelineKeyFrameViewModel keyFrame)
        {
            return;
        }

        var point = e.GetCurrentPoint(marker);
        if (!point.Properties.IsLeftButtonPressed &&
            point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        var track = FindDataContextInAncestors<AnimationTimelineTrackViewModel>(marker);
        var canvas = marker.GetVisualAncestors().OfType<Canvas>().FirstOrDefault();
        if (track is null || canvas is null)
        {
            return;
        }

        if (Shell is { } shell)
        {
            shell.SelectedAnimationTimelineTrack = track;
            shell.SelectedAnimationTimelineKeyFrame = keyFrame;
            shell.AnimationCuePercent = keyFrame.CuePercent;
            shell.AnimationCurrentTimePercent = keyFrame.CuePercent;
        }

        _dragKeyFrame = keyFrame;
        _dragTrack = track;
        _dragCapture = marker;
        _dragCanvas = canvas;
        e.Pointer.Capture(marker);
        e.Handled = true;
    }

    private void OnKeyFramePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragKeyFrame is null ||
            _dragTrack is null ||
            _dragCanvas is null ||
            _dragCapture is null ||
            Shell is not { } shell)
        {
            return;
        }

        var width = _dragCanvas.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var position = e.GetPosition(_dragCanvas);
        var cue = Math.Clamp((int)Math.Round(position.X / width * 100), 0, 100);
        _dragKeyFrame.CuePercent = cue;
        shell.SelectedAnimationTimelineTrack = _dragTrack;
        shell.SelectedAnimationTimelineKeyFrame = _dragKeyFrame;
        shell.AnimationCuePercent = cue;
        shell.AnimationCurrentTimePercent = cue;
        e.Handled = true;
    }

    private void OnKeyFramePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCapture is null)
        {
            return;
        }

        if (Shell is { UpdateAnimationKeyFrameCommand: { } updateCommand } &&
            updateCommand.CanExecute(null))
        {
            updateCommand.Execute(null);
        }

        e.Pointer.Capture(null);
        ClearKeyFrameDrag();
        e.Handled = true;
    }

    private void OnKeyFramePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearKeyFrameDrag();
    }

    private void ClearKeyFrameDrag()
    {
        _dragKeyFrame = null;
        _dragTrack = null;
        _dragCapture = null;
        _dragCanvas = null;
    }

    private static T? FindDataContextInAncestors<T>(Control control)
        where T : class
    {
        return control
            .GetVisualAncestors()
            .OfType<Control>()
            .Select(static ancestor => ancestor.DataContext)
            .OfType<T>()
            .FirstOrDefault();
    }
}
