using System.Threading;
using Avalonia.Controls;
using XamlPlayground.Services.IntelliSense;

namespace XamlPlayground.Tests;

public sealed class IntelliSenseServiceTests
{
    public IntelliSenseServiceTests()
    {
        TestApplication.EnsureAvaloniaInitialized();
    }

    [Fact]
    public async Task CSharpCompletion_UsesRoslynSemanticModel_ForMemberAccess()
    {
        _ = typeof(Button);
        var service = new CSharpIntelliSenseService();
        const string code = """
            using Avalonia.Controls;

            public class SampleView : UserControl
            {
                public void Update()
                {
                    var button = new Button();
                    button.
                }
            }
            """;

        var position = code.IndexOf("button.", StringComparison.Ordinal) + "button.".Length;
        var result = await service.GetCompletionsAsync(code, position, explicitInvocation: true, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == nameof(Button.Content));
        Assert.Contains(result.Items, item => item.Text == nameof(Button.Click));
    }

    [Fact]
    public async Task CSharpSignatureHelp_ReturnsInvocationOverloads()
    {
        _ = typeof(StackPanel);
        var service = new CSharpIntelliSenseService();
        const string code = """
            using Avalonia.Controls;

            public class SampleView : UserControl
            {
                public void Update()
                {
                    var panel = new StackPanel();
                    panel.Children.Add(
                }
            }
            """;

        var position = code.IndexOf("Children.Add(", StringComparison.Ordinal) + "Children.Add(".Length;
        var result = await service.GetSignatureHelpAsync(code, position, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Header.Contains("Add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task XamlCompletion_ReturnsAvaloniaElementNames()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """<UserControl xmlns="https://github.com/avaloniaui"><Bu""";

        var result = await service.GetCompletionsAsync(xaml, xaml.Length, explicitInvocation: true, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == nameof(Button));
    }

    [Fact]
    public async Task XamlCompletion_ReturnsAttributesAndEventsForCurrentElement()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """<Button xmlns="https://github.com/avaloniaui" """;

        var result = await service.GetCompletionsAsync(xaml, xaml.Length, explicitInvocation: true, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == nameof(Button.Content));
        Assert.Contains(result.Items, item => item.Text == nameof(Button.Click));
    }

    [Fact]
    public async Task XamlCompletion_ReturnsEnumValuesForAttributeValues()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """<Button xmlns="https://github.com/avaloniaui" HorizontalAlignment="C""";

        var result = await service.GetCompletionsAsync(xaml, xaml.Length, explicitInvocation: true, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == nameof(Avalonia.Layout.HorizontalAlignment.Center));
    }

    [Fact]
    public async Task XamlQuickInfo_ReturnsMemberTypeHint()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """<Button xmlns="https://github.com/avaloniaui" Content="Save" />""";

        var position = xaml.IndexOf("Content", StringComparison.Ordinal) + 2;
        var result = await service.GetQuickInfoAsync(xaml, position, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(nameof(Button.Content), result.Text, StringComparison.Ordinal);
    }
}
