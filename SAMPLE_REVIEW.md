# Sample Review

Compared against:

- `/Users/wieslawsoltes/GitHub/Avalonia/samples/ControlCatalog`
- `/Users/wieslawsoltes/GitHub/Avalonia/samples/RenderDemo`
- `/Users/wieslawsoltes/GitHub/Avalonia/samples/BindingDemo`

The playground now contains 62 embedded XML samples. Each sample is validated by `XamlPlayground.Tests` through `AvaloniaRuntimeXamlLoader`, which exercises Avalonia's runtime XamlX compiler.

## Porting Constraints

The playground loads each sample as standalone runtime XAML. A sample cannot depend on ControlCatalog page classes, view models, custom controls, event handlers, app navigation services, dialogs, clipboard services, native embedding, OpenGL host controls, or assets that are not packaged by Xaml Playground.

When an upstream sample needed those dependencies, the port keeps the control scenario but replaces the dependency with static XAML, element-name bindings, local assets, or disabled explanatory controls.

## Existing Samples Updated

- `Button.xml` now includes more of `ButtonsPage.xaml`: accent buttons, letter spacing, `HyperlinkButton`, `DropDownButton`, `SplitButton`, and `ToggleSplitButton`. Code-behind click behavior from upstream is intentionally omitted.
- `TextBox.xml` now includes newer upstream text input options: numeric content type, placeholder foregrounds, hidden suggestions, password content type, `AcceptsTab`, persistent custom selection brushes, and multiline `LineHeight`.
- `TextBlock.xml` now includes upstream `SelectableTextBlock` span content and OpenType `FontFeatures`. ControlCatalog-only font assets remain omitted.
- `Animation.xml` and `Transitions.xml` remain close to RenderDemo but use Avalonia 12 `RadialGradientBrush.RadiusX` and `RadiusY` instead of the removed `Radius` property.
- Sample ordering is now stable by sample name before being added to the menu.

## New Standalone Ports

ControlCatalog-derived ports:

- `Accelerator.xml`
- `AdornerLayer.xml`
- `AutoCompleteBox.xml`
- `BitmapCache.xml`
- `Carousel.xml`
- `ColorPicker.xml`
- `CommandBar.xml`
- `ContainerQuery.xml`
- `Cursor.xml`
- `DataValidation.xml`
- `Focus.xml`
- `HeaderedContent.xml`
- `Image.xml`
- `ListBox.xml`
- `PipsPager.xml`
- `PlatformInfo.xml`
- `Pointers.xml`
- `RefreshContainer.xml`
- `ScrollViewer.xml`
- `SplitView.xml`
- `TabbedPage.xml`
- `TabStrip.xml`
- `Theme.xml`
- `TransitioningContentControl.xml`
- `TreeView.xml`

RenderDemo-derived ports:

- `Brushes.xml`
- `Transform3D.xml`

BindingDemo-derived port:

- `Binding.xml`

## Known Divergences

- Event-driven upstream samples are static in the playground. Examples: `ButtonSpinner`, `CommandBar`, `RefreshContainer`, `TransitioningContentControl`, `Carousel`, and `TabbedPage`.
- View-model-driven upstream samples use inline static data or element-name bindings here. Examples: `AutoCompleteBox`, `ListBox`, `ScrollViewer`, `SplitView`, `TreeView`, and `Binding`.
- Asset-heavy upstream samples use `Logo.png`, colored panels, or generated shapes instead of ControlCatalog stock images.
- Color picker controls require an explicit `Avalonia.Controls.ColorPicker` package reference and package namespace because runtime XAML does not get ControlCatalog's compile-time namespace context.
- `Drawing.xml` follows RenderDemo's drawing sample. ControlCatalog's `CustomDrawing.xaml` depends on `CustomDrawingExampleControl` code and was not copied.
- `Pointers.xml` keeps pointer/cursor surfaces but omits ControlCatalog's custom pointer drawing controls and pointer event handlers.

## Not Ported One-to-One

ControlCatalog top-level pages intentionally not ported as standalone samples:

- `CarouselDemoPage.xaml`
- `ClipboardPage.xaml`
- `CompositionPage.axaml`
- `ConnectedAnimationDemoPage.xaml`
- `ContentDemoPage.xaml`
- `DataGridPage.xaml`
- `DialogsPage.xaml`
- `DragAndDropPage.xaml`
- `DrawerDemoPage.xaml`
- `GesturePage.xaml`
- `NativeEmbedPage.xaml`
- `NavigationDemoPage.xaml`
- `NotificationsPage.xaml`
- `OpenGlPage.xaml`
- `TabbedDemoPage.xaml`
- `WindowCustomizationsPage.xaml`

Reasons:

- Native/platform service requirement: clipboard, dialogs, notifications, native embedding, OpenGL, window customization.
- Custom code/control requirement: composition custom visuals, connected animations, pointer drawing, drag/drop handlers.
- Navigation/app shell requirement: drawer, navigation, carousel demos, tabbed demos, content demos.
- Separate dependency and data model requirement: `DataGridPage.xaml` depends on the ControlCatalog data grid setup and view-model data.

RenderDemo pages intentionally not ported as standalone samples:

- `CustomAnimatorPage.xaml`
- `FormattedTextPage.axaml`
- `GlyphRunPage.xaml`
- `LineBoundsPage.xaml`
- `TextFormatterPage.axaml`

Reasons:

- The useful behavior is implemented in code-behind or custom render controls.
- `FormattedTextPage.axaml` is an empty page shell upstream.

## Validation

Run:

```sh
dotnet test XamlPlayground.sln -c Release --nologo -m:1 /p:RunAOTCompilation=false /p:BuildInParallel=false /nodeReuse:false --disable-build-servers
```

This verifies that the Code template and every embedded `Samples/*.xml` file load through the runtime XamlX compiler.
