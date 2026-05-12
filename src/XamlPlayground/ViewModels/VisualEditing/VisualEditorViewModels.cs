using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using XamlPlayground.Services.Animation;
using XamlPlayground.Services.VisualEditing;

namespace XamlPlayground.ViewModels.VisualEditing;

public sealed class VisualEditorNodeViewModel : ViewModelBase
{
    public VisualEditorNodeViewModel(XamlElementSnapshot element)
    {
        Element = element;
    }

    public XamlElementSnapshot Element { get; }

    public ObservableCollection<VisualEditorNodeViewModel> Children { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Element.Name)
        ? Element.TypeName
        : $"{Element.TypeName} #{Element.Name}";

    public string PathText => Element.Path.Count == 0
        ? "root"
        : string.Join(".", Element.Path);

    public string TypeName => Element.TypeName;

    public string Name => Element.Name ?? string.Empty;

    public int ChildElementCount => Element.ChildElementCount;

    public string Badge => Element.ChildElementCount > 0 ? "+" : "-";
}

public sealed class VisualEditorStructureRowViewModel : ViewModelBase
{
    public VisualEditorStructureRowViewModel(VisualEditorNodeViewModel node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    public VisualEditorNodeViewModel Node { get; }

    public XamlElementSnapshot Element => Node.Element;

    public int Depth { get; }

    public double Indent => Depth * 14;

    public string Title => Node.Title;

    public string TypeName => Element.TypeName;

    public string Name => Element.Name ?? string.Empty;

    public string PathText => Node.PathText;

    public int ChildElementCount => Element.ChildElementCount;
}

public sealed partial class VisualEditorPropertyViewModel : ViewModelBase
{
    private readonly Action<VisualEditorPropertyViewModel, string>? _valueChanged;
    private bool _isUpdatingFromModel;
    private bool _isSet;
    private string _value;

    public VisualEditorPropertyViewModel(
        ControlEditorProperty property,
        string value,
        bool isSet,
        string mutationName,
        Action<VisualEditorPropertyViewModel, string>? valueChanged)
    {
        Property = property;
        Name = property.PropertyName;
        MutationName = mutationName;
        Kind = property.ValueKind.ToString();
        Category = property.Group ?? "Common";
        _value = value;
        _isSet = isSet;
        _valueChanged = valueChanged;
    }

    public ControlEditorProperty Property { get; }

    public string Name { get; }

    public string MutationName { get; }

    public string Kind { get; }

    public string TypeName => Property.ValueType?.Name ?? Kind;

    public string Category { get; }

    public string Group => IsSet ? "Assigned Values" : Category;

    public bool IsSet
    {
        get => _isSet;
        private set
        {
            if (SetProperty(ref _isSet, value))
            {
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(Group));
            }
        }
    }

    public string Priority => IsSet ? "Local" : "Default";

    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value))
            {
                return;
            }

            if (_isUpdatingFromModel)
            {
                return;
            }

            IsSet = true;
            _valueChanged?.Invoke(this, value);
        }
    }

    public void UpdateFromModel(string value, bool isSet)
    {
        try
        {
            _isUpdatingFromModel = true;
            Value = value;
            IsSet = isSet;
        }
        finally
        {
            _isUpdatingFromModel = false;
        }
    }
}

public sealed class VisualEditorAvailablePropertyViewModel : ViewModelBase
{
    public VisualEditorAvailablePropertyViewModel(ControlEditorProperty property)
    {
        Property = property;
    }

    public ControlEditorProperty Property { get; }

    public string Name => Property.PropertyName;

    public string DisplayName => Property.DisplayName;

    public string Group => Property.Group ?? "Common";

    public string Kind => Property.ValueKind.ToString();
}

public sealed class ControlThemeDefinitionViewModel : ViewModelBase
{
    public ControlThemeDefinitionViewModel(string key, string targetType, string filePath)
    {
        Key = key;
        TargetType = targetType;
        FilePath = filePath;
    }

