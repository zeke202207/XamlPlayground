using System.Linq;
using XamlPlayground.Services.DesignInspection;

namespace XamlPlayground.Tests;

public sealed class XamlDesignInspectorTests
{
    [Fact]
    public void Analyze_FindsStylesBindingsAndResourcesAcrossDocuments()
    {
        var inspector = new XamlDesignInspector();
        var analysis = inspector.Analyze(new[]
        {
            new XamlDesignDocument(
                "Main.axaml",
                """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <UserControl.Styles>
                    <Style Selector="Button.primary:pointerover">
                      <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                    </Style>
                  </UserControl.Styles>
                  <StackPanel>
                    <TextBlock Text="{Binding Title}" />
                    <TextBox Text="{Binding Path=Name, Mode=TwoWay}" />
                  </StackPanel>
                </UserControl>
                """,
                IsResourceDictionary: false),
            new XamlDesignDocument(
                "Theme.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="AccentBrush">#0078D4</SolidColorBrush>
                </ResourceDictionary>
                """,
                IsResourceDictionary: true)
        });

        Assert.Contains(analysis.Styles, style => style.Selector == "Button.primary:pointerover");
        Assert.Contains(analysis.Styles.SelectMany(style => style.Setters), setter => setter.Property == "Background");
        Assert.Contains(analysis.Bindings, binding => binding.PropertyName == "Text" && binding.Path == "Title");
        Assert.Contains(analysis.Bindings, binding => binding.Path == "Name" && binding.Mode == "TwoWay");
        Assert.Contains(analysis.Resources, resource => resource.Key == "AccentBrush" && resource.ResourceType == "SolidColorBrush");
    }

    [Fact]
    public void Editor_AddsStyleAndReplacesBinding()
    {
        var xaml = """
                   <UserControl xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <StackPanel>
                       <TextBlock Text="{Binding Title}" />
                     </StackPanel>
                   </UserControl>
                   """;

        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var binding = inspector.Analyze(new[]
            {
                new XamlDesignDocument("Main.axaml", xaml, IsResourceDictionary: false)
            })
            .Bindings
            .Single();

        var bindingEdit = editor.ReplaceBinding(xaml, binding, "{Binding DisplayName, Mode=OneWay}");
        Assert.True(bindingEdit.Changed);
        Assert.Contains("{Binding DisplayName, Mode=OneWay}", bindingEdit.Text);

        var styleEdit = editor.AddStyleToDocument(
            bindingEdit.Text,
            "TextBlock.title",
            new[] { ("Foreground", "{DynamicResource AccentBrush}") });
        Assert.True(styleEdit.Changed);
        Assert.Contains("<UserControl.Styles>", styleEdit.Text);
        Assert.Contains("Selector=\"TextBlock.title\"", styleEdit.Text);
        Assert.Contains("Property=\"Foreground\"", styleEdit.Text);
    }

    [Fact]
    public void Editor_ReplacesResourceWithRawXaml()
    {
        var xaml = """
                   <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <SolidColorBrush x:Key="AccentBrush">#0078D4</SolidColorBrush>
                   </ResourceDictionary>
                   """;
        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var resource = inspector.Analyze(new[]
            {
                new XamlDesignDocument("Theme.axaml", xaml, IsResourceDictionary: true)
            })
            .Resources
            .Single();

        var edit = editor.ReplaceResource(
            xaml,
            resource,
            "<LinearGradientBrush x:Key=\"AccentBrush\"><GradientStop Color=\"#0078D4\" Offset=\"0\" /></LinearGradientBrush>");

        Assert.True(edit.Changed);
        Assert.Contains("LinearGradientBrush", edit.Text);
        Assert.DoesNotContain("<SolidColorBrush", edit.Text);
    }

    [Fact]
    public void Analyze_IgnoresEscapedBindingText()
    {
        var inspector = new XamlDesignInspector();
        var analysis = inspector.Analyze(new[]
        {
            new XamlDesignDocument(
                "Main.axaml",
                """
                <UserControl xmlns="https://github.com/avaloniaui">
                  <TextBlock Text="{}{Binding Title}" />
                </UserControl>
                """,
                IsResourceDictionary: false)
        });

        Assert.Empty(analysis.Bindings);
    }

    [Fact]
    public void Editor_UsesDocumentLocalStyleIndexFallback()
    {
        var first = """
                    <UserControl xmlns="https://github.com/avaloniaui">
                      <UserControl.Styles>
                        <Style Selector="Button.shared">
                          <Setter Property="Opacity" Value="0.5" />
                        </Style>
                      </UserControl.Styles>
                    </UserControl>
                    """;
        var second = """
                     <UserControl xmlns="https://github.com/avaloniaui">
                       <UserControl.Styles>
                         <Style Selector="Button.shared">
                           <Setter Property="Opacity" Value="1" />
                         </Style>
                         <Style Selector="TextBlock.shared">
                           <Setter Property="Opacity" Value="1" />
                         </Style>
                       </UserControl.Styles>
                     </UserControl>
                     """;

        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var style = inspector.Analyze(new[]
            {
                new XamlDesignDocument("First.axaml", first, IsResourceDictionary: false),
                new XamlDesignDocument("Second.axaml", second, IsResourceDictionary: false)
            })
            .Styles
            .Single(candidate => candidate.FilePath == "Second.axaml" && candidate.Selector == "Button.shared");

        var edit = editor.SetStyleSetter(second, style with { Line = 999 }, "Button.shared", "Foreground", "Red");

        Assert.True(edit.Changed);
        Assert.Contains("Selector=\"Button.shared\"", edit.Text);
        Assert.Contains("Property=\"Foreground\" Value=\"Red\"", edit.Text);
        Assert.DoesNotContain("Selector=\"TextBlock.shared\">\n      <Setter Property=\"Opacity\" Value=\"1\" />\n      <Setter Property=\"Foreground\"", edit.Text);
    }

    [Fact]
    public void Editor_RejectsStaleBindingAndResourceRanges()
    {
        var xaml = """
                   <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <TextBlock x:Key="TitleText" Text="{Binding Title}" />
                     <SolidColorBrush x:Key="AccentBrush">#0078D4</SolidColorBrush>
                   </ResourceDictionary>
                   """;
        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var analysis = inspector.Analyze(new[]
        {
            new XamlDesignDocument("Theme.axaml", xaml, IsResourceDictionary: true)
        });
        var staleXaml = "<!--changed-->\n" + xaml;

        var bindingEdit = editor.ReplaceBinding(staleXaml, analysis.Bindings.Single(), "{Binding DisplayName}");
        var resourceEdit = editor.ReplaceResource(
            staleXaml,
            analysis.Resources.Single(resource => resource.Key == "AccentBrush"),
            "<SolidColorBrush x:Key=\"AccentBrush\">#FF0000</SolidColorBrush>");

        Assert.False(bindingEdit.Changed);
        Assert.False(resourceEdit.Changed);
        Assert.Contains("changed", bindingEdit.Error);
        Assert.Contains("changed", resourceEdit.Error);
    }

    [Fact]
    public void Editor_BuildsObjectElementBindingReplacement()
    {
        var xaml = """
                   <UserControl xmlns="https://github.com/avaloniaui">
                     <TextBlock>
                       <TextBlock.Text>
                         <Binding Path="Title" />
                       </TextBlock.Text>
                     </TextBlock>
                   </UserControl>
                   """;
        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var binding = inspector.Analyze(new[]
            {
                new XamlDesignDocument("Main.axaml", xaml, IsResourceDictionary: false)
            })
            .Bindings
            .Single();

        var replacement = XamlDesignEditor.BuildBindingObjectElement(
            binding.Kind,
            "DisplayName",
            "OneWay",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
        var edit = editor.ReplaceBinding(xaml, binding, replacement);

        Assert.True(edit.Changed);
        Assert.Contains("<Binding Path=\"DisplayName\" Mode=\"OneWay\" />", edit.Text);
        Assert.DoesNotContain("Path=\"Title\"", edit.Text);
    }

    [Fact]
    public void Editor_AddsMembersWithRootNamespacePrefix()
    {
        var xaml = """
                   <views:PreviewView xmlns="https://github.com/avaloniaui"
                                      xmlns:views="clr-namespace:Sample.Views">
                     <Grid />
                   </views:PreviewView>
                   """;
        var editor = new XamlDesignEditor();

        var styleEdit = editor.AddStyleToDocument(xaml, "Button.primary", new[] { ("Opacity", "1") });
        var resourceEdit = editor.AddResourceToDocument(xaml, "<SolidColorBrush x:Key=\"AccentBrush\">#0078D4</SolidColorBrush>");

        Assert.True(styleEdit.Changed);
        Assert.True(resourceEdit.Changed);
        Assert.Contains("<views:PreviewView.Styles>", styleEdit.Text);
        Assert.Contains("<views:PreviewView.Resources>", resourceEdit.Text);
        Assert.Contains("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", resourceEdit.Text);
    }

    [Fact]
    public void Editor_AddsStyleDirectlyToStylesRoot()
    {
        var xaml = """
                   <Styles xmlns="https://github.com/avaloniaui">
                     <Style Selector="TextBlock.title">
                       <Setter Property="FontWeight" Value="SemiBold" />
                     </Style>
                   </Styles>
                   """;
        var editor = new XamlDesignEditor();

        var edit = editor.AddStyleToDocument(xaml, "Button.primary", new[] { ("Opacity", "1") });

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<Style Selector=\"Button.primary\">", edit.Text);
        Assert.DoesNotContain("<Styles.Styles>", edit.Text);
    }

    [Fact]
    public void Editor_AddsXamlNamespaceWhenReplacingResourceWithXKey()
    {
        var xaml = """
                   <ResourceDictionary xmlns="https://github.com/avaloniaui">
                     <SolidColorBrush Key="AccentBrush">#0078D4</SolidColorBrush>
                   </ResourceDictionary>
                   """;
        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var resource = inspector.Analyze(new[]
            {
                new XamlDesignDocument("Theme.axaml", xaml, IsResourceDictionary: true)
            })
            .Resources
            .Single();

        var edit = editor.ReplaceResource(
            xaml,
            resource,
            "<SolidColorBrush x:Key=\"AccentBrush\">#FF0000</SolidColorBrush>");

        Assert.True(edit.Changed);
        Assert.Contains("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", edit.Text);
        Assert.Contains("x:Key=\"AccentBrush\"", edit.Text);
        Assert.DoesNotContain("Key=\"AccentBrush\">#0078D4", edit.Text);
    }

    [Fact]
    public void Editor_AddsXamlNamespaceWhenReplacingRootResourceWithXKey()
    {
        var xaml = """<SolidColorBrush Key="AccentBrush">#0078D4</SolidColorBrush>""";
        var inspector = new XamlDesignInspector();
        var editor = new XamlDesignEditor();
        var resource = inspector.Analyze(new[]
            {
                new XamlDesignDocument("AccentBrush.axaml", xaml, IsResourceDictionary: true)
            })
            .Resources
            .Single();

        var edit = editor.ReplaceResource(
            xaml,
            resource,
            "<SolidColorBrush x:Key=\"AccentBrush\">#FF0000</SolidColorBrush>");

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", edit.Text);
        Assert.Contains("x:Key=\"AccentBrush\"", edit.Text);
        Assert.Contains("#FF0000", edit.Text);
        Assert.DoesNotContain("#0078D4", edit.Text);
    }

    [Fact]
    public void Editor_QuotesBindingMarkupValuesWithCommas()
    {
        var markup = XamlDesignEditor.BuildBindingMarkup(
            "Binding",
            "Title",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "{}{0}, {1}",
            "N/A, unknown",
            string.Empty);
        var inspector = new XamlDesignInspector();

        var binding = inspector.Analyze(new[]
            {
                new XamlDesignDocument(
                    "Main.axaml",
                    "<TextBlock xmlns=\"https://github.com/avaloniaui\" Text=\"" + markup + "\" />",
                    IsResourceDictionary: false)
            })
            .Bindings
            .Single();

        Assert.Contains("StringFormat='{}{0}, {1}'", markup);
        Assert.Contains("FallbackValue='N/A, unknown'", markup);
        Assert.Equal("{}{0}, {1}", binding.StringFormat);
        Assert.Equal("N/A, unknown", binding.FallbackValue);
    }
}
