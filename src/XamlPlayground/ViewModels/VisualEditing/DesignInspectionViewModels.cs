using System;
using System.Collections.ObjectModel;
using XamlPlayground.Services.DesignInspection;

namespace XamlPlayground.ViewModels.VisualEditing;

public sealed class DesignInspectorNodeViewModel : ViewModelBase
{
    public DesignInspectorNodeViewModel(
        string kind,
        string title,
        string detail = "",
        string filePath = "",
        int? line = null,
        int start = -1,
        int length = 0,
        XamlStyleDefinition? style = null,
        XamlStyleSetterDefinition? styleSetter = null,
        XamlBindingDefinition? binding = null,
        XamlResourceDefinition? resource = null)
    {
        Kind = kind;
        Title = title;
        Detail = detail;
        FilePath = filePath;
        Line = line;
        Start = start;
        Length = length;
        Style = style;
        StyleSetter = styleSetter;
        Binding = binding;
        Resource = resource;
    }

    public string Kind { get; }

    public string Title { get; }

    public string Detail { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int Start { get; }

    public int Length { get; }

    public XamlStyleDefinition? Style { get; }

    public XamlStyleSetterDefinition? StyleSetter { get; }

    public XamlBindingDefinition? Binding { get; }

    public XamlResourceDefinition? Resource { get; }

    public ObservableCollection<DesignInspectorNodeViewModel> Children { get; } = new();

    public string Location => Line is { } line && !string.IsNullOrWhiteSpace(FilePath)
        ? $"{FilePath}:{line}"
        : FilePath;

    public string PathText => string.IsNullOrWhiteSpace(FilePath)
        ? Kind
        : Line is { } line ? $"{FilePath}:{line}" : FilePath;

    public bool CanNavigate => !string.IsNullOrWhiteSpace(FilePath) && Start >= 0;
}

public sealed class StyleSetterEditorViewModel : ViewModelBase
{
    private string _propertyName;
    private string _value;

    public StyleSetterEditorViewModel(string propertyName, string value, bool isComplex, int? line = null)
    {
        _propertyName = propertyName;
        _value = value;
        IsComplex = isComplex;
        Line = line;
    }

    public string PropertyName
    {
        get => _propertyName;
        set => SetProperty(ref _propertyName, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsComplex { get; }

    public int? Line { get; }

    public string Kind => IsComplex ? "Element" : "Attribute";
}

public sealed class StyleSelectorTokenViewModel : ViewModelBase
{
    public StyleSelectorTokenViewModel(string kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public string Kind { get; }

    public string Value { get; }

    public string Title => string.IsNullOrWhiteSpace(Value)
        ? Kind
        : $"{Kind}: {Value}";
}

public sealed class BindingInspectorFieldViewModel : ViewModelBase
{
    public BindingInspectorFieldViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    public string Title => string.IsNullOrWhiteSpace(Value)
        ? Name
        : $"{Name}: {Value}";
}

public sealed class ResourceEditorSummaryViewModel : ViewModelBase
{
    public ResourceEditorSummaryViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    public string Title => string.IsNullOrWhiteSpace(Value)
        ? Name
        : $"{Name}: {Value}";
}
