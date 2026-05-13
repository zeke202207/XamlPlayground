using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XamlPlayground.Services;
using XamlPlayground.Services.DesignInspection;
using XamlPlayground.Services.Editing;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels.VisualEditing;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels;

public partial class MainViewModel
{
    private readonly XamlDesignInspector _designInspector = new();
    private readonly XamlDesignEditor _designEditor = new();
    private XamlDesignInspection _designInspection = XamlDesignInspection.Empty;
    private XamlStyleDefinition? _selectedStyleDefinition;
    private XamlBindingDefinition? _selectedBindingDefinition;
    private XamlResourceDefinition? _selectedResourceDefinition;
    private StyleSetterEditorViewModel? _observedStyleEditorSetter;
    private bool _isRefreshingDesignInspection;
    private bool _isUpdatingStyleEditor;
    private bool _isApplyingDesignInspectionEdit;
    private bool _isSyncingStyleEditorSetter;

    [ObservableProperty] private ObservableCollection<DesignInspectorNodeViewModel> _styleInspectorNodes = new();
    [ObservableProperty] private HierarchicalModel<DesignInspectorNodeViewModel>? _styleInspectorModel;
    [ObservableProperty] private DesignInspectorNodeViewModel? _selectedStyleInspectorNode;
    [ObservableProperty] private ObservableCollection<DesignInspectorNodeViewModel> _bindingInspectorNodes = new();
    [ObservableProperty] private HierarchicalModel<DesignInspectorNodeViewModel>? _bindingInspectorModel;
    [ObservableProperty] private DesignInspectorNodeViewModel? _selectedBindingInspectorNode;
    [ObservableProperty] private ObservableCollection<DesignInspectorNodeViewModel> _resourceInspectorNodes = new();
    [ObservableProperty] private HierarchicalModel<DesignInspectorNodeViewModel>? _resourceInspectorModel;
    [ObservableProperty] private DesignInspectorNodeViewModel? _selectedResourceInspectorNode;
    [ObservableProperty] private string _designInspectionStatus = "No design inspection data.";

    [ObservableProperty] private string _styleEditorSelector = string.Empty;
    [ObservableProperty] private string _styleEditorTargetType = string.Empty;
    [ObservableProperty] private string _styleSelectorTypeName = string.Empty;
    [ObservableProperty] private string _styleSelectorClass = string.Empty;
    [ObservableProperty] private string _styleSelectorName = string.Empty;
    [ObservableProperty] private string _styleSelectorPseudoClass = string.Empty;
    [ObservableProperty] private string _styleSelectorTemplatePart = string.Empty;
    [ObservableProperty] private ObservableCollection<StyleSelectorTokenViewModel> _styleSelectorTokens = new();
    [ObservableProperty] private ObservableCollection<StyleSetterEditorViewModel> _styleEditorSetters = new();
    [ObservableProperty] private StyleSetterEditorViewModel? _selectedStyleEditorSetter;
    [ObservableProperty] private ObservableCollection<VisualEditorAvailablePropertyViewModel> _styleEditorAvailableProperties = new();
    [ObservableProperty] private VisualEditorAvailablePropertyViewModel? _selectedStyleEditorAvailableProperty;
    [ObservableProperty] private string _styleEditorPropertyName = string.Empty;
    [ObservableProperty] private string _styleEditorValue = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _styleEditorPropertyOptions = new();
    [ObservableProperty] private string? _selectedStyleEditorPropertyOption;
    [ObservableProperty] private string _styleEditorPreviewXaml = string.Empty;
    [ObservableProperty] private Control? _styleEditorPreviewControl;
    [ObservableProperty] private string _styleEditorStatus = "Select or create a style.";

    [ObservableProperty] private string _bindingEditorKind = "Binding";
    [ObservableProperty] private string _bindingEditorPropertyName = string.Empty;
    [ObservableProperty] private string _bindingEditorPath = string.Empty;
    [ObservableProperty] private string _bindingEditorMode = string.Empty;
    [ObservableProperty] private string _bindingEditorSource = string.Empty;
    [ObservableProperty] private string _bindingEditorElementName = string.Empty;
    [ObservableProperty] private string _bindingEditorRelativeSource = string.Empty;
    [ObservableProperty] private string _bindingEditorConverter = string.Empty;
    [ObservableProperty] private string _bindingEditorStringFormat = string.Empty;
    [ObservableProperty] private string _bindingEditorFallbackValue = string.Empty;
    [ObservableProperty] private string _bindingEditorTargetNullValue = string.Empty;
    [ObservableProperty] private string _bindingEditorRawValue = string.Empty;
    [ObservableProperty] private ObservableCollection<BindingInspectorFieldViewModel> _bindingEditorFields = new();
    [ObservableProperty] private string _bindingEditorStatus = "Select a binding.";

    [ObservableProperty] private string _resourceEditorKey = string.Empty;
    [ObservableProperty] private string _resourceEditorType = "SolidColorBrush";
    [ObservableProperty] private string _resourceEditorValue = "#0078D4";
    [ObservableProperty] private string _resourceEditorRawXaml = string.Empty;
    [ObservableProperty] private ObservableCollection<ResourceEditorSummaryViewModel> _resourceEditorSummary = new();
    [ObservableProperty] private string _resourceEditorStatus = "Select or create a resource.";

    public ICommand RefreshDesignInspectorsCommand { get; private set; } = null!;

    public ICommand OpenSelectedStyleSourceCommand { get; private set; } = null!;

    public ICommand OpenSelectedBindingSourceCommand { get; private set; } = null!;

    public ICommand OpenSelectedResourceSourceCommand { get; private set; } = null!;

    public ICommand ApplyStyleEditorCommand { get; private set; } = null!;

