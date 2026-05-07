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
    private readonly IDockThemeManager _dockThemeManager;
    private bool _update;
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

        NewFileCommand = new RelayCommand(NewFile);
        OpenXamlFileCommand = new AsyncRelayCommand(async () => await OpenXamlFile());
        SaveXamlFileCommand = new AsyncRelayCommand(async () => await SaveXamlFile());
        OpenCodeFileCommand = new AsyncRelayCommand(async () => await OpenCodeFile());
        SaveCodeFileCommand = new AsyncRelayCommand(async () => await SaveCodeFile());
        RunCommand = new RelayCommand(() => Run(_currentSample?.Xaml.Text, _currentSample?.Code.Text));
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        GistCommand = new AsyncRelayCommand<string?>(Gist);
        InitializeDockLayout();

        if (!string.IsNullOrEmpty(initialGist))
        {
            Gist(initialGist);
        }
        else
        {
            CurrentSample = _samples.FirstOrDefault(x => x.Name == "Demo");
        }
    }

    public IStorageProvider? StorageProvider { get; set; }
    
    public ICommand RunCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand GistCommand { get; }

    public ICommand NewFileCommand { get; }

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
            Open(sampleViewModel);
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
            _samples.Insert(0, sample);
            CurrentSample = sample; 
            AutoRun(CurrentSample);
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
        _openXamlFile = null;
        _openCodeFile = null;

        var sample = new SampleViewModel(GetUntitledSampleName(), Templates.s_newXaml, Templates.s_newCode, Open, AutoRun);
        Samples.Insert(0, sample);
        CurrentSample = sample;
    }

    private void Open(SampleViewModel sampleViewModel)
    {
        Control = null;
        DiagnosticsRoot = null;
        LastErrorMessage = null;

        CurrentSample = sampleViewModel;

        if (_enableAutoRun)
        { 
            Run(sampleViewModel.Xaml.Text, sampleViewModel.Code.Text);
        }
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
        if (EnableAutoRun)
        { 
            Run(sampleViewModel.Xaml.Text, sampleViewModel.Code.Text);
        }
    }

    private void Run(string? xaml, string? code)
    {
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(() => _ = RunInternal(xaml, code), AutoRunDelay);
    }

    private async Task RunInternal(string? xaml, string? code)
    {
        if (_update)
            return;

        _update = true;
        var diagnosticsMessage = default(string);
        var xamlDiagnostics = new List<RuntimeXamlDiagnostic>();

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
            if (!TryValidateXml(xaml, out var xmlErrorMessage))
            {
                LastErrorMessage = xmlErrorMessage;
                return;
            }
            var xamlText = xaml!;

            if (code is { } && !string.IsNullOrWhiteSpace(code))
            {
                try
                {
                    var scriptResult = await Task.Run(async () => await CompilerService.GetScriptAssembly(code));
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
                var type = types.FirstOrDefault(x => x.Name == "SampleView");
                if (type != null)
                {
                    var rootInstance = Activator.CreateInstance(type);

                    var control = LoadRuntimeXaml(xamlText, scriptAssembly, rootInstance, xamlDiagnostics);
                    if (control is { })
                    {
                        ShowControl((Control)control, CombineDiagnostics(diagnosticsMessage, FormatXamlDiagnostics(xamlDiagnostics)));
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
        if (CurrentSample is null)
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
                CurrentSample.Xaml.Text = fileContent;
                AutoRun(CurrentSample);
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
        if (CurrentSample is null)
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
                    await writer.WriteAsync(CurrentSample.Xaml.Text);
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
            await writer.WriteAsync(CurrentSample.Xaml.Text);
        }
    }

    private async Task OpenCodeFile()
    {
        if (CurrentSample is null)
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
                CurrentSample.Code.Text = fileContent;
                AutoRun(CurrentSample);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }
    }

    private async Task SaveCodeFile()
    {
        if (CurrentSample is null)
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
                    await writer.WriteAsync(CurrentSample.Code.Text);
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
            await writer.WriteAsync(CurrentSample.Code.Text);
        }
    }
}
