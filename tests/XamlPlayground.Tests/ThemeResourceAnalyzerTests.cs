using System.Linq;
using XamlPlayground.Services.Theming;

namespace XamlPlayground.Tests;

public sealed class ThemeResourceAnalyzerTests
{
    [Fact]
    public void Analyze_FindsResourcesReferencesDuplicatesAndMissingReferences()
    {
        var analysis = ResourceDictionaryAnalyzer.Analyze(new[]
        {
            new ThemeResourceDocument(
                "Themes/Button.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                  <ControlTheme x:Key="MyButtonTheme1" TargetType="Button" />
                </ResourceDictionary>
                """,
                IsResourceDictionary: true),
            new ThemeResourceDocument(
                "Themes/Duplicate.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="AccentBrush" Color="Blue" />
                </ResourceDictionary>
                """,
                IsResourceDictionary: true),
            new ThemeResourceDocument(
                "Views/MainView.axaml",
                """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Button Theme="{StaticResource MyButtonTheme1}"
                          Background="{DynamicResource MissingBrush}" />
                </UserControl>
                """,
                IsResourceDictionary: false)
        });

        Assert.Contains(analysis.Resources, resource =>
            resource.Key == "MyButtonTheme1" &&
            resource.ResourceType == "ControlTheme" &&
            resource.TargetType == "Button");
        Assert.Contains(analysis.References, reference =>
            reference.Key == "MyButtonTheme1" &&
            reference.Kind == ThemeResourceReferenceKind.StaticResource);
        Assert.Contains(analysis.References, reference =>
            reference.Key == "MissingBrush" &&
            reference.Kind == ThemeResourceReferenceKind.DynamicResource);
        Assert.Equal(2, analysis.Diagnostics.Count(diagnostic =>
            diagnostic.Message.Contains("Duplicate resource key 'AccentBrush'", System.StringComparison.Ordinal)));
        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Resource 'MissingBrush' is referenced but not defined", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ThemeResourceEditor_RenamesReferencesDuplicatesAndDeletesTopLevelResources()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Design.PreviewWith>
                                <Button Theme="{StaticResource MyButtonTheme1}" />
                              </Design.PreviewWith>
                              <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                              <ControlTheme x:Key="MyButtonTheme1" TargetType="Button" />
                            </ResourceDictionary>
                            """;

        var renamed = ThemeResourceEditor.RenameResourceKey(xaml, "MyButtonTheme1", "PrimaryButtonTheme");
        var references = ThemeResourceEditor.RenameResourceReferences(
            renamed.Text,
            "MyButtonTheme1",
            "PrimaryButtonTheme");
        var duplicated = ThemeResourceEditor.DuplicateResource(references, "PrimaryButtonTheme", "SecondaryButtonTheme");
        var cleanedReferences = ThemeResourceEditor.RemoveResourceReferences(references, "PrimaryButtonTheme");
        var deleted = ThemeResourceEditor.DeleteResource(duplicated.Text, "AccentBrush");

        Assert.True(renamed.Changed);
        Assert.Contains("x:Key=\"PrimaryButtonTheme\"", references, System.StringComparison.Ordinal);
        Assert.Contains("Theme=\"{StaticResource PrimaryButtonTheme}\"", references, System.StringComparison.Ordinal);
        Assert.True(duplicated.Changed);
        Assert.Contains("x:Key=\"SecondaryButtonTheme\"", duplicated.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Theme=\"{StaticResource PrimaryButtonTheme}\"", cleanedReferences, System.StringComparison.Ordinal);
        Assert.True(deleted.Changed);
        Assert.DoesNotContain("AccentBrush", deleted.Text, System.StringComparison.Ordinal);
        Assert.False(deleted.RemovedLastResource);
    }

    [Fact]
    public void ControlThemeAnalyzer_FindsStatesPartsAndTemplateBindings()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <ControlTheme x:Key="MyButtonTheme1" TargetType="Button">
                                <Setter Property="Template">
                                  <ControlTemplate>
                                    <Border x:Name="PART_Chrome"
                                            Padding="{TemplateBinding Padding}">
                                      <ContentPresenter Content="{TemplateBinding Content}" />
                                    </Border>
                                  </ControlTemplate>
                                </Setter>
                                <Style Selector="^:pointerover /template/ Border#PART_Chrome">
                                  <Setter Property="Opacity" Value="0.8" />
                                </Style>
                                <Style Selector="^:pressed /template/ Border#PART_Chrome">
                                  <Setter Property="Opacity" Value="0.6" />
                                </Style>
                              </ControlTheme>
                            </ResourceDictionary>
                            """;

        var analysis = ControlThemeAnalyzer.Analyze(xaml, "MyButtonTheme1");

        Assert.Equal("Button", analysis.TargetType);
        Assert.Contains(analysis.Parts, part => part.Name == "PART_Chrome" && part.Type == "Border");
        Assert.Contains(analysis.TemplateBindings, binding => binding.Property == "Padding");
        Assert.Contains(analysis.TemplateBindings, binding => binding.Property == "Content");
        Assert.Contains(analysis.AvailableStates, state => state == "normal");
        Assert.Contains(analysis.AvailableStates, state => state == "checked");
        Assert.Contains(analysis.StateSelectors, selector => selector.State == "pointerover");
        Assert.Contains(analysis.StateSelectors, selector => selector.State == "pressed");
    }

    [Fact]
    public void ControlThemeEditor_AddsStateSetterAndVariantPreview()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <ControlTheme x:Key="MyButtonTheme1" TargetType="Button" />
                            </ResourceDictionary>
                            """;

        var stateEdit = ControlThemeEditor.SetStateSetter(
            xaml,
            "MyButtonTheme1",
            "pointerover",
            "Opacity",
            "0.8");
        var previewEdit = ControlThemeEditor.SetDesignPreview(
            stateEdit.Text,
            ControlThemeResourceBuilder.CreateVariantPreviewXaml("Button", "MyButtonTheme1"));

        Assert.True(stateEdit.Changed);
        Assert.Contains("Selector=\"^:pointerover\"", stateEdit.Text, System.StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"0.8\"", stateEdit.Text, System.StringComparison.Ordinal);
        Assert.True(previewEdit.Changed);
        Assert.Contains("RequestedThemeVariant=\"Light\"", previewEdit.Text, System.StringComparison.Ordinal);
        Assert.Contains("RequestedThemeVariant=\"Dark\"", previewEdit.Text, System.StringComparison.Ordinal);
    }
}
