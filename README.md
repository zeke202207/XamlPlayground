# Xaml Playground for Avalonia

[![CI](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/build.yml)
[![Release](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/release.yml)
[![Deploy to GitHub Pages](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/XamlPlayground/actions/workflows/pages.yml)

<img src="src/XamlPlayground/Assets/Logo.svg" alt="Xaml Playground logo" width="160">

Xaml Playground is an interactive XAML editor and previewer for Avalonia.

## NuGet Packages

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [`XamlPlayground`](https://www.nuget.org/packages/XamlPlayground/) | [![NuGet](https://img.shields.io/nuget/v/XamlPlayground.svg)](https://www.nuget.org/packages/XamlPlayground/) | [![NuGet downloads](https://img.shields.io/nuget/dt/XamlPlayground.svg)](https://www.nuget.org/packages/XamlPlayground/) | Core Avalonia XAML editor and previewer library. |
| [`XamlPlayground.AvaloniaEdit.Minimap`](https://www.nuget.org/packages/XamlPlayground.AvaloniaEdit.Minimap/) | [![NuGet](https://img.shields.io/nuget/v/XamlPlayground.AvaloniaEdit.Minimap.svg)](https://www.nuget.org/packages/XamlPlayground.AvaloniaEdit.Minimap/) | [![NuGet downloads](https://img.shields.io/nuget/dt/XamlPlayground.AvaloniaEdit.Minimap.svg)](https://www.nuget.org/packages/XamlPlayground.AvaloniaEdit.Minimap/) | AvaloniaEdit minimap and inline editor extension controls. |

## Visual XAML Editing

Xaml Playground includes a design-mode visual editor for Avalonia XAML. The preview can run in two explicit modes:

* **Run mode** leaves the preview alone. Controls receive normal pointer, keyboard, focus, drag/drop, selection, and editing input just like a running Avalonia app.
* **Design mode** turns the preview into a WYSIWYG designer. Runtime control interaction is disabled, designer hit testing is enabled, and the overlay handles selection, movement, resizing, insertion, structure edits, and XAML synchronization.

Design mode can be toggled from the visible preview toolbar/menu. When it is off, no designer adorners are rendered and toolbox drops are ignored by the designer surface.

### Canvas Editing

The designer supports direct editing on the preview canvas:

* Select rendered controls from the preview using visual hit testing mapped back to the matching XAML element.
* Keep XAML editor selection, structure selection, property selection, and preview selection synchronized by structural XAML paths, including repeated anonymous elements of the same type.
* Move selected controls with pointer drag or keyboard nudging.
* Resize selected controls with eight sizing thumbs and live preview updates while dragging.
* Preserve selection after drag, drop, resize, and mutation operations.
* Move controls within panels, reorder around sibling controls, and move controls between compatible parent containers.
* Show drop target, insertion line, placeholder, alignment guides, and measurement annotations while dragging.
* Snap-style guide feedback shows alignment against nearby rendered controls and reports size and position during manipulation.
* Support Canvas placement with `Canvas.Left` and `Canvas.Top`; non-Canvas movement writes `Margin`; resizing writes `Width` and `Height`.

### Toolbox

The visual toolbox is generated dynamically from available Avalonia controls and editor metadata:

* Search and filter toolbox items.
* Drag toolbox items into the preview.
* Preview the exact target container and insertion position before dropping.
* Insert into panels at the visual child position.
* Insert into Canvas at the drop coordinates.
* Select the newly inserted element and update the designer overlay immediately after a successful drop.

### Structure And Properties

The designer panels use ProDataGrid-backed editing surfaces:

* Structure view uses a hierarchical data model for the XAML tree.
* Structure operations include refresh, wrap, unwrap, delete, duplicate, move up, and move down.
* Properties view exposes current attributes and available editor fields for the selected control.
* Property fields are grouped by category and filterable.
* Property names show the actual XAML/property name.
* Property editing supports set, reset, remove, suggested values, and bidirectional refresh back to XAML.

### XAML Synchronization

The visual editor is backed by the XAML mutation pipeline and structural AST mapping:

* Designer selection updates the XAML editor selection range.
* XAML editor selection updates the preview selection and designer panels.
* Structure mutations update the XAML document and refresh the rendered preview.
* Property mutations update XAML attributes without losing the selected element.
* Preview hit testing maps rendered controls back to XAML elements using structural paths instead of selecting only the first control of a matching type.
* Text editing in the XAML editor does not constantly steal selection on every typed character.
* Invalid XAML is reported through diagnostics without crashing the editor.

### Keyboard Shortcuts

Keyboard input is handled only in design mode.

| Shortcut | Action |
| --- | --- |
| `Arrow` | Nudge the selected element by 1 px. |
| `Shift+Arrow` | Nudge the selected element by 10 px. |
| `Alt+Shift+Left` / `Alt+Shift+Right` | Decrease or increase selected element width by 10 px. |
| `Alt+Shift+Up` / `Alt+Shift+Down` | Decrease or increase selected element height by 10 px. |
| `Ctrl+Left` / `Ctrl+Up` or `Cmd+Left` / `Cmd+Up` | Move the selected element earlier in its parent. |
| `Ctrl+Right` / `Ctrl+Down` or `Cmd+Right` / `Cmd+Down` | Move the selected element later in its parent. |
| `Alt+Arrow` | Move the selected element into the nearest compatible container in that direction. |
| `Tab` | Select the next element in the XAML structure. |
| `Shift+Tab` | Select the previous element in the XAML structure. |
| `Delete` / `Backspace` | Delete the selected element. |
| `Ctrl+D` / `Cmd+D` | Duplicate the selected element. |

### Headless Coverage

The visual editing surface has headless integration coverage for selection, source sync, structure sync, property editing, toolbox insertion, drag/drop feedback, resize adorners, runtime pass-through mode, and designer keyboard behavior. Screenshot artifacts are written under `artifacts/headless-screenshots/` during the relevant tests.

## Sharing Xaml

Xaml Playground uses GitHub [gists](https://gist.github.com/) to publicly share your creations.

The gist must contain a file named *Main.axaml* and optional *Main.axaml.cs* with code behind. 

To share a xaml with others you should do the following:
* Create a gist with *Main.axaml*.
* Create an optional gist with *Main.axaml.cs*
* Append the gist ID to the Xaml Playground URL. For example, to view [gist.github.com/6b6f586cecb37ada37da24c8e1fe408b](https://gist.github.com/6b6f586cecb37ada37da24c8e1fe408b) in Xaml Playground, use the URL [wieslawsoltes.github.io/XamlPlayground/6b6f586cecb37ada37da24c8e1fe408b](https://wieslawsoltes.github.io/XamlPlayground/6b6f586cecb37ada37da24c8e1fe408b).

Example gists:

* [gist.github.com/6b6f586cecb37ada37da24c8e1fe408b](https://gist.github.com/6b6f586cecb37ada37da24c8e1fe408b)

## Resources

* [GitHub source code repository.](https://github.com/wieslawsoltes/XamlPlayground)

## License

Xaml Playground is licensed under the [MIT license](LICENSE.md).
