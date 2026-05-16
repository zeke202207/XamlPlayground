# XamlPlayground Extension System Specification

Date: 2026-05-16

## Objective

XamlPlayground should support installable extensions that can add editor behavior, commands, menus, views, preview tooling, theme/resource editing features, diagnostics, and developer tooling without letting third-party code take ownership of the application shell or the preview host boundary.

The model follows the proven VS Code extension architecture:

- extensions are described by a manifest
- extensions are activated lazily
- extensions contribute declarative UI and capabilities
- extension code runs behind an extension host boundary
- extension APIs are versioned
- host-owned services remain authoritative for workspace, preview, diagnostics, security, and persistence

The design is adapted for .NET, Avalonia, XAML editing, isolated preview hosts, and the browser build of XamlPlayground.

## Design Goals

- Keep the editor responsive by loading extensions only when activation events require them.
- Keep user code and extension code out of the parent editor process whenever isolation is available.
- Make common customization declarative through contribution points instead of imperative shell mutation.
- Provide stable APIs for XAML, C#, preview, theme resources, animation tooling, diagnostics, and DevTools integrations.
- Use the same logical extension model across desktop, web, and browser-preview surfaces, while allowing each runtime to expose different capabilities.
- Support local development, marketplace-style packages, and bundled first-party extensions.
- Make extension behavior inspectable, diagnosable, disableable, and revocable.

## Non-Goals

- Extensions must not replace the main application lifetime, docking engine, storage engine, compiler service, or preview session manager.
- Extensions must not load arbitrary native code in the browser runtime.
- Extensions must not receive unrestricted filesystem, network, process, reflection, or assembly-load access by default.
- Extensions must not directly mutate Avalonia visual trees owned by the editor shell or preview host except through approved APIs.
- Extensions must not require XamlPlayground to be restarted for normal install, enable, disable, or update operations.

## System Architecture

The extension system has five layers:

1. Extension registry
2. Manifest parser and validator
3. Contribution resolver
4. Extension host runtime
5. Extension API surface

The parent editor owns registry state, contribution metadata, activation decisions, command routing, menu resolution, view placement, workspace state, preview sessions, diagnostics aggregation, and security decisions.

Extension code runs in an extension host. The extension host may run in-process for trusted first-party extensions during early implementation, but the public system contract treats the host as a separate boundary. APIs should be message-based from the beginning so the implementation can move from in-process to out-of-process without changing extension packages.

```text
XamlPlayground Editor
  ExtensionRegistry
  ContributionResolver
  CommandService
  MenuService
  ViewService
  WorkspaceService
  PreviewSessionManager
  DiagnosticsService
  SecurityPolicy
        |
        | JSON-RPC or typed message channel
        v
Extension Host
  Extension loader
  Activation service
  API proxy
  Extension instances
        |
        | preview tooling channel when permitted
        v
Preview Host
  Inline design preview
  Isolated remote designer
  Isolated full host
  Browser iframe host
```

## Extension Package Layout

An extension package is a directory or archive with a manifest at the root.

```text
publisher.extension-name/
  xamlplayground.extension.json
  bin/
    net8.0/
      Extension.dll
  resources/
    icons/
    themes/
    snippets/
  docs/
    README.md
```

Browser-compatible extensions may ship only declarative contributions, WebAssembly-safe code, or JavaScript modules approved for the browser extension host.

Desktop extensions may ship .NET assemblies. Native dependencies require an explicit capability and are disabled by default for untrusted packages.

## Manifest

The manifest is a JSON file named `xamlplayground.extension.json`.

```json
{
  "id": "sample.theme-tools",
  "publisher": "sample",
  "name": "Theme Tools",
  "displayName": "Theme Tools",
  "description": "Adds resource inspection and theme editing helpers.",
  "version": "1.0.0",
  "engines": {
    "xamlPlayground": ">=1.0.0 <2.0.0",
    "avalonia": ">=11.0.0 <12.0.0",
    "dotnet": ">=8.0"
  },
  "kind": ["desktop", "browser"],
  "main": "bin/net8.0/Sample.ThemeTools.dll",
  "browser": "browser/main.js",
  "activationEvents": [
    "onCommand:themeTools.inspectResource",
    "onView:themeTools.resources",
    "onLanguage:xaml",
    "workspaceContains:**/*.axaml"
  ],
  "capabilities": {
    "workspace": "read",
    "network": [],
    "preview": ["inspect", "setResourceValue"],
    "diagnostics": ["publish"],
    "ui": ["views", "menus", "commands"]
  },
  "contributes": {
    "commands": [
      {
        "command": "themeTools.inspectResource",
        "title": "Inspect Resource",
        "category": "Theme Tools",
        "icon": "resources/icons/resource.svg"
      }
    ],
    "menus": {
      "editor/context": [
        {
          "command": "themeTools.inspectResource",
          "when": "editorLang == xaml && selectionHasResourceReference"
        }
      ]
    },
    "views": {
      "right": [
        {
          "id": "themeTools.resources",
          "name": "Resource Inspector",
          "when": "workspaceHasXaml"
        }
      ]
    }
  }
}
```

