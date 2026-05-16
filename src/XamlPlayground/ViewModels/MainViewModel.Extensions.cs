using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using XamlPlayground.Extensions;
using XamlPlayground.Services.IntelliSense;
using XamlPlayground.Services.Preview;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels;

public partial class MainViewModel
{
    private readonly List<IDisposable> _extensionCommandRegistrations = new();

    public IReadOnlyList<ExtensionPackageLoadError> ExtensionPackageLoadErrors { get; private set; } = Array.Empty<ExtensionPackageLoadError>();

    public ObservableCollection<ExtensionCommandMenuItemViewModel> ExtensionCommandMenuItems { get; private set; } = new();

    public IAsyncRelayCommand<ExtensionCommandMenuItemViewModel?> ExecuteExtensionCommand { get; private set; } = null!;

    private void InitializeExtensionCommandSurface()
    {
        ExecuteExtensionCommand = new AsyncRelayCommand<ExtensionCommandMenuItemViewModel?>(ExecuteExtensionCommandAsync);
        RefreshExtensionCommandMenuItems();
    }

    private void RefreshExtensionCommandMenuItems()
    {
        var items = ExtensionHost.GetCommandContributions()
            .Where(static command => !command.Id.StartsWith("workbench.toggleTool.", StringComparison.Ordinal) &&
                                     !command.Id.StartsWith("workbench.applyPerspective.", StringComparison.Ordinal))
            .OrderBy(static command => command.Category ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static command => command.Title, StringComparer.Ordinal)
            .Select(command => new ExtensionCommandMenuItemViewModel(command, ExecuteExtensionCommand))
            .ToArray();

        ExtensionCommandMenuItems = new ObservableCollection<ExtensionCommandMenuItemViewModel>(items);
        OnPropertyChanged(nameof(ExtensionCommandMenuItems));
    }