    public ICommand AddStyleSetterCommand { get; private set; } = null!;

    public ICommand CreateStyleFromSelectedElementCommand { get; private set; } = null!;

    public ICommand BuildStyleSelectorCommand { get; private set; } = null!;

    public ICommand RefreshStylePreviewCommand { get; private set; } = null!;

    public ICommand BuildBindingMarkupCommand { get; private set; } = null!;

    public ICommand ApplyBindingEditorCommand { get; private set; } = null!;

    public ICommand ApplyResourceEditorCommand { get; private set; } = null!;

    public ICommand CreateResourceCommand { get; private set; } = null!;

    private void InitializeDesignInspection()
    {
        RefreshDesignInspectorsCommand = new RelayCommand(RefreshDesignInspection);
        OpenSelectedStyleSourceCommand = new RelayCommand(
            () => OpenDesignNodeSource(SelectedStyleInspectorNode),
            () => SelectedStyleInspectorNode?.CanNavigate == true);
        OpenSelectedBindingSourceCommand = new RelayCommand(
            () => OpenDesignNodeSource(SelectedBindingInspectorNode),
            () => SelectedBindingInspectorNode?.CanNavigate == true);
        OpenSelectedResourceSourceCommand = new RelayCommand(
            () => OpenDesignNodeSource(SelectedResourceInspectorNode),
            () => SelectedResourceInspectorNode?.CanNavigate == true);
        ApplyStyleEditorCommand = new RelayCommand(ApplyStyleEditor, CanApplyStyleEditor);
        AddStyleSetterCommand = new RelayCommand(ApplyStyleEditor, CanApplyStyleEditor);
        CreateStyleFromSelectedElementCommand = new RelayCommand(CreateStyleFromSelectedElement, CanCreateStyleFromSelectedElement);
        BuildStyleSelectorCommand = new RelayCommand(BuildStyleSelectorFromParts);
        RefreshStylePreviewCommand = new RelayCommand(RefreshStylePreview);
        BuildBindingMarkupCommand = new RelayCommand(BuildBindingMarkupFromFields);
        ApplyBindingEditorCommand = new RelayCommand(ApplyBindingEditor, CanApplyBindingEditor);
        ApplyResourceEditorCommand = new RelayCommand(ApplyResourceEditor, CanApplyResourceEditor);
        CreateResourceCommand = new RelayCommand(CreateResource, CanCreateResource);
    }

