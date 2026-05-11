# Blend-Inspired Theme Editor Plan

## Purpose

This document captures how Microsoft Blend handled XAML theme, style, template, and resource editing, then maps those ideas onto XamlPlayground's Avalonia theme editor. The goal is not to clone Blend literally. The goal is to borrow the durable workflow ideas: explicit resource scope, editable template scope, resource discovery, state preview, and safe reuse across projects.

## Blend Model

Blend treated theme editing as resource editing. Styles, templates, brushes, colors, and dictionaries were all reusable resources that could be created, applied, edited, moved, and reset from the designer.

The core workflow was:

1. Select a control on the artboard or in the object tree.
2. Choose `Edit Template` or `Edit Style`.
3. Choose `Edit a Copy` to clone the default control template/style, or `Create Empty` to start fresh.
4. Name the resource and choose where it should be defined.
5. Enter a scoped editor for the style/template.
6. Return to the main document when done.
7. Apply the resource to compatible controls, or reset to default.

Blend's important details:

- `Edit a Copy` starts from the platform default style/template.
- `Create Empty` creates a new blank resource for the selected target type.
- `Edit Current` is available only when a control already uses a user-created style/template.
- Resource placement is explicit: application, current document, selected element, or a resource dictionary file.
- Resource dictionaries are first-class reusable files.
- The resource picker filters by compatibility, so users do not apply the wrong type of resource to a property.
- Visual states are edited as a designer concept, not only as raw XAML.
- The designer has an editing scope and breadcrumb/return flow, so template editing does not feel like leaving the app surface.

## Avalonia Mapping

WPF styles/templates do not map one-to-one to Avalonia, but the workflow maps cleanly.

| Blend/WPF concept | Avalonia equivalent |
| --- | --- |
| `Style` resource | `Style` resource |
| `ControlTemplate` inside style | `ControlTheme` with `Template` setter |
| `Template` property | `Theme` property / `ControlTheme` resource |
| App resources | `App.axaml` resources / merged dictionaries |
| Document resources | root control resources in the current `.axaml` |
| Element resources | selected element resources |
| Resource dictionary | `.axaml` `ResourceDictionary` |
| Visual states | pseudo-classes, selectors, transitions, animations |
| Apply resource | set `Theme`, `Style`, or resource reference |
| Reset | remove local `Theme`, `Style`, or resource reference |

For Avalonia, pseudo-class editing is more important than WPF `VisualStateManager` editing because many built-in themes rely on selectors such as `:pointerover`, `:pressed`, `:disabled`, `:checked`, and `:focus`.

## Current State

The editor now has the first useful theme workflow:

- Right-click a preview control and create a custom template from Avalonia Fluent.
- Copy source `ControlTheme` XAML from `external/Avalonia/src/Avalonia.Themes.Fluent`.
- Create custom theme resources under `Themes/*.axaml`.
- Apply a theme with `Theme="{StaticResource ...}"`.
- Reset a control back to its default theme.
- Open selected theme resource.
- Preview resource dictionaries through `Design.PreviewWith`.
- List custom themes and Fluent source templates.
- Search theme lists.
- Import/export loose `.axaml` / `.xaml` theme resource files.
- Save/load multi-file `.xamltheme` packages.

This is a strong starting point, but it is still a file/template workflow. A Blend-like editor needs a resource model, usage model, and scoped template editing model.

## Main Gaps

### 1. Resource Scope

Current behavior creates standalone files under `Themes/`. Users need to choose where a resource lives:

- Application resources.
- Current document resources.
- Selected element resources.
- Existing resource dictionary file.
- New resource dictionary file.
- External theme project/package.

The editor should show the consequence of scope: where the key can be resolved and what controls are affected.

### 2. Resource Graph

The editor needs a real graph of resource dictionaries and references:

- Resource key.
- Resource type.
- Target type, for styles/control themes/templates.
- File path and source range.
- Merged dictionary order.
- Static and dynamic resource references.
- Duplicate key diagnostics.
- Missing resource diagnostics.
- Shadowed resource diagnostics.
- Usages in project XAML.

Without this graph, rename, usage search, import/export validation, and resource pickers will be fragile.

### 3. Scoped Template Editing

Opening a `.axaml` file is not enough. Template editing should feel scoped:

- Breadcrumb: `Main.axaml > Button#SaveButton > MyButtonTheme1`.
- Return button to the owning document and selected control.
- Target control fixture preview.
- Template visual tree.
- XAML editor synchronized with preview selection.
- Part/resource/property inspector.

### 4. State and Pseudo-Class Editing

Avalonia theme editing needs state preview:

- Force `:pointerover`, `:pressed`, `:disabled`, `:checked`, `:focus`, and custom pseudo-classes.
- Show selectors active for the current state.
- Compare custom theme state coverage against Fluent.
- Edit state-specific setters/resources.
- Preview transitions where practical.

### 5. Resource Picker

Properties should expose resource actions:

- Convert value to resource.
- Apply existing resource.
- Edit referenced resource.
- Rename referenced resource.
- Reset local resource reference.
- Switch between static and dynamic resource where valid.

The picker should filter compatible resources by property type.

### 6. Template Contract Awareness

The editor should understand template expectations:

