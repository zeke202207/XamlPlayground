# AvalonAI Edit Inline Features Spec

## Goals

Implement VS Code-style inline editor capabilities on top of AvaloniaEdit:

- Peek editor panels that open code, XAML, or theme definitions inline without switching tabs.
- Inline controls at document offsets for actions such as run, apply, inspect, or AI-assisted edits.
- CodeLens-style annotations above code or XAML declarations, for example `author: wieslaw` and `usages: 205`.
- A pluggable API so feature providers can be added without modifying the editor control.

## VS Code Model

VS Code builds these features on separate editor primitives:

- `ZoneWidget` reserves vertical space by adding a view zone, then positions a widget over that reserved area. It also handles frame/arrow rendering, relayout, scroll reveal, and resize behavior.
- `PeekViewWidget` subclasses `ZoneWidget` and adds a title/header, actions, close behavior, themed colors, and a body area that can host an embedded editor.
- CodeLens uses language feature providers to collect lens models, then renders small actionable rows near source lines and resolves commands lazily around the viewport.
- Inlay hints and inline completions follow the same provider-driven pattern but render lighter inline text/adornment content instead of a full zone panel.

Primary sources:

- [VS Code `ZoneWidget`](https://raw.githubusercontent.com/microsoft/vscode/main/src/vs/editor/contrib/zoneWidget/browser/zoneWidget.ts)
- [VS Code `PeekViewWidget`](https://raw.githubusercontent.com/microsoft/vscode/main/src/vs/editor/contrib/peekView/browser/peekView.ts)
- [VS Code CodeLens controller](https://raw.githubusercontent.com/microsoft/vscode/main/src/vs/editor/contrib/codelens/browser/codelensController.ts)
- [VS Code source organization](https://github.com/microsoft/vscode/wiki/source-code-organization)
- [VS Code Programmatic Language Features: CodeLens](https://code.visualstudio.com/api/language-extensions/programmatic-language-features#codelens---show-actionable-context-information-within-source-code)

## AvaloniaEdit Mapping

AvaloniaEdit does not expose VS Code's `ViewZone` primitive directly. The compatible mapping is:

- Use `VisualLineElementGenerator` and `InlineObjectElement` to insert zero-length controls into a visual line.
- Use an invisible zero-width spacer control to reserve vertical height at a line boundary.
- Use a custom `TextView` overlay layer to arrange real zone content over the reserved height.
- Use `InlineObjectElement` directly for actual inline controls inside text flow.
- Use grouped annotation zones before a line for CodeLens-style rows.

Primary AvaloniaEdit source concepts:

- [AvaloniaEdit `TextView`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit/Rendering/TextView.cs)
- [AvaloniaEdit `VisualLineElementGenerator`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit/Rendering/VisualLineElementGenerator.cs)
- [AvaloniaEdit `InlineObjectElement`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit/Rendering/InlineObjectRun.cs)
- [AvaloniaEdit `IBackgroundRenderer`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit/Rendering/IBackgroundRenderer.cs)

## Implemented Surface

`MinimapTextEditor` now exposes:

- `InlineFeaturesEnabled`
- `InlineViewZones`
- `InlineControls`
- `InlineAnnotations`
- `InlineExtensions`
- `ShowInlinePeek(...)`
- `CloseInlinePeek()`
- `AddInlineViewZone(...)`
- `AddInlineControl(...)`
- `AddInlineAnnotation(...)`

Core model types:

- `EditorViewZone`: reserved vertical region before or after a line.
- `EditorInlineControl`: interactive control inserted at a document offset.
- `EditorCodeAnnotation`: CodeLens-style row item.
- `IEditorInlineExtension`: pluggable feature entry point.
- `EditorInlineExtensionContext`: provider context for adding zones, controls, annotations, and peek panels.

## XamlPlayground Wiring

The playground uses `PlaygroundInlineFeatures` as an attached behavior on every `MinimapTextEditor` surface:

- Docked sample XAML editor: `SampleXaml` mode.
- Docked sample C# editor: `SampleCode` mode.
- Main `CodeView` XAML/C# tabs: `SampleXaml` and `SampleCode` modes.
- Workspace file editor: `WorkspaceFile` mode with the active `InMemoryProjectFile`.

Current providers:

- `PlaygroundXamlInlineExtension` adds CodeLens-style resource annotations, unresolved-resource diagnostics, ControlTheme part/state annotations, inline `Peek` controls for `{StaticResource ...}` and `{DynamicResource ...}`, and peek panels for definitions/usages.
- `PlaygroundCSharpInlineExtension` adds CodeLens-style author and usage annotations for classes, constructors, methods, and properties, with reference peek panels across the current project snapshot.
- `TextEditorBehavior` installs a VS Code-style editor context menu with selection editing, resource definition/reference peek, IntelliSense entry points, formatting, commenting, indentation, folding, and select-all commands. Resource actions are enabled only when the current XAML cursor location resolves to a resource reference.

## Provider Contract

Providers implement `IEditorInlineExtension`:

```csharp
public sealed class ThemePeekExtension : IEditorInlineExtension
{
    public IDisposable Attach(EditorInlineExtensionContext context)
    {
        var annotation = new EditorCodeAnnotation
        {
            LineNumber = 12,
            Text = "peek theme",
            Command = new RelayCommand(() =>
                context.ShowPeek(12, "Button theme", "Fluent.axaml", themeSource, "xaml"))
        };

        context.Annotations.Add(annotation);

        return new EditorInlineRegistration(() => context.Annotations.Remove(annotation));
    }
}
```

Extensions own their registrations and should remove them from the context when disposed.

## Feature Plan

Completed foundation:

- Add editor-level extension collections and helper methods.
- Simulate VS Code view zones with an invisible spacer generator plus overlay layer.
- Add exclusive peek panels with embedded read-only `TextEditor`.
- Add inline controls through AvaloniaEdit inline object generation.
- Add CodeLens-style annotations grouped by source line.
- Add unit coverage for insertion mapping, peek replacement, helper APIs, and extension lifecycle.

Completed playground provider layer:

- XAML theme-definition peek provider: resolve `{DynamicResource ...}`, `{StaticResource ...}`, and control themes to source snippets.
- C# symbol/reference annotations: use Roslyn syntax snapshots to collect lightweight project references.
- XAML resource/use annotations: show resource type, usage count, ControlTheme parts/states, and diagnostics above relevant declarations.
- VS Code-style context menu provider: Cut, Copy, Paste, Peek Definition, Go to Definition, Peek References, Close Peek, Show Suggestions, Parameter Hints, Quick Info, Format Document, Toggle Line Comment, Indent/Outdent Selection, Fold/Unfold, Fold All/Unfold All, and Select All.

Next provider layer:

- C# semantic symbol peek provider: use Roslyn compilation/workspace symbols to resolve overloads and external definitions accurately.
- XAML binding/style-selector provider: annotate bindings, compiled-binding data contexts, style selectors, and template bindings.
- Inline AI action controls: apply patch, explain binding, generate preview sample, convert hard-coded value to resource.

Polish and performance:

- Virtualize expensive provider refreshes to visible ranges.
- Debounce document changes.
- Add provider cancellation.
- Add per-zone resize support.
- Persist user settings for enabled providers.
- Add theme resources for peek/annotation colors instead of hard-coded fallback brushes.

## Constraints

- The initial view-zone simulation is line based. Zones before a line are first-class; zones after the final line are supported as best effort but should normally target a real following line.
- Inline controls are real Avalonia controls and can receive input, but callers should avoid many heavy controls in large visible ranges.
- Providers should not mutate editor text during rendering callbacks. Use commands, dispatcher scheduling, or explicit user actions.
