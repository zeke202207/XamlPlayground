using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace XamlPlayground.Tests;

public sealed class TestApplication : Application
{
    public TestApplication()
    {
        Styles.Add(new FluentTheme());
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
