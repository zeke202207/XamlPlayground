using System;
using System.Collections.Generic;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using XamlPlayground.Editor.Minimap.Inline;

namespace XamlPlayground.Services.Editing.InlineFeatures;

internal abstract class PlaygroundInlineExtensionBase : IEditorInlineExtension
{
    private readonly TimeSpan _refreshDelay = TimeSpan.FromMilliseconds(180);
    private readonly List<EditorCodeAnnotation> _annotations = new();
    private readonly List<EditorInlineControl> _inlineControls = new();
    private readonly List<EditorViewZone> _viewZones = new();
    private DispatcherTimer? _refreshTimer;
    private EditorInlineExtensionContext? _context;
    private TextDocument? _document;

    public IDisposable Attach(EditorInlineExtensionContext context)
    {
        _context = context;
        _refreshTimer = new DispatcherTimer { Interval = _refreshDelay };
        _refreshTimer.Tick += RefreshTimerOnTick;

        context.Editor.DocumentChanged += EditorOnDocumentChanged;
        SubscribeToDocument(context.Editor.Document);
        Refresh();

        return new EditorInlineRegistration(Detach);
    }

    protected EditorInlineExtensionContext Context =>
        _context ?? throw new InvalidOperationException("Inline extension is not attached.");

    protected void AddAnnotation(EditorCodeAnnotation annotation)
    {
        _annotations.Add(annotation);
        Context.Annotations.Add(annotation);
    }

    protected void AddInlineControl(EditorInlineControl inlineControl)
    {
        _inlineControls.Add(inlineControl);
        Context.InlineControls.Add(inlineControl);
    }

    protected void AddViewZone(EditorViewZone viewZone)
    {
        _viewZones.Add(viewZone);
        Context.ViewZones.Add(viewZone);
    }

    protected abstract void Build();

    protected void ScheduleRefresh()
    {
        if (_refreshTimer is null)
        {
            Refresh();
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        _refreshTimer?.Stop();
        Refresh();
    }

    private void Refresh()
    {
        if (_context is null)
        {
            return;
        }

        ClearGenerated();
        Build();
        _context.Invalidate();
    }

    private void EditorOnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        SubscribeToDocument(e.NewDocument);
        ScheduleRefresh();
    }

    private void SubscribeToDocument(TextDocument? document)
    {
        if (ReferenceEquals(_document, document))
        {
            return;
        }

        if (_document is not null)
        {
            _document.TextChanged -= DocumentOnTextChanged;
        }

        _document = document;
        if (_document is not null)
        {
            _document.TextChanged += DocumentOnTextChanged;
        }
    }

    private void DocumentOnTextChanged(object? sender, EventArgs e)
    {
        ScheduleRefresh();
    }

    private void Detach()
    {
        if (_context is { } context)
        {
            context.Editor.DocumentChanged -= EditorOnDocumentChanged;
            context.ClosePeek();
        }

        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimerOnTick;
            _refreshTimer = null;
        }

        SubscribeToDocument(null);
        ClearGenerated();
        _context?.Invalidate();
        _context = null;
    }

    private void ClearGenerated()
    {
        if (_context is not { } context)
        {
            return;
        }

        foreach (var annotation in _annotations)
        {
            context.Annotations.Remove(annotation);
        }

        foreach (var inlineControl in _inlineControls)
        {
            context.InlineControls.Remove(inlineControl);
        }

        foreach (var viewZone in _viewZones)
        {
            context.ViewZones.Remove(viewZone);
        }

        _annotations.Clear();
        _inlineControls.Clear();
        _viewZones.Clear();
    }
}