    private async Task ExecuteExtensionCommandAsync(ExtensionCommandMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await ExtensionHost.ExecuteCommandAsync(item.Id);
        }
        catch (Exception exception)
        {
            LastErrorMessage = exception.Message;
        }
    }

    private void QueueExtensionActivation(string activationEvent)
    {
        _ = ActivateExtensionsForEventAsync(activationEvent);
    }

    private async Task ActivateExtensionsForEventAsync(string activationEvent)
    {
        try
        {
            await ExtensionHost.ActivateByEventAsync(activationEvent);
        }
        catch (Exception exception)
        {
            LastErrorMessage = exception.Message;
        }
    }

    private void QueueExtensionActivationForWorkspaceFile(InMemoryProjectFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (file.IsXaml)
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnLanguage("xaml"));
        }
        else if (file.IsCSharp)
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnLanguage("csharp"));
        }
    }

    private void QueueExtensionActivationForDockTool(string id)
    {
        QueueExtensionActivation(ExtensionActivationEvents.OnView(id));
        if (id.StartsWith("Diagnostics", StringComparison.Ordinal))
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnDebugTool(id));
            QueueExtensionActivation(ExtensionActivationEvents.OnDiagnostic("runtime"));
        }
        else if (id is "ResourcesInspector" or "ResourceEditor" or "ControlThemes" or "StyleEditor")
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnThemeEditor);
        }
        else if (id is "VisualAnimations" or "AnimationTimelineSheet")
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnAnimationEditor);
        }
    }

    private void QueueExtensionActivationForPreview(PreviewHostCapabilities capabilities)
    {
        QueueExtensionActivation(ExtensionActivationEvents.OnPreviewSession);
        QueueExtensionActivation(ExtensionActivationEvents.OnPreview(capabilities.Mode.ToString()));

        var providerId = capabilities.Mode switch
        {
            PreviewExecutionMode.InlineDesign => "preview.xaml.instant",
            PreviewExecutionMode.IsolatedRemoteDesigner => "preview.xaml.isolated",
            PreviewExecutionMode.IsolatedFullHost => "preview.workspace.msbuild",
            PreviewExecutionMode.BrowserIframe => "preview.xaml.browser",
            _ => null
        };

        if (providerId is not null)
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnPreview(providerId));
        }
    }

    private void RegisterExternalExtensionPackages()
    {
        if (Utilities.IsBrowser())
        {
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return;
        }

        var extensionDirectory = Path.Combine(appData, "XamlPlayground", "Extensions");
        var provider = new ExtensionPackageProvider(extensionDirectory);
        try
        {
            var descriptors = provider.GetExtensions().ToArray();
            var errors = new List<ExtensionPackageLoadError>(provider.LoadErrors);
            foreach (var descriptor in descriptors)
            {
                try
                {
                    ExtensionHost.RegisterExtension(descriptor);
                }
                catch (Exception exception)
                {
                    errors.Add(new ExtensionPackageLoadError(descriptor.Manifest.Identity.Id, exception.Message));
                }
            }

            ExtensionPackageLoadErrors = errors;
        }
        catch (Exception exception)
        {
            ExtensionPackageLoadErrors = new[]
            {
                new ExtensionPackageLoadError(extensionDirectory, exception.Message)
            };
        }
    }

    private void InitializeBuiltInExtensionCommands()
    {
        RegisterExtensionCommand("file.new", NewFileCommand);
        RegisterExtensionCommand("workspace.newProject", ShowNewProjectWizardCommand);
        RegisterExtensionCommand("workspace.addUserControl", AddUserControlCommand);
        RegisterExtensionCommand("workspace.addResourceDictionary", AddResourceDictionaryCommand);
        RegisterExtensionCommand("workspace.importSolution", ImportSolutionCommand);
        RegisterExtensionCommand("workspace.openFolder", OpenMsBuildWorkspaceFolderCommand);
        RegisterExtensionCommand("workspace.openMsBuild", OpenMsBuildWorkspaceCommand);
        RegisterExtensionCommand("workspace.exportSolution", ExportSolutionCommand);
        RegisterExtensionCommand("workspace.exportStandardSolutionFolder", ExportStandardSolutionFolderCommand);
        RegisterExtensionCommand("workspace.saveFile", SaveWorkspaceFileCommand);
        RegisterExtensionCommand("workspace.saveAll", SaveAllWorkspaceFilesCommand);
        RegisterExtensionCommand("xaml.openFile", OpenXamlFileCommand);
        RegisterExtensionCommand("xaml.saveFile", SaveXamlFileCommand);
        RegisterExtensionCommand("xaml.openSample", args =>
        {
            var sampleName = args.FirstOrDefault()?.ToString();
            var sample = string.IsNullOrWhiteSpace(sampleName)
                ? Samples.FirstOrDefault()
                : Samples.FirstOrDefault(candidate => candidate.Name.Equals(sampleName, StringComparison.OrdinalIgnoreCase));
            if (sample is not null)
            {
                Open(sample);
            }

            return sample is not null;
        });
        RegisterExtensionCommand("xaml.compilePreview", RunCommand);
        RegisterExtensionCommand("xaml.formatDocument", FormatActiveDocumentAsync);
        RegisterExtensionCommand("csharp.openFile", OpenCodeFileCommand);
        RegisterExtensionCommand("csharp.saveFile", SaveCodeFileCommand);
        RegisterExtensionCommand("workbench.toggleTheme", ToggleThemeCommand);
        RegisterExtensionCommand("designer.refresh", RefreshVisualEditorCommand);
        RegisterExtensionCommand("designer.applyProperty", ApplyVisualEditorPropertyCommand);
        RegisterExtensionCommand("designer.openPropertyResource", OpenVisualEditorPropertyResourceCommand);
        RegisterExtensionCommand("designer.insertToolboxItem", InsertSelectedToolboxItemCommand);
        RegisterExtensionCommand("designer.wrapSelection", WrapVisualEditorSelectionCommand);
        RegisterExtensionCommand("designer.unwrapSelection", UnwrapVisualEditorSelectionCommand);
        RegisterExtensionCommand("designer.duplicateElement", DuplicateVisualEditorElementCommand);
        RegisterExtensionCommand("designer.deleteElement", DeleteVisualEditorElementCommand);
        RegisterExtensionCommand("theme.openResourceEditor", () =>
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnThemeEditor);
            ApplyDockPerspective("Theme");
            ShowDockTool("ResourceEditor");
        });
        RegisterExtensionCommand("theme.openControlThemeEditor", () =>
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnThemeEditor);
            ApplyDockPerspective("Theme");
            ShowDockTool("ControlThemes");
        });
        RegisterExtensionCommand("theme.createControlTheme", CreateCustomControlThemeCommand);
        RegisterExtensionCommand("theme.applyControlTheme", ApplyControlThemeCommand);
        RegisterExtensionCommand("theme.removeControlTheme", RemoveControlThemeCommand);
        RegisterExtensionCommand("theme.openSelectedControlTheme", OpenSelectedControlThemeCommand);
        RegisterExtensionCommand("theme.openSelectedResource", OpenSelectedThemeResourceCommand);
        RegisterExtensionCommand("theme.renameSelectedResource", RenameSelectedThemeResourceCommand);
        RegisterExtensionCommand("theme.duplicateSelectedResource", DuplicateSelectedThemeResourceCommand);
        RegisterExtensionCommand("theme.deleteSelectedResource", DeleteSelectedThemeResourceCommand);
        RegisterExtensionCommand("theme.applySelectedResource", ApplySelectedThemeResourceCommand);
        RegisterExtensionCommand("theme.createVariantPreview", CreateThemeVariantPreviewCommand);
        RegisterExtensionCommand("theme.importControlThemeFiles", ImportControlThemeFilesCommand);
        RegisterExtensionCommand("theme.exportSelectedControlTheme", ExportSelectedControlThemeCommand);
        RegisterExtensionCommand("theme.saveProject", SaveControlThemeProjectCommand);
        RegisterExtensionCommand("theme.loadProject", LoadControlThemeProjectCommand);
        RegisterExtensionCommand("theme.loadFolder", LoadControlThemeFolderCommand);
        RegisterExtensionCommand("theme.loadBundledFluentProject", LoadBundledFluentThemeProjectCommand);
        RegisterExtensionCommand("theme.loadRepository", LoadControlThemeRepositoryCommand);
        RegisterExtensionCommand("animation.openTimeline", () =>
        {
            QueueExtensionActivation(ExtensionActivationEvents.OnAnimationEditor);
            ApplyDockPerspective("Animation");
            ShowDockTool("AnimationTimelineSheet");
        });
        RegisterExtensionCommand("animation.addTrack", AddAnimationTrackCommand);
        RegisterExtensionCommand("animation.addKeyFrame", AddAnimationKeyFrameCommand);
        RegisterExtensionCommand("animation.updateKeyFrame", UpdateAnimationKeyFrameCommand);
        RegisterExtensionCommand("animation.removeKeyFrame", RemoveAnimationKeyFrameCommand);
        RegisterExtensionCommand("animation.applyTimeline", ApplyAnimationTimelineCommand);
        RegisterExtensionCommand("animation.previewTimeline", PreviewAnimationTimelineCommand);
        RegisterExtensionCommand("animation.captureKeyFrame", CaptureAnimationKeyFrameCommand);
        RegisterExtensionCommand("animation.playTimeline", PlayAnimationTimelineCommand);
        RegisterExtensionCommand("animation.stopTimeline", StopAnimationTimelineCommand);
        RegisterExtensionCommand("animation.seekStart", SeekAnimationStartCommand);
        RegisterExtensionCommand("animation.seekEnd", SeekAnimationEndCommand);
        RegisterExtensionCommand("diagnostics.openCombinedTree", () =>
        {
            ApplyDockPerspective("Diagnostics");
            ShowDockTool("DiagnosticsCombinedTree");
        });
        RegisterExtensionCommand("diagnostics.refreshInspectors", RefreshDesignInspectorsCommand);
        RegisterExtensionCommand("diagnostics.openStyleSource", OpenSelectedStyleSourceCommand);
        RegisterExtensionCommand("diagnostics.openBindingSource", OpenSelectedBindingSourceCommand);
        RegisterExtensionCommand("diagnostics.openResourceSource", OpenSelectedResourceSourceCommand);
        RegisterExtensionCommand("diagnostics.applyStyleEditor", ApplyStyleEditorCommand);
        RegisterExtensionCommand("diagnostics.createStyleFromSelectedElement", CreateStyleFromSelectedElementCommand);
        RegisterExtensionCommand("diagnostics.buildStyleSelector", BuildStyleSelectorCommand);
        RegisterExtensionCommand("diagnostics.refreshStylePreview", RefreshStylePreviewCommand);
        RegisterExtensionCommand("diagnostics.buildBindingMarkup", BuildBindingMarkupCommand);
        RegisterExtensionCommand("diagnostics.applyBindingEditor", ApplyBindingEditorCommand);
        RegisterExtensionCommand("diagnostics.applyResourceEditor", ApplyResourceEditorCommand);
        RegisterExtensionCommand("diagnostics.createResource", CreateResourceCommand);
        RegisterExtensionCommand("workbench.restoreDockTools", RestoreDockTools);

        foreach (var descriptor in PlaygroundDockFactory.ToolDescriptors)
        {
            var id = descriptor.Id;
            RegisterExtensionCommand("workbench.toggleTool." + id, () => ToggleDockTool(id));
        }

        foreach (var descriptor in PlaygroundDockFactory.PerspectiveDescriptors)
        {
            var id = descriptor.Id;
            RegisterExtensionCommand("workbench.applyPerspective." + id, () => ApplyDockPerspective(id));
        }
    }

    private void DisposeBuiltInExtensionCommands()
    {
        for (var i = _extensionCommandRegistrations.Count - 1; i >= 0; i--)
        {
            _extensionCommandRegistrations[i].Dispose();
        }

        _extensionCommandRegistrations.Clear();
    }

    private void RegisterExtensionCommand(string commandId, Action action)
    {
        RegisterExtensionCommand(commandId, _ =>
        {
            action();
            return true;
        });
    }

    private void RegisterExtensionCommand(string commandId, Func<IReadOnlyList<object?>, object?> handler)
    {
        _extensionCommandRegistrations.Add(ExtensionHost.Commands.RegisterCommand(
            commandId,
            (invocation, cancellationToken) => RunOnUiThreadAsync(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(handler(invocation.Arguments));
                },
                cancellationToken),
            extensionId: BuiltInExtensionProvider.ExtensionId));
    }

    private void RegisterExtensionCommand(string commandId, Func<CancellationToken, Task<object?>> handler)
    {
        _extensionCommandRegistrations.Add(ExtensionHost.Commands.RegisterCommand(
            commandId,
            (_, cancellationToken) => RunOnUiThreadAsync(handler, cancellationToken),
            extensionId: BuiltInExtensionProvider.ExtensionId));
    }

    private void RegisterExtensionCommand(string commandId, ICommand command)
    {
        _extensionCommandRegistrations.Add(ExtensionHost.Commands.RegisterCommand(
            commandId,
            (_, cancellationToken) => ExecuteShellCommandAsync(command, null, cancellationToken),
            extensionId: BuiltInExtensionProvider.ExtensionId));
    }

    private static ValueTask<object?> RunOnUiThreadAsync(
        Func<CancellationToken, Task<object?>> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return new ValueTask<object?>(action(cancellationToken));
        }

        var result = new TaskCompletionSource<object?>();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.SetResult(await action(cancellationToken).ConfigureAwait(true));
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        });

        return new ValueTask<object?>(result.Task);
    }

    private static async ValueTask<object?> ExecuteShellCommandAsync(
        ICommand command,
        object? parameter,
        CancellationToken cancellationToken)
    {
        return await RunOnUiThreadAsync(
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (!command.CanExecute(parameter))
                {
                    return false;
                }

                if (command is IAsyncRelayCommand asyncCommand)
                {
                    await asyncCommand.ExecuteAsync(parameter).ConfigureAwait(true);
                }
                else
                {
                    command.Execute(parameter);
                }

                return true;
            },
            cancellationToken);
    }

    private void ShowDockTool(string id)
    {
        QueueExtensionActivationForDockTool(id);
        if (DockFactory is not PlaygroundDockFactory factory)
        {
            return;
        }

        if (factory.ShowTool(id))
        {
            UpdateDockToolMenuState(factory);
        }
    }

    private async Task<object?> FormatActiveDocumentAsync(CancellationToken cancellationToken)
    {
        var file = ActiveWorkspaceFile ?? ActiveXamlFile ?? ActiveCodeFile;
        if (file is null)
        {
            return false;
        }

        string? formatted = null;
        if (file.IsXaml)
        {
            formatted = await new XamlIntelliSenseService().FormatDocumentAsync(file.Text, cancellationToken);
        }
        else if (file.IsCSharp)
        {
            formatted = await new CSharpIntelliSenseService(ActiveProject, file).FormatDocumentAsync(file.Text, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(formatted) ||
            string.Equals(formatted, file.Text, StringComparison.Ordinal))
        {
            return false;
        }

        file.ApplyTextEdit(formatted);
        return true;
    }
}

public sealed class ExtensionCommandMenuItemViewModel
{
    public ExtensionCommandMenuItemViewModel(CommandContribution contribution, IAsyncRelayCommand<ExtensionCommandMenuItemViewModel?> command)
    {
        Id = contribution.Id;
        Title = contribution.Title;
        Category = contribution.Category ?? "Extensions";
        Header = Category + ": " + Title;
        Command = command;
    }

    public string Id { get; }

    public string Title { get; }

    public string Category { get; }

    public string Header { get; }

    public IAsyncRelayCommand<ExtensionCommandMenuItemViewModel?> Command { get; }
}
