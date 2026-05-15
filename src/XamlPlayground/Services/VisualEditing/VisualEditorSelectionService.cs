using System;
using System.Collections.Generic;
using System.Linq;

namespace XamlPlayground.Services.VisualEditing;

public sealed record VisualEditorSelection(
    XamlDocumentSnapshot Document,
    XamlElementSnapshot? XamlElement,
    VisualTreeNodeSnapshot? VisualNode,
    IReadOnlyList<string> Diagnostics)
{
    public bool HasSelection => XamlElement is not null;
}

public sealed class VisualEditorSelectionService
{
    private readonly IXamlMutationEngine _mutationEngine;
    private readonly XamlVisualTreeMapper _visualTreeMapper;

    public VisualEditorSelectionService(
        IXamlMutationEngine mutationEngine,
        XamlVisualTreeMapper visualTreeMapper)
    {
        _mutationEngine = mutationEngine ?? throw new ArgumentNullException(nameof(mutationEngine));
        _visualTreeMapper = visualTreeMapper ?? throw new ArgumentNullException(nameof(visualTreeMapper));
    }

    public VisualEditorSelection? Current { get; private set; }

    public event EventHandler<VisualEditorSelection?>? SelectionChanged;

    public VisualEditorSelection SelectElement(string xaml, XamlElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(selector);

        var document = _mutationEngine.Analyze(xaml);
        var element = FindElement(document, selector);
        var diagnostics = element is null
            ? new[] { "The requested XAML element could not be selected." }
            : Array.Empty<string>();

        return SetCurrent(new VisualEditorSelection(document, element, null, diagnostics));
    }

    public VisualEditorSelection SelectVisual(
        string xaml,
        VisualTreeNodeSnapshot visualNode,
        bool allowTypeFallback = true)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(visualNode);

        var document = _mutationEngine.Analyze(xaml);
        var element = _visualTreeMapper.FindXamlElement(visualNode, document, allowTypeFallback);
        var diagnostics = element is null
            ? new[] { "The selected visual could not be mapped to a XAML source element." }
            : Array.Empty<string>();

        return SetCurrent(new VisualEditorSelection(document, element, visualNode, diagnostics));
    }

    public void Clear()
    {
        Current = null;
        SelectionChanged?.Invoke(this, Current);
    }

    private VisualEditorSelection SetCurrent(VisualEditorSelection selection)
    {
        Current = selection;
        SelectionChanged?.Invoke(this, Current);
        return selection;
    }

    private static XamlElementSnapshot? FindElement(XamlDocumentSnapshot document, XamlElementSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            return document.Elements.FirstOrDefault(element =>
                string.Equals(element.Name, selector.Name, StringComparison.Ordinal));
        }

        if (selector.Path is { } path)
        {
            return document.Elements.FirstOrDefault(element => PathEquals(element.Path, path));
        }

        if (!string.IsNullOrWhiteSpace(selector.TypeName))
        {
            return document.Elements.FirstOrDefault(element =>
                string.Equals(element.TypeName, selector.TypeName, StringComparison.Ordinal));
        }

        return null;
    }

    private static bool PathEquals(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
