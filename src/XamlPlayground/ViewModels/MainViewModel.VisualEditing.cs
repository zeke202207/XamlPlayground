using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XamlPlayground.Services;
using XamlPlayground.Services.Theming;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels.VisualEditing;
using XamlPlayground.ViewModels.Workspace;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels;

public partial class MainViewModel
{
    private IXamlMutationEngine _visualMutationEngine = null!;
    private XamlToolboxInsertionService _visualToolboxInsertion = null!;
    private ControlEditorRegistry _visualEditorRegistry = null!;
    private FluentControlThemeCatalog _controlThemeCatalog = null!;
    private IVisualTreeSnapshotService _visualTreeSnapshotService = null!;
    private VisualEditorSelectionService _visualSelectionService = null!;
    private ThemeResourceAnalysis _themeResourceAnalysis = ThemeResourceAnalysis.Empty;
    private XamlDocumentSnapshot? _visualEditorDocument;
    private XamlElementSelector? _visualEditorSelectedSelector;
    private bool _isRefreshingVisualEditor;
    private bool _isSynchronizingVisualEditorSelection;
    private bool _suppressVisualEditorSourceSelectionUpdate;
    private bool _isApplyingVisualEditorMutation;
    private bool _isSynchronizingVisualEditorPropertySelection;
    private bool _isApplyingVisualEditorPropertyGridValue;
    private XamlElementSelector? _visualEditorCurrentContainerSelector;
    private ThemeEditScope? _themeEditScope;
    private ControlThemeAnalysis _selectedControlThemeAnalysis = ControlThemeAnalysis.Empty;

    [ObservableProperty] private ObservableCollection<VisualEditorNodeViewModel> _visualEditorStructureNodes = new();
    [ObservableProperty] private HierarchicalModel<VisualEditorNodeViewModel>? _visualEditorStructureModel;
    [ObservableProperty] private ObservableCollection<VisualEditorStructureRowViewModel> _visualEditorStructureRows = new();
    [ObservableProperty] private VisualEditorNodeViewModel? _selectedVisualEditorNode;
    [ObservableProperty] private VisualEditorStructureRowViewModel? _selectedVisualEditorStructureRow;
    [ObservableProperty] private ObservableCollection<VisualEditorPropertyViewModel> _visualEditorProperties = new();
    [ObservableProperty] private DataGridCollectionView? _visualEditorPropertiesView;
    [ObservableProperty] private VisualEditorPropertyViewModel? _selectedVisualEditorProperty;
    [ObservableProperty] private ObservableCollection<VisualEditorAvailablePropertyViewModel> _visualEditorAvailableProperties = new();
    [ObservableProperty] private DataGridCollectionView? _visualEditorAvailablePropertiesView;
    [ObservableProperty] private VisualEditorAvailablePropertyViewModel? _selectedVisualEditorAvailableProperty;
    [ObservableProperty] private string _visualEditorPropertyFilter = string.Empty;
    [ObservableProperty] private ObservableCollection<ToolboxItemDescriptor> _visualEditorToolboxItems = new();
    [ObservableProperty] private ObservableCollection<ToolboxItemDescriptor> _filteredVisualEditorToolboxItems = new();
    [ObservableProperty] private ToolboxItemDescriptor? _selectedVisualEditorToolboxItem;
    [ObservableProperty] private string _visualEditorToolboxSearch = string.Empty;
    [ObservableProperty] private string _visualEditorPropertyName = string.Empty;
    [ObservableProperty] private string _visualEditorPropertyValue = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _visualEditorPropertyOptions = new();
    [ObservableProperty] private string? _selectedVisualEditorPropertyOption;
    [ObservableProperty] private bool _visualEditorDesignerMode = true;
    [ObservableProperty] private string _visualEditorCurrentContainerTitle = "No container";
    [ObservableProperty] private bool _visualEditorPreviewSelectionVisible;
    [ObservableProperty] private double _visualEditorPreviewSelectionLeft;
    [ObservableProperty] private double _visualEditorPreviewSelectionTop;
    [ObservableProperty] private double _visualEditorPreviewSelectionWidth;
    [ObservableProperty] private double _visualEditorPreviewSelectionHeight;
    [ObservableProperty] private bool _visualEditorPreviewCurrentContainerVisible;
    [ObservableProperty] private double _visualEditorPreviewCurrentContainerLeft;
    [ObservableProperty] private double _visualEditorPreviewCurrentContainerTop;
    [ObservableProperty] private double _visualEditorPreviewCurrentContainerWidth;
    [ObservableProperty] private double _visualEditorPreviewCurrentContainerHeight;
    [ObservableProperty] private bool _visualEditorPreviewDropTargetVisible;
    [ObservableProperty] private double _visualEditorPreviewDropTargetLeft;
    [ObservableProperty] private double _visualEditorPreviewDropTargetTop;
    [ObservableProperty] private double _visualEditorPreviewDropTargetWidth;
    [ObservableProperty] private double _visualEditorPreviewDropTargetHeight;
    [ObservableProperty] private bool _visualEditorPreviewInsertionVisible;
    [ObservableProperty] private double _visualEditorPreviewInsertionLeft;
    [ObservableProperty] private double _visualEditorPreviewInsertionTop;
    [ObservableProperty] private double _visualEditorPreviewInsertionWidth;
    [ObservableProperty] private double _visualEditorPreviewInsertionHeight;
    [ObservableProperty] private bool _visualEditorPreviewDropPlaceholderVisible;
    [ObservableProperty] private double _visualEditorPreviewDropPlaceholderLeft;
    [ObservableProperty] private double _visualEditorPreviewDropPlaceholderTop;
    [ObservableProperty] private double _visualEditorPreviewDropPlaceholderWidth;
    [ObservableProperty] private double _visualEditorPreviewDropPlaceholderHeight;
    [ObservableProperty] private bool _visualEditorPreviewVerticalGuideVisible;
    [ObservableProperty] private double _visualEditorPreviewVerticalGuideLeft;
    [ObservableProperty] private double _visualEditorPreviewVerticalGuideTop;
    [ObservableProperty] private double _visualEditorPreviewVerticalGuideHeight;
    [ObservableProperty] private bool _visualEditorPreviewHorizontalGuideVisible;
    [ObservableProperty] private double _visualEditorPreviewHorizontalGuideLeft;
    [ObservableProperty] private double _visualEditorPreviewHorizontalGuideTop;
    [ObservableProperty] private double _visualEditorPreviewHorizontalGuideWidth;
    [ObservableProperty] private bool _visualEditorPreviewMeasurementVisible;
    [ObservableProperty] private double _visualEditorPreviewMeasurementLeft;
    [ObservableProperty] private double _visualEditorPreviewMeasurementTop;
    [ObservableProperty] private string _visualEditorPreviewMeasurementText = string.Empty;
    [ObservableProperty] private string? _visualEditorSourceSelectionFilePath;
    [ObservableProperty] private int _visualEditorSourceSelectionStart;
    [ObservableProperty] private int _visualEditorSourceSelectionLength;
    [ObservableProperty] private int _visualEditorSourceSelectionVersion;
    [ObservableProperty] private string _visualEditorSelectedElementTitle = "No selection";
    [ObservableProperty] private string _visualEditorStatus = "No XAML document selected.";
    [ObservableProperty] private ObservableCollection<ControlThemeDefinitionViewModel> _controlThemes = new();
    [ObservableProperty] private ObservableCollection<ControlThemeDefinitionViewModel> _filteredControlThemes = new();
    [ObservableProperty] private ControlThemeDefinitionViewModel? _selectedControlTheme;
    [ObservableProperty] private ObservableCollection<FluentControlThemeTemplateViewModel> _fluentControlThemeTemplates = new();
    [ObservableProperty] private ObservableCollection<FluentControlThemeTemplateViewModel> _filteredFluentControlThemeTemplates = new();
    [ObservableProperty] private string _controlThemeSearchText = string.Empty;
    [ObservableProperty] private string _controlThemeSourceStatus = "Fluent theme source not loaded.";
    [ObservableProperty] private string _controlThemeSelectedTargetType = "No control selected.";
    [ObservableProperty] private string _controlThemeStatus = "No custom control themes.";
    [ObservableProperty] private ObservableCollection<ThemeResourceViewModel> _themeResources = new();
    [ObservableProperty] private ObservableCollection<ThemeResourceViewModel> _filteredThemeResources = new();
    [ObservableProperty] private ThemeResourceViewModel? _selectedThemeResource;
    [ObservableProperty] private ObservableCollection<ThemeResourceUsageViewModel> _selectedThemeResourceUsages = new();
    [ObservableProperty] private ObservableCollection<ThemeResourceDiagnosticViewModel> _themeResourceDiagnostics = new();
    [ObservableProperty] private ObservableCollection<ThemeResourceDiagnosticViewModel> _filteredThemeResourceDiagnostics = new();
    [ObservableProperty] private string _themeResourceKeyEditText = string.Empty;
    [ObservableProperty] private bool _isThemeEditScopeActive;
    [ObservableProperty] private string _themeEditScopeBreadcrumb = string.Empty;
    [ObservableProperty] private ObservableCollection<ThemePreviewStateViewModel> _themePreviewStates = new();
    [ObservableProperty] private ThemePreviewStateViewModel? _selectedThemePreviewState;
    [ObservableProperty] private ObservableCollection<ThemeStateSelectorViewModel> _themeStateSelectors = new();
    [ObservableProperty] private ObservableCollection<ThemeTemplatePartViewModel> _themeTemplateParts = new();
    [ObservableProperty] private ObservableCollection<ThemeTemplateBindingViewModel> _themeTemplateBindings = new();

    public bool VisualEditorPreviewContentHitTestVisible => !VisualEditorDesignerMode;

    public double VisualEditorPreviewThumbSize => 8;

    public double VisualEditorPreviewNorthWestThumbLeft => VisualEditorPreviewSelectionLeft - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewNorthWestThumbTop => VisualEditorPreviewSelectionTop - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewNorthThumbLeft => VisualEditorPreviewSelectionLeft + VisualEditorPreviewSelectionWidth / 2 - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewNorthThumbTop => VisualEditorPreviewSelectionTop - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewNorthEastThumbLeft => VisualEditorPreviewSelectionLeft + VisualEditorPreviewSelectionWidth - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewNorthEastThumbTop => VisualEditorPreviewSelectionTop - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewWestThumbLeft => VisualEditorPreviewSelectionLeft - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewWestThumbTop => VisualEditorPreviewSelectionTop + VisualEditorPreviewSelectionHeight / 2 - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewEastThumbLeft => VisualEditorPreviewSelectionLeft + VisualEditorPreviewSelectionWidth - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewEastThumbTop => VisualEditorPreviewSelectionTop + VisualEditorPreviewSelectionHeight / 2 - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthWestThumbLeft => VisualEditorPreviewSelectionLeft - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthWestThumbTop => VisualEditorPreviewSelectionTop + VisualEditorPreviewSelectionHeight - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthThumbLeft => VisualEditorPreviewSelectionLeft + VisualEditorPreviewSelectionWidth / 2 - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthThumbTop => VisualEditorPreviewSelectionTop + VisualEditorPreviewSelectionHeight - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthEastThumbLeft => VisualEditorPreviewSelectionLeft + VisualEditorPreviewSelectionWidth - VisualEditorPreviewThumbSize / 2;

    public double VisualEditorPreviewSouthEastThumbTop => VisualEditorPreviewSelectionTop + VisualEditorPreviewSelectionHeight - VisualEditorPreviewThumbSize / 2;

    public ICommand RefreshVisualEditorCommand { get; private set; } = null!;

    public ICommand ApplyVisualEditorPropertyCommand { get; private set; } = null!;

    public ICommand RemoveVisualEditorPropertyCommand { get; private set; } = null!;

    public ICommand ResetVisualEditorPropertyCommand { get; private set; } = null!;

    public ICommand DeleteVisualEditorElementCommand { get; private set; } = null!;

    public ICommand DuplicateVisualEditorElementCommand { get; private set; } = null!;

    public ICommand MoveVisualEditorElementUpCommand { get; private set; } = null!;

    public ICommand MoveVisualEditorElementDownCommand { get; private set; } = null!;

    public ICommand InsertSelectedToolboxItemCommand { get; private set; } = null!;

    public ICommand WrapVisualEditorSelectionCommand { get; private set; } = null!;

    public ICommand UnwrapVisualEditorSelectionCommand { get; private set; } = null!;

    public ICommand CreateCustomControlThemeCommand { get; private set; } = null!;

    public ICommand ApplyControlThemeCommand { get; private set; } = null!;

    public ICommand RemoveControlThemeCommand { get; private set; } = null!;

    public ICommand OpenSelectedControlThemeCommand { get; private set; } = null!;

    public ICommand OpenSelectedThemeResourceCommand { get; private set; } = null!;

    public ICommand RenameSelectedThemeResourceCommand { get; private set; } = null!;

    public ICommand DuplicateSelectedThemeResourceCommand { get; private set; } = null!;

    public ICommand DeleteSelectedThemeResourceCommand { get; private set; } = null!;

    public ICommand ApplySelectedThemeResourceCommand { get; private set; } = null!;

    public ICommand ReturnFromThemeEditScopeCommand { get; private set; } = null!;

    public ICommand ImportControlThemeFilesCommand { get; private set; } = null!;

    public ICommand ExportSelectedControlThemeCommand { get; private set; } = null!;

    public ICommand SaveControlThemeProjectCommand { get; private set; } = null!;

    public ICommand LoadControlThemeProjectCommand { get; private set; } = null!;

