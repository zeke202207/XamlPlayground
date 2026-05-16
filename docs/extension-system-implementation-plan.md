# XamlPlayground Extension System Implementation Plan

Date: 2026-05-16

## Objective

Implement an extension system for XamlPlayground in phases, using VS Code extension concepts while preserving the .NET/Avalonia architecture, current preview isolation work, browser constraints, and host-owned editor services.

The first useful outcome should be a small, safe extension platform that can load manifests, register declarative commands and menus, activate a trusted .NET extension, and expose enough APIs to move selected built-in tools toward extension-shaped boundaries.

## Current Implementation Baseline

The initial implementation now includes:

- In-memory extension manifests and contribution contracts.
- VS Code-style `xamlplayground.extension.json` parsing.
- Directory package discovery for manifest-only extensions, with tolerant per-manifest load errors.
- Opt-in trusted .NET extension assembly activation.
- Lazy command activation and command execution through `ExtensionHostService`.
- Implicit activation for command, view, preview, language, theme, animation, diagnostics, and DevTools-style events.
- Built-in XamlPlayground contributions for workbench views, perspectives, preview providers, editor features, theme/resource editors, diagnostics, visual editing, workspace features, animation tooling, and toolbox snippets.
- Built-in command handlers wired to the existing shell command surface.
- A host-rendered `Extensions` command menu backed by extension command contributions.
- Dock tool and perspective descriptors driven by built-in extension contributions.
- Visual designer toolbox population that includes extension-contributed toolbox items.
- Registration-time duplicate contribution id validation for all current contribution families.
- Collectible trusted assembly loading and instance disposal for development/local .NET extensions.

## Constraints

- Do not require existing editor, preview, or visual designer features to be rewritten before the extension system can land.
- Do not expose direct shell, docking, editor control, or preview runtime object access as public API.
- Do not treat browser and desktop as equivalent runtimes. They share manifest and contribution metadata, but expose different execution capabilities.
- Do not load untrusted extension code into the parent editor process once out-of-process hosting is available.
- Keep preview tooling aligned with `PreviewSnapshot`, `PreviewChangeSet`, `PreviewReloadPlan`, `PreviewMessage`, and `PreviewToolingCommand`.

## Phase 0: Design And Inventory

Goals:

- Confirm extension scenarios and API boundaries.
- Inventory current built-in surfaces that should become contribution points.
- Decide the initial extension host transport.

Work items:

- Inventory command-like actions in `MainViewModel`, editor services, designer services, preview services, theme services, diagnostics views, and animation services.
- Inventory dock views and map them to future view containers: left, right, bottom, preview, devtools, theme editor, and animation timeline.
- Inventory context menu and toolbar actions that should become menu locations.
- Inventory editor provider surfaces: completion, hover, code actions, snippets, inline features, and formatting.
- Inventory preview tooling needs against existing preview contracts.
- Define a minimal public API namespace such as `XamlPlayground.Extensions`.
- Define extension host protocol messages before implementing in-process shortcuts.

Deliverables:

- Contribution point list.
- Initial API shape.
- Host boundary decision record.
- Compatibility matrix for desktop and browser runtimes.

## Phase 1: Manifest Registry

Goals:

- Load and validate extension manifests without executing extension code.
- Resolve declarative contributions.
- Surface manifest errors in diagnostics.

Core types:

- `ExtensionManifest`
- `ExtensionIdentity`
- `ExtensionVersion`
- `ExtensionEngineRange`
- `ExtensionKind`
- `ExtensionCapabilityDeclaration`
- `ExtensionContributionSet`
- `ExtensionRegistry`
- `ExtensionManifestValidator`
- `ExtensionInstallLocation`

Work items:

- Add JSON schema for `xamlplayground.extension.json`.
- Implement manifest parsing with strict validation and useful diagnostics.
- Support built-in extension locations.
- Support user extension locations.
- Support disabled extension state.
- Resolve extension dependencies and dependency cycles.
- Validate command identifiers, menu locations, view ids, activation event syntax, capability declarations, and engine ranges.
- Add diagnostics for invalid manifests.
- Add unit tests for valid manifests, malformed manifests, dependency cycles, incompatible engine ranges, duplicate ids, duplicate commands, and invalid contribution points.

