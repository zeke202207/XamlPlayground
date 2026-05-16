using XamlPlayground.Extensions;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using Avalonia.Threading;

namespace XamlPlayground.Tests;

public sealed class ExtensionSystemTests
{
    [Fact]
    public async Task CommandExecution_ActivatesExtensionForCommandEventOnce()
    {
        var extension = new TestExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.echo", "Echo", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.echo") },
                new ExtensionContributions(
                    commands: new[] { new CommandContribution("test.echo", "Echo") })),
            () => extension));

        var result = await host.ExecuteCommandAsync("test.echo", new object?[] { "value" });

        Assert.Equal("value", result);
        Assert.True(host.IsActivated("test.echo"));
        Assert.Equal(1, extension.ActivationCount);
        Assert.True(host.Commands.Contains("test.echo"));

        await host.ExecuteCommandAsync("test.echo", new object?[] { "again" });

        Assert.Equal(1, extension.ActivationCount);
    }

    [Fact]
    public async Task DeactivateAll_DisposesExtensionContextSubscriptions()
    {
        var extension = new TestExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.disposable", "Disposable", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.disposable") }),
            () => extension));

        await host.ExecuteCommandAsync("test.disposable");
        await host.DeactivateAllAsync();

        Assert.Equal(1, extension.DeactivationCount);
        Assert.False(host.Commands.Contains("test.disposable"));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await host.Commands.ExecuteAsync("test.disposable"));
    }

    [Fact]
    public void BuiltInProvider_DeclaresCoreContributionPoints()
    {
        var host = new ExtensionHostService();

        host.RegisterProvider(new BuiltInExtensionProvider());

        var manifest = Assert.Single(host.Manifests);
        Assert.Equal(BuiltInExtensionProvider.ExtensionId, manifest.Identity.Id);
        Assert.NotEmpty(manifest.Contributions.Commands);
        Assert.NotEmpty(manifest.Contributions.Menus);
        Assert.NotEmpty(manifest.Contributions.Views);
        Assert.NotEmpty(manifest.Contributions.Perspectives);
        Assert.NotEmpty(manifest.Contributions.PreviewProviders);
        Assert.NotEmpty(manifest.Contributions.EditorFeatures);
        Assert.NotEmpty(manifest.Contributions.AnimationProviders);
        Assert.NotEmpty(manifest.Contributions.ThemeResourceEditors);
        Assert.NotEmpty(manifest.Contributions.WorkspaceFeatures);
        Assert.NotEmpty(manifest.Contributions.DiagnosticProviders);
        Assert.NotEmpty(manifest.Contributions.VisualEditorFeatures);
        Assert.NotEmpty(manifest.Contributions.ToolboxItems);
        Assert.Contains(manifest.Contributions.Views, view => view.Id == "VisualToolbox" && view.IsTool);
        Assert.Contains(manifest.Contributions.PreviewProviders, provider => provider.Id == "preview.xaml.instant");
        Assert.Contains(manifest.Contributions.ThemeResourceEditors, editor => editor.Id == "theme.resources");
        Assert.Contains(manifest.Contributions.ToolboxItems, item => item.Id == "builtin.toolbox.snippet.gridTwoRows");
    }

    [Fact]
    public void BuiltInProvider_DrivesDockToolAndPerspectiveDescriptors()
    {
        var viewIds = BuiltInExtensionProvider.Manifest.Contributions.Views.Select(static view => view.Id).ToArray();
        var perspectiveIds = BuiltInExtensionProvider.Manifest.Contributions.Perspectives.Select(static perspective => perspective.Id).ToArray();

        Assert.Equal(viewIds, PlaygroundDockFactory.ToolDescriptors.Select(static descriptor => descriptor.Id).ToArray());
        Assert.Equal(perspectiveIds, PlaygroundDockFactory.PerspectiveDescriptors.Select(static descriptor => descriptor.Id).ToArray());
        Assert.Contains(PlaygroundDockFactory.ToolDescriptors, static descriptor =>
            descriptor.Id == "VisualToolbox" && descriptor.Region == DockToolRegion.Left);
        Assert.Contains(PlaygroundDockFactory.ToolDescriptors, static descriptor =>
            descriptor.Id == "Preview" && descriptor.Region == DockToolRegion.Preview);
    }

    [Fact]
    public void ManifestReader_ParsesVsCodeStyleContributionShape()
    {
        var manifest = ExtensionManifestReader.ReadJson("""
                                                        {
                                                          "id": "sample.themeTools",
                                                          "publisher": "sample",
                                                          "displayName": "Theme Tools",
                                                          "version": "1.2.3",
                                                          "main": "bin/net10.0/ThemeTools.dll",
                                                          "kind": [ "desktop" ],
                                                          "activationEvents": [
                                                            "onCommand:themeTools.inspectResource"
                                                          ],
                                                          "contributes": {
                                                            "commands": [
                                                              {
                                                                "command": "themeTools.inspectResource",
                                                                "title": "Inspect Resource",
                                                                "category": "Theme"
                                                              }
                                                            ],
                                                            "menus": {
                                                              "editor/context": [
                                                                {
                                                                  "command": "themeTools.inspectResource",
                                                                  "group": "navigation",
                                                                  "when": "editorLang == xaml",
                                                                  "order": 7
                                                                }
                                                              ]
                                                            },
                                                            "views": {
                                                              "right": [
                                                                {
                                                                  "id": "themeTools.resources",
                                                                  "name": "Resource Inspector"
                                                                }
                                                              ]
                                                            },
                                                            "previewProviders": [
                                                              {
                                                                "id": "themeTools.preview",
                                                                "title": "Theme Preview",
                                                                "patterns": [ "*.axaml" ],
                                                                "priority": 50
                                                              }
                                                            ],
                                                            "toolbox": [
                                                              {
                                                                "id": "themeTools.toolbox.resourceButton",
                                                                "displayName": "Resource Button",
                                                                "category": "Theme Tools",
                                                                "typeName": "Button",
                                                                "xmlNamespace": "https://github.com/avaloniaui",
                                                                "assemblyName": "",
                                                                "defaultXaml": "<Button Content=\"Action\" />",
                                                                "metadata": {
                                                                  "kind": "snippet"
                                                                }
                                                              }
                                                            ]
                                                          }
                                                        }
                                                        """);

        Assert.Equal("sample.themeTools", manifest.Identity.Id);
        Assert.Equal("Theme Tools", manifest.Identity.DisplayName);
        Assert.Equal("1.2.3", manifest.Identity.Version);
        Assert.Equal("bin/net10.0/ThemeTools.dll", manifest.Metadata["main"]);
        Assert.Equal("desktop", manifest.Metadata["kind"]);
        Assert.Contains(ExtensionActivationEvents.OnCommand("themeTools.inspectResource"), manifest.ActivationEvents);
        Assert.Contains(manifest.Contributions.Commands, static command => command.Id == "themeTools.inspectResource");
        Assert.Contains(manifest.Contributions.Menus, static menu =>
            menu.MenuId == "editor/context" &&
            menu.CommandId == "themeTools.inspectResource" &&
            menu.Group == "navigation" &&
            menu.When == "editorLang == xaml" &&
            menu.Order == 7);
        Assert.Contains(manifest.Contributions.Views, static view =>
            view.Id == "themeTools.resources" &&
            view.Title == "Resource Inspector" &&
            view.Location == "right");
        Assert.Contains(manifest.Contributions.PreviewProviders, static provider =>
            provider.Id == "themeTools.preview" &&
            provider.Priority == 50 &&
            provider.SupportedFilePatterns.Single() == "*.axaml");
        Assert.Contains(manifest.Contributions.ToolboxItems, static item =>
            item.Id == "themeTools.toolbox.resourceButton" &&
            item.Metadata["kind"] == "snippet");
    }

    [Fact]
    public void ManifestReader_DerivesIdFromPublisherAndName()
    {
        var manifest = ExtensionManifestReader.ReadJson("""
                                                        {
                                                          "publisher": "sample",
                                                          "name": "theme-tools",
                                                          "displayName": "Theme Tools",
                                                          "version": "1.0.0"
                                                        }
                                                        """);

        Assert.Equal("sample.theme-tools", manifest.Identity.Id);
        Assert.Equal("Theme Tools", manifest.Identity.DisplayName);
        Assert.Equal("theme-tools", manifest.Metadata["name"]);
    }

    [Fact]
    public void ExtensionPackageProvider_DiscoversDirectoryManifests()
    {
        using var temp = TempDirectory.Create();
        var extensionDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "sample.extension"));
        File.WriteAllText(
            Path.Combine(extensionDirectory.FullName, ExtensionManifestReader.ManifestFileName),
            """
            {
              "id": "sample.extension",
              "displayName": "Sample Extension",
              "version": "1.0.0",
              "contributes": {
                "commands": [
                  {
                    "command": "sample.extension.hello",
                    "title": "Hello"
                  }
                ]
              }
            }
            """);

        var descriptor = Assert.Single(new ExtensionPackageProvider(temp.Path).GetExtensions());

        Assert.Equal("sample.extension", descriptor.Manifest.Identity.Id);
        Assert.False(descriptor.IsBuiltIn);
        Assert.Null(descriptor.Factory);
        Assert.Contains(descriptor.Manifest.Contributions.Commands, static command => command.Id == "sample.extension.hello");
    }

    [Fact]
    public void ExtensionPackageProvider_ContinuesAfterInvalidManifest()
    {
        using var temp = TempDirectory.Create();
        var invalidDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "invalid.extension"));
        File.WriteAllText(
            Path.Combine(invalidDirectory.FullName, ExtensionManifestReader.ManifestFileName),
            """{ "displayName": "Invalid" }""");
        var validDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "valid.extension"));
        File.WriteAllText(
            Path.Combine(validDirectory.FullName, ExtensionManifestReader.ManifestFileName),
            """
            {
              "id": "valid.extension",
              "displayName": "Valid Extension",
              "version": "1.0.0"
            }
            """);
        var provider = new ExtensionPackageProvider(temp.Path);

        var descriptor = Assert.Single(provider.GetExtensions());

        Assert.Equal("valid.extension", descriptor.Manifest.Identity.Id);
        Assert.Single(provider.LoadErrors);
        Assert.Contains("id", provider.LoadErrors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterExtension_RejectsDuplicateContributionIdsWithinManifest()
    {
        var host = new ExtensionHostService();

        var exception = Assert.Throws<InvalidOperationException>(() => host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.duplicates", "Duplicates", "1.0.0"),
                contributions: new ExtensionContributions(
                    commands: new[]
                    {
                        new CommandContribution("test.duplicates.command", "First"),
                        new CommandContribution("test.duplicates.command", "Second")
                    })))));

        Assert.Contains("duplicate commands contribution id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterExtension_RejectsDuplicateIdsAcrossAllContributionFamilies()
    {
        var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.first", "First", "1.0.0"),
                contributions: new ExtensionContributions(
                    editorFeatures: new[] { new EditorFeatureContribution("test.feature", "Feature", "xaml", "completion") }))));

        var exception = Assert.Throws<InvalidOperationException>(() => host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.second", "Second", "1.0.0"),
                contributions: new ExtensionContributions(
                    editorFeatures: new[] { new EditorFeatureContribution("test.feature", "Feature", "xaml", "hover") })))));

        Assert.Contains("editor feature contribution id 'test.feature' that is already registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterExtension_RejectsDuplicateContributionIdsAcrossExtensions()
    {
        var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.first", "First", "1.0.0"),
                contributions: new ExtensionContributions(
                    views: new[] { new ViewToolContribution("test.sharedView", "Shared", "bottom") }))));

        var exception = Assert.Throws<InvalidOperationException>(() => host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.second", "Second", "1.0.0"),
                contributions: new ExtensionContributions(
                    views: new[] { new ViewToolContribution("test.sharedView", "Shared Again", "bottom") })))));

        Assert.Contains("view contribution id 'test.sharedView' that is already registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeactivateAll_DisposesRemainingExtensionsWhenOneDeactivationFails()
    {
        var first = new TestExtension();
        var second = new TestExtension { ThrowOnDeactivate = true };
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.first", "First", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.first") }),
            () => first));
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.second", "Second", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.second") }),
            () => second));

        await host.ExecuteCommandAsync("test.first");
        await host.ExecuteCommandAsync("test.second");
        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await host.DeactivateAllAsync());

        Assert.Single(exception.InnerExceptions);
        Assert.Equal(1, first.DeactivationCount);
        Assert.Equal(1, second.DeactivationCount);
        Assert.False(host.IsActivated("test.first"));
        Assert.False(host.IsActivated("test.second"));
    }

    [Fact]
    public async Task DeactivateAll_DisposesExtensionInstanceAfterContextSubscriptions()
    {
        var extension = new DisposableTestExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.disposableInstance", "Disposable Instance", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.disposableInstance") }),
            () => extension));

        await host.ExecuteCommandAsync("test.disposableInstance");
        await host.DeactivateAllAsync();

        Assert.True(extension.ContextSubscriptionDisposed);
        Assert.True(extension.InstanceDisposed);
    }

    [Fact]
    public async Task DeactivateAll_DisposesInstanceWhenContextSubscriptionFails()
    {
        var extension = new ThrowingSubscriptionExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.throwingSubscription", "Throwing Subscription", "1.0.0"),
                new[] { ExtensionActivationEvents.OnCommand("test.throwingSubscription") }),
            () => extension));

        await host.ExecuteCommandAsync("test.throwingSubscription");
        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await host.DeactivateAllAsync());

        Assert.Contains("subscription failure", exception.ToString(), StringComparison.Ordinal);
        Assert.True(extension.NonThrowingSubscriptionDisposed);
        Assert.True(extension.InstanceDisposed);
    }

    [Fact]
    public async Task FeatureContribution_ImplicitlyActivatesForLanguageAndToolingEvents()
    {
        var extension = new TestExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.features", "Features", "1.0.0"),
                contributions: new ExtensionContributions(
                    editorFeatures: new[] { new EditorFeatureContribution("test.features.xaml", "XAML", "xaml", "completion") },
                    themeResourceEditors: new[] { new ThemeResourceEditorContribution("test.features.theme", "Theme", "resourceDictionary") },
                    animationProviders: new[] { new AnimationProviderContribution("test.features.animation", "Animation", "control") },
                    diagnosticProviders: new[] { new DiagnosticProviderContribution("test.features.diagnostics", "Diagnostics", "binding") })),
            () => extension));

        await host.ActivateByEventAsync(ExtensionActivationEvents.OnLanguage("xaml"));

        Assert.True(host.IsActivated("test.features"));
        Assert.Equal(1, extension.ActivationCount);
        Assert.Contains(host.GetEditorFeatureContributions("xaml"), static feature => feature.Id == "test.features.xaml");
        Assert.Contains(host.GetThemeResourceEditorContributions(), static editor => editor.Id == "test.features.theme");
        Assert.Contains(host.GetAnimationProviderContributions(), static provider => provider.Id == "test.features.animation");
        Assert.Contains(host.GetDiagnosticProviderContributions(), static provider => provider.Id == "test.features.diagnostics");
    }


    [Fact]
    public void MainViewModel_RegistersBuiltInExtensionHost()
    {
        TestApplication.EnsureAvaloniaInitialized();

        using var viewModel = new MainViewModel(null);

        var manifest = Assert.Single(viewModel.ExtensionHost.Manifests, static manifest =>
            manifest.Identity.Id == BuiltInExtensionProvider.ExtensionId);
        Assert.Equal(BuiltInExtensionProvider.ExtensionId, manifest.Identity.Id);
    }

    [Fact]
    public void MainViewModel_BuiltInExtensionCommandsExecuteShellFeatures()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            using var viewModel = new MainViewModel(null);

            var perspectiveResult = viewModel.ExtensionHost
                .ExecuteCommandAsync("workbench.applyPerspective.Theme")
                .AsTask()
                .GetAwaiter()
                .GetResult();
            var toolResult = viewModel.ExtensionHost
                .ExecuteCommandAsync("theme.openResourceEditor")
                .AsTask()
                .GetAwaiter()
                .GetResult();

            Assert.True((bool)perspectiveResult!);
            Assert.True((bool)toolResult!);
            Assert.Equal("Theme", viewModel.CurrentDockPerspectiveId);
            Assert.Contains(viewModel.DockToolMenuItems, static item => item.Id == "ResourceEditor" && item.IsVisible);
            Assert.Contains(viewModel.ExtensionHost.Commands.Commands, static command => command.Id == "designer.refresh");
        });
    }

    [Fact]
    public void MainViewModel_ExposesExtensionCommandMenuItems()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            using var viewModel = new MainViewModel(null);

            Assert.Contains(viewModel.ExtensionCommandMenuItems, static item =>
                item.Id == "xaml.formatDocument" &&
                item.Header == "Editor: Format Document");
            Assert.DoesNotContain(viewModel.ExtensionCommandMenuItems, static item =>
                item.Id.StartsWith("workbench.toggleTool.", StringComparison.Ordinal));
            Assert.DoesNotContain(viewModel.ExtensionCommandMenuItems, static item =>
                item.Id.StartsWith("workbench.applyPerspective.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MainViewModel_LoadsExtensionContributedToolboxItems()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            using var viewModel = new MainViewModel(null);

            Assert.Contains(viewModel.VisualEditorToolboxItems, static item =>
                item.Id == "builtin.toolbox.snippet.gridTwoRows" &&
                item.Metadata["kind"] == "snippet");
        });
    }

    [Fact]
    public async Task CommandContribution_ImplicitlyActivatesOnCommand()
    {
        var extension = new TestExtension();
        await using var host = new ExtensionHostService();
        host.RegisterExtension(new ExtensionDescriptor(
            new ExtensionManifest(
                new ExtensionIdentity("test.implicit", "Implicit", "1.0.0"),
                contributions: new ExtensionContributions(
                    commands: new[] { new CommandContribution("test.implicit", "Implicit") })),
            () => extension));

        var result = await host.ExecuteCommandAsync("test.implicit", new object?[] { "implicit" });

        Assert.Equal("implicit", result);
        Assert.Equal(1, extension.ActivationCount);
    }

    [Fact]
    public void ActivationEventMatcher_SupportsWildcardAndExactMatches()
    {
        Assert.True(ExtensionActivationEvents.Matches("*", ExtensionActivationEvents.OnCommand("x")));
        Assert.True(ExtensionActivationEvents.Matches("onStartupFinished", "onStartupFinished"));
        Assert.False(ExtensionActivationEvents.Matches(ExtensionActivationEvents.OnCommand("a"), ExtensionActivationEvents.OnCommand("b")));
    }

    private sealed class TestExtension : IXamlPlaygroundExtension
    {
        public int ActivationCount { get; private set; }

        public int DeactivationCount { get; private set; }

        public bool ThrowOnDeactivate { get; init; }

        public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
        {
            ActivationCount++;
            var commandId = context.Manifest.Identity.Id;
            context.Subscribe(context.Commands.RegisterCommand(
                commandId,
                static (invocation, _) =>
                {
                    var result = invocation.Arguments.Count == 0 ? null : invocation.Arguments[0];
                    return ValueTask.FromResult(result);
                },
                extensionId: context.Manifest.Identity.Id));
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            DeactivationCount++;
            if (ThrowOnDeactivate)
            {
                throw new InvalidOperationException("Deactivation failed.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableTestExtension : IXamlPlaygroundExtension, IDisposable
    {
        public bool ContextSubscriptionDisposed { get; private set; }

        public bool InstanceDisposed { get; private set; }

        public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
        {
            context.Subscribe(ExtensionDisposable.Create(() => ContextSubscriptionDisposed = true));
            context.Subscribe(context.Commands.RegisterCommand(
                context.Manifest.Identity.Id,
                static (_, _) => ValueTask.FromResult<object?>(true),
                extensionId: context.Manifest.Identity.Id));
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            InstanceDisposed = true;
        }
    }

    private sealed class ThrowingSubscriptionExtension : IXamlPlaygroundExtension, IDisposable
    {
        public bool NonThrowingSubscriptionDisposed { get; private set; }

        public bool InstanceDisposed { get; private set; }

        public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
        {
            context.Subscribe(ExtensionDisposable.Create(() => NonThrowingSubscriptionDisposed = true));
            context.Subscribe(ExtensionDisposable.Create(() => throw new InvalidOperationException("subscription failure")));
            context.Subscribe(context.Commands.RegisterCommand(
                context.Manifest.Identity.Id,
                static (_, _) => ValueTask.FromResult<object?>(true),
                extensionId: context.Manifest.Identity.Id));
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            InstanceDisposed = true;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xamlplayground-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