    private void InitializeVisualEditing()
    {
        _visualMutationEngine = new XamlMutationEngine();
        _visualToolboxInsertion = new XamlToolboxInsertionService(_visualMutationEngine);
        _visualEditorRegistry = new ControlEditorRegistry();
        _controlThemeCatalog = new FluentControlThemeCatalog();
        _visualTreeSnapshotService = new AvaloniaVisualTreeSnapshotService();
        _visualSelectionService = new VisualEditorSelectionService(_visualMutationEngine, new XamlVisualTreeMapper());

        RefreshVisualEditorCommand = new RelayCommand(() => RefreshVisualEditingModel());
        ApplyVisualEditorPropertyCommand = new RelayCommand(ApplyVisualEditorProperty);
        RemoveVisualEditorPropertyCommand = new RelayCommand(RemoveVisualEditorProperty);
        ResetVisualEditorPropertyCommand = new RelayCommand(ResetVisualEditorProperty);
        DeleteVisualEditorElementCommand = new RelayCommand(DeleteVisualEditorElement);
        DuplicateVisualEditorElementCommand = new RelayCommand(DuplicateVisualEditorElement);
        MoveVisualEditorElementUpCommand = new RelayCommand(MoveVisualEditorElementUp);
        MoveVisualEditorElementDownCommand = new RelayCommand(MoveVisualEditorElementDown);
        InsertSelectedToolboxItemCommand = new RelayCommand(InsertSelectedToolboxItem);
        WrapVisualEditorSelectionCommand = new RelayCommand(WrapVisualEditorSelection);
        UnwrapVisualEditorSelectionCommand = new RelayCommand(UnwrapVisualEditorSelection);
        CreateCustomControlThemeCommand = new RelayCommand(CreateCustomControlTheme, CanCreateCustomControlTheme);
        ApplyControlThemeCommand = new RelayCommand<ControlThemeDefinitionViewModel?>(ApplyControlTheme, CanApplyControlTheme);
        RemoveControlThemeCommand = new RelayCommand(RemoveControlTheme, CanRemoveControlTheme);
        OpenSelectedControlThemeCommand = new RelayCommand(OpenSelectedControlTheme, () => SelectedControlTheme is not null);
        OpenSelectedThemeResourceCommand = new RelayCommand(OpenSelectedThemeResource, () => SelectedThemeResource is not null);
        RenameSelectedThemeResourceCommand = new RelayCommand(RenameSelectedThemeResource, CanRenameSelectedThemeResource);
        DuplicateSelectedThemeResourceCommand = new RelayCommand(DuplicateSelectedThemeResource, () => SelectedThemeResource is not null);
        DeleteSelectedThemeResourceCommand = new RelayCommand(DeleteSelectedThemeResource, () => SelectedThemeResource is not null);
        ApplySelectedThemeResourceCommand = new RelayCommand(ApplySelectedThemeResource, CanApplySelectedThemeResource);
        ReturnFromThemeEditScopeCommand = new RelayCommand(ReturnFromThemeEditScope, () => IsThemeEditScopeActive);
        ImportControlThemeFilesCommand = new AsyncRelayCommand(ImportControlThemeFiles, () => ActiveProject is not null);
        ExportSelectedControlThemeCommand = new AsyncRelayCommand(ExportSelectedControlTheme, () => SelectedControlTheme is not null);
        SaveControlThemeProjectCommand = new AsyncRelayCommand(SaveControlThemeProject, CanSaveControlThemeProject);
        LoadControlThemeProjectCommand = new AsyncRelayCommand(LoadControlThemeProject, () => ActiveProject is not null);

        LoadVisualEditorToolbox();
        LoadFluentControlThemeTemplates();
        RefreshControlThemes();
    }

    partial void OnSelectedVisualEditorNodeChanged(VisualEditorNodeViewModel? value)
    {
        if (_isRefreshingVisualEditor)
        {
            return;
        }

        SynchronizeVisualEditorStructureRow(value);
        VisualEditorPreviewSelectionVisible = false;
        SelectVisualEditorElement(value?.Element);
    }

    partial void OnSelectedVisualEditorStructureRowChanged(VisualEditorStructureRowViewModel? value)
    {
        if (_isRefreshingVisualEditor ||
            _isSynchronizingVisualEditorSelection)
        {
            return;
        }

        if (!ReferenceEquals(SelectedVisualEditorNode, value?.Node))
        {
            SelectedVisualEditorNode = value?.Node;
        }
    }

    partial void OnSelectedVisualEditorPropertyChanged(VisualEditorPropertyViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        VisualEditorPropertyName = value.MutationName;
        VisualEditorPropertyValue = value.Value;
        RefreshVisualEditorPropertyOptions(value.Property);
        if (VisualEditorPropertiesView is not null &&
            !ReferenceEquals(VisualEditorPropertiesView.CurrentItem, value))
        {
            VisualEditorPropertiesView.MoveCurrentTo(value);
        }

        if (_isSynchronizingVisualEditorPropertySelection)
        {
            return;
        }

        try
        {
            _isSynchronizingVisualEditorPropertySelection = true;
            SelectedVisualEditorAvailableProperty = VisualEditorAvailableProperties.FirstOrDefault(property =>
                string.Equals(property.Name, value.Name, StringComparison.Ordinal) ||
                string.Equals(property.Name, value.MutationName, StringComparison.Ordinal));
        }
        finally
        {
            _isSynchronizingVisualEditorPropertySelection = false;
        }
    }

    partial void OnSelectedVisualEditorAvailablePropertyChanged(VisualEditorAvailablePropertyViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        VisualEditorPropertyName = value.Name;
        VisualEditorPropertyValue = TryGetVisualEditorPropertyValue(value.Name, out var propertyValue)
            ? propertyValue
            : string.Empty;
        RefreshVisualEditorPropertyOptions(value.Property);
        if (VisualEditorAvailablePropertiesView is not null &&
            !ReferenceEquals(VisualEditorAvailablePropertiesView.CurrentItem, value))
        {
            VisualEditorAvailablePropertiesView.MoveCurrentTo(value);
        }

        if (_isSynchronizingVisualEditorPropertySelection)
        {
            return;
        }

        try
        {
            _isSynchronizingVisualEditorPropertySelection = true;
            var propertyRow = VisualEditorProperties.FirstOrDefault(property =>
                string.Equals(property.Name, value.Name, StringComparison.Ordinal) ||
                string.Equals(property.MutationName, value.Name, StringComparison.Ordinal));
            SelectedVisualEditorProperty = propertyRow;
            if (propertyRow is not null &&
                VisualEditorPropertiesView is not null &&
                !ReferenceEquals(VisualEditorPropertiesView.CurrentItem, propertyRow))
            {
                VisualEditorPropertiesView.MoveCurrentTo(propertyRow);
            }
        }
        finally
        {
            _isSynchronizingVisualEditorPropertySelection = false;
        }
    }

    partial void OnSelectedControlThemeChanged(ControlThemeDefinitionViewModel? value)
    {
        RefreshSelectedControlThemeAnalysis();
        NotifyControlThemeCommandsChanged();
    }

    partial void OnControlThemeSearchTextChanged(string value)
    {
        RefreshControlThemeFilters();
    }

    partial void OnSelectedThemeResourceChanged(ThemeResourceViewModel? value)
    {
        ThemeResourceKeyEditText = value?.Key ?? string.Empty;
        RefreshSelectedThemeResourceUsages();
        RefreshSelectedResourceThemeAnalysis(value);
        NotifyControlThemeCommandsChanged();
    }

    partial void OnThemeResourceKeyEditTextChanged(string value)
    {
        NotifyControlThemeCommandsChanged();
    }

    partial void OnSelectedThemePreviewStateChanged(ThemePreviewStateViewModel? value)
    {
        RefreshThemeStateSelectors();
        ControlThemeStatus = value is null || string.Equals(value.State, "normal", StringComparison.Ordinal)
            ? "Theme preview state: Normal."
            : $"Theme preview state: {value.PseudoClass}.";
    }

    partial void OnVisualEditorPropertyFilterChanged(string value)
    {
        ApplyVisualEditorAvailablePropertiesFilter();
    }