### Required Manifest Fields

- `id`: globally unique extension identifier in `publisher.name` form.
- `publisher`: publisher identity.
- `name`: package name.
- `displayName`: UI display name.
- `version`: semantic version.
- `engines.xamlPlayground`: supported XamlPlayground API range.
- `contributes`: declarative contribution object. It may be empty for pure API extensions.

### Optional Manifest Fields

- `description`
- `license`
- `repository`
- `homepage`
- `keywords`
- `kind`: supported runtime kinds: `desktop`, `browser`, `web`, `workspace`, `ui`.
- `main`: .NET assembly entry point for desktop extension hosts.
- `browser`: browser module entry point.
- `activationEvents`
- `capabilities`
- `extensionDependencies`
- `extensionPack`
- `configurationDefaults`

## Activation Events

Extensions activate lazily. Activation should be deterministic and observable in diagnostics.

Supported activation event families:

- `onStartupFinished`: activate after the shell is ready.
- `onCommand:<commandId>`: activate before invoking a contributed command.
- `onView:<viewId>`: activate before creating a contributed view.
- `onLanguage:xaml`, `onLanguage:csharp`: activate when an editor opens a matching language.
- `onPreview:<mode>`: activate when a preview host with the requested mode starts.
- `onPreviewSession`: activate when any preview session is created.
- `onDiagnostic:<kind>`: activate when diagnostics of a kind are produced.
- `onThemeEditor`: activate when theme/resource editing opens.
- `onAnimationEditor`: activate when the animation timeline opens.
- `workspaceContains:<glob>`: activate when workspace content matches a glob.
- `onFileSystem:<scheme>`: activate for virtual filesystem providers.
- `onDebugTool:<toolId>`: activate when a DevTools surface is opened.

Activation must be cancellable. If an extension fails activation, the editor records the error, disables the failed activation instance, and keeps the shell running.

## Extension Entry Point

A .NET extension assembly exports one class implementing an extension entry point interface.

```csharp
public interface IXamlPlaygroundExtension
{
    ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken);

    ValueTask DeactivateAsync(CancellationToken cancellationToken);
}
```

The activation context exposes services through narrow capability-checked interfaces:

- `Commands`
- `Menus`
- `Views`
- `Workspace`
- `Documents`
- `Languages`
- `Preview`
- `Themes`
- `Animations`
- `Diagnostics`
- `DevTools`
- `Storage`
- `Telemetry`

Extension APIs must return disposables or registrations for every event subscription and contribution added imperatively.

## Contribution Points

Contribution points are declarative manifest sections. The contribution resolver validates them before extension activation.

### Commands

Commands are stable string identifiers invoked by menus, keybindings, toolbar buttons, views, command palette entries, and other extensions.

```json
{
  "contributes": {
    "commands": [
      {
        "command": "preview.reloadIsolated",
        "title": "Reload Isolated Preview",
        "category": "Preview",
        "enablement": "previewSessionActive"
      }
    ]
  }
}
```

Command rules:

- Command identifiers must be prefixed by the extension id or an approved namespace.
- Command identifiers are globally unique. Duplicate command, view, perspective, preview provider, or toolbox item ids are rejected at registration time.
- Command handlers are activated on first invocation.
- Commands may return serializable results.
- Long-running commands receive cancellation tokens and progress reporters.
- Commands that mutate workspace, XAML, resources, preview state, or settings must use host APIs so undo, diagnostics, and persistence remain consistent.
- The shell may expose command contributions through a host-rendered extension command menu or command palette. This surface invokes commands through the extension host instead of direct view-model calls.

### Menus

Menu contributions place commands into host-owned menu locations.

Supported menu locations:

- `commandPalette`
- `main/menu`
- `main/toolbar`
- `editor/title`
- `editor/context`
- `preview/toolbar`
- `preview/context`
- `designer/context`
- `solutionExplorer/context`
- `resources/context`
- `diagnostics/context`
- `animationTimeline/context`
- `devtools/context`

