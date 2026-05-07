using System.Collections.Generic;

namespace XamlPlayground.Workspace;

public static class AvaloniaProjectTemplates
{
    public static IReadOnlyList<AvaloniaProjectTemplate> All { get; } =
    [
        new(
            "Avalonia .NET App",
            "avalonia.app",
            "Desktop Avalonia app structure with App.axaml and a MainView user control.",
            SupportsBrowser: false),
        new(
            "Avalonia .NET MVVM App",
            "avalonia.mvvm",
            "Desktop MVVM Avalonia app structure with Views and ViewModels folders.",
            SupportsBrowser: false),
        new(
            "Avalonia Cross Platform Application",
            "avalonia.xplat",
            "Cross platform Avalonia app structure suitable for desktop, browser, and mobile.",
            SupportsBrowser: true)
    ];
}