    public string Key { get; }

    public string TargetType { get; }

    public string FilePath { get; }

    public string Title => $"{Key} ({TargetType})";
}

public sealed class FluentControlThemeTemplateViewModel : ViewModelBase
{
    public FluentControlThemeTemplateViewModel(string key, string targetType, string sourcePath)
    {
        Key = key;
        TargetType = targetType;
        SourcePath = sourcePath;
    }

    public string Key { get; }

    public string TargetType { get; }

    public string SourcePath { get; }

    public string Title => $"{TargetType} - {Key}";
}

public sealed class ThemeResourceViewModel : ViewModelBase
{
    public ThemeResourceViewModel(
        string key,
        string resourceType,
        string? targetType,
        string filePath,
        int? line,
        string themeScope)
    {
        Key = key;
        ResourceType = resourceType;
        TargetType = targetType ?? string.Empty;
        FilePath = filePath;
        Line = line;
        ThemeScope = themeScope;
    }

    public string Key { get; }

    public string ResourceType { get; }

    public string TargetType { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public string ThemeScope { get; }

    public string Location => Line is { } line
        ? $"{FilePath}:{line}"
        : FilePath;

    public string Title => string.IsNullOrWhiteSpace(TargetType)
        ? $"{Key} - {ResourceType}"
        : $"{Key} - {ResourceType} ({TargetType})";
}

public sealed class ThemeResourceUsageViewModel : ViewModelBase
{
    public ThemeResourceUsageViewModel(
        string key,
        string kind,
        string filePath,
        int line,
        string snippet)
    {
        Key = key;
        Kind = kind;
        FilePath = filePath;
        Line = line;
        Snippet = snippet;
    }

    public string Key { get; }

    public string Kind { get; }

    public string FilePath { get; }

    public int Line { get; }

    public string Snippet { get; }

    public string Location => $"{FilePath}:{Line}";

    public string Title => $"{Kind} {Key}";
}

public sealed class ThemeResourceDiagnosticViewModel : ViewModelBase
{
    public ThemeResourceDiagnosticViewModel(
        string severity,
        string message,
        string filePath,
        int? line)
    {
        Severity = severity;
        Message = message;
        FilePath = filePath;
        Line = line;
    }

    public string Severity { get; }

    public string Message { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public string Location => Line is { } line
        ? $"{FilePath}:{line}"
        : FilePath;

    public string Title => $"{Severity}: {Message}";
}

public sealed class ThemeResourceDeleteChangeViewModel : ViewModelBase
{
    public ThemeResourceDeleteChangeViewModel(
        string filePath,
        string changeKind,
        int removedLineCount,
        int addedLineCount,
        string diff)
    {
        FilePath = filePath;
        ChangeKind = changeKind;
        RemovedLineCount = removedLineCount;
        AddedLineCount = addedLineCount;
        Diff = diff;
    }

    public string FilePath { get; }

    public string ChangeKind { get; }

    public int RemovedLineCount { get; }

    public int AddedLineCount { get; }

    public string Diff { get; }

    public string Title => $"{ChangeKind}: {FilePath}";

    public string Summary => $"{RemovedLineCount} removed, {AddedLineCount} added";
}

public sealed class ThemePreviewStateViewModel : ViewModelBase
{
    public ThemePreviewStateViewModel(string state, bool hasSelectors)
    {
        State = state;
        HasSelectors = hasSelectors;
    }

    public string State { get; }

    public bool HasSelectors { get; }

    public string PseudoClass => string.Equals(State, "normal", StringComparison.Ordinal)
        ? string.Empty
        : $":{State}";

    public string Title => string.Equals(State, "normal", StringComparison.Ordinal)
        ? "Normal"
        : PseudoClass;
}

public sealed class ThemeStateSelectorViewModel : ViewModelBase
{
    public ThemeStateSelectorViewModel(string state, string selector, int? line)
    {
        State = state;
        Selector = selector;
        Line = line;
    }

