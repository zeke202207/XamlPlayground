# Animation Timeline Editor Plan

## Research Notes

Blend's animation workflow is built around the Objects and Timeline panel: select an object, create or select a storyboard, move the playhead, edit on the artboard or properties panel, and let the tool write keyframes to XAML. The important pattern for XamlPlayground is the tight loop between visual selection, property editing, keyframe authoring, and generated XAML.

Avalonia's native model maps well to this workflow. Keyframe animations are declared in styles with `Style.Animations`; style selectors decide when the animation starts, and keyframes hold setter values at cue percentages. That means the editor should author selectors and keyframes directly instead of inventing a separate runtime format.

Adobe Animate reinforces the timeline concepts that should be visible in the UI: tracks/layers, frames or time markers, keyframes, span operations, playhead navigation, and preview. SVG editors such as macSVG add another relevant pattern: animation rows sorted on a timeline, playback controls, and scrubbing to inspect time-specific values.

## Product Shape

The animation editor should be a first-class dock tool. It must work from the visual editor and from the theme editor:

- Visual target: the selected XAML element receives an element-scoped styles collection with a `Style Selector="^"` animation.
- Theme root target: the selected `ControlTheme` receives a root-relative selector such as `^`.
- Theme visual state target: the selected state receives selector-driven animation such as `^:pointerover`.
- Theme template part target: the selected part receives selector-driven animation such as `^ /template/ ContentPresenter#PART_ContentPresenter:pointerover`.

The editor should keep the XAML as the source of truth. Loading a target reads existing `Style.Animations`; applying writes Avalonia-native animation XAML.

## Implementation Status

| Task | Status |
| --- | --- |
| Add research-backed implementation plan | Done |
| Add reusable animation timeline parser/writer | Done |
| Generate Avalonia `Style.Animations` for selected visual elements | Done |
| Generate Avalonia `Style.Animations` for control themes, states, and template parts | Done |
| Add top-level Animations dock | Done |
| Add Animations dock inside the Themes docking panel | Done |
| Add timeline tracks, cue markers, keyframe editing, presets, and playhead controls | Done |
| Add apply and apply-plus-preview commands | Done |
| Add playhead frame application for WYSIWYG state inspection | Done |
| Add regression tests for XAML animation writing and dock layout wiring | Done |
| Drag keyframes directly on the timeline strip | Done |
| Non-destructive live preview without writing the current frame into XAML | Done |
| Keyboard keyframe navigation and editing shortcuts | Done |
| Visual record mode for designer moves/resizes/property edits | Done |
| Play/stop live playhead playback controls | Done |
| Keyboard and button nudge controls for selected keyframes | Done |
| Future: graph editor for easing curves and per-property interpolation | Planned |
| Future: animation resource library with copy/paste across targets | Planned |

## Follow-up Refinements

The current implementation establishes the authoring model and XAML integration. The next level is interaction depth: direct keyframe dragging, modifier-key duplication, frame range selection, per-track lock/visibility controls, easing curve editing, and live preview through the loaded preview control instead of only applying generated XAML.

## Sources

- Microsoft Learn: [Blend for Visual Studio overview](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/creating-a-ui-by-using-blend-for-visual-studio?view=visualstudio) and Objects and Timeline panel.
- Microsoft Learn: [Animate objects in XAML Designer](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/animate-objects-in-xaml-designer?view=visualstudio).
- Avalonia Docs: [Using keyframe animations](https://docs.avaloniaui.net/docs/graphics-animation/keyframe-animations), style selectors, cue points, and easing.
- Adobe Help: [How to use the Timeline in Animate](https://helpx.adobe.com/ie/animate/using/timeline.html), frames, layers, playhead, keyframes, and span editing.
- macSVG User Guide: [Animation Timeline View](https://macsvg.org/user-guide/macsvg-user-interface/animation-timeline/), playback controls, and scrubbing.
