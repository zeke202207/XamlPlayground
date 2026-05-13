# XamlVisualEditor Preview And WYSIWYG Parity Plan

Date: 2026-05-13

## Goal

Bring XamlPlayground preview, workspace loading, and visual editing closer to the implementation model used by `/Users/wieslawsoltes/GitHub/XamlVisualEditor`, while keeping XamlPlayground's source-first editing model and browser support.

The target is not to copy UI wholesale. The target is to adopt the proven boundaries:

- workspace-aware target assembly selection;
- isolated previewer process for incompatible workspace assemblies;
- session-based remote preview transport;
- source/visual selection synchronization;
- typed property and structure editing backed by source-preserving XAML rewrites;
- a clear path for remote diagnostics/devtools access.

## Reviewed XamlVisualEditor Architecture

The relevant XamlVisualEditor flow is split into these parts:

- `XamlVisualEditor.Workspace/WorkspaceService.cs`
  - loads projects and records absolute project/file paths plus project-relative paths;
  - classifies XAML, project files, and output assemblies;
  - preserves enough metadata to select the owning project for a XAML file.
- `XamlVisualEditor.Shell.ViewModels/ProjectSelection.cs`
  - selects the preferred project for a file;
  - prefers executable projects;
  - ranks target frameworks with `net10.0`, `net9.0`, `net8.0`, and older .NET fallbacks;
  - resolves target assemblies from `OutputAssemblyPath` or `bin/Debug`/`bin/Release`.
- `XamlVisualEditor.Shell.ViewModels/PreviewerLaunchService.cs`
  - treats preview as a long-lived session per XAML file;
  - builds missing preview assets before launch;
  - sends design width/height to the previewer;
  - reports structured previewer errors with file, line, and column;
  - tracks previewer trust roots separately from normal editing.
- `XamlVisualEditor.Shell.ViewModels/PreviewerTcpSession.cs`
  - owns the remote designer transport connection;
  - sends preflight pixel format, render info, and viewport messages;
  - keeps pending XAML and viewport state until the previewer connects;
  - acknowledges frames and handles protocol error messages.
- `XamlVisualEditor.Designer.PreviewerHost/Program.cs`
  - runs the target assembly in a separate process;
  - uses `AssemblyDependencyResolver` against the target assembly;
  - registers a runtime XAML loader proxy inside that process.
- `XamlVisualEditor.Designer.Core`, `.Designer.Adorners`, `.Designer.Rendering`, and `DesignSurfaceView.axaml.cs`
  - maintain a dedicated design surface model, selection manager, adorners, drag/drop, and source sync;
  - keep visual editing commands separate from raw text editor commands.
- `XamlVisualEditor.PropertyEditor`
  - exposes richer typed property rows and value editor selection than the current playground property grid.

## Current XamlPlayground State

The playground now has:

- MSBuild workspace and solution loading with solution tree editing;
- in-process preview for host-compatible assemblies;
- ALC-based workspace assembly loading with `AssemblyDependencyResolver`;
- private runtime XAML loader use for workspace XAML where possible;
- isolated preview host based on the XamlVisualEditor previewer host model;
- source-first visual editing with structure, toolbox, property, theme, animation, and design inspection docks;
- local DevTools-style diagnostics docks for in-process previews.

This change also closes several parity gaps:

- remote preview sessions now keep viewport state instead of always sending a fixed framebuffer size;
- design `Width`, `Height`, `d:DesignWidth`, and `d:DesignHeight` are used when updating the remote previewer;
- previewer errors are represented with message, file, line, and column;
- local visual editing overlays are disabled for remote framebuffer previews because the local process cannot hit-test or mutate the remote visual tree;
- workspace preview target assembly selection now searches `bin/Debug` and `bin/Release` by preferred framework when Roslyn did not provide a usable `OutputAssemblyPath`.

## Remaining Gaps

### 1. Remote Preview Input

The current remote path displays preview frames, but it does not forward pointer, wheel, keyboard, or text input into the isolated previewer.

Implement:

- `RemotePreviewSurface` parent-side input capture;
- protocol mapping for pointer move/press/release, wheel, key down/up, and text input;
- focus management so the remote preview receives keyboard input after pointer press;
- tests that verify the transport sends input messages with correct coordinates and modifiers.

### 2. Remote Diagnostics And DevTools

In-process diagnostics panels can inspect only in-process controls. Remote previews currently set `DiagnosticsRoot = null`.