    partial void OnSelectedStyleInspectorNodeChanged(DesignInspectorNodeViewModel? value)
    {
        if (_isRefreshingDesignInspection)
        {
            return;
        }

        SelectStyleForEditing(value);
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnSelectedBindingInspectorNodeChanged(DesignInspectorNodeViewModel? value)
    {
        if (_isRefreshingDesignInspection)
        {
            return;
        }

        SelectBindingForEditing(value?.Binding);
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnSelectedResourceInspectorNodeChanged(DesignInspectorNodeViewModel? value)
    {
        if (_isRefreshingDesignInspection)
        {
            return;
        }

        SelectResourceForEditing(value?.Resource);
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnSelectedStyleEditorSetterChanged(StyleSetterEditorViewModel? value)
    {
        ObserveStyleEditorSetter(value);
        if (value is null || _isUpdatingStyleEditor)
        {
            return;
        }

        SyncStyleEditorFieldsFromSetter(value);
        RefreshStyleEditorPropertyOptions();
    }

    private void ObserveStyleEditorSetter(StyleSetterEditorViewModel? setter)
    {
        if (ReferenceEquals(_observedStyleEditorSetter, setter))
        {
            return;
        }

        if (_observedStyleEditorSetter is not null)
        {
            _observedStyleEditorSetter.PropertyChanged -= StyleEditorSetterOnPropertyChanged;
        }

        _observedStyleEditorSetter = setter;
        if (_observedStyleEditorSetter is not null)
        {
            _observedStyleEditorSetter.PropertyChanged += StyleEditorSetterOnPropertyChanged;
        }
    }

    private void StyleEditorSetterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingStyleEditor ||
            _isSyncingStyleEditorSetter ||
            !ReferenceEquals(sender, SelectedStyleEditorSetter) ||
            e.PropertyName is not (nameof(StyleSetterEditorViewModel.PropertyName) or nameof(StyleSetterEditorViewModel.Value)) ||
            sender is not StyleSetterEditorViewModel setter)
        {
            return;
        }

        SyncStyleEditorFieldsFromSetter(setter);
        RefreshStyleEditorPropertyOptions();
        RefreshStylePreview();
        NotifyDesignInspectionCommandsChanged();
    }

    private void SyncStyleEditorFieldsFromSetter(StyleSetterEditorViewModel setter)
    {
        _isSyncingStyleEditorSetter = true;
        try
        {
            StyleEditorPropertyName = setter.PropertyName;
            StyleEditorValue = setter.Value;
        }
        finally
        {
            _isSyncingStyleEditorSetter = false;
        }
    }

    private void SyncSelectedStyleEditorSetterFromFields()
    {
        if (_isUpdatingStyleEditor ||
            _isSyncingStyleEditorSetter ||
            SelectedStyleEditorSetter is not { } setter)
        {
            return;
        }

        _isSyncingStyleEditorSetter = true;
        try
        {
            setter.PropertyName = StyleEditorPropertyName;
            setter.Value = StyleEditorValue;
        }
        finally
        {
            _isSyncingStyleEditorSetter = false;
        }
    }

    partial void OnSelectedStyleEditorAvailablePropertyChanged(VisualEditorAvailablePropertyViewModel? value)
    {
        if (value is null || _isUpdatingStyleEditor)
        {
            return;
        }

        StyleEditorPropertyName = value.Name;
        RefreshStyleEditorPropertyOptions(value.Property);
    }

    partial void OnStyleEditorPropertyNameChanged(string value)
    {
        SyncSelectedStyleEditorSetterFromFields();
        RefreshStyleEditorPropertyOptions();
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnStyleEditorValueChanged(string value)
    {
        SyncSelectedStyleEditorSetterFromFields();
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnStyleEditorSelectorChanged(string value)
    {
        if (!_isUpdatingStyleEditor)
        {
            StyleEditorTargetType = XamlDesignInspector.InferStyleTargetType(value);
            RefreshStyleSelectorTokens();
            RefreshStyleEditorAvailableProperties();
            RefreshStylePreview();
        }

        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnSelectedStyleEditorPropertyOptionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            StyleEditorValue = value;
        }
    }

    partial void OnBindingEditorRawValueChanged(string value)
    {
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnResourceEditorRawXamlChanged(string value)
    {
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnResourceEditorKeyChanged(string value)
    {
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnResourceEditorTypeChanged(string value)
    {
        NotifyDesignInspectionCommandsChanged();
    }

    partial void OnResourceEditorValueChanged(string value)
    {
        NotifyDesignInspectionCommandsChanged();
    }

    private void RefreshDesignInspection()
    {
        var selectedStyleKey = CreateNodeSelectionKey(SelectedStyleInspectorNode);
        var selectedBindingKey = CreateNodeSelectionKey(SelectedBindingInspectorNode);
        var selectedResourceKey = CreateNodeSelectionKey(SelectedResourceInspectorNode);

        _designInspection = ActiveProject is null
            ? XamlDesignInspection.Empty
            : _designInspector.Analyze(
                ActiveProject.GetXamlFiles()
                    .Select(static file => new XamlDesignDocument(
                        file.Path,
                        file.Text,
                        file.Kind == ProjectFileKind.Resource)));

        var styleRoots = BuildStyleInspectorNodes(_designInspection.Styles);
        var bindingRoots = BuildBindingInspectorNodes(_designInspection.Bindings);
        var resourceRoots = BuildResourceInspectorNodes(_designInspection.Resources);

        _isRefreshingDesignInspection = true;
        try
        {
            StyleInspectorNodes = new ObservableCollection<DesignInspectorNodeViewModel>(styleRoots);
            StyleInspectorModel = CreateDesignInspectorModel(StyleInspectorNodes);
            BindingInspectorNodes = new ObservableCollection<DesignInspectorNodeViewModel>(bindingRoots);
            BindingInspectorModel = CreateDesignInspectorModel(BindingInspectorNodes);
            ResourceInspectorNodes = new ObservableCollection<DesignInspectorNodeViewModel>(resourceRoots);
            ResourceInspectorModel = CreateDesignInspectorModel(ResourceInspectorNodes);

            SelectedStyleInspectorNode =
                FindDesignNode(StyleInspectorNodes, selectedStyleKey) ??
                FindFirstLeaf(StyleInspectorNodes, static node => node.Style is not null);
            SelectedBindingInspectorNode =
                FindDesignNode(BindingInspectorNodes, selectedBindingKey) ??
                FindFirstLeaf(BindingInspectorNodes, static node => node.Binding is not null);
            SelectedResourceInspectorNode =
                FindDesignNode(ResourceInspectorNodes, selectedResourceKey) ??
                FindFirstLeaf(ResourceInspectorNodes, static node => node.Resource is not null);
        }
        finally
        {
            _isRefreshingDesignInspection = false;
        }

        SelectStyleForEditing(SelectedStyleInspectorNode);
        SelectBindingForEditing(SelectedBindingInspectorNode?.Binding);
        SelectResourceForEditing(SelectedResourceInspectorNode?.Resource);

        DesignInspectionStatus =
            $"{_designInspection.Styles.Count} style(s), " +
            $"{_designInspection.Bindings.Count} binding(s), " +
            $"{_designInspection.Resources.Count} resource(s).";
        if (_designInspection.Diagnostics.Count > 0)
        {
            DesignInspectionStatus += $" {_designInspection.Diagnostics.Count} XAML file(s) have parse diagnostics.";
        }

        NotifyDesignInspectionCommandsChanged();
    }

    private static IReadOnlyList<DesignInspectorNodeViewModel> BuildStyleInspectorNodes(
        IEnumerable<XamlStyleDefinition> styles)
    {
        return styles
            .GroupBy(static style => style.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var fileNode = new DesignInspectorNodeViewModel("File", group.Key, $"{group.Count()} style(s)", group.Key);
                foreach (var style in group.OrderBy(static style => style.Line))
                {
                    var styleNode = new DesignInspectorNodeViewModel(
                        "Style",
                        string.IsNullOrWhiteSpace(style.Selector) ? "(empty selector)" : style.Selector,
                        $"{style.Setters.Count} setter(s)",
                        style.FilePath,
                        style.Line,
                        style.Start,
                        style.Length,
                        style: style);
                    foreach (var setter in style.Setters)
                    {
                        styleNode.Children.Add(new DesignInspectorNodeViewModel(
                            "Setter",
                            setter.Property,
                            setter.Value,
                            setter.FilePath,
                            setter.Line,
                            setter.Start,
                            setter.Length,
                            style: style,
                            styleSetter: setter));
                    }

                    fileNode.Children.Add(styleNode);
                }

                return fileNode;
            })
            .ToArray();
    }

    private static IReadOnlyList<DesignInspectorNodeViewModel> BuildBindingInspectorNodes(
        IEnumerable<XamlBindingDefinition> bindings)
    {
        return bindings
            .GroupBy(static binding => binding.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var fileNode = new DesignInspectorNodeViewModel("File", group.Key, $"{group.Count()} binding(s)", group.Key);
                foreach (var binding in group.OrderBy(static binding => binding.Line))
                {
                    var title = string.IsNullOrWhiteSpace(binding.Path)
                        ? $"{binding.OwnerType}.{binding.PropertyName}"
                        : $"{binding.OwnerType}.{binding.PropertyName} -> {binding.Path}";
                    var bindingNode = new DesignInspectorNodeViewModel(
                        binding.Kind,
                        title,
                        binding.RawValue,
                        binding.FilePath,
                        binding.Line,
                        binding.Start,
                        binding.Length,
                        binding: binding);
                    foreach (var field in CreateBindingFields(binding))
                    {
                        bindingNode.Children.Add(new DesignInspectorNodeViewModel(
                            "Field",
                            field.Title,
                            string.Empty,
                            binding.FilePath,
                            binding.Line,
                            binding.Start,
                            binding.Length,
                            binding: binding));
                    }

                    fileNode.Children.Add(bindingNode);
                }

                return fileNode;
            })
            .ToArray();
    }

    private static IReadOnlyList<DesignInspectorNodeViewModel> BuildResourceInspectorNodes(
        IEnumerable<XamlResourceDefinition> resources)
    {
        return resources
            .GroupBy(static resource => resource.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var fileNode = new DesignInspectorNodeViewModel("File", group.Key, $"{group.Count()} resource(s)", group.Key);
                foreach (var resource in group.OrderBy(static resource => resource.Line))
                {
                    fileNode.Children.Add(new DesignInspectorNodeViewModel(
                        resource.ResourceType,
                        resource.Key,
                        string.IsNullOrWhiteSpace(resource.ValuePreview)
                            ? resource.TargetType
                            : resource.ValuePreview,
                        resource.FilePath,
                        resource.Line,
                        resource.Start,
                        resource.Length,
                        resource: resource));
                }

                return fileNode;
            })
            .ToArray();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Inspector trees use ProDataGrid hierarchical models for design-time metadata.")]
    private static HierarchicalModel<DesignInspectorNodeViewModel> CreateDesignInspectorModel(
        ObservableCollection<DesignInspectorNodeViewModel> roots)
    {
        var model = new HierarchicalModel<DesignInspectorNodeViewModel>(
            new HierarchicalOptions<DesignInspectorNodeViewModel>
            {
                ChildrenSelector = static node => node.Children,
                IsLeafSelector = static node => node.Children.Count == 0,
                AutoExpandRoot = true,
                MaxAutoExpandDepth = 2,
                VirtualizeChildren = false,
                ItemPathSelector = static node => new[] { node.FilePath.GetHashCode(StringComparison.OrdinalIgnoreCase), node.Start }
            });

        model.SetRoots(roots);
        return model;
    }

    private void SelectStyleForEditing(DesignInspectorNodeViewModel? node)
    {
        var style = node?.Style;
        _selectedStyleDefinition = style;
        if (style is null)
        {
            StyleEditorSetters = new ObservableCollection<StyleSetterEditorViewModel>();
            SelectedStyleEditorSetter = null;
            StyleEditorStatus = "Select a style or create one from the selected element.";
            RefreshStyleEditorAvailableProperties();
            RefreshStylePreview();
            return;
        }

        try
        {
            _isUpdatingStyleEditor = true;
            StyleEditorSelector = style.Selector;
            StyleEditorTargetType = style.TargetType;
            StyleSelectorTypeName = style.TargetType;
            StyleEditorSetters = new ObservableCollection<StyleSetterEditorViewModel>(
                style.Setters.Select(static setter => new StyleSetterEditorViewModel(
                    setter.Property,
                    setter.Value,
                    setter.IsComplex,
                    setter.Line,
                    setter.Start,
                    setter.Length)));
            SelectedStyleEditorSetter = node?.StyleSetter is { } selectedSetter
                ? StyleEditorSetters.FirstOrDefault(setter =>
                    string.Equals(setter.PropertyName, selectedSetter.Property, StringComparison.Ordinal))
                : StyleEditorSetters.FirstOrDefault();
            if (SelectedStyleEditorSetter is { } setter)
            {
                StyleEditorPropertyName = setter.PropertyName;
                StyleEditorValue = setter.Value;
            }
            else
            {
                StyleEditorPropertyName = string.Empty;
                StyleEditorValue = string.Empty;
            }
        }
        finally
        {
            _isUpdatingStyleEditor = false;
        }

        RefreshStyleSelectorTokens();
        RefreshStyleEditorAvailableProperties();
        RefreshStyleEditorPropertyOptions();
        RefreshStylePreview();
        StyleEditorStatus = $"Editing {style.Selector} in {style.FilePath}:{style.Line}.";
    }

    private void SelectBindingForEditing(XamlBindingDefinition? binding)
    {
        _selectedBindingDefinition = binding;
        if (binding is null)
        {
            BindingEditorFields = new ObservableCollection<BindingInspectorFieldViewModel>();
            BindingEditorStatus = "Select a binding.";
            return;
        }

        BindingEditorKind = binding.Kind;
        BindingEditorPropertyName = binding.PropertyName;
        BindingEditorPath = binding.Path;
        BindingEditorMode = binding.Mode;
        BindingEditorSource = binding.Source;
        BindingEditorElementName = binding.ElementName;
        BindingEditorRelativeSource = binding.RelativeSource;
        BindingEditorConverter = binding.Converter;
        BindingEditorStringFormat = binding.StringFormat;
        BindingEditorFallbackValue = binding.FallbackValue;
        BindingEditorTargetNullValue = binding.TargetNullValue;
        BindingEditorRawValue = binding.RawValue;
        BindingEditorFields = new ObservableCollection<BindingInspectorFieldViewModel>(CreateBindingFields(binding));

        BindingEditorStatus = $"Editing {binding.Kind} on {binding.OwnerType}.{binding.PropertyName} in {binding.FilePath}:{binding.Line}.";
    }

    private void SelectResourceForEditing(XamlResourceDefinition? resource)
    {
        _selectedResourceDefinition = resource;
        if (resource is null)
        {
            ResourceEditorSummary = new ObservableCollection<ResourceEditorSummaryViewModel>();
            ResourceEditorStatus = "Select or create a resource.";
            return;
        }

        ResourceEditorKey = resource.Key;
        ResourceEditorType = GetLocalName(resource.ResourceType);
        ResourceEditorValue = resource.ValuePreview;
        ResourceEditorRawXaml = resource.RawXaml;
        ResourceEditorSummary = new ObservableCollection<ResourceEditorSummaryViewModel>(
            new[]
            {
                new ResourceEditorSummaryViewModel("Type", resource.ResourceType),
                new ResourceEditorSummaryViewModel("Target", resource.TargetType),
                new ResourceEditorSummaryViewModel("Location", $"{resource.FilePath}:{resource.Line}")
            });

        ResourceEditorStatus = $"Editing {resource.Key} in {resource.FilePath}:{resource.Line}.";
    }

    private static IEnumerable<BindingInspectorFieldViewModel> CreateBindingFields(XamlBindingDefinition binding)
    {
        yield return new BindingInspectorFieldViewModel("Kind", binding.Kind);
        yield return new BindingInspectorFieldViewModel("Path", binding.Path);
        yield return new BindingInspectorFieldViewModel("Mode", binding.Mode);
        yield return new BindingInspectorFieldViewModel("Source", binding.Source);
        yield return new BindingInspectorFieldViewModel("ElementName", binding.ElementName);
        yield return new BindingInspectorFieldViewModel("RelativeSource", binding.RelativeSource);
        yield return new BindingInspectorFieldViewModel("Converter", binding.Converter);
        yield return new BindingInspectorFieldViewModel("StringFormat", binding.StringFormat);
        yield return new BindingInspectorFieldViewModel("FallbackValue", binding.FallbackValue);
        yield return new BindingInspectorFieldViewModel("TargetNullValue", binding.TargetNullValue);
    }

    private void RefreshStyleSelectorTokens()
    {
        var selector = StyleEditorSelector.Trim();
        StyleSelectorTokens = new ObservableCollection<StyleSelectorTokenViewModel>(
            selector
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => new StyleSelectorTokenViewModel(ResolveSelectorTokenKind(part), part)));
    }

    private static string ResolveSelectorTokenKind(string token)
    {
        if (string.Equals(token, "/template/", StringComparison.Ordinal))
        {
            return "Template";
        }

        if (token.StartsWith(':'))
        {
            return "State";
        }

        if (token.Contains('#', StringComparison.Ordinal))
        {
            return "Name";
        }

        if (token.Contains('.', StringComparison.Ordinal))
        {
            return "Class";
        }

        if (token is ">" or "+" or "~")
        {
            return "Combinator";
        }

        return "Type";
    }

    private void RefreshStyleEditorAvailableProperties()
    {
        var type = ResolveControlType(StyleEditorTargetType);
        var descriptor = _visualEditorRegistry.Resolve(type ?? typeof(Control));
        StyleEditorAvailableProperties = new ObservableCollection<VisualEditorAvailablePropertyViewModel>(
            descriptor.Properties.Select(static property => new VisualEditorAvailablePropertyViewModel(property)));
        SelectedStyleEditorAvailableProperty =
            StyleEditorAvailableProperties.FirstOrDefault(property =>
                string.Equals(property.Name, StyleEditorPropertyName, StringComparison.Ordinal));
        RefreshStyleEditorPropertyOptions();
    }

    private void RefreshStyleEditorPropertyOptions()
    {
        var property = StyleEditorAvailableProperties.FirstOrDefault(available =>
            string.Equals(available.Name, StyleEditorPropertyName, StringComparison.Ordinal))?.Property;
        RefreshStyleEditorPropertyOptions(property);
    }

    private void RefreshStyleEditorPropertyOptions(ControlEditorProperty? property)
    {
        var options = property is null
            ? Array.Empty<string>()
            : GetSuggestedValues(property)
                .Concat(GetCompatibleResourceReferenceValues(property))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        StyleEditorPropertyOptions = new ObservableCollection<string>(options);
        SelectedStyleEditorPropertyOption = options.FirstOrDefault(option =>
            string.Equals(option, StyleEditorValue, StringComparison.Ordinal));
    }

    private void RefreshStylePreview()
    {
        var setters = StyleEditorSetters
            .Select(static setter => (PropertyName: setter.PropertyName, Value: setter.Value))
            .Concat(string.IsNullOrWhiteSpace(StyleEditorPropertyName)
                ? Array.Empty<(string PropertyName, string Value)>()
                : new[] { (PropertyName: StyleEditorPropertyName, Value: StyleEditorValue) })
            .GroupBy(static setter => setter.PropertyName, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToArray();
        StyleEditorPreviewXaml = _designEditor.CreateStylePreviewXaml(StyleEditorSelector, setters);

        var diagnostics = new List<RuntimeXamlDiagnostic>();
        try
        {
            StyleEditorPreviewControl = RuntimeXamlPreviewLoader.LoadControl(
                StyleEditorPreviewXaml,
                _previous?.Assembly,
                fallbackRootTypeName: null,
                documentName: "StylePreview.axaml",
                diagnostics);
            if (diagnostics.Count > 0)
            {
                StyleEditorStatus = diagnostics[0].Title;
            }
        }
        catch (Exception exception)
        {
            StyleEditorPreviewControl = new TextBlock { Text = exception.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
    }

    private bool CanApplyStyleEditor()
    {
        return ActiveXamlFile is { IsXaml: true } &&
               !string.IsNullOrWhiteSpace(StyleEditorSelector) &&
               (_selectedStyleDefinition is not null || !string.IsNullOrWhiteSpace(StyleEditorPropertyName));
    }

    private void ApplyStyleEditor()
    {
        if (ActiveProject is not { } project)
        {
            return;
        }

        var targetFile = _selectedStyleDefinition is { } selectedStyle
            ? project.FindFile(selectedStyle.FilePath)
            : ActiveXamlFile;
        if (targetFile is null)
        {
            StyleEditorStatus = "No style target file is available.";
            return;
        }

        XamlDesignEditResult edit;
        if (_selectedStyleDefinition is { } style)
        {
            edit = string.IsNullOrWhiteSpace(StyleEditorPropertyName)
                ? _designEditor.SetStyleSelector(targetFile.Text, style, StyleEditorSelector)
                : _designEditor.SetStyleSetter(
                    targetFile.Text,
                    style,
                    StyleEditorSelector,
                    StyleEditorPropertyName,
                    StyleEditorValue,
                    CreateSelectedStyleSetterDefinition(style));
        }
        else
        {
            edit = _designEditor.AddStyleToDocument(
                targetFile.Text,
                StyleEditorSelector,
                new[] { (StyleEditorPropertyName, StyleEditorValue) });
        }

        ApplyDesignFileEdit(targetFile, edit, $"Updated style {StyleEditorSelector}.");
        SelectStyleBySelector(StyleEditorSelector);
    }

    private XamlStyleSetterDefinition? CreateSelectedStyleSetterDefinition(XamlStyleDefinition style)
    {
        if (SelectedStyleEditorSetter is not { } setter)
        {
            return null;
        }

        return new XamlStyleSetterDefinition(
            setter.OriginalPropertyName,
            setter.Value,
            setter.IsComplex,
            style.FilePath,
            setter.Line ?? 0,
            setter.Start,
            setter.Length);
    }

    private bool CanCreateStyleFromSelectedElement()
    {
        return ActiveXamlFile is { IsXaml: true } &&
               SelectedVisualEditorNode?.Element is not null;
    }

    private void CreateStyleFromSelectedElement()
    {
        if (ActiveXamlFile is not { } xamlFile ||
            !TryGetSelectedVisualEditorElement(out var element))
        {
            return;
        }

        var targetType = GetLocalName(element.TypeName);
        if (string.IsNullOrWhiteSpace(targetType))
        {
            StyleEditorStatus = "The selected element type cannot be styled.";
            return;
        }

        var className = CreateUniqueStyleClassName(targetType);
        var selector = $"{targetType}.{className}";
        var setters = element.Attributes
            .Where(static attribute => IsStyleableElementAttribute(attribute.Key))
            .Select(static attribute => (PropertyName: attribute.Key, Value: attribute.Value))
            .ToArray();
        if (setters.Length == 0)
        {
            setters = new[] { ("Opacity", "1") };
        }

        var requests = setters
            .Select(setter => new XamlMutationRequest(
                XamlMutationKind.RemoveProperty,
                element.Selector,
                PropertyName: setter.PropertyName))
            .Concat(new[]
            {
                new XamlMutationRequest(
                    XamlMutationKind.SetProperty,
                    element.Selector,
                    PropertyName: "Classes",
                    Value: AppendClass(element.Attributes.TryGetValue("Classes", out var existingClasses) ? existingClasses : string.Empty, className))
            })
            .ToArray();

        var visualEdit = _visualMutationEngine.Batch(xamlFile.Text, requests);
        if (!visualEdit.Success)
        {
            StyleEditorStatus = string.Join(Environment.NewLine, visualEdit.Diagnostics);
            return;
        }

        var styleEdit = _designEditor.AddStyleToDocument(visualEdit.Text, selector, setters);
        if (!styleEdit.Changed)
        {
            StyleEditorStatus = styleEdit.Error ?? "Could not create style.";
            return;
        }

        ApplyVisualEditorMutation(new XamlMutationResult(
            styleEdit.Text,
            _visualMutationEngine.Analyze(styleEdit.Text),
            Array.Empty<string>()));
        RefreshDesignInspection();
        SelectStyleBySelector(selector);
        StyleEditorStatus = $"Created {selector} from {FormatVisualEditorElementTitle(element)}.";
    }

    private static bool IsStyleableElementAttribute(string name)
    {
        if (name.StartsWith("x:", StringComparison.Ordinal) ||
            name.StartsWith("xmlns", StringComparison.Ordinal) ||
            name.Contains('.', StringComparison.Ordinal) ||
            name is "Name" or "Classes" or "Content" or "Text" or "Header" or "Items" or "ItemsSource" or "DataContext")
        {
            return false;
        }

        return true;
    }

    private string CreateUniqueStyleClassName(string targetType)
    {
        var baseName = $"{char.ToLowerInvariant(targetType[0])}{targetType[1..]}Style";
        var className = baseName;
        var index = 2;
        while (_designInspection.Styles.Any(style => style.Selector.Contains("." + className, StringComparison.Ordinal)) ||
               _visualEditorDocument?.Elements.Any(element =>
                   element.Attributes.TryGetValue("Classes", out var classes) &&
                   classes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Contains(className, StringComparer.Ordinal)) == true)
        {
            className = $"{baseName}{index}";
            index++;
        }

        return className;
    }

    private static string AppendClass(string existingClasses, string className)
    {
        var classes = existingClasses
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!classes.Contains(className, StringComparer.Ordinal))
        {
            classes.Add(className);
        }

        return string.Join(" ", classes);
    }

    private void BuildStyleSelectorFromParts()
    {
        var typeName = string.IsNullOrWhiteSpace(StyleSelectorTypeName)
            ? StyleEditorTargetType
            : StyleSelectorTypeName;
        if (string.IsNullOrWhiteSpace(typeName) &&
            SelectedVisualEditorNode?.Element is { } element)
        {
            typeName = GetLocalName(element.TypeName);
        }

        var selector = string.IsNullOrWhiteSpace(typeName) ? "*" : typeName.Trim();
        if (!string.IsNullOrWhiteSpace(StyleSelectorClass))
        {
            selector += "." + StyleSelectorClass.Trim().TrimStart('.');
        }

        if (!string.IsNullOrWhiteSpace(StyleSelectorName))
        {
            selector += "#" + StyleSelectorName.Trim().TrimStart('#');
        }

        if (!string.IsNullOrWhiteSpace(StyleSelectorPseudoClass))
        {
            selector += ":" + StyleSelectorPseudoClass.Trim().TrimStart(':');
        }

        if (!string.IsNullOrWhiteSpace(StyleSelectorTemplatePart))
        {
            selector += " /template/ " + StyleSelectorTemplatePart.Trim();
        }

        StyleEditorSelector = selector;
    }

    private void SelectStyleBySelector(string selector)
    {
        SelectedStyleInspectorNode = FindFirstLeaf(StyleInspectorNodes, node =>
            node.Style is { } style &&
            string.Equals(style.Selector, selector, StringComparison.Ordinal));
    }

    private void BuildBindingMarkupFromFields()
    {
        BindingEditorRawValue = _selectedBindingDefinition?.LocationKind == XamlBindingLocationKind.ObjectElement
            ? XamlDesignEditor.BuildBindingObjectElement(
                BindingEditorKind,
                BindingEditorPath,
                BindingEditorMode,
                BindingEditorSource,
                BindingEditorElementName,
                BindingEditorRelativeSource,
                BindingEditorConverter,
                BindingEditorStringFormat,
                BindingEditorFallbackValue,
                BindingEditorTargetNullValue)
            : XamlDesignEditor.BuildBindingMarkup(
                BindingEditorKind,
                BindingEditorPath,
                BindingEditorMode,
                BindingEditorSource,
                BindingEditorElementName,
                BindingEditorRelativeSource,
                BindingEditorConverter,
                BindingEditorStringFormat,
                BindingEditorFallbackValue,
                BindingEditorTargetNullValue);
    }

    private bool CanApplyBindingEditor()
    {
        return _selectedBindingDefinition is not null &&
               !string.IsNullOrWhiteSpace(BindingEditorRawValue);
    }

    private void ApplyBindingEditor()
    {
        if (ActiveProject is not { } project ||
            _selectedBindingDefinition is not { } binding ||
            project.FindFile(binding.FilePath) is not { } file)
        {
            return;
        }

        var rawValue = binding.LocationKind == XamlBindingLocationKind.ObjectElement &&
                       BindingEditorRawValue.TrimStart().StartsWith('{')
            ? XamlDesignEditor.BuildBindingObjectElement(
                BindingEditorKind,
                BindingEditorPath,
                BindingEditorMode,
                BindingEditorSource,
                BindingEditorElementName,
                BindingEditorRelativeSource,
                BindingEditorConverter,
                BindingEditorStringFormat,
                BindingEditorFallbackValue,
                BindingEditorTargetNullValue)
            : BindingEditorRawValue;
        var edit = _designEditor.ReplaceBinding(file.Text, binding, rawValue);
        ApplyDesignFileEdit(file, edit, $"Updated binding on {binding.PropertyName}.");
        SelectBindingBySource(binding.FilePath, binding.Start);
    }

    private bool CanApplyResourceEditor()
    {
        return _selectedResourceDefinition is not null &&
               !string.IsNullOrWhiteSpace(ResourceEditorRawXaml);
    }

    private void ApplyResourceEditor()
    {
        if (ActiveProject is not { } project ||
            _selectedResourceDefinition is not { } resource ||
            project.FindFile(resource.FilePath) is not { } file)
        {
            return;
        }

        var edit = _designEditor.ReplaceResource(file.Text, resource, ResourceEditorRawXaml);
        ApplyDesignFileEdit(file, edit, $"Updated resource {resource.Key}.");
        SelectResourceByKey(ResourceEditorKey);
    }

    private bool CanCreateResource()
    {
        return ActiveXamlFile is { IsXaml: true } &&
               (!string.IsNullOrWhiteSpace(ResourceEditorRawXaml) ||
                !string.IsNullOrWhiteSpace(ResourceEditorKey));
    }

    private void CreateResource()
    {
        if (ActiveXamlFile is not { } file)
        {
            return;
        }

        var selectedRawUnchanged = _selectedResourceDefinition is { } selected &&
                                   string.Equals(ResourceEditorRawXaml, selected.RawXaml, StringComparison.Ordinal);
        var selectedKeyUnchanged = _selectedResourceDefinition is { } selectedResource &&
                                   string.Equals(ResourceEditorKey, selectedResource.Key, StringComparison.Ordinal);
        var useRawXaml = !string.IsNullOrWhiteSpace(ResourceEditorRawXaml) &&
                         (_selectedResourceDefinition is null || !selectedRawUnchanged);
        var raw = useRawXaml
            ? ResourceEditorRawXaml
            : CreateResourceXamlFromFields(ensureUniqueKey: selectedRawUnchanged && selectedKeyUnchanged);
        var edit = _designEditor.AddResourceToDocument(file.Text, raw);
        ApplyDesignFileEdit(file, edit, "Created resource.");
        var createdKey = ExtractResourceKey(raw);
        if (!string.IsNullOrWhiteSpace(createdKey))
        {
            SelectResourceByKey(createdKey);
        }
    }

    private string CreateResourceXamlFromFields(bool ensureUniqueKey = false)
    {
        var type = string.IsNullOrWhiteSpace(ResourceEditorType) ? "SolidColorBrush" : ResourceEditorType.Trim();
        var key = string.IsNullOrWhiteSpace(ResourceEditorKey) ? CreateUniqueResourceKey(type) : ResourceEditorKey.Trim();
        if (ensureUniqueKey &&
            _designInspection.Resources.Any(resource => string.Equals(resource.Key, key, StringComparison.Ordinal)))
        {
            key = CreateUniqueResourceKeyFromBase(key);
        }

        var value = ResourceEditorValue ?? string.Empty;
        return value.TrimStart().StartsWith('<')
            ? $"<{type} x:Key=\"{EscapeDesignAttribute(key)}\">{Environment.NewLine}{value}{Environment.NewLine}</{type}>"
            : $"<{type} x:Key=\"{EscapeDesignAttribute(key)}\">{EscapeDesignText(value)}</{type}>";
    }

    private string CreateUniqueResourceKey(string type)
    {
        return CreateUniqueResourceKeyFromBase($"My{GetLocalName(type)}");
    }

    private string CreateUniqueResourceKeyFromBase(string baseKey)
    {
        var key = baseKey;
        var index = 2;
        while (_designInspection.Resources.Any(resource => string.Equals(resource.Key, key, StringComparison.Ordinal)))
        {
            key = $"{baseKey}{index}";
            index++;
        }

        return key;
    }

    private static string ExtractResourceKey(string rawXaml)
    {
        var wrapped =
            "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            Environment.NewLine +
            rawXaml +
            Environment.NewLine +
            "</ResourceDictionary>";
        return XamlTextEditor.TryParse(wrapped, out var document, out _) &&
               document.RootSyntax is { } root &&
               XamlTextEditor.DirectChildElements(root).FirstOrDefault() is { } resource
            ? XamlTextEditor.GetAttributeValue(resource, "x:Key") ??
              XamlTextEditor.GetAttributeValueByLocalName(resource, "Key") ??
              string.Empty
            : string.Empty;
    }

    private void ApplyDesignFileEdit(InMemoryProjectFile file, XamlDesignEditResult edit, string successStatus)
    {
        if (!edit.Changed)
        {
            var error = edit.Error ?? "The XAML document was not changed.";
            StyleEditorStatus = error;
            BindingEditorStatus = error;
            ResourceEditorStatus = error;
            LastErrorMessage = error;
            return;
        }

        _isApplyingDesignInspectionEdit = true;
        try
        {
            file.Text = edit.Text;
        }
        finally
        {
            _isApplyingDesignInspectionEdit = false;
        }

        if (ReferenceEquals(file, ActiveXamlFile))
        {
            RefreshVisualEditingModel(updateSourceSelection: false);
        }

        if (file.IsXaml)
        {
            RefreshThemeResourceAnalysis();
            RefreshDesignInspection();
        }

        RefreshControlThemes();
        if (EnableAutoRun && CanPreviewXamlFile(ActiveXamlFile))
        {
            RunActiveDocument();
        }

        StyleEditorStatus = successStatus;
        BindingEditorStatus = successStatus;
        ResourceEditorStatus = successStatus;
    }

    private void OpenDesignNodeSource(DesignInspectorNodeViewModel? node)
    {
        if (ActiveProject is null ||
            node is null ||
            ActiveProject.FindFile(node.FilePath) is not { } file)
        {
            return;
        }

        OpenWorkspaceFile(file);
        SetDesignSourceSelection(file, node.Start, node.Length);
    }

    private void SetDesignSourceSelection(InMemoryProjectFile file, int start, int length)
    {
        VisualEditorSourceSelectionFilePath = file.Path;
        VisualEditorSourceSelectionStart = Math.Max(0, start);
        VisualEditorSourceSelectionLength = Math.Max(0, length);
        VisualEditorSourceSelectionVersion++;
    }

    private void SelectBindingBySource(string filePath, int start)
    {
        SelectedBindingInspectorNode = FindFirstLeaf(BindingInspectorNodes, node =>
            node.Binding is { } binding &&
            string.Equals(binding.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            binding.Start == start);
    }

    private void SelectResourceByKey(string key)
    {
        SelectedResourceInspectorNode = FindFirstLeaf(ResourceInspectorNodes, node =>
            node.Resource is { } resource &&
            string.Equals(resource.Key, key, StringComparison.Ordinal));
    }

    private static string CreateNodeSelectionKey(DesignInspectorNodeViewModel? node)
    {
        return node is null
            ? string.Empty
            : $"{node.Kind}|{node.FilePath}|{node.Start}|{node.Title}";
    }

    private static DesignInspectorNodeViewModel? FindDesignNode(
        IEnumerable<DesignInspectorNodeViewModel> roots,
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var root in roots)
        {
            if (string.Equals(CreateNodeSelectionKey(root), key, StringComparison.Ordinal))
            {
                return root;
            }

            var child = FindDesignNode(root.Children, key);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static DesignInspectorNodeViewModel? FindFirstLeaf(
        IEnumerable<DesignInspectorNodeViewModel> roots,
        Func<DesignInspectorNodeViewModel, bool> predicate)
    {
        foreach (var root in roots)
        {
            if (predicate(root))
            {
                return root;
            }

            var child = FindFirstLeaf(root.Children, predicate);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string EscapeDesignAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EscapeDesignText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private void NotifyDesignInspectionCommandsChanged()
    {
        (OpenSelectedStyleSourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedBindingSourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedResourceSourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyStyleEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (AddStyleSetterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CreateStyleFromSelectedElementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyBindingEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyResourceEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CreateResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