    public string State { get; }

    public string Selector { get; }

    public int? Line { get; }

    public string Title => Line is { } line
        ? $"{Selector} : line {line}"
        : Selector;
}

public sealed class ThemeTemplatePartViewModel : ViewModelBase
{
    public ThemeTemplatePartViewModel(string name, string type, int? line)
    {
        Name = name;
        Type = type;
        Line = line;
    }

    public string Name { get; }

    public string Type { get; }

    public int? Line { get; }

    public string Title => Line is { } line
        ? $"{Name} - {Type} : line {line}"
        : $"{Name} - {Type}";
}

public sealed class ThemeTemplatePartSelectorViewModel : ViewModelBase
{
    public ThemeTemplatePartSelectorViewModel(
        string partName,
        string partType,
        string state,
        string selector,
        int? line)
    {
        PartName = partName;
        PartType = partType;
        State = state;
        Selector = selector;
        Line = line;
    }

    public string PartName { get; }

    public string PartType { get; }

    public string State { get; }

    public string Selector { get; }

    public int? Line { get; }

    public string Title => Line is { } line
        ? $"{Selector} : line {line}"
        : Selector;
}

public sealed class ThemeTemplateBindingViewModel : ViewModelBase
{
    public ThemeTemplateBindingViewModel(string property, int line, string snippet)
    {
        Property = property;
        Line = line;
        Snippet = snippet;
    }

    public string Property { get; }

    public int Line { get; }

    public string Snippet { get; }

    public string Title => Line > 0
        ? $"{Property} : line {Line}"
        : Property;
}

public sealed class ThemeVariantViewModel : ViewModelBase
{
    public ThemeVariantViewModel(string name, int fileCount, string files)
    {
        Name = name;
        FileCount = fileCount;
        Files = files;
    }

    public string Name { get; }

    public int FileCount { get; }

    public string Files { get; }

    public string Title => $"{Name} ({FileCount})";
}

public sealed class AnimationTargetOptionViewModel : ViewModelBase
{
    public AnimationTargetOptionViewModel(
        string id,
        string title,
        string scope,
        string selector,
        int? styleIndex = null)
    {
        Id = id;
        Title = title;
        Scope = scope;
        Selector = selector;
        StyleIndex = styleIndex;
    }

    public string Id { get; }

    public string Title { get; }

    public string Scope { get; }

    public string Selector { get; }

    public int? StyleIndex { get; }

    public string DisplayText => $"{Title} - {Selector}";
}

public sealed class AnimationPresetViewModel : ViewModelBase
{
    public AnimationPresetViewModel(
        string title,
        string propertyName,
        string fromValue,
        string toValue,
        string duration,
        string easing)
    {
        Title = title;
        PropertyName = propertyName;
        FromValue = fromValue;
        ToValue = toValue;
        Duration = duration;
        Easing = easing;
    }

    public string Title { get; }

    public string PropertyName { get; }

    public string FromValue { get; }

    public string ToValue { get; }

    public string Duration { get; }

    public string Easing { get; }

    public string Summary => $"{PropertyName}: {FromValue} -> {ToValue}";
}

public sealed class AnimationTimelineKeyFrameViewModel : ViewModelBase
{
    private const double TimelineAvailableWidth = 420.0;
    private int _cuePercent;
    private string _value;
    private string _keySpline;

    public AnimationTimelineKeyFrameViewModel(int cuePercent, string value, string keySpline)
    {
        _cuePercent = Math.Clamp(cuePercent, 0, 100);
        _value = value;
        _keySpline = keySpline;
    }