- Parse `PART_*` names from default templates.
- Detect deleted named parts from the source template.
- Validate `TemplateBinding` property names.
- Detect likely broken resource references.
- Surface warnings before the user applies a broken template broadly.

## Proposed Architecture

### Domain Model

```text
ThemeWorkspace
  ThemeProject
    ThemeDictionary[]
      ThemeResource[]
        key
        resourceType
        targetType
        filePath
        xamlRange
        dependencies
        usages
        diagnostics
```

### Services

`ThemeProjectService`

- Save/load `.xamltheme`.
- Import/export loose resource dictionaries.
- Normalize project paths.
- Preserve dictionary layout.

`ResourceDictionaryAnalyzer`

- Parse dictionaries.
- Read resources and keys.
- Read merged dictionaries.
- Detect duplicate keys.
- Detect unresolved references.
- Track source positions where possible.

`ResourceUsageAnalyzer`

- Scan project XAML.
- Find `{StaticResource ...}` and `{DynamicResource ...}`.
- Find `Theme="{StaticResource ...}"`.
- Find local style/theme assignments.
- Support rename and find-usages.

`ControlThemeAnalyzer`

- Extract target type.
- Extract setters.
- Extract template root.
- Extract named parts.
- Extract pseudo-class selectors related to the theme.

`ThemeApplicationService`

- Apply theme to selected control.
- Apply theme as default for type.
- Reset theme.
- Rename resource key and update references.

`ThemePreviewService`

- Generate preview fixtures.
- Load resource previews.
- Force pseudo-class states.
- Preview light/dark/theme variants.

## UI Plan

### Themes Panel

Add tabs:

- `Themes`: custom control themes grouped by target type.
- `Resources`: all resources discovered in active dictionaries.
- `Usages`: where the selected resource is referenced.
- `Diagnostics`: duplicate/missing/shadowed resources.
- `Source`: Fluent source templates.

Theme context menu:

- Open.
- Rename key.
- Duplicate.
- Delete.
- Export.
- Find usages.
- Apply to selection.
- Apply as default for target type.

### Create Template Dialog

Options:

- Base template: Fluent default, existing custom theme, empty.
- Resource key.
- Placement scope.
- Apply to selected control.
- Apply to all controls of target type.
- Create preview fixtures.

### Template Editor Scope

Show:

- Scope breadcrumb.
- Preview fixtures.
- Template visual tree.
- XAML editor.
- Properties/resources.
- Part/contract warnings.

### State Panel

State buttons:

- `Normal`
- `PointerOver`
- `Pressed`
- `Disabled`
- `Focused`
- `Checked`
- Custom pseudo-class input.

For each state:

- Force state in preview.
- Show matching selectors.
- Show changed setters/resources.

## Implementation Phases

### Phase 1: Resource Graph

Deliverables:

- Add `ResourceDictionaryAnalyzer`.
- Add resource view models.
- Add `Resources` and `Diagnostics` views in the Themes panel.
- Detect duplicate custom resource keys.
- Detect unresolved static/dynamic resources.
- Find usages for selected resources.

Acceptance:

- Loading/importing themes populates a resource list.
- Duplicate keys show diagnostics.
- Missing references show diagnostics.
- Selecting a resource shows usages.

### Phase 2: Resource Operations

Deliverables:

- Rename resource key with reference updates.
- Duplicate selected theme/resource.
- Delete selected theme/resource with usage warning.
- Apply resource from resource picker.

Acceptance:

- Rename updates the resource declaration and all references in project XAML.
- Delete warns when there are usages.
- Picker only shows compatible resources.

### Phase 3: Scoped Template Editing

Deliverables:

- Add template edit scope state.
- Add breadcrumb/return flow.
- Add target fixture preview.
- Synchronize template visual selection with XAML source.

Acceptance:

- Creating a template opens scoped editing mode.
- Return restores the original document and selection.
- Selecting a preview part selects corresponding XAML.

### Phase 4: State Preview

Deliverables:

- Add pseudo-class forcing to preview.
- Add state buttons.
- Show active selectors for current forced state.

Acceptance:

- Button preview can show normal, pointer-over, pressed, disabled, focused states.
- State changes do not mutate user XAML.

### Phase 5: Theme Variants and Packaging

Deliverables:

- Theme variant dictionaries.
- Side-by-side light/dark preview.
- Export as loose dictionaries or Avalonia library theme layout.

Acceptance:

- A theme project can hold base/light/dark files.
- Exported files can be merged into a real Avalonia app.

## First Implementation Slice

Start with Phase 1 because it unlocks every later workflow.

Concrete first slice:

1. Add `ResourceDictionaryAnalyzer`.
2. Parse resource declarations and resource references from project XAML.
3. Add resource and diagnostic view models.
4. Add a `Resources` tab and `Diagnostics` tab to the Themes panel.
5. Add tests for duplicate keys, missing references, and usage discovery.

## Sources

- Microsoft Learn: Modify the style of objects in Blend for Visual Studio.
- Microsoft Learn: Create and apply a resource in the XAML Designer.
- Microsoft Learn: Blend for Visual Studio feature tour.
- Microsoft Learn: WPF styles, templates, and themes.
- Microsoft Learn: VisualStateManager.