Implement:

- a child-process diagnostics service that snapshots visual/logical tree, resources, events, and selected node properties;
- protocol messages for tree snapshots, selected-node details, resources, and event stream;
- parent-side adapters that feed existing diagnostics docks from remote snapshots;
- selection commands that can request remote node selection without loading remote controls into the parent ALC.

The short-term fallback is to render child-process devtools inside the remote framebuffer, but the product-quality path is structured remote diagnostics.

### 3. Source And Visual Identity

XamlVisualEditor uses a dedicated design document and selection manager. XamlPlayground currently maps runtime visuals to XAML source through source info, names, and structural paths.

Implement:

- stable design identity attributes or attached metadata during runtime load;
- AST node IDs that survive non-overlapping edits;
- a shared `DesignerDocumentSession` that owns source snapshot, visual snapshot, selected node, and mutation queue;
- bidirectional sync rules for text selection, structure tree selection, visual selection, and remote diagnostics selection.

### 4. Typed Property Editing

The current property grid can write raw attributes and several dedicated editor rows. XamlVisualEditor has a richer property editor model.

Implement:

- typed value editors for bool, enum, numeric, thickness, color, brush, geometry, font, image/source URI, command, binding, resource reference, and collection values;
- attached-property discovery grouped by owner type;
- property metadata display: default value, inheritance, binding support, animation support, readonly/direct/styled/attached kind;
- safe conversion/validation before writing source;
- resource picker and binding picker integration with existing resource/binding inspectors.

### 5. Layout-Aware WYSIWYG Operations

The playground already supports move, resize, drop, reparent, reorder, wrap, unwrap, duplicate, delete, and theme/animation editing. The next parity layer is richer layout intent.

Implement:

- grid row/column insertion and reassignment;
- DockPanel dock editing;
- relative/stack/wrap layout insertion previews;
- SnapLine/ruler/grid adorner layer similar to XamlVisualEditor;
- multi-selection with align, distribute, group/wrap, and batch property operations;
- preview-only live transforms during drag with source commit on release.

### 6. Preview Trust And Process Lifetime

XamlVisualEditor separates previewer trust from normal file loading. XamlPlayground starts the isolated preview host automatically for mismatched workspace assemblies.

Implement:

- workspace preview trust prompt for first isolated execution;
- persisted trusted roots;
- "allow once" behavior;
- previewer restart, stop, and clean reload commands;
- host crash telemetry and clear error surfacing in the errors dock.

### 7. Workspace Loading Polish

Remaining workspace loading improvements:

- project selector when multiple projects can own or preview a XAML file;
- explicit target framework/configuration selector;
- output assembly fallback display in the workspace panel;
- restore/build status per project instead of a single workspace status string;
- source-generated file awareness without loading generated intermediates into the editable tree by default.

## Suggested Implementation Order

1. Remote input bridge.
   This makes isolated previews feel like normal previews and is independent of property editing.

2. Remote diagnostics snapshots.
   This restores useful DevTools panels for isolated workspaces without assembly collisions.

3. `DesignerDocumentSession`.
   This creates the same central coordination point that XamlVisualEditor has through its designer document and selection manager.

4. Typed property editor layer.
   Build on the existing property dock and mutation engine.

5. Layout-specific WYSIWYG tools.
   Add richer adorners and layout commands once selection identity is stable.

6. Trust and preview process management UI.
   Add before isolated preview becomes the default for more workspace shapes.

7. Project/framework selection UI.
   Add after the basic previewer behavior is stable, because it affects all workspace loading flows.

## Test Strategy

Each slice should include:

- unit tests for path/project/assembly selection;
- protocol tests for previewer messages;
- integration tests with two sample projects that reference different versions of the same assembly name;
- headless visual tests for in-process WYSIWYG adorners;
- remote preview tests that assert no target workspace assemblies are loaded into the parent process;
- diagnostics tests that verify remote visual/logical tree panels can inspect isolated previews.

## Acceptance Criteria

- Loading a workspace with mismatched Avalonia assemblies uses isolated preview automatically after trust is granted.
- The original XAML is sent to the previewer; no event-handler or root-type stripping is used.
- Workspace target assembly resolution works even when Roslyn does not provide a current `OutputFilePath`.
- In-process previews retain local visual editing and diagnostics.
- Remote previews render, resize, surface structured errors, and do not expose stale local WYSIWYG overlays.
- The plan above is implemented in small, testable slices rather than one large refactor.