Success criteria:

- XamlPlayground can discover extension packages and list manifest metadata.
- Invalid extensions do not break startup.
- No extension code executes during registry load.

## Phase 2: Contribution Resolver

Goals:

- Merge declarative contributions into host-owned registries.
- Keep all UI placement under host control.

Core services:

- `IContributionResolver`
- `ICommandContributionRegistry`
- `IMenuContributionRegistry`
- `IKeybindingContributionRegistry`
- `IViewContributionRegistry`
- `IConfigurationContributionRegistry`
- `IContextKeyService`

Work items:

- Implement command contribution registry.
- Implement menu contribution registry with locations:
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
- Implement restricted `when` expression parser and evaluator.
- Implement context keys for editor language, preview session, preview mode, designer mode, selected element type, diagnostics state, and browser runtime.
- Implement contribution conflict diagnostics.
- Add tests for menu visibility, command enablement, and context key updates.

Success criteria:

- Built-in and external manifest contributions can appear in command palette and menu models.
- Menu visibility changes when context keys change.
- Contributions can be disabled by disabling the extension.

## Phase 3: Command Service And Activation

Goals:

- Route command invocation through extension activation.
- Support lazy extension activation.
- Keep long-running extension work cancellable and observable.

Core types:

- `IExtensionHost`
- `IExtensionActivator`
- `IExtensionContext`
- `ICommandService`
- `ICommandRegistration`
- `ExtensionActivationEvent`
- `ExtensionActivationResult`
- `ExtensionRuntimeState`

Work items:

- Implement activation event matching for:
  - `onStartupFinished`
  - `onCommand:<commandId>`
  - `onView:<viewId>`
  - `onLanguage:xaml`
  - `onLanguage:csharp`
  - `onPreview:<mode>`
  - `onPreviewSession`
  - `onThemeEditor`
  - `onAnimationEditor`
  - `workspaceContains:<glob>`
- Implement command registration and command invocation APIs.
- Add cancellation, progress, and timeout support.
- Add activation failure diagnostics.
- Add extension runtime log channel.
- Add tests for lazy activation, failed activation, command cancellation, and command error reporting.

Success criteria:

- Commands contributed by inactive extensions are visible.
- Invoking a command activates its extension.
- Failed extensions are isolated from the rest of the command system.

## Phase 4: Trusted In-Process .NET Host

Goals:

- Enable first-party and trusted local .NET extensions while preserving the message-shaped boundary.
- Prove the extension entry point and activation lifecycle.

Implementation approach:

- Load trusted extension assemblies through a collectible `AssemblyLoadContext`.
- Communicate through the same logical request/response interfaces intended for out-of-process hosting.
- Restrict this host to trusted built-in or development extensions.

Work items:

- Define `IXamlPlaygroundExtension`.
- Implement .NET assembly discovery from `main`.
- Load extension dependencies from the extension package directory.
- Provide `IExtensionContext`.
- Implement lifecycle:
  - load
  - activate
  - deactivate
  - unload
  - reload
- Track disposables registered by extension APIs.
- Add crash and exception diagnostics.
- Add tests for activation, deactivation, unload, duplicate registrations, and disposable cleanup.

Success criteria:

- A sample trusted extension can register a command and view provider.
- Disabling the extension removes its contributions and disposes runtime registrations.
- The implementation can later be replaced by an out-of-process host without changing manifests.

## Phase 5: Public API Slice

Goals:

- Expose the smallest useful API set for real extensions.

Initial APIs:

- Commands
- Context keys
- Configuration
- Workspace read
- Documents and text edits
- Diagnostics publish
- Basic views
- Preview session metadata
- Extension storage

Work items:

- Define immutable document snapshot API.
- Define text edit and workspace edit API.
- Route edits through existing editor/storage services.
- Define diagnostics collection API with owner extension id.
- Define storage API for global and workspace mementos.
- Define configuration read API and configuration change events.
- Define command invocation API.
- Define view provider API.
- Add API version attributes or compatibility metadata.
- Add XML docs for public API interfaces.

