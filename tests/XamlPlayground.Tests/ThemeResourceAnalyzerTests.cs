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
}
