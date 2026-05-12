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
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Light">
                      <SolidColorBrush x:Key="LightOnlyBrush" Color="White" />
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Dark">
                      <SolidColorBrush x:Key="LightOnlyBrush" Color="Black" />
                    </ResourceDictionary>
                  </ResourceDictionary.ThemeDictionaries>
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
                          BorderBrush="{DynamicResource LightOnlyBrush}"
                          Background="{DynamicResource MissingBrush}" />
                </UserControl>
                """,
                IsResourceDictionary: false)
        });

        Assert.Contains(analysis.Resources, resource =>
            resource.Key == "MyButtonTheme1" &&
            resource.ResourceType == "ControlTheme" &&
            resource.TargetType == "Button");
        Assert.Contains(analysis.Resources, resource =>
            resource.Key == "LightOnlyBrush" &&
            resource.ResourceType == "SolidColorBrush");
        Assert.Contains(analysis.References, reference =>
            reference.Key == "MyButtonTheme1" &&
            reference.Kind == ThemeResourceReferenceKind.StaticResource);
        Assert.Contains(analysis.References, reference =>
            reference.Key == "LightOnlyBrush" &&
            reference.Kind == ThemeResourceReferenceKind.DynamicResource);
        Assert.Contains(analysis.References, reference =>
            reference.Key == "MissingBrush" &&
            reference.Kind == ThemeResourceReferenceKind.DynamicResource);
        Assert.Equal(2, analysis.Diagnostics.Count(diagnostic =>
            diagnostic.Message.Contains("Duplicate resource key 'AccentBrush'", System.StringComparison.Ordinal)));
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Duplicate resource key 'LightOnlyBrush'", System.StringComparison.Ordinal));
        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Resource 'MissingBrush' is referenced but not defined", System.StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Resource 'LightOnlyBrush' is referenced but not defined", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_HandlesMarkupExtensionResourceKeys()
    {
        var analysis = ResourceDictionaryAnalyzer.Analyze(new[]
        {
            new ThemeResourceDocument(
                "Themes/Button.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
                </ResourceDictionary>
                """,
                IsResourceDictionary: true),
            new ThemeResourceDocument(
                "Views/MainView.axaml",
                """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Button Theme="{StaticResource {x:Type Button}}" />
                  <Button Theme="{StaticResource ResourceKey={x:Type Button}}" />
                </UserControl>
                """,
                IsResourceDictionary: false)
        });

        Assert.Contains(analysis.Resources, resource => resource.Key == "{x:Type Button}");
        Assert.Equal(2, analysis.References.Count(reference => reference.Key == "{x:Type Button}"));
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("{x:Type Button}", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WarnsWhenBaseReferenceOnlyExistsInOneThemeScope()
    {
        var analysis = ResourceDictionaryAnalyzer.Analyze(new[]
        {
            new ThemeResourceDocument(
                "Themes/Palette.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Light">
                      <SolidColorBrush x:Key="ScopedOnlyBrush" Color="White" />
                      <SolidColorBrush x:Key="SharedVariantBrush" Color="White" />
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Dark">
                      <SolidColorBrush x:Key="SharedVariantBrush" Color="Black" />
                    </ResourceDictionary>
                  </ResourceDictionary.ThemeDictionaries>
                </ResourceDictionary>
                """,
                IsResourceDictionary: true),
            new ThemeResourceDocument(
                "Views/MainView.axaml",
                """
                <UserControl xmlns="https://github.com/avaloniaui">
                  <Border Background="{DynamicResource ScopedOnlyBrush}"
                          BorderBrush="{DynamicResource SharedVariantBrush}" />
                </UserControl>
                """,
                IsResourceDictionary: false)
        });

        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Resource 'ScopedOnlyBrush' is referenced but not defined", System.StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Resource 'SharedVariantBrush' is referenced but not defined", System.StringComparison.Ordinal));
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
    public void ThemeResourceEditor_RemovesSettersThatReferenceDeletedResources()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Style Selector="Button">
                                <Setter Property="Background" Value="{StaticResource AccentBrush}" />
                                <Setter Property="Foreground" Value="{DynamicResource OtherBrush}" />
                                <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                              </Style>
                              <Button Background="{StaticResource AccentBrush}" Content="Save" />
                            </ResourceDictionary>
                            """;

        var cleaned = ThemeResourceEditor.RemoveResourceReferences(xaml, "AccentBrush");

        Assert.DoesNotContain("Property=\"Background\"", cleaned, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"BorderBrush\"", cleaned, System.StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource OtherBrush}\"", cleaned, System.StringComparison.Ordinal);
        Assert.Contains("<Button Content=\"Save\"", cleaned, System.StringComparison.Ordinal);
        Assert.DoesNotContain("{StaticResource AccentBrush}", cleaned, System.StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource AccentBrush}", cleaned, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeResourceEditor_RenamesAndRemovesMarkupExtensionResourceReferences()
    {
        const string xaml = """
                            <UserControl xmlns="https://github.com/avaloniaui"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Button Theme="{StaticResource {x:Type Button}}" />
                            </UserControl>
                            """;

        var renamed = ThemeResourceEditor.RenameResourceReferences(xaml, "{x:Type Button}", "PrimaryButtonTheme");
        var cleaned = ThemeResourceEditor.RemoveResourceReferences(xaml, "{x:Type Button}");

        Assert.Contains("Theme=\"{StaticResource PrimaryButtonTheme}\"", renamed, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Theme=", cleaned, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeResourceEditor_EditsResourcesInsideThemeDictionaries()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <ResourceDictionary.ThemeDictionaries>
                                <ResourceDictionary x:Key="Light">
                                  <SolidColorBrush x:Key="VariantBrush" Color="White" />
                                </ResourceDictionary>
                              </ResourceDictionary.ThemeDictionaries>
                            </ResourceDictionary>
                            """;

        var renamed = ThemeResourceEditor.RenameResourceKey(xaml, "VariantBrush", "RenamedVariantBrush");
        var duplicated = ThemeResourceEditor.DuplicateResource(renamed.Text, "RenamedVariantBrush", "VariantBrushCopy");
        var deleted = ThemeResourceEditor.DeleteResource(duplicated.Text, "RenamedVariantBrush");

        Assert.True(renamed.Changed);
        Assert.Contains("x:Key=\"RenamedVariantBrush\"", renamed.Text, System.StringComparison.Ordinal);
        Assert.True(duplicated.Changed);
        Assert.Contains("x:Key=\"VariantBrushCopy\"", duplicated.Text, System.StringComparison.Ordinal);
        Assert.True(deleted.Changed);
        Assert.DoesNotContain("x:Key=\"RenamedVariantBrush\"", deleted.Text, System.StringComparison.Ordinal);
        Assert.False(deleted.RemovedLastResource);
    }

    [Fact]
    public void ThemeResourceEditor_RemovesPreviewReferencesWhenDeletingLastKeptResource()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <Design.PreviewWith>
                                <Border Background="{StaticResource AccentBrush}" />
                              </Design.PreviewWith>
                              <SolidColorBrush x:Key="AccentBrush" Color="Red" />
                            </ResourceDictionary>
                            """;

        var deleted = ThemeResourceEditor.DeleteResource(xaml, "AccentBrush");

        Assert.True(deleted.Changed);
        Assert.True(deleted.RemovedLastResource);
        Assert.DoesNotContain("Design.PreviewWith", deleted.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("AccentBrush", deleted.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeResourceEditor_UsesLineToDisambiguateThemeDictionaryResources()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <ResourceDictionary.ThemeDictionaries>
                                <ResourceDictionary x:Key="Light">
                                  <SolidColorBrush x:Key="VariantBrush" Color="White" />
                                </ResourceDictionary>
                                <ResourceDictionary x:Key="Dark">
                                  <SolidColorBrush x:Key="VariantBrush" Color="Black" />
                                </ResourceDictionary>
                              </ResourceDictionary.ThemeDictionaries>
                            </ResourceDictionary>
                            """;
        var darkLine = ResourceDictionaryAnalyzer.Analyze(new[]
            {
                new ThemeResourceDocument("Themes/Palette.axaml", xaml, IsResourceDictionary: true)
            })
            .Resources
            .Where(resource => resource.Key == "VariantBrush")
            .Max(resource => resource.Line);

        var renamed = ThemeResourceEditor.RenameResourceKey(xaml, "VariantBrush", "DarkVariantBrush", darkLine);
        var duplicated = ThemeResourceEditor.DuplicateResource(xaml, "VariantBrush", "DarkVariantBrushCopy", darkLine);
        var deleted = ThemeResourceEditor.DeleteResource(xaml, "VariantBrush", darkLine);

        Assert.True(renamed.Changed);
        Assert.Contains("x:Key=\"VariantBrush\" Color=\"White\"", renamed.Text, System.StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DarkVariantBrush\" Color=\"Black\"", renamed.Text, System.StringComparison.Ordinal);
        Assert.True(duplicated.Changed);
        Assert.Contains("x:Key=\"DarkVariantBrushCopy\" Color=\"Black\"", duplicated.Text, System.StringComparison.Ordinal);
        Assert.True(deleted.Changed);
        Assert.Contains("x:Key=\"VariantBrush\" Color=\"White\"", deleted.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"Black\"", deleted.Text, System.StringComparison.Ordinal);
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
        Assert.Contains(analysis.PartSelectors, selector =>
            selector.PartName == "PART_Chrome" &&
            selector.PartType == "Border" &&
            selector.State == "pointerover");
    }

    [Fact]
    public void ControlThemeEditor_AddsStateAndTemplatePartSettersAndVariantPreview()
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
        var partEdit = ControlThemeEditor.SetSelectorSetter(
            stateEdit.Text,
            "MyButtonTheme1",
            "^ /template/ Border#PART_Chrome:pointerover",
            "Background",
            "{DynamicResource AccentBrush}");
        var previewEdit = ControlThemeEditor.SetDesignPreview(
            partEdit.Text,
            ControlThemeResourceBuilder.CreateVariantPreviewXaml("Button", "MyButtonTheme1"));

        Assert.True(stateEdit.Changed);
        Assert.Contains("Selector=\"^:pointerover\"", stateEdit.Text, System.StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"0.8\"", stateEdit.Text, System.StringComparison.Ordinal);
        Assert.True(partEdit.Changed);
        Assert.Contains("Selector=\"^ /template/ Border#PART_Chrome:pointerover\"", partEdit.Text, System.StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource AccentBrush}\"", partEdit.Text, System.StringComparison.Ordinal);
        Assert.True(previewEdit.Changed);
        Assert.Contains("RequestedThemeVariant=\"Light\"", previewEdit.Text, System.StringComparison.Ordinal);
        Assert.Contains("RequestedThemeVariant=\"Dark\"", previewEdit.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ControlThemeEditor_ReplacesChildSetterValueWhenSettingAttributeValue()
    {
        const string xaml = """
                            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                              <ControlTheme x:Key="MyButtonTheme1" TargetType="Button">
                                <Style Selector="^:pointerover">
                                  <Setter Property="Background">
                                    <Setter.Value>
                                      <SolidColorBrush Color="Red" />
                                    </Setter.Value>
                                  </Setter>
                                </Style>
                              </ControlTheme>
                            </ResourceDictionary>
                            """;

        var edit = ControlThemeEditor.SetSelectorSetter(
            xaml,
            "MyButtonTheme1",
            "^:pointerover",
            "Background",
            "{DynamicResource AccentBrush}");

        Assert.True(edit.Changed);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource AccentBrush}\"", edit.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Setter.Value", edit.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", edit.Text, System.StringComparison.Ordinal);
    }
}