    public int CuePercent
    {
        get => _cuePercent;
        set
        {
            if (SetProperty(ref _cuePercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(TimeText));
                OnPropertyChanged(nameof(TimelineLeft));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string KeySpline
    {
        get => _keySpline;
        set => SetProperty(ref _keySpline, value);
    }

    public double TimelineLeft => CuePercent / 100.0 * TimelineAvailableWidth;

    public string TimeText => $"{CuePercent}%";

    public string Title => $"{TimeText} = {Value}";
}

public sealed class AnimationTimelineTrackViewModel : ViewModelBase
{
    private string _targetSelector;
    private string _propertyName;
    private readonly HashSet<AnimationTimelineKeyFrameViewModel> _trackedKeyFrames = new();

    public AnimationTimelineTrackViewModel(
        string targetSelector,
        string propertyName,
        ObservableCollection<AnimationTimelineKeyFrameViewModel> keyFrames)
    {
        _targetSelector = targetSelector;
        _propertyName = propertyName;
        KeyFrames = keyFrames;
        ResetKeyFrameSubscriptions();

        KeyFrames.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                ResetKeyFrameSubscriptions();
            }
            else
            {
                if (args.OldItems is not null)
                {
                    foreach (AnimationTimelineKeyFrameViewModel keyFrame in args.OldItems)
                    {
                        RemoveKeyFrameSubscription(keyFrame);
                    }
                }

                if (args.NewItems is not null)
                {
                    foreach (AnimationTimelineKeyFrameViewModel keyFrame in args.NewItems)
                    {
                        AddKeyFrameSubscription(keyFrame);
                    }
                }
            }

            OnPropertyChanged(nameof(KeyFrameCount));
            OnPropertyChanged(nameof(KeySummary));
        };
    }

    public string TargetSelector
    {
        get => _targetSelector;
        set => SetProperty(ref _targetSelector, value);
    }

    public string PropertyName
    {
        get => _propertyName;
        set
        {
            if (SetProperty(ref _propertyName, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public ObservableCollection<AnimationTimelineKeyFrameViewModel> KeyFrames { get; }

    public int KeyFrameCount => KeyFrames.Count;

    public string Title => $"{PropertyName} ({TargetSelector})";

    public string KeySummary => string.Join(", ", KeyFrames
        .OrderBy(static frame => frame.CuePercent)
        .Select(static frame => frame.Title));

    private void KeyFrameOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnimationTimelineKeyFrameViewModel.CuePercent) or
            nameof(AnimationTimelineKeyFrameViewModel.Value) or
            nameof(AnimationTimelineKeyFrameViewModel.KeySpline) or
            nameof(AnimationTimelineKeyFrameViewModel.Title))
        {
            OnPropertyChanged(nameof(KeySummary));
        }
    }

    private void ResetKeyFrameSubscriptions()
    {
        foreach (var keyFrame in _trackedKeyFrames)
        {
            keyFrame.PropertyChanged -= KeyFrameOnPropertyChanged;
        }

        _trackedKeyFrames.Clear();
        foreach (var keyFrame in KeyFrames)
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

    private void RemoveKeyFrameSubscription(AnimationTimelineKeyFrameViewModel keyFrame)
    {
        if (_trackedKeyFrames.Remove(keyFrame))
        {
            keyFrame.PropertyChanged -= KeyFrameOnPropertyChanged;
        }
    }

    public AnimationTimelineTrackDefinition ToDefinition()
    {
        return new AnimationTimelineTrackDefinition(
            TargetSelector,
            PropertyName,
            KeyFrames
                .Select((frame, index) => new { Frame = frame, Index = index })
                .GroupBy(static item => item.Frame.CuePercent)
                .Select(static group => group
                    .OrderByDescending(static item => item.Index)
                    .First()
                    .Frame)
                .OrderBy(static frame => frame.CuePercent)
                .Select(frame => new AnimationTimelineKeyFrameDefinition(
                    frame.CuePercent,
                    PropertyName,
                    frame.Value,
                    frame.KeySpline))
                .ToArray());
    }
}
