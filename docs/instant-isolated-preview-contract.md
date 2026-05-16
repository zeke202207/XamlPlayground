# Instant Isolated Preview Contract

Date: 2026-05-16

## Objective

XamlPlayground preview tooling needs a clear boundary between editor state and runtime state. DevTools, visual editors, property editors, resource mutation, animations, MSBuild workspace preview, and browser preview should all operate through a shared preview contract instead of directly assuming that user code lives inside the parent editor process.

The editor owns source changes. Preview hosts own runtime state. Hosts accept small live updates when the change is safe, and they recompile or restart when the contract says the change cannot be applied safely.

## Snapshot Contract

`PreviewSnapshot` is the immutable handoff unit. It captures:

- solution/project identity
- root namespace and assembly name
- active XAML path and code-behind path
- `App.axaml`
- target framework and output assembly path
- all project files, including resources and project files
- workspace assembly references

All paths are normalized to `/`. The snapshot is intentionally host-neutral so the same shape can drive inline design preview, the existing desktop remote designer host, a future full desktop host, and a browser iframe host.

## Change Contract

`PreviewChangeClassifier` compares the next snapshot to the last loaded snapshot:

- `XamlOnly`: active loose XAML can be sent as an instant update.
- `ResourcesOnly` / `XamlAndResources`: resource-aware hosts can update resources; otherwise restart/rebuild.
- `CodeOrProject` / `References`: compile inside the host when supported, otherwise rebuild output and restart/update host.
- `Mixed`: restart the host.

`PreviewSessionManager` turns that classification into a `PreviewReloadPlan` for the host capabilities.

## Compiler Contract

`CompilerService` is split into two responsibilities:

- `EmitProjectAssembly(...)`: returns PE bytes and diagnostics without loading user code into the parent.
- `GetProjectAssembly(...)`: inline design-preview path that emits, then loads into a collectible workspace context.

This gives full preview hosts a no-load contract while preserving the existing fast inline design-preview behavior.

## Implemented Slice

Implemented files:

- `src/XamlPlayground/Services/Preview/PreviewContracts.cs`
- `src/XamlPlayground/Services/Preview/PreviewToolingContracts.cs`
- `src/XamlPlayground/Services/Preview/PreviewSnapshotFactory.cs`
- `src/XamlPlayground/Services/Preview/PreviewChangeClassifier.cs`
- `src/XamlPlayground/Services/Preview/PreviewSessionManager.cs`

Runtime behavior changed:

- MSBuild remote preview now treats the active XAML/resource document as loose live input.
- If the built output assembly exists and only the active loose document is newer than output, the app sends `UpdateXamlMessage` to the isolated preview host instead of forcing `dotnet build`.
- `MainViewModel` exposes `PreviewStatus` and `PreviewReloadStrategy` for tooling/status surfaces.

## Remaining Host Work

The current isolated remote preview still uses Avalonia's designer framebuffer protocol. It gives process isolation and instant active-XAML updates, but not a custom remote DevTools/property/resource/animation command channel.

Next phases:

1. Add a host command channel beside framebuffer messages.
2. Route selected node, property, resource, and animation edit commands through the preview session identity.
3. Move full-preview compilation and resource loading entirely into a dedicated desktop host process.
4. Add browser iframe transport using the same message envelope.
5. Add host-owned diagnostics/DevTools surfaces, with optional remote tree/property snapshots mirrored in the parent UI.