Menu items support:

- `command`
- `when`
- `group`
- `order`
- `alt`
- `icon`

The host evaluates `when` expressions against a restricted context model. Extensions may define context keys through the context API but cannot execute arbitrary code in menu evaluation.

### Keybindings

Keybindings are contributions resolved by the host input service.

```json
{
  "contributes": {
    "keybindings": [
      {
        "command": "designer.duplicateSelectedElement",
        "key": "Ctrl+D",
        "mac": "Cmd+D",
        "when": "designerMode && previewFocus"
      }
    ]
  }
}
```

The host resolves conflicts by priority:

1. User keybindings
2. Workspace keybindings
3. Built-in XamlPlayground keybindings
4. Extension default keybindings

Design-mode shortcuts remain owned by the designer command system. Extensions may add commands to the same context but should not bypass the designer selection and mutation APIs.

### Views And Tools

Extensions can contribute dockable views, tool panes, modal tools, and lightweight inline editor widgets.

Supported view containers:

- `left`
- `right`
- `bottom`
- `preview`
- `editorInline`
- `devtools`
- `themeEditor`
- `animationTimeline`

View contributions describe placement and lifecycle. Actual view UI is created by the extension host through a view provider.

```json
{
  "contributes": {
    "views": {
      "bottom": [
        {
          "id": "sample.bindingTrace",
          "name": "Binding Trace",
          "icon": "resources/icons/trace.svg",
          "when": "previewSessionActive"
        }
      ]
    }
  }
}
```

Desktop view providers may return Avalonia controls only through an approved UI bridge. Browser providers return web components or serialized view models rendered by host-provided components.

Direct shell access is not part of the public API. View providers receive a scoped view context with:

- view id
- extension id
- cancellation token
- theme resources
- command invoker
- local state memento
- host services allowed by manifest capabilities

### Editors And Inline Features

Editor contribution points:

- XAML completion providers
- C# completion providers
- hover providers
- document symbol providers
- code action providers
- formatting providers
- inline adornment providers
- snippet providers
- semantic token providers
- editor context actions

Providers must work against immutable document snapshots and return text edits instead of mutating editor controls directly.

Text edits are applied through the host document service so selection sync, undo/redo, preview reload classification, and diagnostics remain coherent.

### XAML Tooling

XAML-specific contribution points:

- control toolbox items
- property editors
- attached property editors
- markup extension editors
- resource reference providers
- binding inspectors
- visual tree inspectors
- XAML mutation actions
- design-time metadata

Toolbox contributions can add controls, templates, or snippets:

```json
{
  "contributes": {
    "toolbox": [
      {
        "id": "sample.controls.badge",
        "displayName": "Badge",
        "category": "Sample Controls",
        "typeName": "BadgeSnippet",
        "xmlNamespace": "https://github.com/avaloniaui",
        "assemblyName": "",
        "defaultXaml": "<Border Classes=\"badge\"><TextBlock Text=\"Badge\" /></Border>",
        "metadata": {
          "kind": "snippet"
        }
      }
    ]
  }
}
```

Toolbox rules:

- `id` is the stable item identity used for de-duplication.
- `defaultXaml` is inserted through the host mutation engine, so namespace handling, undo, preview refresh, and diagnostics stay consistent.
- `typeName` is display and filtering metadata. Snippets should use a snippet-specific value when they would otherwise duplicate common control type names.
- Third-party toolbox items must not directly instantiate Avalonia controls in the editor process unless the extension is trusted and activated through a runtime provider.

Mutation actions must produce structured XAML edits or call approved mutation APIs. They must not rewrite documents through ad hoc string replacement unless the API explicitly models the operation as a text edit.

### Previewing

Preview contributions interact with `PreviewSnapshot`, `PreviewChangeSet`, `PreviewReloadPlan`, and `PreviewToolingCommand` concepts.

Extensions may contribute:

- preview toolbar commands
- preview overlays
- design adorners
- host diagnostics consumers
- snapshot processors
- preview protocol tools
- iframe-compatible tooling
- render capture/export actions

Preview API boundaries:

- The editor owns source snapshots.
- The preview host owns runtime state.
- Extensions send tooling commands through the preview session API.
- The host decides whether a request can be applied live, requires host compilation, or requires restart.
- Extensions identify elements through `PreviewElementIdentity` and structural XAML paths, not raw runtime object references.

Preview execution modes:

