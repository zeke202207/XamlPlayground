using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Octokit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Styling;
using Dock.Avalonia.Themes;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.CodeAnalysis;
using XamlPlayground.Services;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.Workspace;
using XamlPlayground.Workspace;
using Avalonia.Threading;

namespace XamlPlayground.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoRunDelay = TimeSpan.FromMilliseconds(300);
    private const string PlaygroundXamlDocument = "Main.axaml";

    [ObservableProperty] private ObservableCollection<SampleViewModel> _samples;
    [ObservableProperty] private SampleViewModel? _currentSample;
    [ObservableProperty] private Control? _control;
    [ObservableProperty] private AvaloniaObject? _diagnosticsRoot;
    [ObservableProperty] private IFactory? _dockFactory;
    [ObservableProperty] private IRootDock? _dockLayout;
    [ObservableProperty] private bool _enableAutoRun;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private string? _lastErrorMessage;
    [ObservableProperty] private int _editorFontSize;
    [ObservableProperty] private InMemorySolution? _solution;
    [ObservableProperty] private ObservableCollection<SolutionExplorerNodeViewModel> _solutionExplorerNodes = new();
    [ObservableProperty] private SolutionExplorerNodeViewModel? _selectedSolutionExplorerNode;
    [ObservableProperty] private NewProjectWizardViewModel _newProjectWizard = new();
    [ObservableProperty] private InMemoryProject? _activeProject;
    [ObservableProperty] private InMemoryProjectFile? _activeXamlFile;
    [ObservableProperty] private InMemoryProjectFile? _activeCodeFile;
    [ObservableProperty] private string _workspaceStatus = "No solution loaded.";
    private readonly IDockThemeManager _dockThemeManager;
    private readonly InMemorySolutionFactory _solutionFactory;
    private bool _update;
    private bool _openingSample;
    private (Assembly? Assembly, AssemblyLoadContext? Context)? _previous;
    private IStorageFile? _openXamlFile;
    private IStorageFile? _openCodeFile;
    private IDisposable? _timer;

    public MainViewModel(string? initialGist)
    {
        _editorFontSize = 14;
        _samples = GetSamples(".xml");
        _enableAutoRun = true;
        _isDarkTheme = IsApplicationDarkTheme();
        _dockThemeManager = new DockFluentThemeManager();
        _solutionFactory = new InMemorySolutionFactory(OnProjectFileChanged);

        NewFileCommand = new RelayCommand(NewFile);
        ShowNewProjectWizardCommand = new RelayCommand(ShowNewProjectWizard);
        CreateProjectCommand = new RelayCommand(CreateProjectFromWizard);
        CancelNewProjectCommand = new RelayCommand(() => NewProjectWizard.IsOpen = false);
        AddUserControlCommand = new RelayCommand(AddUserControl, () => ActiveProject is not null);
        AddResourceDictionaryCommand = new RelayCommand(AddResourceDictionary, () => ActiveProject is not null);
        BuildSolutionCommand = new RelayCommand(RunActiveDocument);
        OpenXamlFileCommand = new AsyncRelayCommand(async () => await OpenXamlFile());
        SaveXamlFileCommand = new AsyncRelayCommand(async () => await SaveXamlFile());
        OpenCodeFileCommand = new AsyncRelayCommand(async () => await OpenCodeFile());
        SaveCodeFileCommand = new AsyncRelayCommand(async () => await SaveCodeFile());
        RunCommand = new RelayCommand(RunActiveDocument);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        GistCommand = new AsyncRelayCommand<string?>(Gist);
        InitializeDockLayout();

        if (!string.IsNullOrEmpty(initialGist))
        {
            _ = Gist(initialGist);
        }
        else
        {
            CurrentSample = Samples.FirstOrDefault(x => x.Name == "Demo");
        }
    }

    public IStorageProvider? StorageProvider { get; set; }
    
    public ICommand RunCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand GistCommand { get; }

    public ICommand NewFileCommand { get; }

    public ICommand ShowNewProjectWizardCommand { get; }

    public ICommand CreateProjectCommand { get; }

    public ICommand CancelNewProjectCommand { get; }

    public ICommand AddUserControlCommand { get; }

    public ICommand AddResourceDictionaryCommand { get; }

    public ICommand BuildSolutionCommand { get; }

    public ICommand OpenXamlFileCommand { get; }

    public ICommand SaveXamlFileCommand { get; }

    public ICommand OpenCodeFileCommand { get; }

    public ICommand SaveCodeFileCommand { get; }

    public bool IsLightTheme => !IsDarkTheme;

    public string ThemeToggleToolTip => IsDarkTheme ? "Switch to light theme" : "Switch to dark theme";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(CurrentSample)
            && CurrentSample is { } sampleViewModel)
        {
            OpenCurrentSample(sampleViewModel);
            return;
        }

        if (e.PropertyName == nameof(ActiveProject))
        {
            (AddUserControlCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (AddResourceDictionaryCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
    }

    partial void OnLastErrorMessageChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            DockFactory is not PlaygroundDockFactory factory)
        {
            return;
        }

        factory.ActivateErrors();
    }

    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyThemeCore();
            return;
        }

        Dispatcher.UIThread.Invoke(ApplyThemeCore);
    }

    private void ApplyThemeCore()
    {
        var themeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = themeVariant;
        }

        if (Avalonia.Application.Current is not XamlPlayground.App)
        {
            return;
        }

        try
        {
            _dockThemeManager.Switch(IsDarkTheme ? 1 : 0);

            var presetIndex = FindDockRiderPresetIndex(IsDarkTheme);
            if (presetIndex >= 0)
            {
                _dockThemeManager.SwitchPreset(presetIndex);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private static bool IsApplicationDarkTheme()
    {
        return Dispatcher.UIThread.CheckAccess()
               && Avalonia.Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
    }

    private int FindDockRiderPresetIndex(bool dark)
    {
        var presetNames = _dockThemeManager.PresetNames;
        var themeName = dark ? "Dark" : "Light";

        for (var i = 0; i < presetNames.Count; i++)
        {
            var name = presetNames[i];
            if (IsRiderPreset(name)
                && name.Contains(themeName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < presetNames.Count; i++)
        {
            if (presetNames[i].Contains(themeName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsRiderPreset(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Contains("Rider", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Xaml, string Code)> GetGistContent(string id)
    {
        var client = new GitHubClient(new ProductHeaderValue("XamlPlayground"));
        var gist = await client.Gist.Get(id);
        var xaml = gist.Files
            .FirstOrDefault(x => string.Compare(x.Key, "Main.axaml", StringComparison.OrdinalIgnoreCase) == 0)
            .Value;
        var code = gist.Files
            .FirstOrDefault(x => string.Compare(x.Key, "Main.axaml.cs", StringComparison.OrdinalIgnoreCase) == 0)
            .Value;
        return (xaml?.Content ?? "", code?.Content ?? "");
    }

    public async Task Gist(string? id)
    {
        if (id is null)
        {
            return;
        }
        try
        {
            var (xaml, code) = await GetGistContent(id);
            var sample = new SampleViewModel("Gist", xaml, code, Open, AutoRun);
            Samples.Insert(0, sample);
            CurrentSample = sample;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private string? GetSampleName(string resourceName)
    {
        var parts = resourceName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[^2]}" : null;
    }

    private string? LoadResourceString(string name)
    {
        var assembly = typeof(MainViewModel).Assembly;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ObservableCollection<SampleViewModel> GetSamples(string sampleExtension)
    {
        var samples = new ObservableCollection<SampleViewModel>();
        var assembly = typeof(MainViewModel).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        samples.Add(new SampleViewModel("Code", Templates.s_xaml, Templates.s_code, Open, AutoRun));

        foreach (var resourceName in resourceNames.OrderBy(GetSampleName, StringComparer.Ordinal))
        {
            if (!resourceName.EndsWith(sampleExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (LoadResourceString(resourceName) is { } xaml)
            {
                if (GetSampleName(resourceName) is { } name)
                {
                    samples.Add(new SampleViewModel(name, xaml, string.Empty, Open, AutoRun));
                }
            }
        }

        return samples;
    }

    private void InitializeDockLayout()
    {
        var factory = new PlaygroundDockFactory(this);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        DockFactory = factory;
        DockLayout = layout;
    }

    private void LoadSolution(InMemorySolution solution)
    {
        Solution = solution;
        ActiveProject = solution.Projects.FirstOrDefault();
        SolutionExplorerNodes = BuildSolutionExplorer(solution);
        WorkspaceStatus = $"{solution.Name}: {solution.Projects.Count} project(s)";

        var firstXaml = ActiveProject?.GetXamlFiles()
            .FirstOrDefault(file => file.Kind == ProjectFileKind.Xaml && !file.Path.StartsWith("App.", StringComparison.OrdinalIgnoreCase))
            ?? ActiveProject?.GetXamlFiles().FirstOrDefault();
        var firstCode = firstXaml is { } ? ActiveProject?.FindCodeBehind(firstXaml) : ActiveProject?.GetCSharpFiles().FirstOrDefault();

        ActiveXamlFile = firstXaml;
        ActiveCodeFile = firstCode;

        if (DockFactory is PlaygroundDockFactory factory && firstXaml is { })
        {
            var files = firstCode is { }
                ? new[] { firstXaml, firstCode }
                : new[] { firstXaml };
            factory.ResetDocuments(files);
        }
    }

    private ObservableCollection<SolutionExplorerNodeViewModel> BuildSolutionExplorer(InMemorySolution solution)
    {
        var solutionNode = new SolutionExplorerNodeViewModel($"Solution '{solution.Name}'", ProjectFileKind.Solution);
        foreach (var project in solution.Projects)
        {
            var projectNode = new SolutionExplorerNodeViewModel(project.Name, ProjectFileKind.Project, project: project);
            solutionNode.Children.Add(projectNode);

            foreach (var file in project.Files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                AddFileNode(projectNode, project, file);
            }
        }

        return new ObservableCollection<SolutionExplorerNodeViewModel> { solutionNode };
    }

    private void AddFileNode(SolutionExplorerNodeViewModel projectNode, InMemoryProject project, InMemoryProjectFile file)
    {
        var segments = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = projectNode;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var folderName = segments[i];
            var folder = parent.Children.FirstOrDefault(child =>
                child.Kind == ProjectFileKind.Folder &&
                string.Equals(child.Title, folderName, StringComparison.OrdinalIgnoreCase));
            if (folder is null)
            {
                folder = new SolutionExplorerNodeViewModel(folderName, ProjectFileKind.Folder, project: project);
                parent.Children.Add(folder);
            }

            parent = folder;
        }

        parent.Children.Add(new SolutionExplorerNodeViewModel(
            file.Name,
            file.Kind,
            new RelayCommand(() => OpenWorkspaceFile(file)),
            project,
            file));
    }

    private string GetUntitledSampleName()
    {
        const string name = "Untitled";
        if (!Samples.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
        {
            return name;
        }

        var index = 2;
        while (Samples.Any(x => string.Equals(x.Name, $"{name} {index}", StringComparison.Ordinal)))
        {
            index++;
        }

        return $"{name} {index}";
    }

    private void NewFile()
    {
        if (ActiveProject is { })
        {
            AddUserControl();
            return;
        }

        CreateUntitledSample();
    }

    private void Open(SampleViewModel sampleViewModel)
    {
        if (ReferenceEquals(CurrentSample, sampleViewModel))
        {
            OpenCurrentSample(sampleViewModel);
            return;
        }

        CurrentSample = sampleViewModel;
    }

    private void OpenCurrentSample(SampleViewModel sampleViewModel)
    {
        if (_openingSample)
        {
            return;
        }

        _openingSample = true;
        try
        {
            Control = null;
            DiagnosticsRoot = null;
            LastErrorMessage = null;
            _openXamlFile = null;
            _openCodeFile = null;

            LoadSolution(_solutionFactory.CreateSampleSolution(sampleViewModel.Name, sampleViewModel.Xaml, sampleViewModel.Code));

            if (EnableAutoRun)
            {
                RunActiveDocument();
            }
        }
        finally
        {
            _openingSample = false;
        }
    }

    private void CreateUntitledSample()
    {
        _openXamlFile = null;
        _openCodeFile = null;

        var sample = new SampleViewModel(GetUntitledSampleName(), Templates.s_newXaml, Templates.s_newCode, Open, AutoRun);
        Samples.Insert(0, sample);
        CurrentSample = sample;
    }

    private void ShowNewProjectWizard()
    {
        NewProjectWizard.SolutionName = GetUniqueSolutionName();
        NewProjectWizard.SelectedTemplate ??= NewProjectWizard.Templates.FirstOrDefault();
        NewProjectWizard.IsOpen = true;
    }

    private void CreateProjectFromWizard()
    {
        var template = NewProjectWizard.SelectedTemplate ?? NewProjectWizard.Templates.FirstOrDefault();
        if (template is null)
        {
            return;
        }

        var solutionName = string.IsNullOrWhiteSpace(NewProjectWizard.SolutionName)
            ? GetUniqueSolutionName()
            : NewProjectWizard.SolutionName.Trim();

        CurrentSample = null;
        LoadSolution(_solutionFactory.CreateSolution(solutionName, template));
        NewProjectWizard.IsOpen = false;
        LastErrorMessage = null;
        RunActiveDocument();
    }

    private string GetUniqueSolutionName()
    {
        var index = 1;
        var name = $"App{index}";
        while (Solution?.Name == name || Samples.Any(sample => sample.Name == name))
        {
            index++;
            name = $"App{index}";
        }

        return name;
    }

    private void AddUserControl()
    {
        if (ActiveProject is not { } project)
        {
            return;
        }

        var file = _solutionFactory.AddUserControl(project);
        SolutionExplorerNodes = Solution is { } solution
            ? BuildSolutionExplorer(solution)
            : new ObservableCollection<SolutionExplorerNodeViewModel>();
        OpenWorkspaceFile(file);
    }

    private void AddResourceDictionary()
    {
        if (ActiveProject is not { } project)
        {
            return;
        }

        var file = _solutionFactory.AddResourceDictionary(project);
        SolutionExplorerNodes = Solution is { } solution
            ? BuildSolutionExplorer(solution)
            : new ObservableCollection<SolutionExplorerNodeViewModel>();
        OpenWorkspaceFile(file);
    }

    private void OpenWorkspaceFile(InMemoryProjectFile file)
    {
        if (!file.CanEdit)
        {
            return;
        }

        ActivateWorkspaceFileFromDocument(file);
        if (DockFactory is PlaygroundDockFactory factory)
        {
            factory.OpenDocument(file);
        }
    }

    internal void ActivateWorkspaceFileFromDocument(InMemoryProjectFile file)
    {
        if (ActiveProject is not { } project)
        {
            return;
        }

        if (file.IsXaml)
        {
            ActiveXamlFile = file;
            ActiveCodeFile = project.FindCodeBehind(file) ?? ActiveCodeFile;
        }
        else if (file.IsCSharp)
        {
            ActiveCodeFile = file;
            ActiveXamlFile = project.FindXamlForCodeBehind(file) ?? ActiveXamlFile;
        }

        WorkspaceStatus = $"{project.Name}: {file.Path}";
    }

    private static List<FilePickerFileType> GetXamlFileTypes()
    {
        return new List<FilePickerFileType>
        {
            StorageService.Axaml,
            StorageService.Xaml,
            StorageService.All
        };
    }

    private static List<FilePickerFileType> GetCodeFileTypes()
    {
        return new List<FilePickerFileType>
        {
            StorageService.CSharp,
            StorageService.All
        };
    }

    private void AutoRun(SampleViewModel sampleViewModel)
    {
        if (EnableAutoRun && ReferenceEquals(sampleViewModel, CurrentSample))
        {
            RunActiveDocument();
        }
    }

    private void OnProjectFileChanged(InMemoryProjectFile file)
    {
        if (!EnableAutoRun || !ReferenceEquals(file, ActiveXamlFile) && !ReferenceEquals(file, ActiveCodeFile))
        {
            return;
        }

        RunActiveDocument();
    }

    private void RunActiveDocument()
    {
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(() => _ = RunInternal(), AutoRunDelay);
    }

    private async Task RunInternal()
    {
        if (_update)
            return;

        _update = true;
        var diagnosticsMessage = default(string);
        var xamlDiagnostics = new List<RuntimeXamlDiagnostic>();
        var xamlFile = ActiveXamlFile;
        var project = ActiveProject;

        try
        {
            // Control = null;
#if false
            if (!Utilities.IsBrowser())
            {
                // TODO: Unload previously loaded assembly.
                if (_previous is { })
                {
                    _previous?.Context?.Unload();
                    _previous = null;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
#endif
            Assembly? scriptAssembly = null;
            if (xamlFile is null)
            {
                LastErrorMessage = "No XAML document is active.";
                return;
            }

            if (!TryValidateXml(xamlFile.Text, out var xmlErrorMessage))
            {
                LastErrorMessage = xmlErrorMessage;
                return;
            }
            var xamlText = xamlFile.Text;

            var codeFiles = project?.GetCSharpFileSnapshot() ?? Array.Empty<(string Path, string Text)>();

            if (project is { } && codeFiles.Length > 0)
            {
                try
                {
                    var scriptResult = await Task.Run(async () => await CompilerService.GetProjectAssembly(
                        project.Name,
                        codeFiles));
                    diagnosticsMessage = FormatCompilerDiagnostics(scriptResult.Diagnostics);

                    if (scriptResult.Success && scriptResult.Assembly is { })
                    {
                        _previous = new ValueTuple<Assembly?, AssemblyLoadContext?>(scriptResult.Assembly, scriptResult.Context);
                        scriptAssembly = scriptResult.Assembly;
                        Console.WriteLine($"Compiled assembly: {scriptAssembly?.GetName().Name}");
                    }
                    else
                    {
                        LastErrorMessage = diagnosticsMessage ?? "Failed to compile code.";
                        return;
                    }
                }
                catch (Exception exception)
                {
                    LastErrorMessage = FormatException(exception);
                    Console.WriteLine(exception);
                    return;
                }
            }

            if (scriptAssembly is { })
            {
                var types = scriptAssembly.GetTypes();
                var type = ResolveRootType(xamlText, scriptAssembly)
                    ?? types.FirstOrDefault(x => x.Name == "SampleView")
                    ?? types.FirstOrDefault(x => x.Name == Path.GetFileNameWithoutExtension(xamlFile.Name));
                if (type != null)
                {
                    var rootInstance = Activator.CreateInstance(type);

                    var control = LoadRuntimeXaml(xamlText, scriptAssembly, rootInstance, xamlDiagnostics);
                    if (control is { })
                    {
                        ShowControl((Control)control, CombineDiagnostics(diagnosticsMessage, FormatXamlDiagnostics(xamlDiagnostics)));
                    }
                }
                else
                {
                    var control = LoadRuntimeXaml(xamlText, scriptAssembly, null, xamlDiagnostics) as Control;
                    if (control is { })
                    {
                        ShowControl(control, CombineDiagnostics(diagnosticsMessage, FormatXamlDiagnostics(xamlDiagnostics)));
                    }
                }
            }
            else
            {
                var control = LoadRuntimeXaml(xamlText, null, null, xamlDiagnostics) as Control;
                if (control is { })
                {
                    ShowControl(control, CombineDiagnostics(diagnosticsMessage, FormatXamlDiagnostics(xamlDiagnostics)));
                }
            }
        }
        catch (Exception exception)
        {
            LastErrorMessage = CombineDiagnostics(
                diagnosticsMessage,
                FormatXamlDiagnostics(xamlDiagnostics),
                FormatException(exception));
            Console.WriteLine(exception);
        }
        finally
        {
            _update = false;
        }
    }

    private static bool TryValidateXml(string? xaml, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(xaml))
        {
            errorMessage = "XAML is empty.";
            return false;
        }

        try
        {
            XDocument.Parse(xaml, LoadOptions.SetLineInfo);
            return true;
        }
        catch (XmlException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static object LoadRuntimeXaml(
        string xaml,
        Assembly? localAssembly,
        object? rootInstance,
        ICollection<RuntimeXamlDiagnostic> diagnostics)
    {
        var document = new RuntimeXamlLoaderDocument(rootInstance, xaml)
        {
            Document = PlaygroundXamlDocument
        };

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            CreateSourceInfo = true,
            DiagnosticHandler = diagnostic =>
            {
                diagnostics.Add(diagnostic);
                return diagnostic.Severity;
            }
        };

        return AvaloniaRuntimeXamlLoader.Load(document, configuration);
    }

    private static Type? ResolveRootType(string xaml, Assembly assembly)
    {
        try
        {
            var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
            var className = document.Root?.Attributes()
                .FirstOrDefault(static attribute =>
                    attribute.Name.LocalName == "Class" &&
                    attribute.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml")
                ?.Value;

            return string.IsNullOrWhiteSpace(className)
                ? null
                : assembly.GetType(className, throwOnError: false, ignoreCase: false);
        }
        catch
        {
            return null;
        }
    }

    private static string? FormatCompilerDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        var lines = diagnostics
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(FormatCompilerDiagnostic)
            .Distinct()
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string FormatCompilerDiagnostic(Diagnostic diagnostic)
    {
        var location = diagnostic.Location.GetLineSpan();
        var line = location.IsValid ? location.StartLinePosition.Line + 1 : (int?)null;
        var column = location.IsValid ? location.StartLinePosition.Character + 1 : (int?)null;
        var position = line is null
            ? string.Empty
            : $" Line {line}, position {column}";

        return $"C# {diagnostic.Severity}{position}: {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static string? FormatXamlDiagnostics(IEnumerable<RuntimeXamlDiagnostic> diagnostics)
    {
        var lines = diagnostics
            .Select(FormatXamlDiagnostic)
            .Distinct()
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string FormatXamlDiagnostic(RuntimeXamlDiagnostic diagnostic)
    {
        var document = string.IsNullOrWhiteSpace(diagnostic.Document)
            ? PlaygroundXamlDocument
            : diagnostic.Document;
        var position = diagnostic.LineNumber is { } lineNumber
            ? $" Line {lineNumber}, position {diagnostic.LinePosition ?? 1}"
            : string.Empty;

        return $"XAML {diagnostic.Severity} {document}{position}: {diagnostic.Id}: {diagnostic.Title}";
    }

    private static string? CombineDiagnostics(params string?[] messages)
    {
        var lines = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => message!.Trim())
            .Distinct()
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string FormatException(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            var messages = aggregateException.Flatten()
                .InnerExceptions
                .Select(static innerException => innerException.Message)
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct()
                .ToArray();

            if (messages.Length > 0)
            {
                return string.Join(Environment.NewLine, messages);
            }
        }

        return exception.Message;
    }

    private void ShowControl(Control control, string? diagnosticsMessage)
    {
        var scope = new Border
        {
            Name = "GeneratedSampleScope",
            Child = control
        };

        Control = scope;
        DiagnosticsRoot = scope;
        LastErrorMessage = diagnosticsMessage;
    }

    private async Task OpenXamlFile()
    {
        if (ActiveXamlFile is null)
        {
            return;
        }

        if (StorageProvider is null)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open xaml",
            FileTypeFilter = GetXamlFileTypes(),
            AllowMultiple = false
        });

        var file = result.FirstOrDefault();
        if (file is not null)
        {
            try
            {
                _openXamlFile = file;
                await using var stream = await _openXamlFile.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var fileContent = await reader.ReadToEndAsync();
                ActiveXamlFile.Text = fileContent;
                RunActiveDocument();
                reader.Dispose();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }
    }

    private async Task SaveXamlFile()
    {
        if (ActiveXamlFile is null)
        {
            return;
        }

        if (_openXamlFile is null)
        {
            if (StorageProvider is null)
            {
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save xaml",
                FileTypeChoices = GetXamlFileTypes(),
                SuggestedFileName = Path.GetFileNameWithoutExtension("playground"),
                DefaultExtension = "axaml",
                ShowOverwritePrompt = true
            });

            if (file is not null)
            {
                try
                {
                    _openXamlFile = file;
                    await using var stream = await _openXamlFile.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(ActiveXamlFile.Text);
                    ActiveXamlFile.MarkClean();
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }
            }
        }
        else
        {
            await using var stream = await _openXamlFile.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(ActiveXamlFile.Text);
            ActiveXamlFile.MarkClean();
        }
    }

    private async Task OpenCodeFile()
    {
        if (ActiveCodeFile is null)
        {
            return;
        }

        if (StorageProvider is null)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open code",
            FileTypeFilter = GetCodeFileTypes(),
            AllowMultiple = false
        });

        var file = result.FirstOrDefault();
        if (file is not null)
        {
            try
            {
                _openCodeFile = file;
                await using var stream = await _openCodeFile.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var fileContent = await reader.ReadToEndAsync();
                ActiveCodeFile.Text = fileContent;
                RunActiveDocument();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }
    }

    private async Task SaveCodeFile()
    {
        if (ActiveCodeFile is null)
        {
            return;
        }

        if (_openCodeFile is null)
        {
            if (StorageProvider is null)
            {
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save code",
                FileTypeChoices = GetCodeFileTypes(),
                SuggestedFileName = Path.GetFileNameWithoutExtension("playground"),
                DefaultExtension = "cs",
                ShowOverwritePrompt = true
            });

            if (file is not null)
            {
                try
                {
                    _openCodeFile = file;
                    await using var stream = await _openCodeFile.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(ActiveCodeFile.Text);
                    ActiveCodeFile.MarkClean();
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }
            }
        }
        else
        {
            await using var stream = await _openCodeFile.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(ActiveCodeFile.Text);
            ActiveCodeFile.MarkClean();
        }
    }
}
