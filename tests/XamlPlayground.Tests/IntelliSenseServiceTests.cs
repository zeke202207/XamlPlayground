using System.Threading;
using Avalonia.Controls;
using XamlPlayground.Services.IntelliSense;
using XamlPlayground.Workspace;

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
    public async Task CSharpCompletion_FiltersInaccessibleTargetMembers()
    {
        var service = new CSharpIntelliSenseService();
        const string code = """
            public sealed class Customer
            {
                public string Name { get; set; } = "";
                private string Secret { get; set; } = "";
                protected string ProtectedCode { get; set; } = "";
            }

            public sealed class Consumer
            {
                public void Update()
                {
                    var customer = new Customer();
                    customer.
                }
            }
            """;

        var position = code.IndexOf("customer.", StringComparison.Ordinal) + "customer.".Length;
        var result = await service.GetCompletionsAsync(code, position, explicitInvocation: true, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == "Name");
        Assert.DoesNotContain(result.Items, item => item.Text == "Secret");
        Assert.DoesNotContain(result.Items, item => item.Text == "ProtectedCode");
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
    public async Task CSharpDiagnostics_ReturnCompilerErrors()
    {
        var service = new CSharpIntelliSenseService();
        const string code = """
            public class Sample
            {
                public void Update()
                {
                    MissingSymbol();
                }
            }
            """;

        var result = await service.GetDiagnosticsAsync(code, CancellationToken.None);

        Assert.Contains(result, diagnostic =>
            diagnostic.Code == "CS0103" &&
            diagnostic.Severity == EditorDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CSharpDefinitionAndReferences_ReturnSymbolLocations()
    {
        var service = new CSharpIntelliSenseService();
        const string code = """
            public class Sample
            {
                private int Value;

                public int Read()
                {
                    return Value;
                }
            }
            """;

        var usagePosition = code.LastIndexOf("Value", StringComparison.Ordinal) + 2;
        var definition = await service.GetDefinitionAsync(code, usagePosition, CancellationToken.None);
        var references = await service.GetReferencesAsync(code, usagePosition, CancellationToken.None);

        Assert.NotNull(definition);
        Assert.Equal(code.IndexOf("Value", StringComparison.Ordinal), definition.StartOffset);
        Assert.Contains(references, reference => reference.IsDefinition);
        Assert.Contains(references, reference => !reference.IsDefinition);
    }

    [Fact]
    public async Task CSharpDefinition_UsesWorkspaceFiles()
    {
        var project = new InMemoryProject("Demo", "Demo", "blank");
        var modelFile = project.AddFile(new InMemoryProjectFile(
            "Models/Customer.cs",
            "namespace Demo; public sealed class Customer { }",
            ProjectFileKind.CSharp));
        var currentFile = project.AddFile(new InMemoryProjectFile(
            "Views/MainViewModel.cs",
            "namespace Demo; public sealed class MainViewModel { private Customer? Current; }",
            ProjectFileKind.CSharp));
        var service = new CSharpIntelliSenseService(project, currentFile);

        var position = currentFile.Text.IndexOf("Customer", StringComparison.Ordinal) + 2;
        var definition = await service.GetDefinitionAsync(currentFile.Text, position, CancellationToken.None);

        Assert.NotNull(definition);
        Assert.Equal(modelFile.Path, definition.FilePath);
    }

    [Fact]
    public async Task CSharpFormatDocument_FormatsSource()
    {
        var service = new CSharpIntelliSenseService();
        const string code = "public class Sample{public int Read(){return 42;}}";

        var formatted = await service.FormatDocumentAsync(code, CancellationToken.None);

        Assert.NotNull(formatted);
        Assert.Contains("\n", formatted, StringComparison.Ordinal);
        Assert.Contains("return 42;", formatted, StringComparison.Ordinal);
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
    public async Task XamlCompletion_RecognizesStaticAttachedPropertyOwners()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string incompleteXaml = """<Button xmlns="https://github.com/avaloniaui" """;
        const string xaml = """<Button xmlns="https://github.com/avaloniaui" AutomationProperties.Name="Save" />""";

        var result = await service.GetCompletionsAsync(incompleteXaml, incompleteXaml.Length, explicitInvocation: true, null, CancellationToken.None);
        var diagnostics = await service.GetDiagnosticsAsync(xaml, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Text == "AutomationProperties.Name");
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == "XAML1002" &&
            diagnostic.Message.Contains("AutomationProperties.Name", StringComparison.Ordinal));
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

    [Fact]
    public async Task XamlDiagnostics_ReturnUnknownMemberWarnings()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """<Button xmlns="https://github.com/avaloniaui" MissingProperty="1" />""";

        var result = await service.GetDiagnosticsAsync(xaml, CancellationToken.None);

        Assert.Contains(result, diagnostic =>
            diagnostic.Code == "XAML1002" &&
            diagnostic.Severity == EditorDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task XamlDiagnostics_IgnoresCommentedMarkup()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <!-- <Button MissingProperty="1" /> -->
              <Button Content="Save" />
            </UserControl>
            """;

        var result = await service.GetDiagnosticsAsync(xaml, CancellationToken.None);

        Assert.DoesNotContain(result, diagnostic =>
            diagnostic.Code == "XAML1002" &&
            diagnostic.Message.Contains("MissingProperty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task XamlDiagnostics_UsesScopedXmlNamespaces()
    {
        _ = typeof(Button);
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:controls="using:Avalonia.Controls">
              <controls:Button Content="Before" />
              <StackPanel xmlns:controls="using:Missing.Namespace" />
              <!-- xmlns:controls="using:Missing.Namespace" -->
              <controls:Button Content="After" />
            </UserControl>
            """;

        var result = await service.GetDiagnosticsAsync(xaml, CancellationToken.None);

        Assert.DoesNotContain(result, diagnostic =>
            diagnostic.Code is "XAML1001" or "XAML1002");
    }

    [Fact]
    public async Task XamlReferences_ReturnResourceReferences()
    {
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <UserControl.Resources>
                <SolidColorBrush x:Key="AccentBrush" Color="Red" />
              </UserControl.Resources>
              <Border Background="{StaticResource AccentBrush}" />
            </UserControl>
            """;

        var position = xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2;
        var result = await service.GetReferencesAsync(xaml, position, CancellationToken.None);

        Assert.Contains(result, reference => reference.IsDefinition);
        Assert.Contains(result, reference => !reference.IsDefinition);
    }

    [Fact]
    public async Task XamlReferences_SupportResourceKeyNamedArgument()
    {
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <UserControl.Resources>
                <SolidColorBrush x:Key="AccentBrush" Color="Red" />
              </UserControl.Resources>
              <Border Background="{StaticResource ResourceKey=AccentBrush}" />
            </UserControl>
            """;

        var position = xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2;
        var result = await service.GetReferencesAsync(xaml, position, CancellationToken.None);

        Assert.Contains(result, reference => reference.IsDefinition);
        Assert.Contains(result, reference => !reference.IsDefinition);
    }

    [Fact]
    public async Task XamlReferences_IgnorePlainKeyAttributes()
    {
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <KeyBinding Key="AccentBrush" />
              <UserControl.Resources>
                <SolidColorBrush x:Key="AccentBrush" Color="Red" />
              </UserControl.Resources>
              <Border Background="{StaticResource AccentBrush}" />
            </UserControl>
            """;

        var referencePosition = xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2;
        var definition = await service.GetDefinitionAsync(xaml, referencePosition, CancellationToken.None);
        var plainKeyPosition = xaml.IndexOf("Key=\"AccentBrush\"", StringComparison.Ordinal) + "Key=\"".Length + 2;
        var plainKeyReferences = await service.GetReferencesAsync(xaml, plainKeyPosition, CancellationToken.None);

        Assert.NotNull(definition);
        Assert.Equal(xaml.IndexOf("x:Key=\"AccentBrush\"", StringComparison.Ordinal) + "x:Key=\"".Length, definition.StartOffset);
        Assert.Empty(plainKeyReferences);
    }

    [Fact]
    public async Task XamlReferences_IgnoreCommentedResourceDefinitions()
    {
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <UserControl.Resources>
                <!-- <SolidColorBrush x:Key="AccentBrush" Color="Blue" /> -->
                <SolidColorBrush x:Key="AccentBrush" Color="Red" />
              </UserControl.Resources>
              <Border Background="{StaticResource AccentBrush}" />
            </UserControl>
            """;

        var referencePosition = xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2;
        var definition = await service.GetDefinitionAsync(xaml, referencePosition, CancellationToken.None);
        var references = await service.GetReferencesAsync(xaml, referencePosition, CancellationToken.None);
        var commentedKeyPosition = xaml.IndexOf("x:Key=\"AccentBrush\"", StringComparison.Ordinal) + "x:Key=\"".Length + 2;
        var commentedKeyReferences = await service.GetReferencesAsync(xaml, commentedKeyPosition, CancellationToken.None);

        Assert.NotNull(definition);
        Assert.Equal(xaml.LastIndexOf("x:Key=\"AccentBrush\"", StringComparison.Ordinal) + "x:Key=\"".Length, definition.StartOffset);
        Assert.Contains(references, reference => reference.IsDefinition && reference.Location.StartOffset == definition.StartOffset);
        Assert.Empty(commentedKeyReferences);
    }

    [Fact]
    public async Task XamlReferences_IgnoresPlainAttributeValues()
    {
        var service = new XamlIntelliSenseService();
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <UserControl.Resources>
                <SolidColorBrush x:Key="AccentBrush" Color="Red" />
              </UserControl.Resources>
              <Button HotKey="AccentBrush" />
            </UserControl>
            """;

        var position = xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2;
        var result = await service.GetReferencesAsync(xaml, position, CancellationToken.None);

        Assert.Empty(result);
    }
}