- `InlineDesign`: fast local design preview, not isolated.
- `IsolatedRemoteDesigner`: process-isolated designer framebuffer protocol.
- `IsolatedFullHost`: future full desktop host with compilation and tooling command channel.
- `BrowserIframe`: browser iframe host with message transport and browser-safe capabilities.

Preview capabilities:

- `inspect`: request visual/logical tree snapshots.
- `selectElement`: synchronize selection with the designer.
- `setPropertyValue`: request property mutation.
- `setResourceValue`: request resource mutation.
- `setResourceReference`: request static or dynamic resource assignment.
- `beginAnimationPreview`: preview a timeline on a selected element.
- `stopAnimationPreview`: stop timeline preview.
- `requestDiagnostics`: request host diagnostics.

The preview API is asynchronous. Every command has a request id and session id. Responses include diagnostics and may include updated tooling snapshots.

### Animations

Animation extensions can contribute:

- timeline editors
- easing catalogs
- keyframe generators
- animation preview commands
- export/import providers
- property-specific animation editors

Animation preview is host-owned because it runs against live Avalonia controls. Extensions submit serialized timeline descriptions or structured animation mutations. The preview host validates target property compatibility, starts or stops the preview, and reports diagnostics through the preview tooling channel.

Animation extensions must not hold direct references to runtime controls across preview reloads. They must track targets by `PreviewElementIdentity`.

### Theme Editor And Resources

Theme/resource extensions can contribute:

- resource dictionary analyzers
- resource value editors
- swatch providers
- palette generators
- control theme inspectors
- resource reference fixers
- theme export providers
- resource preview controls

The host resource API works on workspace files and preview sessions:

- Workspace resource edits update source files.
- Preview resource edits may be applied live when the host supports `HostResourceUpdate`.
- Dynamic resources may update in-place.
- Static resources may require XAML reload or host restart depending on scope and host capability.

Resource identifiers must include file path, key, and resource kind. Extension APIs must distinguish literal values, `StaticResource`, `DynamicResource`, bindings, and animation values.

### Diagnostics And DevTools

Diagnostic extensions can:

- publish diagnostics
- transform diagnostics
- provide quick fixes
- add diagnostic views
- subscribe to preview host diagnostics
- add DevTools panels
- inspect binding, style, resource, layout, and visual tree state

Diagnostic kinds align with current preview contracts:

- `Compiler`
- `Xaml`
- `Resource`
- `Runtime`
- `Host`
- `Protocol`

Diagnostics must identify file path, line, column, severity, source, code, and optional related information where available.

DevTools extensions use snapshots and commands rather than raw runtime objects. A DevTools panel may request a visual tree snapshot, inspect properties, select an element, and issue approved mutations through preview tooling commands.

### File Systems And Storage

Extensions may contribute virtual file systems and storage providers for snippets, samples, generated resources, or remote sources.

Storage scopes:

- global extension state
- workspace extension state
- secret storage
- cache storage

Secrets are host-owned and never exposed to other extensions. Browser builds may use browser storage with user-consent prompts and stricter quotas.

### Configuration

Extensions can contribute configuration keys.

```json
{
  "contributes": {
    "configuration": {
      "title": "Theme Tools",
      "properties": {
        "themeTools.previewDynamicResources": {
          "type": "boolean",
          "default": true,
          "description": "Apply dynamic resource changes to active previews when supported."
        }
      }
    }
  }
}
```

Settings scopes:

- application
- workspace
- project
- language
- preview mode

The host validates setting values against manifest schemas before exposing them to extensions.

## Context Keys And When Expressions

Declarative UI uses restricted expressions over host-owned context keys.

Example context keys:

- `workspaceOpen`
- `workspaceHasXaml`
- `editorLang`
- `editorSelection`
- `selectionHasResourceReference`
- `previewSessionActive`
- `previewMode`
- `previewHostSupportsResources`
- `designerMode`
- `selectedElementType`
- `diagnosticsVisible`
- `browserRuntime`

Expressions support boolean operators, equality, inequality, regex match, and set membership. Expressions must not call extension code.

## Extension Host Boundaries

### Desktop Host

The desktop extension host should run out-of-process for untrusted extensions. The host communicates with the editor through JSON-RPC or an equivalent typed message channel.

Allowed desktop host capabilities are granted by manifest and user policy:

- workspace read
- workspace write
- preview inspect
- preview mutate
- diagnostics publish
- UI views
- network allowlist
- process execution
- native library load
- reflection over loaded assemblies

The default policy grants only read-only metadata and declarative UI contributions until activation requires more.

### Browser Host

The browser extension host is constrained by WebAssembly and browser sandboxing.

