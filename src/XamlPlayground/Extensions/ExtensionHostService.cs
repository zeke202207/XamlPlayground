using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XamlPlayground.Extensions;

public interface IXamlPlaygroundExtension
{
    ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken);

    ValueTask DeactivateAsync(CancellationToken cancellationToken);
}

public interface IExtensionProvider
{
    IEnumerable<ExtensionDescriptor> GetExtensions();
}

public sealed class ExtensionDescriptor
{
    public ExtensionDescriptor(ExtensionManifest manifest, Func<IXamlPlaygroundExtension>? factory = null, bool isBuiltIn = false)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Factory = factory;
        IsBuiltIn = isBuiltIn;
    }

    public ExtensionManifest Manifest { get; }

    public Func<IXamlPlaygroundExtension>? Factory { get; }

    public bool IsBuiltIn { get; }
}

public sealed class ExtensionHostService : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ExtensionRuntime> _extensions = new(StringComparer.Ordinal);

    public ExtensionHostService(IExtensionCommandRegistry? commands = null)
    {
        Commands = commands ?? new ExtensionCommandRegistry();
    }

    public IExtensionCommandRegistry Commands { get; }

    public IReadOnlyList<ExtensionManifest> Manifests
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_extensions.Values.Select(static extension => extension.Descriptor.Manifest).ToArray());
            }
        }
    }

    public void RegisterProvider(IExtensionProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        foreach (var descriptor in provider.GetExtensions())
        {
            RegisterExtension(descriptor);
        }
    }

    public void RegisterExtension(ExtensionDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var id = descriptor.Manifest.Identity.Id;
        lock (_gate)
        {
            if (_extensions.ContainsKey(id))
            {
                throw new InvalidOperationException("An extension with id '" + id + "' is already registered.");
            }

            ValidateContributionIdentifiers(descriptor.Manifest);
            _extensions.Add(id, new ExtensionRuntime(descriptor));
        }
    }

    public bool IsActivated(string extensionId)
    {
        var id = ExtensionCollections.RequireIdentifier(extensionId, nameof(extensionId));

        lock (_gate)
        {
            return _extensions.TryGetValue(id, out var extension) && extension.Context is not null;
        }
    }

    public IReadOnlyList<ExtensionManifest> FindExtensionsForActivationEvent(string activationEvent)
    {
        if (string.IsNullOrWhiteSpace(activationEvent))
        {
            throw new ArgumentException("The activation event cannot be empty.", nameof(activationEvent));
        }

        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .Where(extension => ShouldActivateForEvent(extension.Descriptor.Manifest, activationEvent))
                .Select(static extension => extension.Descriptor.Manifest)
                .ToArray());
        }
    }

    public async ValueTask ActivateByEventAsync(string activationEvent, CancellationToken cancellationToken = default)
    {
        var manifests = FindExtensionsForActivationEvent(activationEvent);
        foreach (var manifest in manifests)
        {
            await ActivateExtensionAsync(manifest.Identity.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ActivateExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        var id = ExtensionCollections.RequireIdentifier(extensionId, nameof(extensionId));
        ExtensionRuntime runtime;
        ExtensionContext context;
        IXamlPlaygroundExtension instance;

        lock (_gate)
        {
            if (!_extensions.TryGetValue(id, out runtime!))
            {
                throw new KeyNotFoundException("No extension with id '" + id + "' is registered.");
            }

            if (runtime.Context is not null)
            {
                return;
            }
        }

        await runtime.ActivationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_gate)
            {
                if (runtime.Context is not null)
                {
                    return;
                }
            }

            context = new ExtensionContext(runtime.Descriptor.Manifest, Commands);

            try
            {
                instance = runtime.Descriptor.Factory?.Invoke() ?? new ContributionOnlyExtension();
                await instance.ActivateAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                context.Dispose();
                throw;
            }

            lock (_gate)
            {
                runtime.Context = context;
                runtime.Instance = instance;
            }
        }
        finally
        {
            runtime.ActivationGate.Release();
        }
    }

    public async ValueTask<object?> ExecuteCommandAsync(string commandId, IEnumerable<object?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var id = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));
        await ActivateByEventAsync(ExtensionActivationEvents.OnCommand(id), cancellationToken).ConfigureAwait(false);
        return await Commands.ExecuteAsync(id, arguments, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<CommandContribution> GetCommandContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.Commands)
                .ToArray());
        }
    }

    public IReadOnlyList<MenuContribution> GetMenuContributions(string menuId)
    {
        var id = ExtensionCollections.RequireIdentifier(menuId, nameof(menuId));

        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.Menus)
                .Where(menu => string.Equals(menu.MenuId, id, StringComparison.Ordinal))
                .OrderBy(static menu => menu.Group, StringComparer.Ordinal)
                .ThenBy(static menu => menu.Order)
                .ToArray());
        }
    }

    public IReadOnlyList<ViewToolContribution> GetViewContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.Views)
                .ToArray());
        }
    }

    public IReadOnlyList<PerspectiveContribution> GetPerspectiveContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.Perspectives)
                .ToArray());
        }
    }

    public IReadOnlyList<PreviewProviderContribution> GetPreviewProviderContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.PreviewProviders)
                .OrderByDescending(static provider => provider.Priority)
                .ToArray());
        }
    }

    public IReadOnlyList<ToolboxItemContribution> GetToolboxItemContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.ToolboxItems)
                .ToArray());
        }
    }

    public IReadOnlyList<EditorFeatureContribution> GetEditorFeatureContributions(string? language = null)
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.EditorFeatures)
                .Where(feature => language is null || feature.Language.Equals(language, StringComparison.Ordinal))
                .ToArray());
        }
    }

    public IReadOnlyList<AnimationProviderContribution> GetAnimationProviderContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.AnimationProviders)
                .ToArray());
        }
    }

    public IReadOnlyList<ThemeResourceEditorContribution> GetThemeResourceEditorContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.ThemeResourceEditors)
                .ToArray());
        }
    }

    public IReadOnlyList<WorkspaceFeatureContribution> GetWorkspaceFeatureContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.WorkspaceFeatures)
                .ToArray());
        }
    }

    public IReadOnlyList<DiagnosticProviderContribution> GetDiagnosticProviderContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.DiagnosticProviders)
                .ToArray());
        }
    }

    public IReadOnlyList<VisualEditorFeatureContribution> GetVisualEditorFeatureContributions()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_extensions.Values
                .SelectMany(static extension => extension.Descriptor.Manifest.Contributions.VisualEditorFeatures)
                .ToArray());
        }
    }

    public async ValueTask DeactivateAllAsync(CancellationToken cancellationToken = default)
    {
        List<ExtensionRuntime> runtimes;
        List<Exception>? exceptions = null;

        lock (_gate)
        {
            runtimes = _extensions.Values.Where(static runtime => runtime.Context is not null).Reverse().ToList();
        }

        foreach (var runtime in runtimes)
        {
            try
            {
                if (runtime.Instance is not null)
                {
                    await runtime.Instance.DeactivateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(exception);
            }
            finally
            {
                try
                {
                    runtime.Context?.Dispose();
                }
                catch (Exception exception)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }

                try
                {
                    await DisposeExtensionInstanceAsync(runtime.Instance).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }

                lock (_gate)
                {
                    runtime.Context = null;
                    runtime.Instance = null;
                }
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more extensions failed during deactivation.", exceptions);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DeactivateAllAsync().ConfigureAwait(false);
    }

    private sealed class ExtensionRuntime
    {
        public ExtensionRuntime(ExtensionDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public ExtensionDescriptor Descriptor { get; }

        public SemaphoreSlim ActivationGate { get; } = new(1, 1);

        public ExtensionContext? Context { get; set; }

        public IXamlPlaygroundExtension? Instance { get; set; }
    }

    private static bool ShouldActivateForEvent(ExtensionManifest manifest, string activationEvent)
    {
        if (manifest.ActivationEvents.Any(declared => ExtensionActivationEvents.Matches(declared, activationEvent)))
        {
            return true;
        }

        if (TryGetActivationArgument(activationEvent, "onCommand:", out var commandId))
        {
            return manifest.Contributions.Commands.Any(command => command.Id.Equals(commandId, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onView:", out var viewId))
        {
            return manifest.Contributions.Views.Any(view => view.Id.Equals(viewId, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onPreview:", out var previewId))
        {
            return manifest.Contributions.PreviewProviders.Any(provider => provider.Id.Equals(previewId, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onEditorFeature:", out var featureId))
        {
            return manifest.Contributions.EditorFeatures.Any(feature => feature.Id.Equals(featureId, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onLanguage:", out var languageId))
        {
            return manifest.Contributions.EditorFeatures.Any(feature => feature.Language.Equals(languageId, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onDiagnostic:", out var diagnosticKind))
        {
            return manifest.Contributions.DiagnosticProviders.Any(provider => provider.DiagnosticKind.Equals(diagnosticKind, StringComparison.Ordinal));
        }

        if (TryGetActivationArgument(activationEvent, "onDebugTool:", out var toolId))
        {
            return manifest.Contributions.Views.Any(view => view.Id.Equals(toolId, StringComparison.Ordinal)) ||
                   manifest.Contributions.DiagnosticProviders.Any(provider => provider.Id.Equals(toolId, StringComparison.Ordinal));
        }

        if (string.Equals(activationEvent, ExtensionActivationEvents.OnThemeEditor, StringComparison.Ordinal))
        {
            return manifest.Contributions.ThemeResourceEditors.Count > 0;
        }

        if (string.Equals(activationEvent, ExtensionActivationEvents.OnAnimationEditor, StringComparison.Ordinal))
        {
            return manifest.Contributions.AnimationProviders.Count > 0;
        }

        if (string.Equals(activationEvent, ExtensionActivationEvents.OnPreviewSession, StringComparison.Ordinal))
        {
            return manifest.Contributions.PreviewProviders.Count > 0;
        }

        return false;
    }

    private void ValidateContributionIdentifiers(ExtensionManifest manifest)
    {
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "commands",
            manifest.Contributions.Commands.Select(static command => command.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "views",
            manifest.Contributions.Views.Select(static view => view.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "perspectives",
            manifest.Contributions.Perspectives.Select(static perspective => perspective.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "previewProviders",
            manifest.Contributions.PreviewProviders.Select(static provider => provider.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "toolbox",
            manifest.Contributions.ToolboxItems.Select(static item => item.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "editorFeatures",
            manifest.Contributions.EditorFeatures.Select(static feature => feature.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "animationProviders",
            manifest.Contributions.AnimationProviders.Select(static provider => provider.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "themeResourceEditors",
            manifest.Contributions.ThemeResourceEditors.Select(static editor => editor.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "workspaceFeatures",
            manifest.Contributions.WorkspaceFeatures.Select(static feature => feature.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "diagnosticProviders",
            manifest.Contributions.DiagnosticProviders.Select(static provider => provider.Id));
        ValidateUniqueWithinExtension(
            manifest.Identity.Id,
            "visualEditorFeatures",
            manifest.Contributions.VisualEditorFeatures.Select(static feature => feature.Id));

        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "command",
            manifest.Contributions.Commands.Select(static command => command.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.Commands).Select(static command => command.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "view",
            manifest.Contributions.Views.Select(static view => view.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.Views).Select(static view => view.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "perspective",
            manifest.Contributions.Perspectives.Select(static perspective => perspective.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.Perspectives).Select(static perspective => perspective.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "preview provider",
            manifest.Contributions.PreviewProviders.Select(static provider => provider.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.PreviewProviders).Select(static provider => provider.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "toolbox item",
            manifest.Contributions.ToolboxItems.Select(static item => item.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.ToolboxItems).Select(static item => item.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "editor feature",
            manifest.Contributions.EditorFeatures.Select(static feature => feature.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.EditorFeatures).Select(static feature => feature.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "animation provider",
            manifest.Contributions.AnimationProviders.Select(static provider => provider.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.AnimationProviders).Select(static provider => provider.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "theme resource editor",
            manifest.Contributions.ThemeResourceEditors.Select(static editor => editor.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.ThemeResourceEditors).Select(static editor => editor.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "workspace feature",
            manifest.Contributions.WorkspaceFeatures.Select(static feature => feature.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.WorkspaceFeatures).Select(static feature => feature.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "diagnostic provider",
            manifest.Contributions.DiagnosticProviders.Select(static provider => provider.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.DiagnosticProviders).Select(static provider => provider.Id));
        ValidateUniqueAgainstRegistered(
            manifest.Identity.Id,
            "visual editor feature",
            manifest.Contributions.VisualEditorFeatures.Select(static feature => feature.Id),
            _extensions.Values.SelectMany(static runtime => runtime.Descriptor.Manifest.Contributions.VisualEditorFeatures).Select(static feature => feature.Id));
    }

    private static void ValidateUniqueWithinExtension(string extensionId, string contributionKind, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                throw new InvalidOperationException(
                    "Extension '" + extensionId + "' declares duplicate " + contributionKind + " contribution id '" + id + "'.");
            }
        }
    }

    private static void ValidateUniqueAgainstRegistered(
        string extensionId,
        string contributionKind,
        IEnumerable<string> ids,
        IEnumerable<string> registeredIds)
    {
        var registered = new HashSet<string>(registeredIds, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (registered.Contains(id))
            {
                throw new InvalidOperationException(
                    "Extension '" + extensionId + "' declares " + contributionKind + " contribution id '" + id + "' that is already registered.");
            }
        }
    }

    private static bool TryGetActivationArgument(string activationEvent, string prefix, out string value)
    {
        if (activationEvent.StartsWith(prefix, StringComparison.Ordinal) &&
            activationEvent.Length > prefix.Length)
        {
            value = activationEvent[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static async ValueTask DisposeExtensionInstanceAsync(IXamlPlaygroundExtension? instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private sealed class ContributionOnlyExtension : IXamlPlaygroundExtension
    {
        public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
