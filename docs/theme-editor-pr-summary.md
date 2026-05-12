## Summary

This PR adds a Blend-inspired theme editing workflow for XamlPlayground, centered on Avalonia Fluent control themes and custom theme authoring. The work introduces a theme panel with docked tools, resource analysis, theme project persistence, import/export, scoped template editing, preview state controls, and richer template selector authoring.

The branch starts from the Blend theme editor plan commit and includes all implementation commits through the optional refinement work.

## Major Changes

- Added the Avalonia source submodule and Fluent theme catalog support so the editor can discover standard Fluent control templates.
- Added custom control theme creation from selected controls, with generated theme dictionaries, preview fixtures, and automatic application to the selected control.
- Added theme panel docking tools for Custom, Resources, Usages, Diagnostics, States, Variants, Parts, and Fluent source templates.
- Added search across custom themes, Fluent templates, resources, and diagnostics.
- Added theme resource analysis for definitions, usages, duplicate keys, and unresolved resource references.
- Added resource operations for open, apply, rename, duplicate, and delete.
- Replaced the guarded double-delete workflow with a modal confirmation dialog that previews per-file diffs before deletion.
- Added property-grid resource suggestions for compatible theme resources, including control themes, brushes, thickness values, numeric values, strings, collections, templates, data resources, and content-like values.
- Added save/load support for `.xamltheme` theme project packages, with v2 metadata and backward compatibility for v1 packages.
- Added import/export support for theme resource files on disk.
- Added base/light/dark theme variant visibility and side-by-side light/dark preview generation.
- Added scoped control-template editing with breadcrumb return support.
- Added template contract inspection for named template parts and `TemplateBinding` usage.
- Added preview pseudo-class forcing for theme states without mutating user XAML.
- Added visual state selector editing for `Style Selector="^:state"` setters.
- Added selector-specific template-part editing so a named template part can receive state-specific setters such as `^ /template/ ContentPresenter#PART_ContentPresenter:pointerover`.

## Bug Fixes

- Prevented crashes when preview pseudo-class state forcing tried to remove protected Avalonia pseudo-classes.
- Preserved existing custom themes when creating additional themes for other control types.
- Converted the themes tab strip into docked tools consistent with the rest of the dev tools surface.

## Documentation

- Added a detailed Blend-inspired theme editor analysis and implementation plan.
- Updated the plan status table to mark completed work, including the optional refinements.

## Tests

- Added and updated tests for theme resource analysis, resource operations, custom theme creation, search, project save/load, import/export, state editing, variant preview generation, template part selector editing, and resource compatibility suggestions.
- Verified with:

```bash
dotnet test tests/XamlPlayground.Tests/XamlPlayground.Tests.csproj
```

Result: 114 passed, 0 failed.