    partial void OnSelectedVisualEditorPropertyOptionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            VisualEditorPropertyValue = value;
        }
    }

    partial void OnVisualEditorToolboxSearchChanged(string value)
    {
        FilterVisualEditorToolbox();
    }

    partial void OnVisualEditorDesignerModeChanged(bool value)
    {
        OnPropertyChanged(nameof(VisualEditorPreviewContentHitTestVisible));
        if (!value)
        {
            ClearVisualEditorPreviewDropFeedback();
        }
    }

    partial void OnVisualEditorPreviewSelectionLeftChanged(double value)
    {
        NotifyVisualEditorPreviewSelectionGeometryChanged();
    }

    partial void OnVisualEditorPreviewSelectionTopChanged(double value)
    {
        NotifyVisualEditorPreviewSelectionGeometryChanged();
    }

    partial void OnVisualEditorPreviewSelectionWidthChanged(double value)
    {
        NotifyVisualEditorPreviewSelectionGeometryChanged();
    }

    partial void OnVisualEditorPreviewSelectionHeightChanged(double value)
    {
        NotifyVisualEditorPreviewSelectionGeometryChanged();
    }

    private void RefreshVisualEditingModel(bool updateSourceSelection = true)
    {
        var previousSuppressSourceSelectionUpdate = _suppressVisualEditorSourceSelectionUpdate;
        if (!updateSourceSelection)
        {
            _suppressVisualEditorSourceSelectionUpdate = true;
        }

        try
        {
            if (ActiveXamlFile is not { } xamlFile)
            {
                ClearVisualEditor("No XAML document selected.");
                return;
            }

            if (!xamlFile.IsXaml)
            {
                ClearVisualEditor($"{xamlFile.Path} is not a XAML document.");
                return;
            }

            _visualEditorDocument = _visualMutationEngine.Analyze(xamlFile.Text);
            var roots = BuildVisualEditorNodes(_visualEditorDocument);
            var rows = BuildVisualEditorStructureRows(roots);
            var rootCollection = new ObservableCollection<VisualEditorNodeViewModel>(roots);
            var structureModel = CreateVisualEditorStructureModel(rootCollection);

            _isRefreshingVisualEditor = true;
            try
            {
                VisualEditorStructureNodes = rootCollection;
                VisualEditorStructureModel = structureModel;
                VisualEditorStructureRows = new ObservableCollection<VisualEditorStructureRowViewModel>(rows);
                var selected = FindNode(roots, _visualEditorSelectedSelector);
                var usedPassiveFallbackSelection = selected is null;
                selected ??= roots.FirstOrDefault();
                SelectedVisualEditorNode = selected;
                SelectedVisualEditorStructureRow = rows.FirstOrDefault(row => ReferenceEquals(row.Node, selected));
                var previousSuppressSelectionUpdate = _suppressVisualEditorSourceSelectionUpdate;
                if (usedPassiveFallbackSelection)
                {
                    _suppressVisualEditorSourceSelectionUpdate = true;
                }

                try
                {
                    SelectVisualEditorElement(selected?.Element);
                }
                finally
                {
                    _suppressVisualEditorSourceSelectionUpdate = previousSuppressSelectionUpdate;
                }

                if (usedPassiveFallbackSelection)
                {
                    _visualEditorSelectedSelector = null;
                    if (VisualEditorSourceSelectionFilePath is not null ||
                        VisualEditorSourceSelectionLength != 0)
                    {
                        ClearVisualEditorSourceSelection();
                    }
                }
            }
            finally
            {
                _isRefreshingVisualEditor = false;
            }
        }
        finally
        {
            _suppressVisualEditorSourceSelectionUpdate = previousSuppressSourceSelectionUpdate;
        }
    }

    private void ClearVisualEditor(string status)
    {
        _visualEditorDocument = null;
        _visualEditorSelectedSelector = null;
        _visualEditorCurrentContainerSelector = null;
        VisualEditorStructureNodes = new ObservableCollection<VisualEditorNodeViewModel>();
        VisualEditorStructureModel = null;
        VisualEditorStructureRows = new ObservableCollection<VisualEditorStructureRowViewModel>();
        SelectedVisualEditorNode = null;
        SelectedVisualEditorStructureRow = null;
        VisualEditorProperties = new ObservableCollection<VisualEditorPropertyViewModel>();
        VisualEditorPropertiesView = null;
        SelectedVisualEditorProperty = null;
        VisualEditorAvailableProperties = new ObservableCollection<VisualEditorAvailablePropertyViewModel>();
        VisualEditorAvailablePropertiesView = null;
        SelectedVisualEditorAvailableProperty = null;
        VisualEditorPropertyOptions = new ObservableCollection<string>();
        SelectedVisualEditorPropertyOption = null;
        VisualEditorPreviewSelectionVisible = false;
        VisualEditorPreviewCurrentContainerVisible = false;
        ClearVisualEditorSourceSelection();
        VisualEditorSelectedElementTitle = "No selection";
        VisualEditorCurrentContainerTitle = "No container";
        VisualEditorStatus = status;
        RefreshControlThemeSelectionState();
    }

    private void SelectVisualEditorElement(XamlElementSnapshot? element)
    {
        if (element is null)
        {
            _visualEditorSelectedSelector = null;
            _visualEditorCurrentContainerSelector = null;
            VisualEditorSelectedElementTitle = "No selection";
            VisualEditorCurrentContainerTitle = "No container";
            VisualEditorProperties = new ObservableCollection<VisualEditorPropertyViewModel>();
            VisualEditorPropertiesView = null;
            SelectedVisualEditorProperty = null;
            VisualEditorAvailableProperties = new ObservableCollection<VisualEditorAvailablePropertyViewModel>();
            VisualEditorAvailablePropertiesView = null;
            SelectedVisualEditorAvailableProperty = null;
            VisualEditorPropertyOptions = new ObservableCollection<string>();
            SelectedVisualEditorPropertyOption = null;
            VisualEditorPreviewSelectionVisible = false;
            VisualEditorPreviewCurrentContainerVisible = false;
            ClearVisualEditorSourceSelection();
            VisualEditorStatus = _visualEditorDocument?.Diagnostics.FirstOrDefault() ?? "No XAML element selected.";
            RefreshControlThemeSelectionState();
            return;
        }

        _visualEditorSelectedSelector = element.Selector;
        if (!_suppressVisualEditorSourceSelectionUpdate)
        {
            SetVisualEditorSourceSelection(element);
        }
        VisualEditorSelectedElementTitle = string.IsNullOrWhiteSpace(element.Name)
            ? element.TypeName
            : $"{element.TypeName} #{element.Name}";
        SetVisualEditorCurrentContainer(FindCurrentContainerForSelection(element));
        VisualEditorProperties = new ObservableCollection<VisualEditorPropertyViewModel>(
            BuildVisualEditorPropertyRows(element));
        VisualEditorPropertiesView = CreateGroupedVisualEditorPropertyRowsView(VisualEditorProperties);
        VisualEditorAvailableProperties = new ObservableCollection<VisualEditorAvailablePropertyViewModel>(
            ResolveEditorDescriptor(element)
                .Properties
                .Select(static property => new VisualEditorAvailablePropertyViewModel(property)));
        VisualEditorAvailablePropertiesView = CreateGroupedVisualEditorPropertiesView(VisualEditorAvailableProperties);
        ApplyVisualEditorAvailablePropertiesFilter();
        var preferredPropertyName = VisualEditorPropertyName;
        SelectedVisualEditorAvailableProperty =
            VisualEditorAvailableProperties.FirstOrDefault(property =>
                !IsIdentityProperty(property.Name) &&
                string.Equals(property.Name, preferredPropertyName, StringComparison.Ordinal) &&
                HasVisualEditorAttribute(element, property.Name)) ??
            VisualEditorAvailableProperties.FirstOrDefault(property =>
                IsPrimaryContentProperty(property.Name) &&
                HasVisualEditorAttribute(element, property.Name)) ??
            VisualEditorAvailableProperties.FirstOrDefault(property =>
                string.Equals(property.Name, preferredPropertyName, StringComparison.Ordinal) &&
                HasVisualEditorAttribute(element, property.Name)) ??
            VisualEditorAvailableProperties.FirstOrDefault(property =>
                HasVisualEditorAttribute(element, property.Name)) ??
            VisualEditorAvailableProperties.FirstOrDefault(property =>
                string.Equals(property.Name, preferredPropertyName, StringComparison.Ordinal)) ??
            VisualEditorAvailableProperties.FirstOrDefault();
        if (SelectedVisualEditorAvailableProperty is not null)
        {
            VisualEditorAvailablePropertiesView?.MoveCurrentTo(SelectedVisualEditorAvailableProperty);
        }

        SelectedVisualEditorProperty =
            SelectedVisualEditorAvailableProperty is null
                ? VisualEditorProperties.FirstOrDefault()
                : VisualEditorProperties.FirstOrDefault(property =>
                    string.Equals(property.Name, SelectedVisualEditorAvailableProperty.Name, StringComparison.Ordinal) ||
                    string.Equals(property.MutationName, SelectedVisualEditorAvailableProperty.Name, StringComparison.Ordinal)) ??
                  VisualEditorProperties.FirstOrDefault();
        if (SelectedVisualEditorProperty is not null)
        {
            VisualEditorPropertiesView?.MoveCurrentTo(SelectedVisualEditorProperty);
        }
        VisualEditorStatus = $"{element.TypeName}: {element.Attributes.Count} attribute(s), {element.ChildElementCount} child element(s)";
        RefreshControlThemeSelectionState();
    }

    private IReadOnlyList<VisualEditorPropertyViewModel> BuildVisualEditorPropertyRows(XamlElementSnapshot element)
    {
        var descriptor = ResolveEditorDescriptor(element);
        var rows = new List<VisualEditorPropertyViewModel>();
        var representedAttributes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in descriptor.Properties)
        {
            var mutationName = ResolveMutationPropertyName(element, property.PropertyName);
            var isSet = TryGetVisualEditorAttributeValue(element, property.PropertyName, out var value);
            rows.Add(new VisualEditorPropertyViewModel(
                property,
                value,
                isSet,
                mutationName,
                ApplyVisualEditorPropertyFromGrid));

            representedAttributes.Add(property.PropertyName);
            representedAttributes.Add(mutationName);
        }

        foreach (var attribute in element.Attributes.OrderBy(static attribute => attribute.Key, StringComparer.Ordinal))
        {
            if (representedAttributes.Contains(attribute.Key))
            {
                continue;
            }

            rows.Add(new VisualEditorPropertyViewModel(
                new ControlEditorProperty(
                    attribute.Key,
                    attribute.Key,
                    ControlEditorValueKind.String,
                    "Attributes"),
                attribute.Value,
                isSet: true,
                mutationName: attribute.Key,
                valueChanged: ApplyVisualEditorPropertyFromGrid));
        }

        return rows;
    }

    private static string ResolveMutationPropertyName(XamlElementSnapshot element, string propertyName)
    {
        if (string.Equals(propertyName, "Name", StringComparison.Ordinal) &&
            !element.Attributes.ContainsKey("Name") &&
            element.Attributes.ContainsKey("x:Name"))
        {
            return "x:Name";
        }

        return propertyName;
    }

    private void SetVisualEditorCurrentContainer(XamlElementSnapshot? container)
    {
        _visualEditorCurrentContainerSelector = container?.Selector;
        VisualEditorCurrentContainerTitle = container is null
            ? "No container"
            : FormatVisualEditorElementTitle(container);
    }

    private XamlElementSnapshot? FindCurrentContainerForSelection(XamlElementSnapshot? element)
    {
        if (_visualEditorDocument is null ||
            element is null)
        {
            return null;
        }

        if (IsContainerElement(element))
        {
            return element;
        }

        return _visualEditorDocument.Elements
            .Where(candidate =>
                candidate.Path.Count < element.Path.Count &&
                IsSameOrDescendantPath(element.Path, candidate.Path) &&
                IsContainerElement(candidate))
            .OrderByDescending(static candidate => candidate.Path.Count)
            .FirstOrDefault() ??
            _visualEditorDocument.Elements.FirstOrDefault(candidate => candidate.Path.Count == 0);
    }

    private VisualEditorStructureRowViewModel[] GetCurrentContainerChildRows()
    {
        var container = FindElement(_visualEditorDocument, _visualEditorCurrentContainerSelector);
        if (container is null)
        {
            return Array.Empty<VisualEditorStructureRowViewModel>();
        }

        return VisualEditorStructureRows
            .Where(row => HasParentPath(row.Element, container.Path))
            .ToArray();
    }

    private static bool HasParentPath(XamlElementSnapshot element, IReadOnlyList<int> parentPath)
    {
        return element.Path.Count == parentPath.Count + 1 &&
               element.Path.Take(parentPath.Count).SequenceEqual(parentPath);
    }

    private static bool IsSameOrDescendantPath(IReadOnlyList<int> path, IReadOnlyList<int> ancestorPath)
    {
        return path.Count >= ancestorPath.Count &&
               path.Take(ancestorPath.Count).SequenceEqual(ancestorPath);
    }

    private bool IsContainerElement(XamlElementSnapshot element)
    {
        if (element.Path.Count == 0)
        {
            return true;
        }

        var type = ResolveControlType(element.TypeName);
        return type is not null &&
               (typeof(Panel).IsAssignableFrom(type) ||
                element.ChildElementCount == 0 &&
                (typeof(Decorator).IsAssignableFrom(type) ||
                 typeof(ContentControl).IsAssignableFrom(type) &&
                 !element.Attributes.ContainsKey("Content")));
    }

    private static string FormatVisualEditorElementTitle(XamlElementSnapshot element)
    {
        return string.IsNullOrWhiteSpace(element.Name)
            ? element.TypeName
            : $"{element.TypeName} #{element.Name}";
    }

    private void ApplyVisualEditorPropertyFromGrid(VisualEditorPropertyViewModel property, string value)
    {
        if (_isApplyingVisualEditorMutation ||
            _isApplyingVisualEditorPropertyGridValue ||
            !TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            return;
        }

        try
        {
            _isApplyingVisualEditorPropertyGridValue = true;
            VisualEditorPropertyName = property.MutationName;
            VisualEditorPropertyValue = value;
            ApplyVisualEditorMutation(_visualMutationEngine.SetProperty(
                xamlFile.Text,
                selector,
                property.MutationName,
                value));
        }
        finally
        {
            _isApplyingVisualEditorPropertyGridValue = false;
        }
    }

    private bool TryGetVisualEditorPropertyValue(string propertyName, out string value)
    {
        if (SelectedVisualEditorNode?.Element is { } element &&
            TryGetVisualEditorAttributeValue(element, propertyName, out value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool HasVisualEditorAttribute(XamlElementSnapshot element, string propertyName)
    {
        return TryGetVisualEditorAttributeValue(element, propertyName, out _);
    }

    private static bool TryGetVisualEditorAttributeValue(
        XamlElementSnapshot element,
        string propertyName,
        out string value)
    {
        if (element.Attributes.TryGetValue(propertyName, out value!))
        {
            return true;
        }

        if (string.Equals(propertyName, "Name", StringComparison.Ordinal) &&
            element.Attributes.TryGetValue("x:Name", out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsPrimaryContentProperty(string propertyName)
    {
        return propertyName is "Content" or "Text" or "Header";
    }

    private static bool IsIdentityProperty(string propertyName)
    {
        return propertyName is "Name" or "Classes" ||
               propertyName.StartsWith("AutomationProperties.", StringComparison.Ordinal);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The visual editor property grid uses ProDataGrid grouping for design-time metadata.")]
    private static DataGridCollectionView CreateGroupedVisualEditorPropertyRowsView(
        IEnumerable<VisualEditorPropertyViewModel> properties)
    {
        var view = new DataGridCollectionView(properties.ToArray());
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(VisualEditorPropertyViewModel.Group)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(
            nameof(VisualEditorPropertyViewModel.Group),
            ListSortDirection.Ascending));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(
            nameof(VisualEditorPropertyViewModel.Name),
            ListSortDirection.Ascending));
        return view;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The visual editor property grid uses ProDataGrid grouping for design-time metadata.")]
    private static DataGridCollectionView CreateGroupedVisualEditorPropertiesView(
        IEnumerable<VisualEditorAvailablePropertyViewModel> properties)
    {
        var view = new DataGridCollectionView(properties.ToArray());
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(VisualEditorAvailablePropertyViewModel.Group)));
        return view;
    }

    private void ApplyVisualEditorAvailablePropertiesFilter()
    {
        var query = VisualEditorPropertyFilter.Trim();

        if (VisualEditorPropertiesView is { } propertiesView)
        {
            propertiesView.Filter = string.IsNullOrWhiteSpace(query)
                ? null!
                : item => item is VisualEditorPropertyViewModel property &&
                          MatchesVisualEditorPropertyFilter(property, query);

            if (SelectedVisualEditorProperty is { } selectedProperty &&
                propertiesView.Contains(selectedProperty))
            {
                propertiesView.MoveCurrentTo(selectedProperty);
            }
            else
            {
                propertiesView.MoveCurrentToFirst();
            }
        }

        if (VisualEditorAvailablePropertiesView is not { } view)
        {
            return;
        }

        view.Filter = string.IsNullOrWhiteSpace(query)
            ? null!
            : item => item is VisualEditorAvailablePropertyViewModel property &&
                      MatchesVisualEditorPropertyFilter(property, query);

        if (SelectedVisualEditorAvailableProperty is { } selected &&
            view.Contains(selected))
        {
            view.MoveCurrentTo(selected);
        }
        else
        {
            view.MoveCurrentToFirst();
        }
    }

    private static bool MatchesVisualEditorPropertyFilter(
        VisualEditorPropertyViewModel property,
        string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                property.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.MutationName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Group.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Kind.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.TypeName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Value.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Priority.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesVisualEditorPropertyFilter(
        VisualEditorAvailablePropertyViewModel property,
        string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                property.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Group.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                property.Kind.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private void SynchronizeVisualEditorStructureRow(VisualEditorNodeViewModel? node)
    {
        if (_isSynchronizingVisualEditorSelection)
        {
            return;
        }

        try
        {
            _isSynchronizingVisualEditorSelection = true;
            SelectedVisualEditorStructureRow = node is null
                ? null
                : VisualEditorStructureRows.FirstOrDefault(row => ReferenceEquals(row.Node, node));
        }
        finally
        {
            _isSynchronizingVisualEditorSelection = false;
        }
    }

    private void SetVisualEditorSourceSelection(XamlElementSnapshot element)
    {
        VisualEditorSourceSelectionFilePath = ActiveXamlFile?.Path;
        VisualEditorSourceSelectionStart = element.Start;
        VisualEditorSourceSelectionLength = Math.Max(0, element.Length);
        VisualEditorSourceSelectionVersion++;
    }

    private void ClearVisualEditorSourceSelection()
    {
        VisualEditorSourceSelectionFilePath = null;
        VisualEditorSourceSelectionStart = 0;
        VisualEditorSourceSelectionLength = 0;
        VisualEditorSourceSelectionVersion++;
    }

    private ControlEditorDescriptor ResolveEditorDescriptor(XamlElementSnapshot element)
    {
        return _visualEditorRegistry.Resolve(ResolveControlType(element.TypeName) ?? typeof(Control));
    }

    private void RefreshVisualEditorPropertyOptions(string propertyName)
    {
        var property = VisualEditorAvailableProperties.FirstOrDefault(available =>
            string.Equals(available.Name, propertyName, StringComparison.Ordinal))?.Property;
        RefreshVisualEditorPropertyOptions(property);
    }

    private void RefreshVisualEditorPropertyOptions(ControlEditorProperty? property)
    {
        var options = property is null
            ? Array.Empty<string>()
            : GetSuggestedValues(property).ToArray();

        VisualEditorPropertyOptions = new ObservableCollection<string>(options);
        SelectedVisualEditorPropertyOption = options.FirstOrDefault(option =>
            string.Equals(option, VisualEditorPropertyValue, StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetSuggestedValues(ControlEditorProperty property)
    {
        return property.ValueKind switch
        {
            ControlEditorValueKind.Boolean => new[] { "True", "False" },
            ControlEditorValueKind.Brush => new[]
            {
                "Transparent",
                "Black",
                "White",
                "Red",
                "Green",
                "Blue",
                "#0078D4",
                "#1F2937",
                "#F8FAFC"
            },
            ControlEditorValueKind.Thickness => new[] { "0", "4", "8", "12", "16", "0,8", "8,0", "4,8,4,8" },
            ControlEditorValueKind.Enum => GetEnumSuggestedValues(property.PropertyName),
            _ => Array.Empty<string>()
        };
    }

    private static IEnumerable<string> GetEnumSuggestedValues(string propertyName)
    {
        return propertyName switch
        {
            "HorizontalAlignment" => new[] { "Stretch", "Left", "Center", "Right" },
            "VerticalAlignment" => new[] { "Stretch", "Top", "Center", "Bottom" },
            "TextWrapping" => new[] { "NoWrap", "Wrap", "WrapWithOverflow" },
            "FontWeight" => new[] { "Normal", "SemiBold", "Bold", "Light" },
            _ => Array.Empty<string>()
        };
    }

    private static Type? ResolveControlType(string xamlTypeName)
    {
        var localName = GetLocalName(xamlTypeName);
        return GetAvaloniaControlTypes()
            .FirstOrDefault(type =>
                typeof(Control).IsAssignableFrom(type) &&
                string.Equals(type.Name, localName, StringComparison.Ordinal));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Visual editor control resolution is dynamic by design for designer metadata.")]
    private static IEnumerable<Type> GetAvaloniaControlTypes()
    {
        try
        {
            return typeof(Control).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }

    private static string GetLocalName(string typeName)
    {
        var index = typeName.IndexOf(':', StringComparison.Ordinal);
        return index < 0 ? typeName : typeName[(index + 1)..];
    }

    private void ApplyVisualEditorProperty()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(VisualEditorPropertyName))
        {
            VisualEditorStatus = "Property name is required.";
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.SetProperty(
            xamlFile.Text,
            selector,
            VisualEditorPropertyName.Trim(),
            VisualEditorPropertyValue));
    }

    private void RemoveVisualEditorProperty()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            string.IsNullOrWhiteSpace(VisualEditorPropertyName))
        {
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.RemoveProperty(
            xamlFile.Text,
            selector,
            VisualEditorPropertyName.Trim()));
    }

    private void ResetVisualEditorProperty()
    {
        RemoveVisualEditorProperty();
    }

    private void DeleteVisualEditorElement()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.RemoveElement(xamlFile.Text, selector));
    }

    private void DuplicateVisualEditorElement()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return;
        }

        if (selected.Path.Count == 0)
        {
            VisualEditorStatus = "The root element cannot be duplicated.";
            return;
        }

        var duplicatePath = selected.Path.ToArray();
        duplicatePath[^1]++;
        _visualEditorSelectedSelector = XamlElementSelector.ByPath(duplicatePath);
        ApplyVisualEditorMutation(_visualMutationEngine.DuplicateElement(xamlFile.Text, selector));
    }

    private void MoveVisualEditorElementUp()
    {
        MoveSelectedVisualEditorElement(-1);
    }

    private void MoveVisualEditorElementDown()
    {
        MoveSelectedVisualEditorElement(1);
    }

    private void MoveSelectedVisualEditorElement(int delta)
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return;
        }

        if (selected.Path.Count == 0)
        {
            VisualEditorStatus = "The root element cannot be reordered.";
            return;
        }

        var index = selected.Path[^1];
        var targetIndex = index + delta;
        var parentPath = selected.Path.Take(selected.Path.Count - 1).ToArray();
        var siblings = _visualEditorDocument?.Elements.Count(element =>
            element.Path.Count == selected.Path.Count &&
            element.Path.Take(parentPath.Length).SequenceEqual(parentPath)) ?? 0;

        if (targetIndex < 0 || targetIndex >= siblings)
        {
            VisualEditorStatus = delta < 0
                ? "The selected element is already first."
                : "The selected element is already last.";
            return;
        }

        var newPath = selected.Path.ToArray();
        newPath[^1] = targetIndex;
        _visualEditorSelectedSelector = XamlElementSelector.ByPath(newPath);
        ApplyVisualEditorMutation(_visualMutationEngine.ReorderElement(xamlFile.Text, selector, targetIndex));
    }

    public bool SelectVisualEditorPreviewControl(Control control, Rect selectionBounds)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (ActiveXamlFile is not { } xamlFile)
        {
            ClearVisualEditor("No XAML document selected.");
            return false;
        }

        var visualNode = _visualTreeSnapshotService.Snapshot(control);
        var selection = _visualSelectionService.SelectVisual(xamlFile.Text, visualNode);
        if (!selection.HasSelection || selection.XamlElement is null)
        {
            VisualEditorStatus = string.Join(Environment.NewLine, selection.Diagnostics);
            return false;
        }

        _visualEditorDocument = selection.Document;
        _visualEditorSelectedSelector = selection.XamlElement.Selector;
        SetVisualEditorPreviewSelectionBounds(selectionBounds);
        RefreshVisualEditingModel();
        VisualEditorStatus = $"Selected {selection.XamlElement.TypeName} from preview.";
        return true;
    }

    public bool TryResolveVisualEditorPreviewControl(
        Control control,
        [NotNullWhen(true)] out XamlDocumentSnapshot? document,
        [NotNullWhen(true)] out XamlElementSnapshot? element,
        out IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(control);

        document = null;
        element = null;
        diagnostics = Array.Empty<string>();

        if (ActiveXamlFile is not { } xamlFile)
        {
            diagnostics = new[] { "No XAML document selected." };
            return false;
        }

        var visualNode = _visualTreeSnapshotService.Snapshot(control);
        var selection = _visualSelectionService.SelectVisual(xamlFile.Text, visualNode);
        document = selection.Document;
        element = selection.XamlElement;
        diagnostics = selection.Diagnostics;
        return selection.HasSelection && selection.XamlElement is not null;
    }

    public bool SelectVisualEditorPreviewElement(
        XamlDocumentSnapshot document,
        XamlElementSnapshot element,
        Rect selectionBounds,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        _visualEditorDocument = document;
        _visualEditorSelectedSelector = element.Selector;
        SetVisualEditorPreviewSelectionBounds(selectionBounds);
        RefreshVisualEditingModel();
        VisualEditorStatus = $"Selected {element.TypeName} {reason}.";
        return true;
    }

    public bool SelectVisualEditorSourceRange(
        string? filePath,
        int selectionStart,
        int selectionLength,
        int caretOffset)
    {
        if (_isApplyingVisualEditorMutation)
        {
            return false;
        }

        if (ActiveXamlFile is not { } xamlFile ||
            !xamlFile.IsXaml ||
            !string.Equals(filePath, xamlFile.Path, StringComparison.Ordinal))
        {
            return false;
        }

        var document = _visualMutationEngine.Analyze(xamlFile.Text);
        var element = FindVisualEditorElementAtSourceRange(
            document,
            selectionStart,
            selectionLength,
            caretOffset);
        if (element is null)
        {
            return false;
        }

        _visualEditorSelectedSelector = element.Selector;
        try
        {
            _suppressVisualEditorSourceSelectionUpdate = true;
            RefreshVisualEditingModel();
        }
        finally
        {
            _suppressVisualEditorSourceSelectionUpdate = false;
        }

        VisualEditorStatus = $"Selected {element.TypeName} from XAML.";
        return true;
    }

    public void UpdateVisualEditorPreviewSelectionBounds(Rect bounds)
    {
        SetVisualEditorPreviewSelectionBounds(bounds);
    }

    public void UpdateVisualEditorPreviewCurrentContainerBounds(Rect? bounds)
    {
        if (bounds is not { } value)
        {
            VisualEditorPreviewCurrentContainerVisible = false;
            return;
        }

        VisualEditorPreviewCurrentContainerLeft = value.X;
        VisualEditorPreviewCurrentContainerTop = value.Y;
        VisualEditorPreviewCurrentContainerWidth = Math.Max(1, value.Width);
        VisualEditorPreviewCurrentContainerHeight = Math.Max(1, value.Height);
        VisualEditorPreviewCurrentContainerVisible = true;
    }

    public void UpdateVisualEditorPreviewDropFeedback(
        Rect? targetBounds,
        Rect? insertionBounds,
        Rect? placeholderBounds = null)
    {
        if (targetBounds is { } target)
        {
            VisualEditorPreviewDropTargetLeft = target.X;
            VisualEditorPreviewDropTargetTop = target.Y;
            VisualEditorPreviewDropTargetWidth = Math.Max(1, target.Width);
            VisualEditorPreviewDropTargetHeight = Math.Max(1, target.Height);
            VisualEditorPreviewDropTargetVisible = true;
        }
        else
        {
            VisualEditorPreviewDropTargetVisible = false;
        }

        if (insertionBounds is { } insertion)
        {
            VisualEditorPreviewInsertionLeft = insertion.X;
            VisualEditorPreviewInsertionTop = insertion.Y;
            VisualEditorPreviewInsertionWidth = Math.Max(1, insertion.Width);
            VisualEditorPreviewInsertionHeight = Math.Max(1, insertion.Height);
            VisualEditorPreviewInsertionVisible = true;
        }
        else
        {
            VisualEditorPreviewInsertionVisible = false;
        }

        if (placeholderBounds is { } placeholder)
        {
            VisualEditorPreviewDropPlaceholderLeft = placeholder.X;
            VisualEditorPreviewDropPlaceholderTop = placeholder.Y;
            VisualEditorPreviewDropPlaceholderWidth = Math.Max(1, placeholder.Width);
            VisualEditorPreviewDropPlaceholderHeight = Math.Max(1, placeholder.Height);
            VisualEditorPreviewDropPlaceholderVisible = true;
        }
        else
        {
            VisualEditorPreviewDropPlaceholderVisible = false;
        }
    }

    public void UpdateVisualEditorPreviewGuides(
        Rect? verticalGuide,
        Rect? horizontalGuide,
        Point? measurementPosition,
        string? measurementText)
    {
        if (verticalGuide is { } vertical)
        {
            VisualEditorPreviewVerticalGuideLeft = vertical.X;
            VisualEditorPreviewVerticalGuideTop = vertical.Y;
            VisualEditorPreviewVerticalGuideHeight = Math.Max(1, vertical.Height);
            VisualEditorPreviewVerticalGuideVisible = true;
        }
        else
        {
            VisualEditorPreviewVerticalGuideVisible = false;
        }

        if (horizontalGuide is { } horizontal)
        {
            VisualEditorPreviewHorizontalGuideLeft = horizontal.X;
            VisualEditorPreviewHorizontalGuideTop = horizontal.Y;
            VisualEditorPreviewHorizontalGuideWidth = Math.Max(1, horizontal.Width);
            VisualEditorPreviewHorizontalGuideVisible = true;
        }
        else
        {
            VisualEditorPreviewHorizontalGuideVisible = false;
        }

        if (measurementPosition is { } position &&
            !string.IsNullOrWhiteSpace(measurementText))
        {
            VisualEditorPreviewMeasurementLeft = position.X;
            VisualEditorPreviewMeasurementTop = position.Y;
            VisualEditorPreviewMeasurementText = measurementText;
            VisualEditorPreviewMeasurementVisible = true;
        }
        else
        {
            VisualEditorPreviewMeasurementVisible = false;
            VisualEditorPreviewMeasurementText = string.Empty;
        }
    }

    public void ClearVisualEditorPreviewDropFeedback()
    {
        VisualEditorPreviewDropTargetVisible = false;
        VisualEditorPreviewInsertionVisible = false;
        VisualEditorPreviewDropPlaceholderVisible = false;
        UpdateVisualEditorPreviewGuides(null, null, null, null);
    }

    public bool SelectAdjacentVisualEditorElement(bool previous)
    {
        var rows = GetCurrentContainerChildRows();
        if (rows.Length == 0)
        {
            rows = VisualEditorStructureRows.ToArray();
        }

        if (rows.Length == 0)
        {
            return false;
        }

        var currentIndex = SelectedVisualEditorStructureRow is { } current
            ? Array.IndexOf(rows, current)
            : -1;
        if (currentIndex < 0 && SelectedVisualEditorNode is not null)
        {
            for (var index = 0; index < rows.Length; index++)
            {
                if (ReferenceEquals(rows[index].Node, SelectedVisualEditorNode))
                {
                    currentIndex = index;
                    break;
                }
            }
        }
        if (currentIndex < 0)
        {
            currentIndex = previous ? rows.Length : -1;
        }

        var nextIndex = previous
            ? (currentIndex - 1 + rows.Length) % rows.Length
            : (currentIndex + 1) % rows.Length;
        var next = rows[nextIndex];

        SelectedVisualEditorNode = next.Node;
        VisualEditorStatus = $"Selected {next.Element.TypeName} from keyboard in {VisualEditorCurrentContainerTitle}.";
        return true;
    }

    public bool SelectVisualEditorParentElement()
    {
        if (!TryGetSelectedVisualEditorElement(out var selected) ||
            selected.Path.Count == 0 ||
            _visualEditorDocument is null)
        {
            return false;
        }

        var parentPath = selected.Path.Take(selected.Path.Count - 1).ToArray();
        var parent = _visualEditorDocument.Elements.FirstOrDefault(element =>
            element.Path.SequenceEqual(parentPath));
        if (parent is null)
        {
            return false;
        }

        _visualEditorSelectedSelector = parent.Selector;
        RefreshVisualEditingModel();
        VisualEditorStatus = $"Selected parent {parent.TypeName}.";
        return true;
    }

    public bool EnterVisualEditorCurrentContainer()
    {
        if (!TryGetSelectedVisualEditorElement(out var selected) ||
            _visualEditorDocument is null)
        {
            return false;
        }

        var selectedIsContainer = IsContainerElement(selected);
        var container = selectedIsContainer
            ? selected
            : FindCurrentContainerForSelection(selected);
        if (container is null ||
            !selectedIsContainer)
        {
            return false;
        }

        SetVisualEditorCurrentContainer(container);
        var firstChild = _visualEditorDocument.Elements
            .Where(element =>
                element.Path.Count == container.Path.Count + 1 &&
                element.Path.Take(container.Path.Count).SequenceEqual(container.Path))
            .OrderBy(static element => element.Path[^1])
            .FirstOrDefault();
        if (firstChild is not null)
        {
            _visualEditorSelectedSelector = firstChild.Selector;
            RefreshVisualEditingModel();
        }

        VisualEditorStatus = $"Entered {FormatVisualEditorElementTitle(container)}.";
        return true;
    }

    public bool IsVisualEditorCandidateInCurrentContainer(XamlElementSnapshot element)
    {
        if (_visualEditorCurrentContainerSelector is null)
        {
            return false;
        }

        var container = FindElement(_visualEditorDocument, _visualEditorCurrentContainerSelector);
        return container is not null && HasParentPath(element, container.Path);
    }

    public bool IsVisualEditorCandidateInCurrentContainerSubtree(XamlElementSnapshot element)
    {
        if (_visualEditorCurrentContainerSelector is null)
        {
            return true;
        }

        var container = FindElement(_visualEditorDocument, _visualEditorCurrentContainerSelector);
        return container is null || IsSameOrDescendantPath(element.Path, container.Path);
    }

    public XamlElementSnapshot? GetVisualEditorCurrentContainerElement()
    {
        return FindElement(_visualEditorDocument, _visualEditorCurrentContainerSelector);
    }

    public bool MoveVisualEditorSelectionBy(double deltaX, double deltaY)
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return false;
        }

        if (Math.Abs(deltaX) < 0.1 && Math.Abs(deltaY) < 0.1)
        {
            return false;
        }

        var requests = UsesCanvasPositioning(selected)
            ? CreateCanvasMoveRequests(selected, selector, deltaX, deltaY)
            : CreateMarginMoveRequests(selected, selector, deltaX, deltaY);
        var result = _visualMutationEngine.Batch(xamlFile.Text, requests);
        ApplyVisualEditorMutation(result);

        if (result.Success)
        {
            VisualEditorStatus = UsesCanvasPositioning(selected)
                ? "Moved selection using Canvas.Left and Canvas.Top."
                : "Moved selection using Margin.";
        }

        return result.Success;
    }

    public bool ResizeVisualEditorSelectionBy(double deltaWidth, double deltaHeight)
    {
        var start = new Rect(
            VisualEditorPreviewSelectionLeft,
            VisualEditorPreviewSelectionTop,
            VisualEditorPreviewSelectionWidth,
            VisualEditorPreviewSelectionHeight);
        return ResizeVisualEditorSelectionToBounds(
            start,
            new Rect(
                start.Position,
                new Size(
                    Math.Max(1, start.Width + deltaWidth),
                    Math.Max(1, start.Height + deltaHeight))));
    }

    public bool ResizeVisualEditorSelectionToBounds(Rect oldBounds, Rect newBounds)
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return false;
        }

        var deltaX = newBounds.X - oldBounds.X;
        var deltaY = newBounds.Y - oldBounds.Y;
        var width = Math.Max(1, newBounds.Width);
        var height = Math.Max(1, newBounds.Height);
        if (Math.Abs(deltaX) < 0.1 &&
            Math.Abs(deltaY) < 0.1 &&
            Math.Abs(width - oldBounds.Width) < 0.1 &&
            Math.Abs(height - oldBounds.Height) < 0.1)
        {
            return false;
        }

        var requests = new List<XamlMutationRequest>();
        if (Math.Abs(deltaX) >= 0.1 || Math.Abs(deltaY) >= 0.1)
        {
            requests.AddRange(UsesCanvasPositioning(selected)
                ? CreateCanvasMoveRequests(selected, selector, deltaX, deltaY)
                : CreateMarginMoveRequests(selected, selector, deltaX, deltaY));
        }

        requests.AddRange(new[]
        {
            new XamlMutationRequest(
                XamlMutationKind.SetProperty,
                selector,
                PropertyName: "Width",
                Value: FormatDesignerDouble(width)),
            new XamlMutationRequest(
                XamlMutationKind.SetProperty,
                selector,
                PropertyName: "Height",
                Value: FormatDesignerDouble(height))
        });

        var result = _visualMutationEngine.Batch(
            xamlFile.Text,
            requests);

        ApplyVisualEditorMutation(result);

        if (result.Success)
        {
            VisualEditorStatus = "Resized selection using Width and Height.";
        }

        return result.Success;
    }

    public bool MoveVisualEditorSelectionNearPreviewControl(
        Control targetControl,
        bool after,
        XamlElementSnapshot? resolvedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(targetControl);

        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return false;
        }

        var target = ResolvePreviewTargetElement(
            xamlFile.Text,
            targetControl,
            resolvedTarget,
            out var diagnostics);
        if (target is null)
        {
            VisualEditorStatus = diagnostics.Count > 0
                ? string.Join(Environment.NewLine, diagnostics)
                : "The drop target could not be mapped to XAML.";
            return false;
        }

        if (Matches(target, selector) ||
            IsDescendantOf(target, selected) ||
            target.Path.Count == 0)
        {
            return false;
        }

        var targetParentPath = target.Path.Take(target.Path.Count - 1).ToArray();
        var selectedParentPath = selected.Path.Take(Math.Max(0, selected.Path.Count - 1)).ToArray();
        var targetIndex = target.Path[^1] + (after ? 1 : 0);
        if (selectedParentPath.SequenceEqual(targetParentPath) &&
            selected.Path.Count > 0 &&
            selected.Path[^1] < targetIndex)
        {
            targetIndex--;
        }

        if (selectedParentPath.SequenceEqual(targetParentPath) &&
            selected.Path.Count > 0 &&
            selected.Path[^1] == targetIndex)
        {
            return false;
        }

        var targetParentSelector = XamlElementSelector.ByPath(targetParentPath);
        var selectedPathAfterMove = selectedParentPath.SequenceEqual(targetParentPath)
            ? targetParentPath.Concat(new[] { targetIndex }).ToArray()
            : AdjustPathAfterRemoval(targetParentPath, selected.Path).Concat(new[] { targetIndex }).ToArray();
        var result = selectedParentPath.SequenceEqual(targetParentPath)
            ? _visualMutationEngine.ReorderElement(xamlFile.Text, selector, targetIndex)
            : _visualMutationEngine.MoveElement(xamlFile.Text, selector, targetParentSelector, targetIndex);
        if (result.Success)
        {
            _visualEditorSelectedSelector = XamlElementSelector.ByPath(selectedPathAfterMove);
        }

        ApplyVisualEditorMutation(result);

        if (result.Success)
        {
            VisualEditorStatus = after
                ? $"Moved selection after {target.TypeName}."
                : $"Moved selection before {target.TypeName}.";
        }

        return result.Success;
    }

    public bool MoveVisualEditorSelectionIntoPreviewControl(
        Control targetParent,
        XamlElementSnapshot? resolvedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(targetParent);

        if (!TryGetVisualEditingContext(out var xamlFile, out var selector) ||
            !TryGetSelectedVisualEditorElement(out var selected))
        {
            return false;
        }

        if (targetParent is Decorator { Child: not null } ||
            targetParent is ContentControl { Content: not null })
        {
            VisualEditorStatus = "Drop target already has content.";
            return false;
        }

        if (targetParent is not Panel and not Decorator and not ContentControl)
        {
            VisualEditorStatus = "Drop target must be a layout container.";
            return false;
        }

        var target = ResolvePreviewTargetElement(
            xamlFile.Text,
            targetParent,
            resolvedTarget,
            out var diagnostics);
        if (target is null)
        {
            VisualEditorStatus = diagnostics.Count > 0
                ? string.Join(Environment.NewLine, diagnostics)
                : "The drop target could not be mapped to XAML.";
            return false;
        }

        if (Matches(target, selector) ||
            IsDescendantOf(target, selected))
        {
            VisualEditorStatus = "Cannot move an element into itself.";
            return false;
        }

        if (HasSameParent(selected, target))
        {
            return false;
        }

        var selectedPathAfterMove = AdjustPathAfterRemoval(target.Path, selected.Path)
            .Concat(new[] { target.ChildElementCount })
            .ToArray();
        var result = _visualMutationEngine.MoveElement(xamlFile.Text, selector, target.Selector);
        if (result.Success)
        {
            _visualEditorSelectedSelector = XamlElementSelector.ByPath(selectedPathAfterMove);
        }

        ApplyVisualEditorMutation(result);

        if (result.Success)
        {
            VisualEditorStatus = $"Moved selection into {target.TypeName}.";
        }

        return result.Success;
    }

    public bool InsertToolboxItemIntoPreviewControl(
        ToolboxItemDescriptor item,
        Control targetParent,
        int? childIndex = null,
        Point? canvasPosition = null,
        XamlElementSnapshot? resolvedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(targetParent);

        if (ActiveXamlFile is not { } xamlFile)
        {
            ClearVisualEditor("No XAML document selected.");
            return false;
        }

        var target = resolvedTarget is not null
            ? FindElement(_visualMutationEngine.Analyze(xamlFile.Text), XamlElementSelector.ByPath(resolvedTarget.Path.ToArray()))
            : null;

        if (target is null)
        {
            var targetVisualNode = _visualTreeSnapshotService.Snapshot(targetParent);
            var targetSelection = _visualSelectionService.SelectVisual(xamlFile.Text, targetVisualNode);
            if (!targetSelection.HasSelection || targetSelection.XamlElement is null)
            {
                VisualEditorStatus = string.Join(Environment.NewLine, targetSelection.Diagnostics);
                return false;
            }

            target = targetSelection.XamlElement;
        }

        var insertionProperties = CreateToolboxInsertionProperties(targetParent, canvasPosition);
        var insertionIndex = childIndex is { } index
            ? Math.Clamp(index, 0, target.ChildElementCount)
            : target.ChildElementCount;
        var selectedPath = target.Path.Concat(new[] { insertionIndex }).ToArray();
        _visualEditorSelectedSelector = XamlElementSelector.ByPath(selectedPath);
        var targetSelector = XamlElementSelector.ByPath(target.Path.ToArray());

        var insertion = _visualToolboxInsertion.Insert(
            xamlFile.Text,
            targetSelector,
            item,
            childIndex is null ? null : insertionIndex,
            insertionProperties);
        ApplyVisualEditorMutation(insertion.Mutation);

        if (insertion.Success)
        {
            VisualEditorStatus = canvasPosition is not null
                ? $"Inserted {item.TypeName} at canvas position."
                : $"Inserted {item.TypeName} into {target.TypeName}.";
        }

        return insertion.Success;
    }

    private XamlElementSnapshot? ResolvePreviewTargetElement(
        string xaml,
        Control targetControl,
        XamlElementSnapshot? resolvedTarget,
        out IReadOnlyList<string> diagnostics)
    {
        if (resolvedTarget is not null)
        {
            var document = _visualMutationEngine.Analyze(xaml);
            if (FindElement(document, XamlElementSelector.ByPath(resolvedTarget.Path.ToArray())) is { } target)
            {
                diagnostics = Array.Empty<string>();
                return target;
            }
        }

        var targetVisualNode = _visualTreeSnapshotService.Snapshot(targetControl);
        var targetSelection = _visualSelectionService.SelectVisual(xaml, targetVisualNode);
        diagnostics = targetSelection.Diagnostics;
        return targetSelection.HasSelection
            ? targetSelection.XamlElement
            : null;
    }

    private void SetVisualEditorPreviewSelectionBounds(Rect bounds)
    {
        VisualEditorPreviewSelectionLeft = bounds.X;
        VisualEditorPreviewSelectionTop = bounds.Y;
        VisualEditorPreviewSelectionWidth = Math.Max(1, bounds.Width);
        VisualEditorPreviewSelectionHeight = Math.Max(1, bounds.Height);
        VisualEditorPreviewSelectionVisible = true;
    }

    private void NotifyVisualEditorPreviewSelectionGeometryChanged()
    {
        OnPropertyChanged(nameof(VisualEditorPreviewNorthWestThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewNorthWestThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewNorthThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewNorthThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewNorthEastThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewNorthEastThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewWestThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewWestThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewEastThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewEastThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthWestThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthWestThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthThumbTop));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthEastThumbLeft));
        OnPropertyChanged(nameof(VisualEditorPreviewSouthEastThumbTop));
    }

    private IEnumerable<XamlMutationRequest> CreateCanvasMoveRequests(
        XamlElementSnapshot selected,
        XamlElementSelector selector,
        double deltaX,
        double deltaY)
    {
        var left = GetDoubleAttribute(selected, "Canvas.Left", 0) + deltaX;
        var top = GetDoubleAttribute(selected, "Canvas.Top", 0) + deltaY;

        return new[]
        {
            new XamlMutationRequest(
                XamlMutationKind.SetProperty,
                selector,
                PropertyName: "Canvas.Left",
                Value: FormatDesignerDouble(left)),
            new XamlMutationRequest(
                XamlMutationKind.SetProperty,
                selector,
                PropertyName: "Canvas.Top",
                Value: FormatDesignerDouble(top))
        };
    }

    private static IEnumerable<XamlMutationRequest> CreateMarginMoveRequests(
        XamlElementSnapshot selected,
        XamlElementSelector selector,
        double deltaX,
        double deltaY)
    {
        var thickness = ParseThicknessAttribute(selected, "Margin");
        thickness = thickness with
        {
            Left = thickness.Left + deltaX,
            Top = thickness.Top + deltaY
        };

        return new[]
        {
            new XamlMutationRequest(
                XamlMutationKind.SetProperty,
                selector,
                PropertyName: "Margin",
                Value: FormatDesignerThickness(thickness))
        };
    }

    private bool UsesCanvasPositioning(XamlElementSnapshot selected)
    {
        if (selected.Attributes.ContainsKey("Canvas.Left") ||
            selected.Attributes.ContainsKey("Canvas.Top"))
        {
            return true;
        }

        if (_visualEditorDocument is null || selected.Path.Count == 0)
        {
            return false;
        }

        var parentPath = selected.Path.Take(selected.Path.Count - 1);
        var parent = _visualEditorDocument.Elements.FirstOrDefault(element =>
            element.Path.SequenceEqual(parentPath));

        return parent is not null &&
               string.Equals(GetLocalName(parent.TypeName), "Canvas", StringComparison.Ordinal);
    }

    private static bool IsDescendantOf(XamlElementSnapshot candidate, XamlElementSnapshot ancestor)
    {
        return candidate.Path.Count > ancestor.Path.Count &&
               candidate.Path.Take(ancestor.Path.Count).SequenceEqual(ancestor.Path);
    }

    private static IReadOnlyList<int> AdjustPathAfterRemoval(
        IReadOnlyList<int> path,
        IReadOnlyList<int> removedPath)
    {
        if (path.Count == 0 || removedPath.Count == 0)
        {
            return path.ToArray();
        }

        var adjusted = path.ToArray();
        var removedIndexDepth = removedPath.Count - 1;
        if (path.Count <= removedIndexDepth ||
            !path.Take(removedIndexDepth).SequenceEqual(removedPath.Take(removedIndexDepth)))
        {
            return adjusted;
        }

        if (removedPath[removedIndexDepth] < path[removedIndexDepth])
        {
            adjusted[removedIndexDepth]--;
        }

        return adjusted;
    }

    private static XamlElementSnapshot? FindVisualEditorElementAtSourceRange(
        XamlDocumentSnapshot document,
        int selectionStart,
        int selectionLength,
        int caretOffset)
    {
        if (document.Elements.Count == 0)
        {
            return null;
        }

        var documentLength = document.Text.Length;
        var start = Math.Clamp(selectionStart, 0, documentLength);
        var length = Math.Clamp(selectionLength, 0, documentLength - start);
        var offset = length > 0
            ? start
            : Math.Clamp(caretOffset, 0, documentLength);
        var end = length > 0
            ? start + length
            : offset;

        return document.Elements
            .Where(element => ContainsSourceRange(element, offset, end))
            .OrderByDescending(static element => element.Path.Count)
            .ThenBy(static element => element.Length)
            .FirstOrDefault();
    }

    private static bool ContainsSourceRange(XamlElementSnapshot element, int start, int end)
    {
        var elementStart = element.Start;
        var elementEnd = element.Start + element.Length;
        return start >= elementStart &&
               start <= elementEnd &&
               end >= elementStart &&
               end <= elementEnd;
    }

    private static bool HasSameParent(XamlElementSnapshot selected, XamlElementSnapshot target)
    {
        return selected.Path.Count > 0 &&
               selected.Path.Take(selected.Path.Count - 1).SequenceEqual(target.Path);
    }

    private static double GetDoubleAttribute(
        XamlElementSnapshot selected,
        string attributeName,
        double fallback)
    {
        return selected.Attributes.TryGetValue(attributeName, out var value) &&
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
               !double.IsNaN(result) &&
               !double.IsInfinity(result)
            ? result
            : fallback;
    }

    private static DesignerThickness ParseThicknessAttribute(
        XamlElementSnapshot selected,
        string attributeName)
    {
        if (!selected.Attributes.TryGetValue(attributeName, out var value))
        {
            return default;
        }

        var values = value
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0)
            .ToArray();

        return values.Length switch
        {
            1 => new DesignerThickness(values[0], values[0], values[0], values[0]),
            2 => new DesignerThickness(values[0], values[1], values[0], values[1]),
            4 => new DesignerThickness(values[0], values[1], values[2], values[3]),
            _ => default
        };
    }

    private static string FormatDesignerThickness(DesignerThickness thickness)
    {
        return string.Join(
            ",",
            FormatDesignerDouble(thickness.Left),
            FormatDesignerDouble(thickness.Top),
            FormatDesignerDouble(thickness.Right),
            FormatDesignerDouble(thickness.Bottom));
    }

    private static string FormatDesignerDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct DesignerThickness(double Left, double Top, double Right, double Bottom);

    private sealed record ThemeEditScope(
        InMemoryProjectFile OwnerFile,
        string? SelectionFilePath,
        int SelectionStart,
        int SelectionLength,
        int CaretOffset,
        string ThemeKey);

    private static IReadOnlyDictionary<string, string>? CreateToolboxInsertionProperties(
        Control targetParent,
        Point? canvasPosition)
    {
        if (targetParent is not Canvas || canvasPosition is not { } point)
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["Canvas.Left"] = FormatDesignerDouble(Math.Max(0, point.X)),
            ["Canvas.Top"] = FormatDesignerDouble(Math.Max(0, point.Y))
        };
    }

    private void InsertSelectedToolboxItem()
    {
        if (ActiveXamlFile is not { } xamlFile ||
            SelectedVisualEditorToolboxItem is not { } item)
        {
            return;
        }

        var parentSelector = _visualEditorCurrentContainerSelector ??
                             _visualEditorSelectedSelector ??
                             XamlElementSelector.ByPath();
        var insertion = _visualToolboxInsertion.Insert(xamlFile.Text, parentSelector, item);
        ApplyVisualEditorMutation(insertion.Mutation);
    }

    private void WrapVisualEditorSelection()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.WrapElement(
            xamlFile.Text,
            selector,
            "<Border Padding=\"8\" />"));
    }

    private void UnwrapVisualEditorSelection()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.UnwrapElement(xamlFile.Text, selector));
    }

    private void LoadFluentControlThemeTemplates()
    {
        FluentControlThemeTemplates = new ObservableCollection<FluentControlThemeTemplateViewModel>(
            _controlThemeCatalog.Templates
                .Select(static template => new FluentControlThemeTemplateViewModel(
                    template.Key,
                    FluentControlThemeCatalog.GetLocalName(template.TargetType),
                    template.SourcePath)));
        RefreshControlThemeFilters();

        ControlThemeSourceStatus = _controlThemeCatalog.SourceRoot is null
            ? "Fluent theme source not found. Initialize external/Avalonia or set XAML_PLAYGROUND_AVALONIA_FLUENT_THEME_PATH."
            : $"Fluent source: {_controlThemeCatalog.SourceRoot}";
    }

    private void RefreshControlThemes()
    {
        var previousKey = SelectedControlTheme?.Key;
        var themes = ActiveProject is null
            ? Array.Empty<ControlThemeDefinition>()
            : ControlThemeResourceBuilder.FindCustomThemes(
                ActiveProject
                    .GetXamlFiles()
                    .Where(static file => file.Kind == ProjectFileKind.Resource)
                    .Select(static file => (file.Path, file.Text)));

        ControlThemes = new ObservableCollection<ControlThemeDefinitionViewModel>(
            themes.Select(static theme => new ControlThemeDefinitionViewModel(
                theme.Key,
                theme.TargetType,
                theme.FilePath)));

        RefreshThemeResourceAnalysis();
        RefreshControlThemeFilters(previousKey);
        NotifyControlThemeCommandsChanged();
    }

    private void RefreshThemeResourceAnalysis()
    {
        var previousKey = SelectedThemeResource?.Key;
        _themeResourceAnalysis = ActiveProject is null
            ? ThemeResourceAnalysis.Empty
            : ResourceDictionaryAnalyzer.Analyze(
                ActiveProject.GetXamlFiles()
                    .Select(static file => new ThemeResourceDocument(
                        file.Path,
                        file.Text,
                        file.Kind == ProjectFileKind.Resource)));

        ThemeResources = new ObservableCollection<ThemeResourceViewModel>(
            _themeResourceAnalysis.Resources.Select(static resource => new ThemeResourceViewModel(
                resource.Key,
                resource.ResourceType,
                resource.TargetType,
                resource.FilePath,
                resource.Line)));

        ThemeResourceDiagnostics = new ObservableCollection<ThemeResourceDiagnosticViewModel>(
            _themeResourceAnalysis.Diagnostics.Select(static diagnostic => new ThemeResourceDiagnosticViewModel(
                diagnostic.Severity.ToString(),
                diagnostic.Message,
                diagnostic.FilePath,
                diagnostic.Line)));

        RefreshThemeResourceFilters();
        SelectedThemeResource =
            FilteredThemeResources.FirstOrDefault(resource => string.Equals(resource.Key, previousKey, StringComparison.Ordinal)) ??
            FilteredThemeResources.FirstOrDefault();
        RefreshSelectedThemeResourceUsages();
    }

    private void RefreshSelectedThemeResourceUsages()
    {
        var selectedKey = SelectedThemeResource?.Key;
        SelectedThemeResourceUsages = string.IsNullOrWhiteSpace(selectedKey)
            ? new ObservableCollection<ThemeResourceUsageViewModel>()
            : new ObservableCollection<ThemeResourceUsageViewModel>(
                _themeResourceAnalysis.References
                    .Where(reference => string.Equals(reference.Key, selectedKey, StringComparison.Ordinal))
                    .Select(static reference => new ThemeResourceUsageViewModel(
                        reference.Key,
                        reference.Kind.ToString(),
                        reference.FilePath,
                        reference.Line,
                        reference.Snippet)));
    }

    private void RefreshThemeResourceFilters()
    {
        var query = ControlThemeSearchText?.Trim();
        var resources = ThemeResources.AsEnumerable();
        var diagnostics = ThemeResourceDiagnostics.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            resources = resources.Where(resource => MatchesThemeResourceSearch(resource, query));
            diagnostics = diagnostics.Where(diagnostic => MatchesThemeResourceSearch(diagnostic, query));
        }

        FilteredThemeResources = new ObservableCollection<ThemeResourceViewModel>(resources);
        FilteredThemeResourceDiagnostics = new ObservableCollection<ThemeResourceDiagnosticViewModel>(diagnostics);
    }

    private void RefreshControlThemeFilters(string? preferredThemeKey = null)
    {
        var query = ControlThemeSearchText?.Trim();
        var customThemes = ControlThemes.AsEnumerable();
        var fluentTemplates = FluentControlThemeTemplates.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            customThemes = customThemes.Where(theme => MatchesControlThemeSearch(theme, query));
            fluentTemplates = fluentTemplates.Where(template => MatchesControlThemeSearch(template, query));
        }

        FilteredControlThemes = new ObservableCollection<ControlThemeDefinitionViewModel>(customThemes);
        FilteredFluentControlThemeTemplates = new ObservableCollection<FluentControlThemeTemplateViewModel>(fluentTemplates);
        RefreshThemeResourceFilters();

        SelectedControlTheme = SelectFilteredControlTheme(preferredThemeKey ?? SelectedControlTheme?.Key);
        if (SelectedThemeResource is null ||
            !FilteredThemeResources.Any(resource => ReferenceEquals(resource, SelectedThemeResource)))
        {
            SelectedThemeResource = FilteredThemeResources.FirstOrDefault();
        }

        UpdateControlThemeStatus(query);

        NotifyControlThemeCommandsChanged();
    }

    private ControlThemeDefinitionViewModel? SelectFilteredControlTheme(string? preferredThemeKey)
    {
        if (!string.IsNullOrWhiteSpace(preferredThemeKey))
        {
            var preferredTheme = FilteredControlThemes.FirstOrDefault(theme =>
                string.Equals(theme.Key, preferredThemeKey, StringComparison.Ordinal));
            if (preferredTheme is not null)
            {
                return preferredTheme;
            }
        }

        return GetControlThemesForSelectedVisualElement(FilteredControlThemes).FirstOrDefault() ??
               FilteredControlThemes.FirstOrDefault();
    }

    private void UpdateControlThemeStatus(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ControlThemeStatus = ControlThemes.Count == 0
                ? "No custom control themes."
                : $"{ControlThemes.Count} custom control theme(s).";
            return;
        }

        ControlThemeStatus =
            $"{FilteredControlThemes.Count} of {ControlThemes.Count} custom, " +
            $"{FilteredFluentControlThemeTemplates.Count} of {FluentControlThemeTemplates.Count} Fluent source template(s).";
    }

    private static bool MatchesControlThemeSearch(ControlThemeDefinitionViewModel theme, string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                theme.Key.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                theme.TargetType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                theme.FilePath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                theme.Title.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesControlThemeSearch(FluentControlThemeTemplateViewModel template, string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                template.Key.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                template.TargetType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                template.SourcePath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                template.Title.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesThemeResourceSearch(ThemeResourceViewModel resource, string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                resource.Key.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                resource.ResourceType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                resource.TargetType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                resource.FilePath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                resource.Title.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesThemeResourceSearch(ThemeResourceDiagnosticViewModel diagnostic, string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token =>
                diagnostic.Severity.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Message.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                diagnostic.FilePath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Title.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ControlThemeDefinitionViewModel> GetControlThemesForSelectedVisualElement()
    {
        return GetControlThemesForSelectedVisualElement(ControlThemes);
    }

    private IReadOnlyList<ControlThemeDefinitionViewModel> GetControlThemesForSelectedVisualElement(
        IEnumerable<ControlThemeDefinitionViewModel> themes)
    {
        return TryGetSelectedControlThemeTargetType(out var targetType)
            ? themes
                .Where(theme => string.Equals(theme.TargetType, targetType, StringComparison.Ordinal))
                .ToArray()
            : Array.Empty<ControlThemeDefinitionViewModel>();
    }

    private void RefreshControlThemeSelectionState()
    {
        if (TryGetSelectedControlThemeTargetType(out var targetType))
        {
            ControlThemeSelectedTargetType = targetType;
            if (SelectedControlTheme is null ||
                !FilteredControlThemes.Contains(SelectedControlTheme) ||
                !string.Equals(SelectedControlTheme.TargetType, targetType, StringComparison.Ordinal))
            {
                SelectedControlTheme = FilteredControlThemes.FirstOrDefault(theme =>
                    string.Equals(theme.TargetType, targetType, StringComparison.Ordinal));
            }
        }
        else
        {
            ControlThemeSelectedTargetType = "No control selected.";
        }

        NotifyControlThemeCommandsChanged();
    }

    private bool TryGetSelectedControlThemeTargetType([NotNullWhen(true)] out string? targetType)
    {
        targetType = null;
        var element = FindElement(_visualEditorDocument, _visualEditorSelectedSelector);
        if (element is null)
        {
            return false;
        }

        var localName = GetLocalName(element.TypeName);
        if (ResolveControlType(localName) is null)
        {
            return false;
        }

        targetType = localName;
        return true;
    }

    private void RefreshSelectedResourceThemeAnalysis(ThemeResourceViewModel? resource)
    {
        if (resource is { ResourceType: "ControlTheme" } &&
            ControlThemes.FirstOrDefault(theme =>
                string.Equals(theme.Key, resource.Key, StringComparison.Ordinal) &&
                string.Equals(theme.FilePath, resource.FilePath, StringComparison.OrdinalIgnoreCase)) is { } theme)
        {
            SelectedControlTheme = theme;
            return;
        }

        _selectedControlThemeAnalysis = ControlThemeAnalysis.Empty;
        ThemePreviewStates = new ObservableCollection<ThemePreviewStateViewModel>();
        SelectedThemePreviewState = null;
        ThemeStateSelectors = new ObservableCollection<ThemeStateSelectorViewModel>();
        ThemeTemplateParts = new ObservableCollection<ThemeTemplatePartViewModel>();
        ThemeTemplateBindings = new ObservableCollection<ThemeTemplateBindingViewModel>();
    }

    private void RefreshSelectedControlThemeAnalysis()
    {
        if (ActiveProject is null ||
            SelectedControlTheme is not { } selectedTheme ||
            ActiveProject.FindFile(selectedTheme.FilePath) is not { } themeFile)
        {
            _selectedControlThemeAnalysis = ControlThemeAnalysis.Empty;
            ThemePreviewStates = new ObservableCollection<ThemePreviewStateViewModel>();
            SelectedThemePreviewState = null;
            ThemeStateSelectors = new ObservableCollection<ThemeStateSelectorViewModel>();
            ThemeTemplateParts = new ObservableCollection<ThemeTemplatePartViewModel>();
            ThemeTemplateBindings = new ObservableCollection<ThemeTemplateBindingViewModel>();
            return;
        }

        var previousState = SelectedThemePreviewState?.State;
        _selectedControlThemeAnalysis = ControlThemeAnalyzer.Analyze(themeFile.Text, selectedTheme.Key);
        var stateSelectorKeys = _selectedControlThemeAnalysis.StateSelectors
            .Select(static selector => selector.State)
            .ToHashSet(StringComparer.Ordinal);
        ThemePreviewStates = new ObservableCollection<ThemePreviewStateViewModel>(
            _selectedControlThemeAnalysis.AvailableStates.Select(state => new ThemePreviewStateViewModel(
                state,
                stateSelectorKeys.Contains(state))));
        ThemeTemplateParts = new ObservableCollection<ThemeTemplatePartViewModel>(
            _selectedControlThemeAnalysis.Parts.Select(static part => new ThemeTemplatePartViewModel(
                part.Name,
                part.Type,
                part.Line)));
        ThemeTemplateBindings = new ObservableCollection<ThemeTemplateBindingViewModel>(
            _selectedControlThemeAnalysis.TemplateBindings.Select(static binding => new ThemeTemplateBindingViewModel(
                binding.Property,
                binding.Line,
                binding.Snippet)));
        SelectedThemePreviewState =
            ThemePreviewStates.FirstOrDefault(state => string.Equals(state.State, previousState, StringComparison.Ordinal)) ??
            ThemePreviewStates.FirstOrDefault(state => string.Equals(state.State, "normal", StringComparison.Ordinal)) ??
            ThemePreviewStates.FirstOrDefault();
        RefreshThemeStateSelectors();
    }

    private void RefreshThemeStateSelectors()
    {
        var state = SelectedThemePreviewState?.State;
        ThemeStateSelectors = string.IsNullOrWhiteSpace(state) ||
                              string.Equals(state, "normal", StringComparison.Ordinal)
            ? new ObservableCollection<ThemeStateSelectorViewModel>()
            : new ObservableCollection<ThemeStateSelectorViewModel>(
                _selectedControlThemeAnalysis.StateSelectors
                    .Where(selector => string.Equals(selector.State, state, StringComparison.Ordinal))
                    .Select(static selector => new ThemeStateSelectorViewModel(
                        selector.State,
                        selector.Selector,
                        selector.Line)));
    }

    private bool CanCreateCustomControlTheme()
    {
        return ActiveProject is not null &&
               TryGetSelectedControlThemeTargetType(out var targetType) &&
               _controlThemeCatalog.FindDefaultTemplate(targetType) is not null;
    }

    private void CreateCustomControlTheme()
    {
        if (ActiveProject is not { } project ||
            !TryGetSelectedControlThemeTargetType(out var targetType))
        {
            ControlThemeStatus = "Select a preview control first.";
            return;
        }

        var template = _controlThemeCatalog.FindDefaultTemplate(targetType);
        if (template is null)
        {
            ControlThemeStatus = $"No Fluent ControlTheme template found for {targetType}.";
            return;
        }

        var themeKey = CreateUniqueControlThemeKey(project, targetType);
        var xaml = ControlThemeResourceBuilder.CreateResourceDictionary(template, themeKey);
        var themeFile = _solutionFactory.AddControlThemeResource(project, themeKey, xaml);
        var ownerFile = ActiveXamlFile;

        SolutionExplorerNodes = Solution is { } solution
            ? BuildSolutionExplorer(solution)
            : new ObservableCollection<SolutionExplorerNodeViewModel>();
        RefreshControlThemes();

        var createdTheme = ControlThemes.FirstOrDefault(theme =>
            string.Equals(theme.Key, themeKey, StringComparison.Ordinal));
        if (createdTheme is not null)
        {
            SelectedControlTheme = createdTheme;
            ApplyControlTheme(createdTheme);
        }

        if (ownerFile is not null)
        {
            EnterThemeEditScope(ownerFile, themeKey);
        }

        OpenWorkspaceFile(themeFile);
        ControlThemeStatus = $"Created {themeKey} from Fluent {targetType}.";
    }

    private string CreateUniqueControlThemeKey(InMemoryProject project, string targetType)
    {
        var index = 1;
        string key;
        do
        {
            key = $"My{targetType}Theme{index}";
            index++;
        }
        while (ControlThemes.Any(theme => string.Equals(theme.Key, key, StringComparison.Ordinal)) ||
               project.FindFile($"Themes/{key}.axaml") is not null);

        return key;
    }

    private bool CanApplyControlTheme(ControlThemeDefinitionViewModel? theme)
    {
        theme ??= SelectedControlTheme;
        return theme is not null &&
               ActiveXamlFile is { Kind: ProjectFileKind.Xaml } &&
               TryGetSelectedControlThemeTargetType(out var targetType) &&
               string.Equals(theme.TargetType, targetType, StringComparison.Ordinal);
    }

    private void ApplyControlTheme(ControlThemeDefinitionViewModel? theme)
    {
        theme ??= SelectedControlTheme;
        if (theme is null ||
            !TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            ControlThemeStatus = "Select a control and a theme first.";
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.SetProperty(
            xamlFile.Text,
            selector,
            "Theme",
            $"{{StaticResource {theme.Key}}}"));
        VisualEditorPropertyName = "Theme";
        VisualEditorPropertyValue = $"{{StaticResource {theme.Key}}}";
        ControlThemeStatus = $"Applied {theme.Key}.";
    }

    private bool CanRemoveControlTheme()
    {
        return ActiveXamlFile is { Kind: ProjectFileKind.Xaml } &&
               TryGetSelectedControlThemeTargetType(out _);
    }

    private void RemoveControlTheme()
    {
        if (!TryGetVisualEditingContext(out var xamlFile, out var selector))
        {
            ControlThemeStatus = "Select a control first.";
            return;
        }

        ApplyVisualEditorMutation(_visualMutationEngine.RemoveProperty(
            xamlFile.Text,
            selector,
            "Theme"));
        ControlThemeStatus = "Restored default theme.";
    }

    private void OpenSelectedControlTheme()
    {
        if (ActiveProject is null || SelectedControlTheme is null)
        {
            return;
        }

        var file = ActiveProject.FindFile(SelectedControlTheme.FilePath);
        if (file is not null)
        {
            if (ActiveXamlFile is { } ownerFile &&
                !ReferenceEquals(ownerFile, file))
            {
                EnterThemeEditScope(ownerFile, SelectedControlTheme.Key);
            }

            OpenWorkspaceFile(file);
        }
    }

    private void OpenSelectedThemeResource()
    {
        if (ActiveProject is null || SelectedThemeResource is null)
        {
            return;
        }

        var file = ActiveProject.FindFile(SelectedThemeResource.FilePath);
        if (file is null)
        {
            return;
        }

        if (SelectedThemeResource.ResourceType == "ControlTheme" &&
            ActiveXamlFile is { } ownerFile &&
            !ReferenceEquals(ownerFile, file))
        {
            EnterThemeEditScope(ownerFile, SelectedThemeResource.Key);
        }

        OpenWorkspaceFile(file);
    }

    private bool CanRenameSelectedThemeResource()
    {
        var newKey = ThemeResourceKeyEditText.Trim();
        return SelectedThemeResource is { } selectedResource &&
               !string.IsNullOrWhiteSpace(newKey) &&
               !string.Equals(selectedResource.Key, newKey, StringComparison.Ordinal) &&
               !ThemeResources.Any(resource =>
                   !ReferenceEquals(resource, selectedResource) &&
                   string.Equals(resource.Key, newKey, StringComparison.Ordinal));
    }

    private void RenameSelectedThemeResource()
    {
        if (ActiveProject is not { } project ||
            SelectedThemeResource is not { } selectedResource ||
            project.FindFile(selectedResource.FilePath) is not { } resourceFile)
        {
            return;
        }

        var newKey = ThemeResourceKeyEditText.Trim();
        var rename = ThemeResourceEditor.RenameResourceKey(resourceFile.Text, selectedResource.Key, newKey);
        if (!rename.Changed)
        {
            ControlThemeStatus = rename.Error ?? $"Could not rename {selectedResource.Key}.";
            return;
        }

        resourceFile.Text = rename.Text;
        foreach (var file in project.GetXamlFiles())
        {
            file.Text = ThemeResourceEditor.RenameResourceReferences(file.Text, selectedResource.Key, newKey);
        }

        RefreshWorkspaceAfterThemeFileChanges(resourceFile);
        SelectThemeResource(newKey);
        ControlThemeStatus = $"Renamed {selectedResource.Key} to {newKey}.";
    }

    private void DuplicateSelectedThemeResource()
    {
        if (ActiveProject is not { } project ||
            SelectedThemeResource is not { } selectedResource ||
            project.FindFile(selectedResource.FilePath) is not { } resourceFile)
        {
            return;
        }

        var duplicateKey = CreateUniqueThemeResourceKey(selectedResource.Key);
        var duplicate = ThemeResourceEditor.DuplicateResource(resourceFile.Text, selectedResource.Key, duplicateKey);
        if (!duplicate.Changed)
        {
            ControlThemeStatus = duplicate.Error ?? $"Could not duplicate {selectedResource.Key}.";
            return;
        }

        resourceFile.Text = duplicate.Text;
        RefreshWorkspaceAfterThemeFileChanges(resourceFile);
        SelectThemeResource(duplicateKey);
        ControlThemeStatus = $"Duplicated {selectedResource.Key} as {duplicateKey}.";
    }

    private void DeleteSelectedThemeResource()
    {
        if (ActiveProject is not { } project ||
            SelectedThemeResource is not { } selectedResource ||
            project.FindFile(selectedResource.FilePath) is not { } resourceFile)
        {
            return;
        }

        var usages = _themeResourceAnalysis.References
            .Where(reference => string.Equals(reference.Key, selectedResource.Key, StringComparison.Ordinal))
            .ToArray();
        if (usages.Length > 0)
        {
            ControlThemeStatus = $"Cannot delete {selectedResource.Key}; it has {usages.Length} usage(s).";
            return;
        }

        var delete = ThemeResourceEditor.DeleteResource(resourceFile.Text, selectedResource.Key);
        if (!delete.Changed)
        {
            ControlThemeStatus = delete.Error ?? $"Could not delete {selectedResource.Key}.";
            return;
        }

        if (delete.RemovedLastResource &&
            resourceFile.Path.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase))
        {
            project.Files.Remove(resourceFile);
            if (ReferenceEquals(ActiveXamlFile, resourceFile))
            {
                ActiveXamlFile = project.GetXamlFiles().FirstOrDefault(file => file.Kind == ProjectFileKind.Xaml) ??
                                 project.GetXamlFiles().FirstOrDefault();
                RefreshVisualEditingModel(updateSourceSelection: false);
            }

            RefreshWorkspaceAfterThemeFileChanges(null);
        }
        else
        {
            resourceFile.Text = delete.Text;
            RefreshWorkspaceAfterThemeFileChanges(resourceFile);
        }

        ControlThemeStatus = $"Deleted {selectedResource.Key}.";
    }

    private bool CanApplySelectedThemeResource()
    {
        return SelectedThemeResource is { ResourceType: "ControlTheme" } selectedResource &&
               ControlThemes.FirstOrDefault(theme =>
                   string.Equals(theme.Key, selectedResource.Key, StringComparison.Ordinal) &&
                   string.Equals(theme.FilePath, selectedResource.FilePath, StringComparison.OrdinalIgnoreCase)) is { } theme &&
               CanApplyControlTheme(theme);
    }

    private void ApplySelectedThemeResource()
    {
        if (SelectedThemeResource is not { ResourceType: "ControlTheme" } selectedResource)
        {
            ControlThemeStatus = "Select a ControlTheme resource first.";
            return;
        }

        var theme = ControlThemes.FirstOrDefault(theme =>
            string.Equals(theme.Key, selectedResource.Key, StringComparison.Ordinal) &&
            string.Equals(theme.FilePath, selectedResource.FilePath, StringComparison.OrdinalIgnoreCase));
        if (theme is null)
        {
            ControlThemeStatus = $"{selectedResource.Key} is not a custom ControlTheme.";
            return;
        }

        ApplyControlTheme(theme);
    }

    private void SelectThemeResource(string key)
    {
        SelectedThemeResource =
            FilteredThemeResources.FirstOrDefault(resource => string.Equals(resource.Key, key, StringComparison.Ordinal)) ??
            ThemeResources.FirstOrDefault(resource => string.Equals(resource.Key, key, StringComparison.Ordinal));
    }

    private string CreateUniqueThemeResourceKey(string sourceKey)
    {
        var baseKey = sourceKey.EndsWith("Copy", StringComparison.Ordinal)
            ? sourceKey
            : $"{sourceKey}Copy";
        var key = baseKey;
        var index = 2;
        while (ThemeResources.Any(resource => string.Equals(resource.Key, key, StringComparison.Ordinal)))
        {
            key = $"{baseKey}{index}";
            index++;
        }

        return key;
    }

    private void EnterThemeEditScope(InMemoryProjectFile ownerFile, string themeKey)
    {
        _themeEditScope = new ThemeEditScope(
            ownerFile,
            VisualEditorSourceSelectionFilePath,
            VisualEditorSourceSelectionStart,
            VisualEditorSourceSelectionLength,
            VisualEditorSourceSelectionStart,
            themeKey);
        IsThemeEditScopeActive = true;
        ThemeEditScopeBreadcrumb = $"{ownerFile.Path} > {themeKey}";
        (ReturnFromThemeEditScopeCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ReturnFromThemeEditScope()
    {
        if (_themeEditScope is not { } scope)
        {
            return;
        }

        _themeEditScope = null;
        IsThemeEditScopeActive = false;
        ThemeEditScopeBreadcrumb = string.Empty;
        OpenWorkspaceFile(scope.OwnerFile);
        if (!string.IsNullOrWhiteSpace(scope.SelectionFilePath))
        {
            SelectVisualEditorSourceRange(
                scope.SelectionFilePath,
                scope.SelectionStart,
                scope.SelectionLength,
                scope.CaretOffset);
        }

        (ReturnFromThemeEditScopeCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private async Task ImportControlThemeFiles()
    {
        if (ActiveProject is not { } project || StorageProvider is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import theme file",
            FileTypeFilter = GetThemeResourceFileTypes(),
            AllowMultiple = true
        });

        if (files.Count == 0)
        {
            return;
        }

        var importedFiles = new List<InMemoryProjectFile>();
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var xaml = await reader.ReadToEndAsync();
                importedFiles.Add(_solutionFactory.AddImportedThemeResource(project, file.Name, xaml));
            }
            catch (Exception exception)
            {
                ControlThemeStatus = $"Failed to import {file.Name}: {exception.Message}";
                return;
            }
        }

        RefreshWorkspaceAfterThemeFileChanges(importedFiles.FirstOrDefault());
        var importedThemeCount = ControlThemeResourceBuilder.FindCustomThemes(
                importedFiles.Select(static file => (file.Path, file.Text)))
            .Count;
        ControlThemeStatus = importedThemeCount == 0
            ? $"Imported {importedFiles.Count} resource file(s). No custom ControlTheme was found."
            : $"Imported {importedThemeCount} control theme(s) from {importedFiles.Count} file(s).";
    }

    private async Task ExportSelectedControlTheme()
    {
        if (ActiveProject is null ||
            SelectedControlTheme is not { } selectedTheme ||
            ActiveProject.FindFile(selectedTheme.FilePath) is not { } themeFile ||
            StorageProvider is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export theme file",
            FileTypeChoices = GetThemeResourceFileTypes(),
            SuggestedFileName = themeFile.Name,
            DefaultExtension = themeFile.Extension.TrimStart('.') is { Length: > 0 } extension ? extension : "axaml",
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(themeFile.Text);
            ControlThemeStatus = $"Exported {selectedTheme.Key}.";
        }
        catch (Exception exception)
        {
            ControlThemeStatus = $"Failed to export {selectedTheme.Key}: {exception.Message}";
        }
    }

    private bool CanSaveControlThemeProject()
    {
        return ActiveProject is not null && GetControlThemeProjectFiles().Count > 0;
    }

    private async Task SaveControlThemeProject()
    {
        if (ActiveProject is not { } project || StorageProvider is null)
        {
            return;
        }

        var themeFiles = GetControlThemeProjectFiles();
        if (themeFiles.Count == 0)
        {
            ControlThemeStatus = "No custom theme files to save.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save theme project",
            FileTypeChoices = GetThemeProjectFileTypes(),
            SuggestedFileName = $"{project.Name}.xamltheme",
            DefaultExtension = "xamltheme",
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return;
        }

        try
        {
            var json = ThemeProjectStorage.Save(
                project.Name,
                themeFiles.Select(static themeFile => (themeFile.Path, themeFile.Text)));
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            ControlThemeStatus = $"Saved {themeFiles.Count} theme file(s) to theme project.";
        }
        catch (Exception exception)
        {
            ControlThemeStatus = $"Failed to save theme project: {exception.Message}";
        }
    }

    private async Task LoadControlThemeProject()
    {
        if (ActiveProject is not { } project || StorageProvider is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load theme project",
            FileTypeFilter = GetThemeProjectFileTypes(),
            AllowMultiple = false
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var themeProject = ThemeProjectStorage.Load(json);
            var loadedFiles = themeProject.Files
                .Select(themeFile => _solutionFactory.AddOrUpdateResource(project, themeFile.Path, themeFile.Text))
                .ToArray();

            RefreshWorkspaceAfterThemeFileChanges(loadedFiles.FirstOrDefault());
            ControlThemeStatus = $"Loaded {loadedFiles.Length} theme file(s) from {themeProject.Name}.";
        }
        catch (Exception exception)
        {
            ControlThemeStatus = $"Failed to load theme project: {exception.Message}";
        }
    }

    private IReadOnlyList<InMemoryProjectFile> GetControlThemeProjectFiles()
    {
        if (ActiveProject is not { } project)
        {
            return Array.Empty<InMemoryProjectFile>();
        }

        var customThemeFilePaths = ControlThemeResourceBuilder.FindCustomThemes(
                project.GetXamlFiles()
                    .Where(static file => file.Kind == ProjectFileKind.Resource)
                    .Select(static file => (file.Path, file.Text)))
            .Select(static theme => theme.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return project.GetXamlFiles()
            .Where(static file => file.Kind == ProjectFileKind.Resource)
            .Where(file =>
                file.Path.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase) ||
                customThemeFilePaths.Contains(file.Path))
            .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RefreshWorkspaceAfterThemeFileChanges(InMemoryProjectFile? fileToOpen)
    {
        SolutionExplorerNodes = Solution is { } solution
            ? BuildSolutionExplorer(solution)
            : new ObservableCollection<SolutionExplorerNodeViewModel>();
        RefreshControlThemes();

        if (fileToOpen is not null)
        {
            OpenWorkspaceFile(fileToOpen);
        }
        else if (CanPreviewXamlFile(ActiveXamlFile))
        {
            RunActiveDocument();
        }
    }

    private void NotifyControlThemeCommandsChanged()
    {
        (CreateCustomControlThemeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyControlThemeCommand as RelayCommand<ControlThemeDefinitionViewModel?>)?.NotifyCanExecuteChanged();
        (RemoveControlThemeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedControlThemeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedThemeResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RenameSelectedThemeResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (DuplicateSelectedThemeResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (DeleteSelectedThemeResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplySelectedThemeResourceCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ReturnFromThemeEditScopeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ImportControlThemeFilesCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ExportSelectedControlThemeCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (SaveControlThemeProjectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (LoadControlThemeProjectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool TryGetVisualEditingContext(
        out InMemoryProjectFile xamlFile,
        out XamlElementSelector selector)
    {
        xamlFile = ActiveXamlFile!;
        selector = _visualEditorSelectedSelector!;

        if (ActiveXamlFile is null || _visualEditorSelectedSelector is null)
        {
            VisualEditorStatus = "Select a XAML element first.";
            return false;
        }

        return true;
    }

    private bool TryGetSelectedVisualEditorElement(out XamlElementSnapshot element)
    {
        element = null!;

        if (_visualEditorDocument is null || _visualEditorSelectedSelector is null)
        {
            VisualEditorStatus = "Select a XAML element first.";
            return false;
        }

        var selected = _visualEditorDocument.Elements.FirstOrDefault(candidate =>
            Matches(candidate, _visualEditorSelectedSelector));
        if (selected is null)
        {
            VisualEditorStatus = "The selected XAML element is no longer available.";
            return false;
        }

        element = selected;
        return true;
    }

    private void ApplyVisualEditorMutation(XamlMutationResult result)
    {
        if (ActiveXamlFile is null)
        {
            return;
        }

        if (!result.Success)
        {
            VisualEditorStatus = string.Join(Environment.NewLine, result.Diagnostics);
            LastErrorMessage = VisualEditorStatus;
            return;
        }

        var selectedSelector = _visualEditorSelectedSelector;
        try
        {
            _isApplyingVisualEditorMutation = true;
            ActiveXamlFile.Text = result.Text;
            _visualEditorDocument = result.Snapshot;
            _visualEditorSelectedSelector = selectedSelector;
            RefreshVisualEditingModel();
        }
        finally
        {
            _isApplyingVisualEditorMutation = false;
        }
    }

    private void LoadVisualEditorToolbox()
    {
        var catalog = new ToolboxCatalogBuilder().Build(new ToolboxContext(new[]
        {
            typeof(Control).Assembly,
            typeof(MainViewModel).Assembly
        }));

        VisualEditorToolboxItems = new ObservableCollection<ToolboxItemDescriptor>(catalog.Items);
        FilterVisualEditorToolbox();
        SelectedVisualEditorToolboxItem = FilteredVisualEditorToolboxItems.FirstOrDefault(item =>
            string.Equals(item.TypeName, "Button", StringComparison.Ordinal));
    }

    private void FilterVisualEditorToolbox()
    {
        var query = VisualEditorToolboxSearch?.Trim();
        var items = VisualEditorToolboxItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(item =>
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredVisualEditorToolboxItems = new ObservableCollection<ToolboxItemDescriptor>(items);
    }

    private static IReadOnlyList<VisualEditorNodeViewModel> BuildVisualEditorNodes(XamlDocumentSnapshot document)
    {
        var nodes = document.Elements
            .Select(static element => new VisualEditorNodeViewModel(element))
            .ToDictionary(static node => GetPathKey(node.Element.Path), StringComparer.Ordinal);
        var roots = new List<VisualEditorNodeViewModel>();

        foreach (var node in nodes.Values.OrderBy(static node => node.Element.Path.Count))
        {
            if (node.Element.Path.Count == 0)
            {
                roots.Add(node);
                continue;
            }

            var parentPath = node.Element.Path.Take(node.Element.Path.Count - 1).ToArray();
            if (nodes.TryGetValue(GetPathKey(parentPath), out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The visual editor structure pane uses ProDataGrid's hierarchical model for design-time XAML metadata.")]
    private static HierarchicalModel<VisualEditorNodeViewModel> CreateVisualEditorStructureModel(
        ObservableCollection<VisualEditorNodeViewModel> roots)
    {
        var model = new HierarchicalModel<VisualEditorNodeViewModel>(
            new HierarchicalOptions<VisualEditorNodeViewModel>
            {
                ChildrenSelector = static node => node.Children,
                IsLeafSelector = static node => node.Children.Count == 0,
                AutoExpandRoot = true,
                MaxAutoExpandDepth = null,
                VirtualizeChildren = false,
                ItemPathSelector = static node => node.Element.Path
            });

        model.SetRoots(roots);
        return model;
    }

    private static IReadOnlyList<VisualEditorStructureRowViewModel> BuildVisualEditorStructureRows(
        IReadOnlyList<VisualEditorNodeViewModel> roots)
    {
        var rows = new List<VisualEditorStructureRowViewModel>();
        foreach (var root in roots)
        {
            AddVisualEditorStructureRows(root, 0, rows);
        }

        return rows;
    }

    private static void AddVisualEditorStructureRows(
        VisualEditorNodeViewModel node,
        int depth,
        ICollection<VisualEditorStructureRowViewModel> rows)
    {
        rows.Add(new VisualEditorStructureRowViewModel(node, depth));

        foreach (var child in node.Children)
        {
            AddVisualEditorStructureRows(child, depth + 1, rows);
        }
    }

    private static VisualEditorNodeViewModel? FindNode(
        IEnumerable<VisualEditorNodeViewModel> roots,
        XamlElementSelector? selector)
    {
        if (selector is null)
        {
            return null;
        }

        foreach (var root in roots)
        {
            if (Matches(root.Element, selector))
            {
                return root;
            }

            var child = FindNode(root.Children, selector);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static bool Matches(XamlElementSnapshot element, XamlElementSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            return string.Equals(element.Name, selector.Name, StringComparison.Ordinal);
        }

        if (selector.Path is { } path)
        {
            return GetPathKey(element.Path) == GetPathKey(path);
        }

        return !string.IsNullOrWhiteSpace(selector.TypeName) &&
               string.Equals(element.TypeName, selector.TypeName, StringComparison.Ordinal);
    }

    private static XamlElementSnapshot? FindElement(
        XamlDocumentSnapshot? document,
        XamlElementSelector? selector)
    {
        return document is null || selector is null
            ? null
            : document.Elements.FirstOrDefault(element => Matches(element, selector));
    }

    private static string GetPathKey(IEnumerable<int> path)
    {
        return string.Join("/", path);
    }
}