Success criteria:

- A sample extension can inspect the active XAML document, publish diagnostics, offer a command, and apply a host-mediated text edit.
- Undo/redo, preview reload classification, diagnostics, and selection state continue to be owned by the host.

## Phase 6: Views And Tools

Goals:

- Let extensions add dockable tools without taking ownership of the shell or Dock layout internals.

Work items:

- Define view provider contracts.
- Decide first UI model:
  - Avalonia control bridge for trusted desktop extensions.
  - Host-rendered declarative model for browser and untrusted extensions.
- Add view containers for left, right, bottom, preview, devtools, theme editor, and animation timeline.
- Add view lifecycle:
  - create
  - show
  - hide
  - dispose
  - save state
  - restore state
- Add view context with command invoker, theme resources, storage, and cancellation.
- Add diagnostics for view creation failures.
- Add tests for view registration, container placement, visibility, disposal, and disabled extensions.

Success criteria:

- A sample extension can contribute a bottom tool view.
- The host controls placement and lifetime.
- View failures do not crash the editor shell.

## Phase 7: XAML And Editor Contributions

Goals:

- Add extension points for XAML and C# authoring without exposing editor control internals.

Contribution points:

- snippets
- completion providers
- hover providers
- code action providers
- formatting providers
- document symbols
- inline adornments
- context actions
- toolbox items
- property editors
- markup extension editors

Work items:

- Create provider APIs based on immutable document snapshots.
- Return structured edits and commands.
- Route all edits through host document services.
- Add cancellation and debounce behavior.
- Add provider ordering and conflict rules.
- Add toolbox contribution support for XAML snippets and control templates.
- Add property editor contribution metadata.
- Add tests for provider registration, cancellation, edit application, and invalid provider results.

Success criteria:

- A sample extension can add a XAML snippet, a toolbox item, and a code action.
- Text changes still integrate with preview snapshots and diagnostics.

## Phase 8: Preview Tooling Integration

Goals:

- Let extensions inspect and influence previews through the existing preview session boundary.

Work items:

- Expose preview session events:
  - created
  - snapshot changed
  - reload planned
  - host started
  - host stopped
  - diagnostics received
- Expose preview capabilities from `PreviewHostCapabilities`.
- Add extension API wrappers over `PreviewToolingCommand`.
- Support command kinds:
  - inspect tree
  - select element
  - set property value
  - set resource reference
  - set resource value
  - begin animation preview
  - stop animation preview
  - request diagnostics
- Add preview toolbar and preview context menu contribution locations.
- Add preview overlay/adornment contribution design, but keep runtime control access unavailable.
- Add protocol version negotiation.
- Add tests with fake preview hosts for successful commands, unsupported commands, stale sessions, and host restarts.

Success criteria:

- A DevTools-style sample extension can request a preview tree snapshot, show it in a view, and select an element through the preview API.
- Unsupported preview capabilities are reported clearly.
- Browser iframe and isolated host constraints are visible to extensions.

## Phase 9: Theme, Resources, And Animation APIs

Goals:

- Add extension points for the domain-specific tools that make XamlPlayground different from a generic editor.

Theme/resource work items:

- Expose resource dictionary snapshots.
- Expose resource edit operations by file path, key, value kind, and scope.
- Add resource analyzer provider API.
- Add resource value editor contribution point.
- Add palette and swatch provider contribution point.
- Add control theme inspector provider API.
- Connect live resource updates to preview host capability checks.
- Add tests for static resource, dynamic resource, literal resource, invalid resource, and host unsupported cases.

Animation work items:

- Expose selected target identity from the preview/design surface.
- Add animation preset contribution point.
- Add easing provider contribution point.
- Add timeline serialization contract.
- Route preview playback through preview tooling commands.
- Add tests for timeline preview, invalid target property, stale target identity, and host restart.

Success criteria:

- A sample theme extension can add a palette provider and apply dynamic resource edits to a capable preview host.
- A sample animation extension can preview a timeline on a selected element without direct runtime object access.

## Phase 10: Diagnostics And DevTools

Goals:

- Make diagnostics and runtime inspection extensible.

Work items:

- Add diagnostics publisher API.
- Add diagnostics transformer API.
- Add quick fix provider API.
- Add DevTools panel contribution point.
- Add preview diagnostics subscription API.
- Add visual tree, logical tree, binding, style, resource, and layout snapshot request APIs where supported by the host.
- Add diagnostic source filtering by extension id.
- Add tests for diagnostic publishing, clearing, quick fixes, preview diagnostic mapping, and extension failure diagnostics.

Success criteria:

- A sample binding diagnostics extension can publish diagnostics, show a DevTools panel, and offer a quick fix.

## Phase 11: Browser Runtime

Goals:

- Support browser-safe extensions without weakening the desktop security model.

Work items:

- Define browser extension kind and compatibility checks.
- Support declarative-only extensions.
- Support browser entry points for approved JavaScript or WebAssembly-safe modules.
- Restrict capabilities:
  - no process execution
  - no native dependencies
  - no arbitrary filesystem
  - network subject to policy and CORS
  - preview access through iframe message transport
- Add browser storage implementation.
- Add browser-compatible view model rendering.
- Add package size and load-time diagnostics.
- Add tests for manifest filtering, unsupported desktop-only extensions, iframe preview commands, storage quota errors, and network denial.

Success criteria:

- Browser builds can load snippet/toolbox/theme metadata extensions.
- Desktop-only extensions are rejected or ignored with clear diagnostics.
- Browser preview tools use the same logical API with reduced capabilities.

## Phase 12: Out-Of-Process Extension Host

Goals:

- Move untrusted extension code outside the parent editor process.

Work items:

- Define extension host process executable.
- Implement JSON-RPC or typed IPC transport.
- Add request ids, cancellation, progress, tracing, and protocol version negotiation.
- Add host process lifecycle:
  - start
  - activate extension
  - deactivate extension
  - restart after crash
  - shutdown
- Add capability enforcement in both editor and extension host.
- Add serialization for API DTOs.
- Add streaming for large diagnostics and tree snapshots.
- Add tests for host crash, protocol mismatch, cancellation, timeout, and restart recovery.

Success criteria:

- A non-built-in .NET extension runs outside the editor process.
- A crashing extension host does not crash the editor.
- Existing extension APIs continue to work through the transport.

## Phase 13: Security, Trust, And Package Management

Goals:

- Make installation and execution policy explicit.

Work items:

- Add extension enable/disable UI.
- Add per-workspace enablement.
- Add workspace trust integration.
- Add capability prompts.
- Add trusted publisher support.
- Add package signature verification.
- Add package checksum verification.
- Add extension quarantine after repeated crashes.
- Add audit log for sensitive operations.
- Add secret storage API.
- Add network allowlist policy.
- Add native dependency policy.
- Add tests for denied capabilities, revoked capabilities, untrusted workspace behavior, signature failure, and quarantine.

Success criteria:

- Users can see what an extension can do before enabling it.
- Sensitive APIs cannot be called unless declared and granted.
- Extension updates can add capabilities only with renewed approval.

## Phase 14: Versioning And Compatibility

Goals:

- Keep the extension platform maintainable as XamlPlayground evolves.

Work items:

- Version the manifest schema.
- Version the public extension API.
- Version preview tooling protocol messages.
- Add compatibility checks for:
  - XamlPlayground version
  - Avalonia version
  - .NET target
  - browser support
  - preview protocol version
- Add proposed API opt-in.
- Add deprecation diagnostics.
- Add compatibility tests with fixture manifests.

Success criteria:

- Incompatible extensions are rejected before activation.
- Deprecated APIs produce actionable diagnostics.
- Preview protocol changes are negotiated rather than assumed.

## Phase 15: Developer Experience

Goals:

- Make extension development practical.

Work items:

