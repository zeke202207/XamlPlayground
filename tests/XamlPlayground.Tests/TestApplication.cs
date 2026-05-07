using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace XamlPlayground.Tests;

public sealed class TestApplication : Application
{
    private static readonly object s_avaloniaLock = new();
    private static bool s_avaloniaInitialized;

    public TestApplication()
    {
        Styles.Add(new FluentTheme());
    }

    public static void EnsureAvaloniaInitialized()
    {
        if (s_avaloniaInitialized)
        {
            return;
        }

        lock (s_avaloniaLock)
        {
            if (s_avaloniaInitialized)
            {
                return;
            }

            BuildAvaloniaApp().SetupWithoutStarting();
            s_avaloniaInitialized = true;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}
