using System;
using Avalonia;
using Dock.Model.Core;
using Dock.Settings;

namespace XamlPlayground.NetCore;

class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseFloatingWindowHostMode(DockFloatingWindowHostMode.Native)
            .LogToTrace();
}