Browser restrictions:

- No arbitrary local filesystem access.
- No process spawning.
- No native library loading.
- No unrestricted reflection over runtime assemblies.
- Network access follows browser CORS and host permission policy.
- Extension packages must be browser-compatible.
- Preview tooling uses iframe or worker message passing.
- Large assemblies and analyzers may be disabled or replaced by server/workspace-hosted services.

Browser-compatible extensions should prefer:

- declarative contributions
- snippets
- commands implemented through host APIs
- web-safe views
- diagnostics that operate on document snapshots
- preview commands supported by `BrowserIframe`

### Preview Host

Preview hosts are separate from extension hosts. Extension code must not be loaded into a preview host by default. If a preview extension needs runtime participation, it must provide a separate preview participant package and declare it explicitly.

Preview participant rules:

- Runs with preview-host permissions, not editor-host permissions.
- Has access only to the active preview session.
- Receives no workspace secrets.
- Must handle reloads and restarts.
- Must communicate through the preview tooling protocol.

## Security Model

Security is capability-based.

Every extension starts with:

- manifest metadata access
- contributed commands/menus/views registration
- read access to its own package files
- local extension storage

Additional capabilities require declaration and host approval:

- workspace read
- workspace write
- project build access
- preview inspect
- preview mutate
- diagnostics publish
- network access
- process execution
- native dependencies
- secret storage
- telemetry

Security policy is evaluated at install, update, activation, and API call time.

The host should support:

- trusted publishers
- workspace trust
- per-extension enable/disable
- per-workspace enable/disable
- capability prompts
- revoked capabilities
- extension quarantine after repeated crashes
- signed package verification
- package checksum verification
- audit logging for sensitive operations

## Versioning

The extension API uses semantic versioning.

Version surfaces:

- XamlPlayground product version
- extension manifest schema version
- extension API version
- preview protocol version
- contribution point version
- Avalonia compatibility range
- .NET target framework

Compatibility rules:

- Stable APIs remain backward compatible within a major version.
- Proposed APIs require an explicit manifest flag.
- Deprecated APIs remain available for at least one minor release cycle unless they expose a security issue.
- Preview protocol changes must be negotiated between editor, extension host, and preview host.
- Browser and desktop capabilities may differ even when the extension API version matches.

## Error Handling And Diagnostics

Extension failures must not crash the editor.

The host records:

- manifest validation errors
- activation failures
- command failures
- unhandled extension exceptions
- extension host crashes
- timeout cancellations
- rejected capability requests
- preview protocol failures
- contribution conflicts

Users should be able to inspect extension logs, disable the extension, reload the extension host, and include extension diagnostics in issue reports.

## Performance Rules

Extensions must:

- activate lazily
- avoid blocking the UI thread
- use cancellation tokens
- use immutable document snapshots for analysis
- debounce expensive document analysis
- stream large diagnostics or tree snapshots
- dispose subscriptions and view resources
- avoid holding stale preview element identities after reload

The host should enforce:

- activation time budgets
- command time budgets
- memory limits where supported
- extension host restart after crashes
- diagnostic throttling
- preview command throttling

## Built-In Extensions

First-party features may be implemented as built-in extensions over time:

- XAML editor language features
- C# editor language features
- visual designer tools
- toolbox
- resource editor
- control theme editor
- animation timeline
- diagnostics tree
- bindings inspector
- styles inspector
- browser preview tooling

Built-in extensions can use internal APIs during migration, but public extension APIs should be designed from the built-in use cases and kept host-neutral.

## Example Extension Scenarios

### Resource Inspector

Adds a resource inspector view, a command in the XAML editor context menu, and preview resource mutation commands. Requires workspace read, workspace write for fixes, and preview resource update capability.

### Animation Preset Pack

Adds timeline presets, easing catalogs, preview commands, and export actions. Requires animation contribution points and preview animation capability.

### Binding Diagnostics Tool

Adds a DevTools panel that listens to runtime binding diagnostics from the preview host, maps them to source ranges, and contributes quick fixes.

### Browser-Safe Snippet Pack

Adds commands, snippets, toolbox items, and templates. Requires no code execution and works in browser builds.

## Open Design Questions

- Whether the first public desktop extension host should be separate process from day one or start as an in-process compatibility layer over a message boundary.
- Whether extension UI should use raw Avalonia controls, host-rendered declarative UI, or both.
- How much preview participant code should be allowed in isolated full hosts.
- How extension packages should be signed and distributed for local builds.
- Whether browser extensions should be limited to declarative contributions in the first release.
