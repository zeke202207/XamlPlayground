using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace XamlPlayground.Tests;

public sealed class SampleXamlTests
{
    [Fact]
    public void Samples_Load_WithXamlXRuntimeCompiler()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var failures = new List<string>();

        Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var sample in EnumerateSamples())
            {
                try
                {
                    var control = AvaloniaRuntimeXamlLoader.Parse<Control?>(sample.Xaml, typeof(App).Assembly);
                    Assert.NotNull(control);
                }
                catch (Exception ex)
                {
                    failures.Add($"{sample.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        Assert.True(
            failures.Count == 0,
            "Expected every sample to load through Avalonia's XamlX runtime compiler." +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<(string Name, string Xaml)> EnumerateSamples()
    {
        yield return ("Code", GetTemplate("s_xaml"));
        yield return ("New", GetTemplate("s_newXaml"));

        var assembly = typeof(App).Assembly;
        foreach (var resourceName in assembly
                     .GetManifestResourceNames()
                     .Where(static name => name.StartsWith("XamlPlayground.Samples.", StringComparison.Ordinal) &&
                                           name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);

            using var reader = new StreamReader(stream);
            yield return (GetSampleName(resourceName), reader.ReadToEnd());
        }
    }

    private static string GetTemplate(string fieldName)
    {
        var templates = typeof(App).Assembly.GetType("XamlPlayground.Templates", throwOnError: true)!;
        var field = templates.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        return Assert.IsType<string>(field.GetValue(null));
    }

    private static string GetSampleName(string resourceName)
    {
        var name = resourceName["XamlPlayground.Samples.".Length..];
        return Path.GetFileNameWithoutExtension(name);
    }
}