- Add extension template project.
- Add manifest schema file for editor validation.
- Add sample extensions:
  - command and menu sample
  - bottom view sample
  - XAML snippet/toolbox sample
  - resource analyzer sample
  - preview DevTools sample
  - browser-safe declarative sample
- Add extension development mode.
- Add reload extension command.
- Add extension host logs view.
- Add package command.
- Add local install command.
- Add documentation for capabilities, contribution points, and APIs.

Success criteria:

- A developer can create, run, reload, and debug a local extension.
- Samples cover the main platform features.

## Suggested Initial Vertical Slice

The first implementation should be intentionally narrow:

1. Manifest registry and validation.
2. Declarative commands and command palette entries.
3. `onCommand` activation.
4. Trusted in-process .NET extension host.
5. Minimal `Commands`, `Documents`, `Diagnostics`, and `Storage` APIs.
6. One sample extension that reads the active XAML document, publishes a diagnostic, and provides a command that applies a host-mediated text edit.

This validates the package model, activation lifecycle, API shape, diagnostics, and disposal behavior without taking on preview tooling, browser hosting, or untrusted execution immediately.

## Testing Strategy

Unit tests:

- manifest parsing
- schema validation
- contribution resolution
- context expression evaluation
- activation event matching
- command routing
- API capability enforcement
- diagnostics publishing
- text edit application
- storage scopes

Integration tests:

- load sample extension
- invoke command from command palette model
- open contributed view
- apply workspace edit
- publish diagnostics
- disable extension
- reload extension
- simulate activation failure
- simulate extension host crash

Preview tests:

- fake preview host capabilities
- preview tree request
- preview element selection
- resource update supported
- resource update unsupported
- animation preview command
- stale session rejection
- protocol version mismatch

Browser tests:

- browser-compatible manifest accepted
- desktop-only manifest rejected
- declarative command contribution loaded
- iframe preview command routed
- storage quota failure handled
- unsupported capability hidden or rejected

Security tests:

- undeclared capability rejected
- denied capability rejected
- revoked capability rejected
- workspace write blocked in untrusted workspace
- network request blocked without allowlist
- process execution blocked by default

## Migration Strategy For Built-In Features

Existing built-in features should migrate gradually:

1. Define public contribution and API shapes from the built-in feature.
2. Wrap the existing implementation with host-owned services.
3. Register the feature through built-in extension metadata.
4. Move command/menu/view declarations into built-in manifests.
5. Keep internal access only where public APIs are not ready.
6. Replace internal access with public APIs when stable.

Candidate migrations:

- visual toolbox
- XAML inline features
- resource editor
- control theme editor
- animation timeline
- diagnostics tree
- bindings inspector
- styles inspector
- preview toolbar actions

## Risks And Mitigations

Risk: public APIs expose too much editor internals.

Mitigation: start with DTOs, immutable snapshots, command routing, and text edits. Keep controls, dock models, and preview runtime objects private.

Risk: in-process trusted host becomes permanent.

Mitigation: design APIs as message-shaped contracts from the start and limit in-process hosting to built-in and development extensions.

Risk: browser support blocks desktop progress.

Mitigation: share manifests and contribution metadata, but ship desktop execution first. Browser can initially support declarative-only extensions.

Risk: preview tooling becomes a second extension API.

Mitigation: route preview tooling through `PreviewToolingCommand` and preview session capabilities. Keep preview host behavior negotiated and asynchronous.

Risk: extension UI destabilizes the shell.

Mitigation: host owns containers and lifecycle. Prefer host-rendered declarative UI for untrusted and browser extensions.

## Open Decisions

- Choose JSON-RPC, MessagePack-RPC, StreamJsonRpc, or a custom typed protocol for the extension host boundary.
- Choose whether desktop extension views initially use trusted Avalonia controls or a declarative UI schema.
- Choose package format: directory, zip archive, NuGet package, or VSIX-like package.
- Choose signature and publisher trust model.
- Choose whether extension host process is per extension, per workspace, or shared.
- Choose whether browser extensions can execute code in the first release or remain declarative-only.
