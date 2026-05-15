using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.DataGridDragDrop;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.Primitives;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using XamlPlayground.Behaviors;
using XamlPlayground.Services;
using XamlPlayground.Services.Animation;
using XamlPlayground.Services.VisualEditing;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;
using XamlPlayground.ViewModels.VisualEditing;
using XamlPlayground.Views;
using XamlPlayground.Views.Docking;
using XamlPlayground.Workspace;

namespace XamlPlayground.Tests;

public sealed class VisualEditingTests
{
    [Fact]
    public void AnimationTimelineEditor_AddsElementStyleAnimation()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <Button x:Name="SaveButton" Content="Save" />
                   </Grid>
                   """;
        var timeline = new AnimationTimelineDefinition(
            "Button",
            "0:0:0.25",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "CubicEaseOut",
            new[]
            {
                new AnimationTimelineTrackDefinition(
                    "Button",
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(0, "Opacity", "0", string.Empty),
                        new AnimationTimelineKeyFrameDefinition(100, "Opacity", "1", string.Empty)
                    })
            });

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<Button.Styles>", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Button\">", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style.Animations><Animation", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style.Animations>" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Animation Duration=\"0:0:0.25\" PlaybackDirection=\"Normal\" FillMode=\"Both\" Easing=\"CubicEaseOut\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<KeyFrame Cue=\"0%\">" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0\" />", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<KeyFrame Cue=\"100%\">" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"1\" />", edit.Text, StringComparison.Ordinal);
        Assert.IsAssignableFrom<Grid>(AvaloniaRuntimeXamlLoader.Load(edit.Text));

        var loaded = editor.ReadElementAnimation(edit.Text, XamlElementSelector.ByName("SaveButton"), "Button");
        var track = Assert.Single(loaded.Tracks);
        Assert.Equal("Button", loaded.TargetSelector);
        Assert.Equal("Opacity", track.PropertyName);
        Assert.Equal(new[] { 0, 100 }, track.KeyFrames.Select(static frame => frame.CuePercent));
    }

    [Fact]
    public void AnimationTimelineEditor_PreservesElementLexicalFormattingWhenAddingAnimation()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = string.Join(
            "\r\n",
            "<Grid xmlns=\"https://github.com/avaloniaui\"",
            "      xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">",
            "\t<Button",
            "\t\tx:Name='SaveButton'",
            "\t\tContent = 'Save' />",
            "\t<TextBlock Text='Keep' />",
            "</Grid>") + "\r\n";
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("\t\tx:Name='SaveButton'\r\n\t\tContent = 'Save'>", edit.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n\t<TextBlock Text='Keep' />\r\n", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SaveButton\"", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Save\"", edit.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n\t\t<Button.Styles>\r\n", edit.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n\t\t\t<Style Selector=\"Button\">\r\n", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_PreservesCarriageReturnOnlyLineEndings()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = string.Join(
            "\r",
            "<Grid xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">",
            "  <Button x:Name=\"SaveButton\" />",
            "</Grid>") + "\r";
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.DoesNotContain("\n", edit.Text, StringComparison.Ordinal);
        Assert.Contains("\r  <Button x:Name=\"SaveButton\">\r", edit.Text, StringComparison.Ordinal);
        Assert.Contains("\r    <Button.Styles>\r", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_RejectsMalformedXamlBeforeEditing()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Button x:Name="SaveButton">
                   </Grid>
                   """;
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.False(edit.Changed);
        Assert.Equal(xaml, edit.Text);
        Assert.NotNull(edit.Error);
    }

    [Fact]
    public void AnimationTimelineEditor_ReplacesOnlyEditedAnimationBlock()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = string.Join(
            "\r\n",
            "<Grid xmlns=\"https://github.com/avaloniaui\">",
            "\t<Grid.Styles>",
            "\t\t<Style Selector=\"Button.primary\">",
            "\t\t\t<Setter Property=\"MinWidth\" Value=\"80\" />",
            "\t\t\t<Style.Animations>",
            "\t\t\t\t<Animation Duration=\"0:0:0.1\" FillMode=\"Both\">",
            "\t\t\t\t\t<KeyFrame Cue=\"0%\">",
            "\t\t\t\t\t\t<Setter Property=\"Opacity\" Value=\"0.1\" />",
            "\t\t\t\t\t</KeyFrame>",
            "\t\t\t\t</Animation>",
            "\t\t\t\t<Animation Duration=\"0:0:1\" Easing=\"SineEaseOut\">",
            "\t\t\t\t\t<KeyFrame Cue=\"100%\">",
            "\t\t\t\t\t\t<Setter Property=\"RenderTransform.ScaleX\" Value=\"1.2\" />",
            "\t\t\t\t\t</KeyFrame>",
            "\t\t\t\t</Animation>",
            "\t\t\t</Style.Animations>",
            "\t\t</Style>",
            "\t</Grid.Styles>",
            "\t<Button Classes=\"primary\" />",
            "</Grid>") + "\r\n";
        var target = Assert.Single(editor.GetDocumentStyleTargets(xaml));
        var timeline = CreateOpacityTimeline(target.Selector, "0", "1");
        var siblingAnimation = string.Join(
            "\r\n",
            "\t\t\t\t<Animation Duration=\"0:0:1\" Easing=\"SineEaseOut\">",
            "\t\t\t\t\t<KeyFrame Cue=\"100%\">",
            "\t\t\t\t\t\t<Setter Property=\"RenderTransform.ScaleX\" Value=\"1.2\" />",
            "\t\t\t\t\t</KeyFrame>",
            "\t\t\t\t</Animation>");

        var edit = editor.SetDocumentStyleAnimation(xaml, target.Index, target.Selector, timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("\t\t\t<Setter Property=\"MinWidth\" Value=\"80\" />\r\n", edit.Text, StringComparison.Ordinal);
        Assert.Contains(siblingAnimation, edit.Text, StringComparison.Ordinal);
        Assert.Contains("\t<Button Classes=\"primary\" />\r\n", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"0.1\"", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_NormalizesElementNestingSelectorToControlType()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <TextBox x:Name="NameBox" />
                   </Grid>
                   """;
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("NameBox"), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<TextBox.Styles>", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"TextBox\">", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Selector=\"^\"", edit.Text, StringComparison.Ordinal);
        Assert.IsAssignableFrom<Grid>(AvaloniaRuntimeXamlLoader.Load(edit.Text));
    }

    [Fact]
    public void AnimationTimelineEditor_ReusesExistingCaretElementStyleForTypeTarget()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <Button x:Name="SaveButton" Content="Save">
                       <Button.Styles>
                         <Style Selector="^">
                           <Setter Property="MinWidth" Value="80" />
                           <Style.Animations>
                             <Animation Duration="0:0:0.1">
                               <KeyFrame Cue="100%">
                                 <Setter Property="Opacity" Value="0.4" />
                               </KeyFrame>
                             </Animation>
                           </Style.Animations>
                         </Style>
                       </Button.Styles>
                     </Button>
                   </Grid>
                   """;
        var timeline = CreateOpacityTimeline("Button", "0", "1");

        var loaded = editor.ReadElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), "Button");
        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.Equal("Opacity", Assert.Single(loaded.Tracks).PropertyName);
        Assert.True(edit.Changed, edit.Error);
        Assert.Equal(1, edit.Text.Split("<Style Selector=", StringSplitOptions.None).Length - 1);
        Assert.Contains("<Style Selector=\"Button\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"80\" />", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Selector=\"^\"", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"0.4\"", edit.Text, StringComparison.Ordinal);
        Assert.IsAssignableFrom<Grid>(AvaloniaRuntimeXamlLoader.Load(edit.Text));
    }

    [Fact]
    public void AnimationTimelineEditor_DeduplicatesPropertySettersAtSameCue()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <Button x:Name="SaveButton" Content="Save" />
                   </Grid>
                   """;
        var timeline = new AnimationTimelineDefinition(
            "Button",
            "0:0:0.25",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "CubicEaseOut",
            new[]
            {
                new AnimationTimelineTrackDefinition(
                    "Button",
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(50, " Opacity ", "0.25", string.Empty)
                    }),
                new AnimationTimelineTrackDefinition(
                    "Button",
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(50, "Opacity", "0.75", string.Empty)
                    })
            });

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.DoesNotContain("Value=\"0.25\"", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<KeyFrame Cue=\"50%\">" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0.75\" />", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_ReadsLastPropertySetterAtSameCue()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <Button x:Name="SaveButton" Content="Save">
                       <Button.Styles>
                         <Style Selector="^">
                           <Style.Animations>
                             <Animation Duration="0:0:0.25">
                               <KeyFrame Cue="50%">
                                 <Setter Property="Opacity" Value="0.25" />
                                 <Setter Property=" Opacity " Value="0.75" />
                               </KeyFrame>
                             </Animation>
                           </Style.Animations>
                         </Style>
                       </Button.Styles>
                     </Button>
                   </Grid>
                   """;

        var timeline = editor.ReadElementAnimation(xaml, XamlElementSelector.ByName("SaveButton"), "^");

        var track = Assert.Single(timeline.Tracks);
        var frame = Assert.Single(track.KeyFrames);
        Assert.Equal("Button", timeline.TargetSelector);
        Assert.Equal("Opacity", track.PropertyName);
        Assert.Equal(50, frame.CuePercent);
        Assert.Equal("0.75", frame.Value);
    }

    [Fact]
    public void AnimationTimelineEditor_EditsDocumentStyleAnimation()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Grid.Styles>
                       <Style Selector="Button.primary">
                         <Setter Property="Opacity" Value="0.9" />
                       </Style>
                     </Grid.Styles>
                     <Button Classes="primary" />
                   </Grid>
                   """;
        var targets = editor.GetDocumentStyleTargets(xaml);
        var target = Assert.Single(targets);
        var timeline = new AnimationTimelineDefinition(
            "Button.primary:pointerover",
            "0:0:0.2",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "CubicEaseOut",
            new[]
            {
                new AnimationTimelineTrackDefinition(
                    "Button.primary:pointerover",
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(0, "Opacity", "0.8", string.Empty),
                        new AnimationTimelineKeyFrameDefinition(100, "Opacity", "1", string.Empty)
                    })
            });

        var edit = editor.SetDocumentStyleAnimation(xaml, target.Index, target.Selector, timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<Style Selector=\"Button.primary:pointerover\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style.Animations>" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Animation Duration=\"0:0:0.2\" PlaybackDirection=\"Normal\" FillMode=\"Both\" Easing=\"CubicEaseOut\">", edit.Text, StringComparison.Ordinal);

        var loaded = editor.ReadDocumentStyleAnimation(edit.Text, target.Index, "Button.primary:pointerover", "Button.primary:pointerover");
        var track = Assert.Single(loaded.Tracks);
        Assert.Equal("Opacity", track.PropertyName);
        Assert.Equal(new[] { 0, 100 }, track.KeyFrames.Select(static frame => frame.CuePercent));
    }

    [Fact]
    public void AnimationTimelineEditor_PreservesSiblingAnimationsWhenApplyingTimeline()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Grid.Styles>
                       <Style Selector="Button.primary">
                         <Style.Animations>
                           <Animation Duration="0:0:0.1" FillMode="Both">
                             <KeyFrame Cue="0%">
                               <Setter Property="Opacity" Value="0.1" />
                             </KeyFrame>
                           </Animation>
                           <Animation Duration="0:0:1" Easing="SineEaseOut">
                             <KeyFrame Cue="100%">
                               <Setter Property="RenderTransform.ScaleX" Value="1.2" />
                             </KeyFrame>
                           </Animation>
                         </Style.Animations>
                       </Style>
                     </Grid.Styles>
                   </Grid>
                   """;
        var target = Assert.Single(editor.GetDocumentStyleTargets(xaml));
        var timeline = CreateOpacityTimeline(target.Selector, "0", "1");

        var edit = editor.SetDocumentStyleAnimation(xaml, target.Index, target.Selector, timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Equal(2, edit.Text.Split("<Animation Duration=", StringSplitOptions.None).Length - 1);
        Assert.Contains("<Animation Duration=\"0:0:0.25\" PlaybackDirection=\"Normal\" FillMode=\"Both\" Easing=\"CubicEaseOut\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Animation Duration=\"0:0:1\" Easing=\"SineEaseOut\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RenderTransform.ScaleX\" Value=\"1.2\" />", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"0.1\"", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_AppliesDocumentStyleFrameSetter()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Grid.Styles>
                       <Style Selector="Button.primary">
                         <Setter Property="Opacity" Value="0.9" />
                       </Style>
                     </Grid.Styles>
                   </Grid>
                   """;
        var target = Assert.Single(editor.GetDocumentStyleTargets(xaml));

        var edit = editor.SetDocumentStyleSetter(
            xaml,
            target.Index,
            target.Selector,
            "Button.primary:pressed",
            "Opacity",
            "0.7");

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<Style Selector=\"Button.primary:pressed\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0.7\" />", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_UsesVisualPathsThatIgnoreNonVisualMemberElements()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Grid.Styles>
                       <Style Selector="Button" />
                     </Grid.Styles>
                     <Button Content="Save" />
                   </Grid>
                   """;
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByPath(0), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<Button.Styles>", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Button\">", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style.Styles>", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_PreservesPrefixedElementNamespaceForMemberElement()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <local:Widget xmlns="https://github.com/avaloniaui"
                                 xmlns:local="using:Sample.Controls" />
                   """;
        var timeline = CreateOpacityTimeline("^", "0", "1");

        var edit = editor.SetElementAnimation(xaml, XamlElementSelector.ByPath(), timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("<local:Widget.Styles>", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"local|Widget\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("</local:Widget.Styles>", edit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimationTimelineEditor_AddsControlThemeTemplatePartAnimation()
    {
        var editor = new AnimationTimelineEditor();
        var xaml = """
                   <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <ControlTheme x:Key="MyButtonTheme" TargetType="Button">
                       <Setter Property="Template">
                         <ControlTemplate>
                           <ContentPresenter x:Name="PART_ContentPresenter" />
                         </ControlTemplate>
                       </Setter>
                     </ControlTheme>
                   </ResourceDictionary>
                   """;
        var timeline = new AnimationTimelineDefinition(
            "^ /template/ ContentPresenter#PART_ContentPresenter:pointerover",
            "0:0:0.18",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "QuadraticEaseOut",
            new[]
            {
                new AnimationTimelineTrackDefinition(
                    "^ /template/ ContentPresenter#PART_ContentPresenter:pointerover",
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(0, "Opacity", "1", string.Empty),
                        new AnimationTimelineKeyFrameDefinition(100, "Opacity", "0.7", "0.1,0.9,0.2,1")
                    })
            });

        var edit = editor.SetControlThemeAnimation(xaml, "MyButtonTheme", timeline);

        Assert.True(edit.Changed, edit.Error);
        Assert.Contains("Selector=\"^ /template/ ContentPresenter#PART_ContentPresenter:pointerover\"", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Style.Animations>" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Animation Duration=\"0:0:0.18\" PlaybackDirection=\"Normal\" FillMode=\"Both\" Easing=\"QuadraticEaseOut\">", edit.Text, StringComparison.Ordinal);
        Assert.Contains("<KeyFrame Cue=\"100%\" KeySpline=\"0.1,0.9,0.2,1\">" + Environment.NewLine, edit.Text, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0.7\" />", edit.Text, StringComparison.Ordinal);

        var loaded = editor.ReadControlThemeAnimation(edit.Text, "MyButtonTheme", "^ /template/ ContentPresenter#PART_ContentPresenter:pointerover");
        Assert.Equal("0:0:0.18", loaded.Duration);
        Assert.Equal("QuadraticEaseOut", loaded.Easing);
        Assert.Equal("Opacity", Assert.Single(loaded.Tracks).PropertyName);
    }

    private static AnimationTimelineDefinition CreateOpacityTimeline(string selector, string fromValue, string toValue)
    {
        return new AnimationTimelineDefinition(
            selector,
            "0:0:0.25",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "CubicEaseOut",
            new[]
            {
                new AnimationTimelineTrackDefinition(
                    selector,
                    "Opacity",
                    new[]
                    {
                        new AnimationTimelineKeyFrameDefinition(0, "Opacity", fromValue, string.Empty),
                        new AnimationTimelineKeyFrameDefinition(100, "Opacity", toValue, string.Empty)
                    })
            });
    }

    [Fact]
    public void MutationEngine_AnalyzesFullFidelityXamlTree()
    {
        var engine = new XamlMutationEngine();
        var snapshot = engine.Analyze("""
                                      <Grid xmlns="https://github.com/avaloniaui">
                                        <Button x:Name="SaveButton" Content="Save" />
                                      </Grid>
                                      """);

        Assert.Empty(snapshot.Diagnostics);
        Assert.NotNull(snapshot.Root);
        Assert.Equal("Grid", snapshot.Root.TypeName);
        Assert.Collection(
            snapshot.Elements,
            root => Assert.Equal(Array.Empty<int>(), root.Path),
            button =>
            {
                Assert.Equal("Button", button.TypeName);
                Assert.Equal("SaveButton", button.Name);
                Assert.Equal(new[] { 0 }, button.Path);
                Assert.Equal("Save", button.Attributes["Content"]);
            });
    }

    [Fact]
    public void MutationEngine_ExcludesMemberElementsFromVisualPaths()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Grid.RowDefinitions>
                       <RowDefinition Height="Auto" />
                     </Grid.RowDefinitions>
                     <Button x:Name="Action" />
                   </Grid>
                   """;

        var snapshot = engine.Analyze(xaml);
        var inserted = engine.InsertChild(xaml, XamlElementSelector.ByPath(), "<TextBlock />", 0);

        Assert.Empty(snapshot.Diagnostics);
        Assert.DoesNotContain(snapshot.Elements, element => element.TypeName == "Grid.RowDefinitions");
        Assert.DoesNotContain(snapshot.Elements, element => element.TypeName == "RowDefinition");
        Assert.Collection(
            snapshot.Elements,
            root =>
            {
                Assert.Equal(Array.Empty<int>(), root.Path);
                Assert.Equal(1, root.ChildElementCount);
            },
            button =>
            {
                Assert.Equal("Button", button.TypeName);
                Assert.Equal(new[] { 0 }, button.Path);
            });
        Assert.Empty(inserted.Diagnostics);
        Assert.Contains(
            "</Grid.RowDefinitions>\n  <TextBlock />\n  <Button x:Name=\"Action\" />",
            NormalizeLineEndings(inserted.Text),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_PreservesVisualContentInsideContentPropertyElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <ContentControl xmlns="https://github.com/avaloniaui">
                     <ContentControl.Content>
                       <Button x:Name="Action" Content="Save" />
                     </ContentControl.Content>
                   </ContentControl>
                   """;

        var snapshot = engine.Analyze(xaml);
        var changed = engine.SetProperty(xaml, XamlElementSelector.ByPath(0), "Content", "Apply");

        Assert.Empty(snapshot.Diagnostics);
        Assert.DoesNotContain(snapshot.Elements, element => element.TypeName == "ContentControl.Content");
        Assert.Collection(
            snapshot.Elements,
            root =>
            {
                Assert.Equal("ContentControl", root.TypeName);
                Assert.Equal(Array.Empty<int>(), root.Path);
                Assert.Equal(1, root.ChildElementCount);
            },
            button =>
            {
                Assert.Equal("Button", button.TypeName);
                Assert.Equal("Action", button.Name);
                Assert.Equal(new[] { 0 }, button.Path);
                Assert.Equal("Save", button.Attributes["Content"]);
            });
        Assert.Empty(changed.Diagnostics);
        Assert.Contains("<Button x:Name=\"Action\" Content=\"Apply\" />", changed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_ResolvesAnonymousVisualContentPropertyDescendantPaths()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                     <ContentControl>
                       <ContentControl.Content>
                         <Button Content="Save" />
                       </ContentControl.Content>
                     </ContentControl>
                   </StackPanel>
                   """;

        var snapshot = engine.Analyze(xaml);
        var changed = engine.SetProperty(xaml, XamlElementSelector.ByPath(0, 0), "Content", "Apply");

        Assert.Empty(snapshot.Diagnostics);
        Assert.Collection(
            snapshot.Elements,
            root => Assert.Equal(Array.Empty<int>(), root.Path),
            contentControl => Assert.Equal(new[] { 0 }, contentControl.Path),
            button =>
            {
                Assert.Equal("Button", button.TypeName);
                Assert.Equal(new[] { 0, 0 }, button.Path);
                Assert.Equal("Save", button.Attributes["Content"]);
            });
        Assert.Empty(changed.Diagnostics);
        Assert.Contains("<Button Content=\"Apply\" />", changed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_InsertsIntoEmptyVisualContentPropertyElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <ContentControl xmlns="https://github.com/avaloniaui">
                     <ContentControl.Content>
                     </ContentControl.Content>
                   </ContentControl>
                   """;

        var inserted = engine.InsertChild(xaml, XamlElementSelector.ByPath(), "<Button Content=\"Save\" />");

        Assert.Empty(inserted.Diagnostics);
        Assert.Contains(
            NormalizeLineEndings(
                """
                  <ContentControl.Content>
                    <Button Content="Save" />
                  </ContentControl.Content>
                """),
            NormalizeLineEndings(inserted.Text),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "</ContentControl.Content>\n  <Button Content=\"Save\" />",
            NormalizeLineEndings(inserted.Text),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_DuplicatesVisualContentPropertyChildrenInsideWrapper()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                     <StackPanel.Children>
                       <Button Content="Save" />
                     </StackPanel.Children>
                   </StackPanel>
                   """;

        var duplicated = engine.DuplicateElement(xaml, XamlElementSelector.ByPath(0));

        Assert.Empty(duplicated.Diagnostics);
        Assert.Contains(
            NormalizeLineEndings(
                """
                  <StackPanel.Children>
                    <Button Content="Save" />
                    <Button Content="Save" />
                  </StackPanel.Children>
                """),
            NormalizeLineEndings(duplicated.Text),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "</StackPanel.Children>\n  <Button Content=\"Save\" />",
            NormalizeLineEndings(duplicated.Text),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_SetsAddsAndRemovesProperties()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Button x:Name="SaveButton" Content="Save" />
                   </Grid>
                   """;

        var changed = engine.SetProperty(xaml, XamlElementSelector.ByName("SaveButton"), "Content", "Apply");
        changed = engine.SetProperty(changed.Text, XamlElementSelector.ByName("SaveButton"), "Margin", "8");
        changed = engine.RemoveProperty(changed.Text, XamlElementSelector.ByName("SaveButton"), "Margin");

        Assert.Empty(changed.Diagnostics);
        Assert.Contains("Content=\"Apply\"", changed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"8\"", changed.Text, StringComparison.Ordinal);
        Assert.Contains("<Button x:Name=\"SaveButton\" Content=\"Apply\" />", changed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_UsesStructuralPathForAnonymousElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <Border>
                       <TextBlock Text="Before" />
                     </Border>
                   </Grid>
                   """;

        var changed = engine.SetProperty(xaml, XamlElementSelector.ByPath(0, 0), "Text", "After");

        Assert.Empty(changed.Diagnostics);
        Assert.Contains("<TextBlock Text=\"After\" />", changed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_InsertsAndRemovesChildElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui" x:Name="Root" />
                   """;

        var inserted = engine.InsertChild(
            xaml,
            XamlElementSelector.ByName("Root"),
            "<TextBlock x:Name=\"Title\" Text=\"Hello\" />");

        Assert.Empty(inserted.Diagnostics);
        Assert.Contains("<StackPanel xmlns=\"https://github.com/avaloniaui\" x:Name=\"Root\">", inserted.Text, StringComparison.Ordinal);
        Assert.Contains("<TextBlock x:Name=\"Title\" Text=\"Hello\" />", inserted.Text, StringComparison.Ordinal);
        Assert.Contains("</StackPanel>", inserted.Text, StringComparison.Ordinal);

        var removed = engine.RemoveElement(inserted.Text, XamlElementSelector.ByName("Title"));

        Assert.Empty(removed.Diagnostics);
        Assert.DoesNotContain("TextBlock", removed.Text, StringComparison.Ordinal);
        Assert.Contains("<StackPanel", removed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_PreservesAstWhitespaceWhenInsertingChildren()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                       <Button x:Name="A" />
                       <Button x:Name="B" />
                   </StackPanel>
                   """;

        var inserted = engine.InsertChild(
            xaml,
            XamlElementSelector.ByPath(),
            "<TextBlock x:Name=\"Title\" />",
            1);
        var appended = engine.InsertChild(
            inserted.Text,
            XamlElementSelector.ByPath(),
            "<TextBlock x:Name=\"Tail\" />");

        Assert.Empty(inserted.Diagnostics);
        Assert.Equal(
            """
            <StackPanel xmlns="https://github.com/avaloniaui">
                <Button x:Name="A" />
                <TextBlock x:Name="Title" />
                <Button x:Name="B" />
            </StackPanel>
            """,
            inserted.Text);
        Assert.Empty(appended.Diagnostics);
        Assert.Equal(
            """
            <StackPanel xmlns="https://github.com/avaloniaui">
                <Button x:Name="A" />
                <TextBlock x:Name="Title" />
                <Button x:Name="B" />
                <TextBlock x:Name="Tail" />
            </StackPanel>
            """,
            appended.Text);
    }

    [Fact]
    public void MutationEngine_PreservesNewLineStyleAndMultilineAttributes()
    {
        var engine = new XamlMutationEngine();
        var xaml = "<Button\r\n    x:Name=\"Save\"\r\n    Content=\"Save\" />";

        var changed = engine.SetProperty(xaml, XamlElementSelector.ByName("Save"), "Margin", "8");
        var removed = engine.RemoveProperty(changed.Text, XamlElementSelector.ByName("Save"), "Content");

        Assert.Empty(changed.Diagnostics);
        Assert.Equal(
            "<Button\r\n    x:Name=\"Save\"\r\n    Content=\"Save\"\r\n    Margin=\"8\" />",
            changed.Text);
        Assert.Empty(removed.Diagnostics);
        Assert.Equal(
            "<Button\r\n    x:Name=\"Save\"\r\n    Margin=\"8\" />",
            removed.Text);
    }

    [Fact]
    public void MutationEngine_RenamesReplacesWrapsAndUnwrapsElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                     <Button x:Name="Action" Content="Save" />
                   </StackPanel>
                   """;

        var renamed = engine.RenameElement(xaml, XamlElementSelector.ByName("Action"), "ToggleButton");
        var replaced = engine.ReplaceElement(renamed.Text, XamlElementSelector.ByName("Action"), "<TextBlock x:Name=\"Action\" Text=\"Save\" />");
        var wrapped = engine.WrapElement(replaced.Text, XamlElementSelector.ByName("Action"), "<Border Padding=\"8\" />");
        var unwrapped = engine.UnwrapElement(wrapped.Text, XamlElementSelector.ByPath(0));

        Assert.Empty(renamed.Diagnostics);
        Assert.Contains("<ToggleButton x:Name=\"Action\" Content=\"Save\" />", renamed.Text, StringComparison.Ordinal);
        Assert.Empty(replaced.Diagnostics);
        Assert.Contains("<TextBlock x:Name=\"Action\" Text=\"Save\" />", replaced.Text, StringComparison.Ordinal);
        Assert.Empty(wrapped.Diagnostics);
        Assert.Contains("<Border Padding=\"8\">", wrapped.Text, StringComparison.Ordinal);
        Assert.Contains("<TextBlock x:Name=\"Action\" Text=\"Save\" />", wrapped.Text, StringComparison.Ordinal);
        Assert.Empty(unwrapped.Diagnostics);
        Assert.DoesNotContain("Border Padding", unwrapped.Text, StringComparison.Ordinal);
        Assert.Contains("<TextBlock x:Name=\"Action\" Text=\"Save\" />", unwrapped.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_MovesAndReordersElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <StackPanel x:Name="Left">
                       <Button x:Name="A" />
                       <Button x:Name="B" />
                     </StackPanel>
                     <StackPanel x:Name="Right" />
                   </Grid>
                   """;

        var moved = engine.MoveElement(xaml, XamlElementSelector.ByName("B"), XamlElementSelector.ByName("Right"));
        var reordered = engine.ReorderElement(moved.Text, XamlElementSelector.ByName("A"), 0);

        Assert.Empty(moved.Diagnostics);
        Assert.Contains(
            "<StackPanel x:Name=\"Right\">\n    <Button x:Name=\"B\" />\n  </StackPanel>",
            NormalizeLineEndings(moved.Text),
            StringComparison.Ordinal);
        Assert.Empty(reordered.Diagnostics);
        Assert.Contains("<Button x:Name=\"A\" />", reordered.Text, StringComparison.Ordinal);
        Assert.Contains("<Button x:Name=\"B\" />", reordered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_MovesEarlierSiblingIntoUnnamedLaterParent()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <StackPanel>
                       <Button x:Name="A" />
                     </StackPanel>
                     <StackPanel>
                       <Button x:Name="B" />
                     </StackPanel>
                   </Grid>
                   """;

        var moved = engine.MoveElement(xaml, XamlElementSelector.ByPath(0), XamlElementSelector.ByPath(1));
        var movedA = Assert.Single(moved.Snapshot.Elements, element => element.Name == "A");
        var movedB = Assert.Single(moved.Snapshot.Elements, element => element.Name == "B");

        Assert.Empty(moved.Diagnostics);
        Assert.Equal(new[] { 0, 1, 0 }, movedA.Path);
        Assert.Equal(new[] { 0, 0 }, movedB.Path);
    }

    [Fact]
    public void MutationEngine_DoesNotShiftTargetParentForNestedSourceRemoval()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui">
                     <StackPanel>
                       <Button Content="Move" />
                     </StackPanel>
                     <StackPanel />
                   </Grid>
                   """;

        var moved = engine.MoveElement(xaml, XamlElementSelector.ByPath(0, 0), XamlElementSelector.ByPath(1));

        Assert.Empty(moved.Diagnostics);
        var movedButton = Assert.Single(moved.Snapshot.Elements, element =>
            element.Attributes.TryGetValue("Content", out var content) &&
            string.Equals(content, "Move", StringComparison.Ordinal));
        Assert.Equal(new[] { 1, 0 }, movedButton.Path);
    }

    [Fact]
    public void MutationEngine_DuplicatesElementsWithoutDuplicatingNames()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                     <TextBlock x:Name="Title" Text="Hello" />
                     <Button x:Name="Action" Content="Run" />
                   </StackPanel>
                   """;

        var duplicated = engine.DuplicateElement(xaml, XamlElementSelector.ByName("Title"));
        var reordered = engine.ReorderElement(duplicated.Text, XamlElementSelector.ByName("Action"), 0);

        Assert.Empty(duplicated.Diagnostics);
        Assert.Contains("<TextBlock Text=\"Hello\" />", duplicated.Text, StringComparison.Ordinal);
        Assert.Single(duplicated.Snapshot.Elements, element => element.Name == "Title");
        Assert.Equal(3, duplicated.Snapshot.Root?.ChildElementCount);
        Assert.Empty(reordered.Diagnostics);
        Assert.True(
            reordered.Text.IndexOf("<Button x:Name=\"Action\"", StringComparison.Ordinal) <
            reordered.Text.IndexOf("<TextBlock x:Name=\"Title\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MutationEngine_DuplicatesElementsWithoutDuplicatingDescendantNames()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                     <StackPanel x:Name="Panel">
                       <Button x:Name="Save" />
                       <TextBlock Name="Legacy" />
                     </StackPanel>
                   </StackPanel>
                   """;

        var duplicated = engine.DuplicateElement(xaml, XamlElementSelector.ByName("Panel"));

        Assert.Empty(duplicated.Diagnostics);
        Assert.Equal(1, CountOccurrences(duplicated.Text, "x:Name=\"Panel\""));
        Assert.Equal(1, CountOccurrences(duplicated.Text, "x:Name=\"Save\""));
        Assert.Equal(1, CountOccurrences(duplicated.Text, "Name=\"Legacy\""));
    }

    [Fact]
    public void MutationEngine_SetsAndRemovesMemberElements()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <Grid xmlns="https://github.com/avaloniaui" x:Name="Root">
                     <Button />
                   </Grid>
                   """;

        var changed = engine.SetMemberElement(
            xaml,
            XamlElementSelector.ByName("Root"),
            "RowDefinitions",
            """
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            """);
        var removed = engine.RemoveMemberElement(changed.Text, XamlElementSelector.ByName("Root"), "RowDefinitions");

        Assert.Empty(changed.Diagnostics);
        Assert.Contains("<Grid.RowDefinitions>", changed.Text, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\" />", changed.Text, StringComparison.Ordinal);
        Assert.Empty(removed.Diagnostics);
        Assert.DoesNotContain("Grid.RowDefinitions", removed.Text, StringComparison.Ordinal);
        Assert.Contains("<Button />", removed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEngine_AppliesBatchMutationsInOrder()
    {
        var engine = new XamlMutationEngine();
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                     <TextBlock x:Name="Title" Text="Before" />
                   </StackPanel>
                   """;

        var result = engine.Batch(
            xaml,
            new[]
            {
                new XamlMutationRequest(
                    XamlMutationKind.SetProperty,
                    XamlElementSelector.ByName("Title"),
                    PropertyName: "Text",
                    Value: "After"),
                new XamlMutationRequest(
                    XamlMutationKind.InsertChild,
                    XamlElementSelector.ByPath(),
                    Xaml: "<Button x:Name=\"Action\" Content=\"Run\" />")
            });

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Text=\"After\"", result.Text, StringComparison.Ordinal);
        Assert.Contains("<Button x:Name=\"Action\" Content=\"Run\" />", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolboxInsertion_AddsMissingNamespaceAndAvoidsPrefixCollisions()
    {
        var engine = new XamlMutationEngine();
        var service = new XamlToolboxInsertionService(engine);
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui"
                               xmlns:local="clr-namespace:Existing">
                   </StackPanel>
                   """;
        var item = new ToolboxItemDescriptor(
            "custom-control",
            "Custom Control",
            "Custom",
            "CustomControl",
            "clr-namespace:Demo.Controls;assembly=Demo",
            "Demo",
            "<local:CustomControl />",
            new Dictionary<string, string>());

        var inserted = service.Insert(
            xaml,
            XamlElementSelector.ByPath(),
            item,
            insertionProperties: new Dictionary<string, string>
            {
                ["Canvas.Left"] = "12"
            });

        Assert.True(inserted.Success, string.Join(Environment.NewLine, inserted.Mutation.Diagnostics));
        Assert.Equal("local1", inserted.NamespacePrefix);
        Assert.Contains("xmlns:local1=\"clr-namespace:Demo.Controls;assembly=Demo\"", inserted.Mutation.Text, StringComparison.Ordinal);
        Assert.Contains("<local1:CustomControl Canvas.Left=\"12\" />", inserted.Mutation.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolboxInsertion_PrefixesUnprefixedCustomNamespaceSnippets()
    {
        var engine = new XamlMutationEngine();
        var service = new XamlToolboxInsertionService(engine);
        var xaml = """
                   <StackPanel xmlns="https://github.com/avaloniaui">
                   </StackPanel>
                   """;
        var item = new ToolboxItemDescriptor(
            "custom-widget",
            "Custom Widget",
            "Custom",
            "MyWidget",
            "clr-namespace:Demo.Controls;assembly=Demo",
            "Demo",
            "<MyWidget><ChildWidget /></MyWidget>",
            new Dictionary<string, string>());

        var inserted = service.Insert(
            xaml,
            XamlElementSelector.ByPath(),
            item);

        Assert.True(inserted.Success, string.Join(Environment.NewLine, inserted.Mutation.Diagnostics));
        Assert.Equal("local", inserted.NamespacePrefix);
        Assert.Contains("xmlns:local=\"clr-namespace:Demo.Controls;assembly=Demo\"", inserted.Mutation.Text, StringComparison.Ordinal);
        Assert.Contains("<local:MyWidget>", inserted.Mutation.Text, StringComparison.Ordinal);
        Assert.Contains("<local:ChildWidget />", inserted.Mutation.Text, StringComparison.Ordinal);
        Assert.Contains("</local:MyWidget>", inserted.Mutation.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlEditorRegistry_ResolvesDedicatedEditorsBeforeFallback()
    {
        var registry = new ControlEditorRegistry();

        var text = registry.Resolve(typeof(TextBlock));
        var button = registry.Resolve(typeof(Button));
        var border = registry.Resolve(typeof(Border));

        Assert.Equal("Text", text.DisplayName);
        Assert.Contains(text.Properties, property => property.PropertyName == "Text");
        Assert.Contains(text.Properties, property => property.PropertyName == "Opacity" && property.Group == "Effects");
        Assert.Contains(text.Properties, property => property.PropertyName == "Grid.Row" && property.Group == "Layout");
        Assert.Contains(text.Properties, property => property.PropertyName == "ToolTip.Tip");
        Assert.True(text.Properties.Count > 25);
        Assert.Equal(text.Properties.Count, text.Properties.Select(property => property.PropertyName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Button", button.DisplayName);
        Assert.Contains(button.Properties, property => property.PropertyName == "Command");
        Assert.Equal("Border", border.DisplayName);
        Assert.Contains(border.Properties, property => property.PropertyName == "Margin");
    }

    [Fact]
    public void ControlEditorRegistry_QualifiesAttachedPropertiesForOwnerControls()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var descriptor = new ControlEditorRegistry().Resolve(typeof(Grid));

        Assert.Contains(descriptor.Properties, property => property.PropertyName == "Grid.Row");
        Assert.Contains(descriptor.Properties, property => property.PropertyName == "Grid.Column");
        Assert.DoesNotContain(descriptor.Properties, property => property.PropertyName == "Row");
        Assert.DoesNotContain(descriptor.Properties, property => property.PropertyName == "Column");
    }

    [Fact]
    public void VisualTreeMapper_PrefersInnermostSourceInfoMatch()
    {
        var engine = new XamlMutationEngine();
        var snapshot = engine.Analyze(
            "<Border xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Border>\n" +
            "    <TextBlock />\n" +
            "  </Border>\n" +
            "</Border>");
        var mapper = new XamlVisualTreeMapper();
        var visualNode = new VisualTreeNodeSnapshot(
            "inner-border",
            "Border",
            null,
            default,
            Array.Empty<int>(),
            null,
            2,
            4,
            Array.Empty<VisualTreeNodeSnapshot>());

        var element = mapper.FindXamlElement(visualNode, snapshot);

        Assert.NotNull(element);
        Assert.Equal(new[] { 0 }, element.Path);
    }

    [Fact]
    public void ToolboxCatalogBuilder_ScansAvaloniaControlAssemblies()
    {
        var builder = new ToolboxCatalogBuilder();

        var catalog = builder.Build(new ToolboxContext(new[] { typeof(Button).Assembly }));

        Assert.Contains(catalog.Items, item => item.TypeName == "Button" && item.Category == "Input");
        Assert.Contains(catalog.Items, item => item.TypeName == "StackPanel" && item.Category == "Layout");
        Assert.Contains(catalog.Items, item => item.DefaultXaml == "<Button />");
    }

    [Fact]
    public void ToolboxDragPayload_RoundTripsSelectedToolboxItem()
    {
        var item = new ToolboxItemDescriptor(
            "avalonia-button",
            "Button",
            "Input",
            "Button",
            "https://github.com/avaloniaui",
            string.Empty,
            "<Button />",
            new Dictionary<string, string>());

        var payload = ToolboxDragPayload.Create(item);

        Assert.True(ToolboxDragPayload.TryGetItemId(payload, out var itemId));
        Assert.Equal("avalonia-button", itemId);
        Assert.False(ToolboxDragPayload.TryGetItemId("plain text", out _));

        var data = ToolboxDragPayload.CreateDataTransfer(item);
        Assert.True(ToolboxDragPayload.TryGetItemId(data, out var transferredItemId));
        Assert.Equal("avalonia-button", transferredItemId);
        Assert.Equal("<Button />", data.TryGetText());
        Assert.False(ToolboxDragPayload.TryGetItemId(data.TryGetText(), out _));
    }

    [Fact]
    public void MainViewModel_VisualEditorCommandsKeepXamlInSync()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureAvaloniaEditTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <TextBlock x:Name="Title" Text="Before" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var titleNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Title");
            Assert.NotNull(titleNode);
            var structureModel = typeof(MainViewModel)
                .GetProperty("VisualEditorStructureModel")!
                .GetValue(viewModel);
            Assert.NotNull(structureModel);
            Assert.True((int)structureModel.GetType().GetProperty("Count")!.GetValue(structureModel)! >= 2);
            var flattenedProperty = structureModel.GetType()
                .GetProperties()
                .First(property => property.Name == "Flattened" &&
                                   property.DeclaringType == structureModel.GetType());
            var flattenedStructureNodes = ((System.Collections.IEnumerable)flattenedProperty
                    .GetValue(structureModel)!)
                .Cast<object>();
            Assert.Contains(flattenedStructureNodes, node =>
                ReferenceEquals(node.GetType().GetProperty("Item")!.GetValue(node), titleNode));
            Assert.Contains(viewModel.VisualEditorStructureRows, row => row.Element.Name == "Title");
            viewModel.SelectedVisualEditorNode = titleNode;
            var groupedPropertiesView = typeof(MainViewModel)
                .GetProperty("VisualEditorAvailablePropertiesView")!
                .GetValue(viewModel);
            Assert.NotNull(groupedPropertiesView);
            var groupDescriptions = Assert.IsAssignableFrom<System.Collections.ICollection>(
                groupedPropertiesView.GetType().GetProperty("GroupDescriptions")!.GetValue(groupedPropertiesView));
            Assert.Single(groupDescriptions.Cast<object>());
            Assert.Contains(viewModel.VisualEditorAvailableProperties, property => property.Name == "Grid.Row");
            Assert.Contains(viewModel.VisualEditorAvailableProperties, property => property.Name == "Opacity");
            var fontSizeProperty = Assert.Single(
                viewModel.VisualEditorAvailableProperties,
                property => property.Name == "FontSize");
            var textAlignmentProperty = Assert.Single(
                viewModel.VisualEditorAvailableProperties,
                property => property.Name.EndsWith("TextAlignment", StringComparison.Ordinal));
            viewModel.VisualEditorPropertyFilter = "TextAlignment";
            Assert.True(viewModel.VisualEditorAvailablePropertiesView!.Contains(textAlignmentProperty));
            Assert.False(viewModel.VisualEditorAvailablePropertiesView.Contains(fontSizeProperty));
            viewModel.VisualEditorPropertyFilter = "typography";
            Assert.True(viewModel.VisualEditorAvailablePropertiesView.Contains(fontSizeProperty));
            Assert.False(viewModel.VisualEditorAvailablePropertiesView.Contains(textAlignmentProperty));
            viewModel.VisualEditorPropertyFilter = string.Empty;
            Assert.True(viewModel.VisualEditorAvailablePropertiesView.Contains(fontSizeProperty));
            Assert.True(viewModel.VisualEditorAvailablePropertiesView.Contains(textAlignmentProperty));
            viewModel.VisualEditorPropertyName = "Text";
            viewModel.VisualEditorPropertyValue = "After";

            viewModel.ApplyVisualEditorPropertyCommand.Execute(null);

            Assert.Contains("Text=\"After\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Equal("TextBlock #Title", viewModel.VisualEditorSelectedElementTitle);
            Assert.Contains(viewModel.VisualEditorProperties, property =>
                property.Name == "Text" && property.Value == "After");

            viewModel.SelectedVisualEditorAvailableProperty =
                viewModel.VisualEditorAvailableProperties.First(property =>
                    property.Name == "TextWrapping");
            Assert.Contains("Wrap", viewModel.VisualEditorPropertyOptions);
            viewModel.SelectedVisualEditorPropertyOption = "Wrap";

            viewModel.ApplyVisualEditorPropertyCommand.Execute(null);

            Assert.Contains($"{viewModel.SelectedVisualEditorAvailableProperty.Name}=\"Wrap\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

            viewModel.ResetVisualEditorPropertyCommand.Execute(null);

            Assert.DoesNotContain($"{viewModel.SelectedVisualEditorAvailableProperty.Name}=\"Wrap\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

            titleNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Title");
            Assert.NotNull(titleNode);
            viewModel.SelectedVisualEditorNode = titleNode;
            viewModel.DuplicateVisualEditorElementCommand.Execute(null);

            Assert.Contains("<TextBlock Text=\"After\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Single(
                viewModel.VisualEditorStructureNodes.SelectMany(FlattenVisualEditorNodes),
                node => node.Element.Name == "Title");
            viewModel.MoveVisualEditorElementUpCommand.Execute(null);
            Assert.Equal("TextBlock", viewModel.SelectedVisualEditorNode?.Element.TypeName);

            var rootNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Root");
            Assert.NotNull(rootNode);
            viewModel.SelectedVisualEditorNode = rootNode;
            viewModel.SelectedVisualEditorToolboxItem = Assert.Single(
                viewModel.VisualEditorToolboxItems,
                item => item.TypeName == "Button");

            viewModel.InsertSelectedToolboxItemCommand.Execute(null);

            Assert.Contains("<Button />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains(viewModel.VisualEditorStructureNodes.SelectMany(FlattenVisualEditorNodes), node =>
                node.Element.TypeName == "Button");

            var buttonRow = Assert.Single(
                viewModel.VisualEditorStructureRows,
                row => row.Element.TypeName == "Button");
            viewModel.SelectedVisualEditorStructureRow = buttonRow;
            Assert.Equal(buttonRow.Node, viewModel.SelectedVisualEditorNode);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsMutateXamlWithUndoRedo()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            Assert.True(viewModel.DiagnosticsDevToolsOptions.ShowEventsTab);
            var handler = viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!;

            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Run",
                "Apply",
                "Run",
                "Apply",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Apply\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.True(viewModel.ActiveXamlFile.Document.UndoStack.CanUndo);

            viewModel.ActiveXamlFile.Document.UndoStack.Undo();
            Assert.Contains("Content=\"Run\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Content=\"Apply\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

            viewModel.ActiveXamlFile.Document.UndoStack.Redo();
            Assert.Contains("Content=\"Apply\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsResourceReferenceEditsMutateXamlAsResourceMarkup()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Border xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="Root"
                                                     Background="Red" />
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var border = Assert.IsAssignableFrom<Border>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                border,
                border,
                "Background",
                "Background",
                typeof(IBrush),
                typeof(Border),
                Brushes.Red,
                Brushes.Blue,
                "Red",
                "{DynamicResource AccentBrush}",
                isAttached: false,
                isAvaloniaProperty: true,
                resourceReferenceKind: DevToolsResourceReferenceKind.Dynamic,
                resourceKey: "AccentBrush",
                resourceKeyText: "AccentBrush"));

            Assert.Contains("Background=\"{DynamicResource AccentBrush}\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Background=\"Blue\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsDoNotSchedulePreviewReload()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            using var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            var handler = viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!;
            var timerField = typeof(MainViewModel).GetField(
                "_timer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(timerField);
            Assert.Null(timerField.GetValue(viewModel));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(timerField.GetValue(viewModel));

            viewModel.EnableAutoRun = true;
            Assert.Null(timerField.GetValue(viewModel));
            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Run",
                "Apply",
                "Run",
                "Apply",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Apply\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Null(timerField.GetValue(viewModel));

            viewModel.ActiveXamlFile.Text += Environment.NewLine;

            Assert.NotNull(timerField.GetValue(viewModel));
            viewModel.EnableAutoRun = false;
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsPreserveNumericPrecision()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Width="24" />
                                             </StackPanel>
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            var handler = viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!;

            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Width",
                "Width",
                typeof(double),
                typeof(Layoutable),
                24d,
                12.345678912345d,
                "24",
                "12.345678912345",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Width=\"12.345678912345\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsNameEditUpdatesExistingXName()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            var handler = viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!;

            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Name",
                "Name",
                typeof(string),
                typeof(Control),
                "Action",
                "PrimaryAction",
                "Action",
                "PrimaryAction",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("x:Name=\"PrimaryAction\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("x:Name=\"Action\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(" Name=\"PrimaryAction\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsIgnoreStalePreviewRoot()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            using var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            var previewFile = viewModel.ActiveXamlFile!;
            previewFile.Text = """
                               <StackPanel xmlns="https://github.com/avaloniaui"
                                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                           x:Name="Root">
                                 <Button x:Name="Action" Content="Run" />
                               </StackPanel>
                               """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    previewFile.Text,
                    null,
                    null,
                    previewFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());
            SetDiagnosticsPreviewXamlFile(viewModel, previewFile);

            var activeFile = new InMemoryProjectFile(
                "Other.axaml",
                """
                <StackPanel xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            x:Name="Root">
                  <Button x:Name="Action" Content="Other" />
                </StackPanel>
                """,
                ProjectFileKind.Xaml);
            viewModel.ActiveXamlFile = activeFile;
            viewModel.ActiveWorkspaceFile = activeFile;

            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Run",
                "Apply",
                "Run",
                "Apply",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Other\"", activeFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Content=\"Apply\"", activeFile.Text, StringComparison.Ordinal);
            Assert.Contains("Content=\"Run\"", previewFile.Text, StringComparison.Ordinal);
            Assert.Contains("Diagnostics preview is stale", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsRejectDocumentChangedAfterPreviewLoad()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            var previewXaml = """
                              <StackPanel xmlns="https://github.com/avaloniaui"
                                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                <Button Content="Preview" />
                              </StackPanel>
                              """;
            viewModel.ActiveXamlFile!.Text = previewXaml;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    previewXaml,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root.Children.Single());

            viewModel.ActiveXamlFile.Text = """
                                            <StackPanel xmlns="https://github.com/avaloniaui"
                                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                              <Button Content="Edited" />
                                            </StackPanel>
                                            """;
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile, previewXaml);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Preview",
                "Mutated",
                "Preview",
                "Mutated",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Edited\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Content=\"Mutated\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Diagnostics preview is stale", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsMapAgainstPreviewSourceAfterLineShift()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                               <Button Content="First" />
                                               <Button Content="Second" />
                                             </StackPanel>
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<StackPanel>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var buttons = root.Children.OfType<Button>().ToArray();
            Assert.Equal(2, buttons.Length);
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);
            var handler = viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!;

            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                root,
                root,
                "Margin",
                "Margin",
                typeof(Thickness),
                typeof(Layoutable),
                default(Thickness),
                new Thickness(4),
                "0",
                "4",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Margin=\"4,4,4,4\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

            handler.OnPropertyEdited(new DevToolsPropertyEdit(
                buttons[1],
                buttons[1],
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Second",
                "Updated Second",
                "Second",
                "Updated Second",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("<Button Content=\"First\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("<Button Content=\"Updated Second\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("<Button Content=\"Updated First\" />", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsIgnoreGeneratedPreviewWrapper()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Border xmlns="https://github.com/avaloniaui"
                                                     Background="Red" />
                                             """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var root = Assert.IsAssignableFrom<Border>(
                RuntimeXamlPreviewLoader.LoadControl(
                    viewModel.ActiveXamlFile.Text,
                    null,
                    null,
                    viewModel.ActiveXamlFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var generatedScope = new Border
            {
                Name = "GeneratedSampleScope",
                Child = root
            };
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                generatedScope,
                generatedScope,
                "Background",
                "Background",
                typeof(object),
                typeof(Border),
                "Red",
                "Blue",
                "Red",
                "Blue",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Background=\"Red\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Background=\"Blue\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("does not have XAML source information", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsIgnoreSourceLessNameCollision()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Border xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="GeneratedSampleScope"
                                                     Background="Red" />
                                             """;
            var generatedRuntimeControl = new Border
            {
                Name = "GeneratedSampleScope",
                Background = Brushes.Red
            };
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                generatedRuntimeControl,
                generatedRuntimeControl,
                "Background",
                "Background",
                typeof(object),
                typeof(Border),
                Brushes.Red,
                Brushes.Blue,
                "Red",
                "Blue",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Background=\"Red\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Background=\"Blue\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("does not have XAML source information", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsIgnoreNonActiveSourceUri()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text =
                "<Button xmlns=\"https://github.com/avaloniaui\" Content=\"Active\" />";
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var resourceButton = Assert.IsAssignableFrom<Button>(
                RuntimeXamlPreviewLoader.LoadControl(
                    "<Button xmlns=\"https://github.com/avaloniaui\" Content=\"Resource\" />",
                    null,
                    null,
                    "Resources/Template.axaml",
                    diagnostics));
            Assert.Empty(diagnostics);
            SetDiagnosticsPreviewXamlFile(viewModel, viewModel.ActiveXamlFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                resourceButton,
                resourceButton,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Resource",
                "Mutated",
                "Resource",
                "Mutated",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Active\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Content=\"Mutated\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("resources/Template.axaml", viewModel.VisualEditorStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not the active XAML document", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DiagnosticsPropertyEditsIgnoreGeneratedResourcePreview()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            var resourceFile = new InMemoryProjectFile(
                "Themes/Theme.axaml",
                """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Design.PreviewWith>
                    <Button Content="Preview" />
                  </Design.PreviewWith>
                </ResourceDictionary>
                """,
                ProjectFileKind.Resource);
            viewModel.ActiveXamlFile = resourceFile;
            viewModel.ActiveWorkspaceFile = resourceFile;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var preview = Assert.IsType<UserControl>(
                RuntimeXamlPreviewLoader.LoadResourceDictionaryPreview(
                    resourceFile.Text,
                    null,
                    resourceFile.Path,
                    diagnostics));
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(preview.Content);
            SetGeneratedDiagnosticsPreviewXamlFile(viewModel, resourceFile);
            Assert.NotNull(viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler);

            viewModel.DiagnosticsDevToolsOptions.PropertyEditHandler!.OnPropertyEdited(new DevToolsPropertyEdit(
                button,
                button,
                "Content",
                "Content",
                typeof(object),
                typeof(ContentControl),
                "Preview",
                "Mutated",
                "Preview",
                "Mutated",
                isAttached: false,
                isAvaloniaProperty: true));

            Assert.Contains("Content=\"Preview\"", resourceFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Content=\"Mutated\"", resourceFile.Text, StringComparison.Ordinal);
            Assert.Contains("not available for generated resource previews", viewModel.VisualEditorStatus, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_ToolboxCommandInsertsIntoSelectedEmptyDecoratorContainer()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Border x:Name="Target"
                                                       Width="120"
                                                       Height="80"
                                                       Background="LightBlue" />
                                               <Button x:Name="Peer" Content="Peer" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Target");
            Assert.Equal("Border #Target", viewModel.VisualEditorCurrentContainerTitle);

            viewModel.SelectedVisualEditorToolboxItem = Assert.Single(
                viewModel.VisualEditorToolboxItems,
                item => item.TypeName == "TextBlock");
            viewModel.InsertSelectedToolboxItemCommand.Execute(null);

            var updated = viewModel.ActiveXamlFile.Text;
            var targetStart = updated.IndexOf("x:Name=\"Target\"", StringComparison.Ordinal);
            var targetEnd = updated.IndexOf("</Border>", targetStart, StringComparison.Ordinal);
            var inserted = updated.IndexOf("<TextBlock", targetStart, StringComparison.Ordinal);
            var peer = updated.IndexOf("x:Name=\"Peer\"", StringComparison.Ordinal);

            Assert.True(targetStart >= 0);
            Assert.True(targetEnd > targetStart);
            Assert.True(inserted > targetStart && inserted < targetEnd);
            Assert.True(peer > targetEnd);
        });
    }

    [Fact]
    public void MainViewModel_StructureTreeDropOperationsMutateXaml()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <StackPanel x:Name="TargetPanel" />
                                               <Button x:Name="MoveMe" Content="Move" />
                                               <Button x:Name="Peer" Content="Peer" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var targetPanel = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "TargetPanel");
            Assert.NotNull(targetPanel);
            var textBlockTool = Assert.Single(
                viewModel.VisualEditorToolboxItems,
                item => item.TypeName == "TextBlock");

            Assert.True(viewModel.InsertToolboxItemIntoStructure(
                textBlockTool,
                targetPanel!.Element,
                VisualEditorStructureDropPosition.Inside));

            var updated = viewModel.ActiveXamlFile.Text;
            var targetPanelStart = updated.IndexOf("x:Name=\"TargetPanel\"", StringComparison.Ordinal);
            var targetPanelEnd = updated.IndexOf("</StackPanel>", targetPanelStart, StringComparison.Ordinal);
            var insertedTextBlock = updated.IndexOf("<TextBlock", targetPanelStart, StringComparison.Ordinal);
            Assert.True(targetPanelStart >= 0);
            Assert.True(targetPanelEnd > targetPanelStart);
            Assert.True(insertedTextBlock > targetPanelStart && insertedTextBlock < targetPanelEnd);

            var moveNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "MoveMe");
            targetPanel = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "TargetPanel");
            Assert.NotNull(moveNode);
            Assert.NotNull(targetPanel);

            Assert.True(viewModel.MoveVisualEditorElementInStructure(
                moveNode!.Element,
                targetPanel!.Element,
                VisualEditorStructureDropPosition.Inside));

            updated = viewModel.ActiveXamlFile.Text;
            targetPanelStart = updated.IndexOf("x:Name=\"TargetPanel\"", StringComparison.Ordinal);
            targetPanelEnd = updated.IndexOf("</StackPanel>", targetPanelStart, StringComparison.Ordinal);
            var movedButton = updated.IndexOf("x:Name=\"MoveMe\"", StringComparison.Ordinal);
            var peerButton = updated.IndexOf("x:Name=\"Peer\"", StringComparison.Ordinal);
            Assert.True(movedButton > targetPanelStart && movedButton < targetPanelEnd);
            Assert.True(peerButton > targetPanelEnd);
            Assert.Equal(new[] { 0, 1 }, viewModel.SelectedVisualEditorNode?.Element.Path);

            var peerNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Peer");
            targetPanel = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "TargetPanel");
            Assert.NotNull(peerNode);
            Assert.NotNull(targetPanel);

            Assert.True(viewModel.MoveVisualEditorElementInStructure(
                peerNode!.Element,
                targetPanel!.Element,
                VisualEditorStructureDropPosition.Before));

            updated = viewModel.ActiveXamlFile.Text;
            Assert.True(
                updated.IndexOf("x:Name=\"Peer\"", StringComparison.Ordinal) <
                updated.IndexOf("x:Name=\"TargetPanel\"", StringComparison.Ordinal));
            Assert.Equal(new[] { 0 }, viewModel.SelectedVisualEditorNode?.Element.Path);
        });
    }

    [Fact]
    public void MainViewModel_DesignerManipulationUpdatesCanvasBounds()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="Root">
                                               <Button x:Name="Action"
                                                       Canvas.Left="10"
                                                       Canvas.Top="20"
                                                       Width="100"
                                                       Height="30"
                                                       Content="Run" />
                                             </Canvas>
                                             """;

            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(10, 20, 100, 30));

            Assert.True(viewModel.VisualEditorDesignerMode);
            Assert.True(viewModel.MoveVisualEditorSelectionBy(5, 7));
            Assert.Contains("Canvas.Left=\"15\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Canvas.Top=\"27\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Equal("Moved selection using Canvas.Left and Canvas.Top.", viewModel.VisualEditorStatus);

            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(15, 27, 100, 30));

            Assert.True(viewModel.ResizeVisualEditorSelectionBy(20, 10));
            Assert.Contains("Width=\"120\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Height=\"40\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Equal("Resized selection using Width and Height.", viewModel.VisualEditorStatus);
            Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
        });
    }

    [Fact]
    public void MainViewModel_AnimationRecordModeCapturesDesignerMoveKeyFrames()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="Root">
                                               <Button x:Name="Action"
                                                       Canvas.Left="10"
                                                       Canvas.Top="20"
                                                       Width="100"
                                                       Height="30"
                                                       Content="Run" />
                                             </Canvas>
                                             """;

            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(10, 20, 100, 30));
            viewModel.AnimationRecordModeEnabled = true;
            viewModel.AnimationCurrentTimePercent = 50;

            Assert.NotNull(viewModel.SelectedAnimationTargetOption);
            Assert.Equal("Button", viewModel.SelectedAnimationTargetOption.Selector);
            Assert.True(viewModel.MoveVisualEditorSelectionBy(5, 7));

            var leftTrack = Assert.Single(
                viewModel.AnimationTimelineTracks,
                track => track.PropertyName == "Canvas.Left");
            var topTrack = Assert.Single(
                viewModel.AnimationTimelineTracks,
                track => track.PropertyName == "Canvas.Top");
            Assert.Contains(leftTrack.KeyFrames, frame => frame.CuePercent == 50 && frame.Value == "15");
            Assert.Contains(topTrack.KeyFrames, frame => frame.CuePercent == 50 && frame.Value == "27");
            Assert.Contains("<Style.Animations>", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("<Style Selector=\"Button\">", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Selector=\"^\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Property=\"Canvas.Left\" Value=\"15\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Property=\"Canvas.Top\" Value=\"27\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_CanvasMoveFallbacksAreParentRelative()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="Root">
                                               <Canvas x:Name="Nested"
                                                       Canvas.Left="200"
                                                       Canvas.Top="80">
                                                 <Button x:Name="Action"
                                                         Width="100"
                                                         Height="30"
                                                         Content="Run" />
                                               </Canvas>
                                             </Canvas>
                                             """;

            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(200, 80, 100, 30));

            Assert.True(viewModel.MoveVisualEditorSelectionBy(10, 5));
            Assert.Contains("Canvas.Left=\"10\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Canvas.Top=\"5\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Canvas.Left=\"210\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Canvas.Top=\"85\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_ResizeKeepsSelectionWhenSourceFeedbackFiresDuringMutation()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     x:Name="Root">
                                               <Button Canvas.Left="10" Canvas.Top="20" Width="100" Height="30" Content="First" />
                                               <Button Canvas.Left="30" Canvas.Top="40" Width="100" Height="30" Content="Second" />
                                             </Canvas>
                                             """;

            viewModel.RefreshVisualEditorCommand.Execute(null);
            var secondButton = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 1);
            Assert.NotNull(secondButton);
            viewModel.SelectedVisualEditorNode = secondButton;
            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(30, 40, 100, 30));

            var sourceFeedbackAttempted = false;
            var sourceFeedbackAccepted = false;
            viewModel.ActiveXamlFile.Document.TextChanged += (_, _) =>
            {
                sourceFeedbackAttempted = true;
                sourceFeedbackAccepted = viewModel.SelectVisualEditorSourceRange(
                    viewModel.ActiveXamlFile.Path,
                    selectionStart: 0,
                    selectionLength: 0,
                    caretOffset: 0);
            };

            Assert.True(viewModel.ResizeVisualEditorSelectionToBounds(
                new Rect(30, 40, 100, 30),
                new Rect(30, 40, 130, 45)));

            Assert.True(sourceFeedbackAttempted);
            Assert.False(sourceFeedbackAccepted);
            Assert.Equal(new[] { 1 }, viewModel.SelectedVisualEditorNode?.Element.Path);
            Assert.Equal("Second", viewModel.SelectedVisualEditorNode?.Element.Attributes["Content"]);
            Assert.Contains("Width=\"130\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Height=\"45\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainViewModel_DesignerManipulationFallsBackToMarginOutsideCanvas()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action"
                                                       Margin="1,2,3,4"
                                                       Content="Run" />
                                             </StackPanel>
                                             """;

            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");

            Assert.True(viewModel.MoveVisualEditorSelectionBy(5, 7));
            Assert.Contains("Margin=\"6,9,3,4\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Equal("Moved selection using Margin.", viewModel.VisualEditorStatus);
        });
    }

    [Fact]
    public void MainViewModel_DesignerAdornerGeometryTracksSelectionBounds()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.UpdateVisualEditorPreviewSelectionBounds(new Rect(10, 20, 100, 40));

            Assert.True(viewModel.VisualEditorDesignerMode);
            Assert.False(viewModel.VisualEditorPreviewContentHitTestVisible);
            Assert.Equal(6, viewModel.VisualEditorPreviewNorthWestThumbLeft);
            Assert.Equal(16, viewModel.VisualEditorPreviewNorthWestThumbTop);
            Assert.Equal(56, viewModel.VisualEditorPreviewNorthThumbLeft);
            Assert.Equal(16, viewModel.VisualEditorPreviewNorthThumbTop);
            Assert.Equal(106, viewModel.VisualEditorPreviewNorthEastThumbLeft);
            Assert.Equal(16, viewModel.VisualEditorPreviewNorthEastThumbTop);
            Assert.Equal(106, viewModel.VisualEditorPreviewEastThumbLeft);
            Assert.Equal(36, viewModel.VisualEditorPreviewEastThumbTop);
            Assert.Equal(106, viewModel.VisualEditorPreviewSouthEastThumbLeft);
            Assert.Equal(56, viewModel.VisualEditorPreviewSouthEastThumbTop);

            viewModel.VisualEditorDesignerMode = false;

            Assert.True(viewModel.VisualEditorPreviewContentHitTestVisible);
        });
    }

    [Fact]
    public void MainViewModel_DesignerReparentsSelectionIntoPreviewPanel()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Grid xmlns="https://github.com/avaloniaui"
                                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                   x:Name="Root">
                                               <StackPanel x:Name="Left">
                                                 <Button x:Name="Action" Content="Run" />
                                               </StackPanel>
                                               <StackPanel x:Name="Right" />
                                             </Grid>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");

            var rightPanel = Assert.Single(
                viewModel.Control.GetVisualDescendants().OfType<StackPanel>(),
                panel => panel.Name == "Right");

            Assert.True(viewModel.MoveVisualEditorSelectionIntoPreviewControl(rightPanel));
            Assert.Contains("<StackPanel x:Name=\"Right\">", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Right\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Action\"", StringComparison.Ordinal));
            Assert.Equal("Moved selection into StackPanel.", viewModel.VisualEditorStatus);
            Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
        });
    }

    [Fact]
    public void MainViewModel_DesignerReparentsUnnamedSelectionIntoPreviewPanel()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Grid xmlns="https://github.com/avaloniaui"
                                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                   x:Name="Root">
                                               <StackPanel x:Name="Left">
                                                 <Button Content="Run" />
                                               </StackPanel>
                                               <StackPanel x:Name="Right" />
                                             </Grid>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 0, 0);

            var rightPanel = Assert.Single(
                viewModel.Control.GetVisualDescendants().OfType<StackPanel>(),
                panel => panel.Name == "Right");

            Assert.True(viewModel.MoveVisualEditorSelectionIntoPreviewControl(rightPanel));
            Assert.Contains("<StackPanel x:Name=\"Right\">", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Right\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("Content=\"Run\"", StringComparison.Ordinal));
            Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
            Assert.Equal("Run", viewModel.SelectedVisualEditorNode?.Element.Attributes["Content"]);
            Assert.Equal(new[] { 1, 0 }, viewModel.SelectedVisualEditorNode?.Element.Path);
        });
    }

    [Fact]
    public void MainViewModel_DesignerReordersSelectionNearPreviewSibling()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="First" Content="First" />
                                               <Button x:Name="Second" Content="Second" />
                                               <Button x:Name="Third" Content="Third" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "First");

            var thirdButton = Assert.Single(
                viewModel.Control.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "Third");

            Assert.True(viewModel.MoveVisualEditorSelectionNearPreviewControl(thirdButton, after: true));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Second\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Third\"", StringComparison.Ordinal));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Third\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"First\"", StringComparison.Ordinal));
            Assert.Equal("Moved selection after Button.", viewModel.VisualEditorStatus);
        });
    }

    [Fact]
    public void MainViewModel_DesignerReordersUnnamedSelectionNearPreviewSibling()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button Content="Move" />
                                               <Button x:Name="Second" Content="Second" />
                                               <Button x:Name="Third" Content="Third" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 0);

            var thirdButton = Assert.Single(
                viewModel.Control.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "Third");

            Assert.True(viewModel.MoveVisualEditorSelectionNearPreviewControl(thirdButton, after: true));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Second\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Third\"", StringComparison.Ordinal));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Third\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("Content=\"Move\"", StringComparison.Ordinal));
            Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
            Assert.Equal("Move", viewModel.SelectedVisualEditorNode?.Element.Attributes["Content"]);
            Assert.Equal(new[] { 2 }, viewModel.SelectedVisualEditorNode?.Element.Path);
        });
    }

    [Fact]
    public void MainViewModel_DesignerReordersSelectionNearResolvedPreviewSibling()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button Content="Move" />
                                               <Button Content="Middle" />
                                               <Button Content="Target" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 0);

            var target = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 2);
            Assert.NotNull(target);

            Assert.True(viewModel.MoveVisualEditorSelectionNearPreviewControl(new Button(), after: true, target!.Element));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("Content=\"Target\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("Content=\"Move\"", StringComparison.Ordinal));
            Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
            Assert.Equal("Move", viewModel.SelectedVisualEditorNode?.Element.Attributes["Content"]);
            Assert.Equal(new[] { 2 }, viewModel.SelectedVisualEditorNode?.Element.Path);
        });
    }

    [Fact]
    public void MainViewModel_ToolboxDropInsertsIntoPanelAtRequestedIndex()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <TextBlock x:Name="First" Text="First" />
                                               <TextBlock x:Name="Second" Text="Second" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var root = Assert.IsType<StackPanel>(viewModel.Control);
            var button = Assert.Single(
                viewModel.VisualEditorToolboxItems,
                item => item.TypeName == "Button");

            Assert.True(viewModel.InsertToolboxItemIntoPreviewControl(button, root, childIndex: 1));

            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"First\"", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("<Button", StringComparison.Ordinal));
            Assert.True(
                viewModel.ActiveXamlFile.Text.IndexOf("<Button", StringComparison.Ordinal) <
                viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Second\"", StringComparison.Ordinal));
            Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
        });
    }

    [Fact]
    public void MainViewModel_ToolboxDropIntoCanvasWritesPreviewPosition()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     Width="320"
                                                     Height="180"
                                                     x:Name="Root" />
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var root = Assert.IsType<Canvas>(viewModel.Control);
            var button = Assert.Single(
                viewModel.VisualEditorToolboxItems,
                item => item.TypeName == "Button");

            Assert.True(viewModel.InsertToolboxItemIntoPreviewControl(
                button,
                root,
                childIndex: null,
                canvasPosition: new Point(42, 55)));

            Assert.Contains("Canvas.Left=\"42\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Contains("Canvas.Top=\"55\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
            Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
        });
    }

    [Fact]
    public void VisualTreeMapper_MapsNamedRuntimeControlsToXamlElements()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var engine = new XamlMutationEngine();
            var snapshot = engine.Analyze("""
                                          <StackPanel xmlns="https://github.com/avaloniaui">
                                            <Button x:Name="SaveButton" Content="Save" />
                                          </StackPanel>
                                          """);
            var root = new StackPanel
            {
                Children =
                {
                    new Button
                    {
                        Name = "SaveButton",
                        Content = "Save"
                    }
                }
            };

            var visualSnapshot = new AvaloniaVisualTreeSnapshotService().Snapshot(root);
            var buttonNode = Assert.Single(visualSnapshot.Children);
            var mapped = new XamlVisualTreeMapper().FindXamlElement(buttonNode, snapshot);

            Assert.NotNull(mapped);
            Assert.Equal("SaveButton", mapped.Name);
            Assert.Equal("Button", mapped.TypeName);
        });
    }

    [Fact]
    public void VisualTreeMapper_UsesRuntimeSourceInfoForAnonymousRepeatedControls()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var xaml = """
                       <UserControl xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                         <TabControl>
                           <TabItem Header="Element">
                             <StackPanel Margin="18" Spacing="8" Width="320">
                               <TextBlock FontSize="16" Text="Element name bindings" />
                               <TextBox Name="NameBox" UseFloatingPlaceholder="True" PlaceholderText="Name" Text="Avalonia" />
                               <TextBlock Text="{Binding #NameBox.Text, StringFormat='Hello, {0}'}" />

                               <Slider Name="AmountSlider" Minimum="0" Maximum="100" Value="42" />
                               <TextBlock Text="{Binding #AmountSlider.Value, StringFormat='Slider value: {0:F0}'}" />
                               <ProgressBar Minimum="0" Maximum="100" Value="{Binding #AmountSlider.Value}" />
                             </StackPanel>
                           </TabItem>
                         </TabControl>
                       </UserControl>
                       """;
            var diagnostics = new List<RuntimeXamlDiagnostic>();
            var control = Assert.IsAssignableFrom<Control>(
                RuntimeXamlPreviewLoader.LoadControl(xaml, null, null, "Main.axaml", diagnostics));
            var window = new Window
            {
                Width = 480,
                Height = 360,
                Content = control
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var sliderValueText = control.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(textBlock => textBlock.Text?.StartsWith("Slider value:", StringComparison.Ordinal) == true);
                var visualNode = new AvaloniaVisualTreeSnapshotService().Snapshot(sliderValueText);
                var snapshot = new XamlMutationEngine().Analyze(xaml);
                var mapped = new XamlVisualTreeMapper().FindXamlElement(visualNode, snapshot);

                Assert.NotNull(mapped);
                Assert.Equal("TextBlock", mapped.TypeName);
                Assert.Contains("#AmountSlider.Value", mapped.Attributes["Text"], StringComparison.Ordinal);
                Assert.DoesNotContain("Element name bindings", mapped.Attributes["Text"], StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void VisualTreeMapper_DoesNotGuessFirstTypeWhenRepeatedAnonymousControlsHaveNoSourceInfo()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var xaml = """
                       <StackPanel xmlns="https://github.com/avaloniaui">
                         <TextBlock Text="First" />
                         <TextBlock Text="Second" />
                       </StackPanel>
                       """;
            var runtimeRoot = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "First" },
                    new TextBlock { Text = "Second" }
                }
            };

            var visualNode = new AvaloniaVisualTreeSnapshotService().Snapshot(
                Assert.IsType<TextBlock>(runtimeRoot.Children[1]));
            var snapshot = new XamlMutationEngine().Analyze(xaml);
            var mapped = new XamlVisualTreeMapper().FindXamlElement(visualNode, snapshot);

            Assert.Null(mapped);
        });
    }

    [Fact]
    public void SelectionService_CoordinatesSourceAndVisualSelection()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var engine = new XamlMutationEngine();
            var selectionService = new VisualEditorSelectionService(engine, new XamlVisualTreeMapper());
            var changedCount = 0;
            selectionService.SelectionChanged += (_, _) => changedCount++;
            var xaml = """
                       <StackPanel xmlns="https://github.com/avaloniaui">
                         <Button x:Name="SaveButton" Content="Save" />
                       </StackPanel>
                       """;

            var sourceSelection = selectionService.SelectElement(xaml, XamlElementSelector.ByName("SaveButton"));
            var root = new StackPanel
            {
                Children =
                {
                    new Button
                    {
                        Name = "SaveButton",
                        Content = "Save"
                    }
                }
            };
            var visualNode = Assert.Single(new AvaloniaVisualTreeSnapshotService().Snapshot(root).Children);
            var visualSelection = selectionService.SelectVisual(xaml, visualNode);

            Assert.True(sourceSelection.HasSelection);
            Assert.Equal("SaveButton", sourceSelection.XamlElement?.Name);
            Assert.True(visualSelection.HasSelection);
            Assert.Equal("SaveButton", visualSelection.XamlElement?.Name);
            Assert.Same(visualSelection, selectionService.Current);
            Assert.Equal(2, changedCount);
        });
    }

    [Fact]
    public void HeadlessPreview_CapturesScreenshotAfterXamlMutation()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var engine = new XamlMutationEngine();
            var xaml = """
                       <Border xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               Width="320"
                               Height="160"
                               Background="White">
                         <TextBlock x:Name="Title"
                                    Text="Before"
                                    FontSize="24"
                                    HorizontalAlignment="Center"
                                    VerticalAlignment="Center" />
                       </Border>
                       """;
            var changed = engine.SetProperty(xaml, XamlElementSelector.ByName("Title"), "Text", "After");
            Assert.Empty(changed.Diagnostics);

            var control = Assert.IsAssignableFrom<Control>(AvaloniaRuntimeXamlLoader.Load(changed.Text));
            var window = new Window
            {
                Width = 360,
                Height = 220,
                Background = Brushes.White,
                Content = control
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-mutation.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PreviewView_SetPseudoClass_RemovesControlOwnedPseudoClassWithoutThrowing()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var button = new Button();
        var setPseudoClass = typeof(PreviewView)
            .GetMethod("SetPseudoClass", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(setPseudoClass);

        var removeException = Record.Exception(() =>
            setPseudoClass.Invoke(null, new object[] { button, ":pointerover", false }));
        Assert.Null(removeException);

        setPseudoClass.Invoke(null, new object[] { button, ":pointerover", true });
        Assert.Contains(":pointerover", button.Classes);

        removeException = Record.Exception(() =>
            setPseudoClass.Invoke(null, new object[] { button, ":pointerover", false }));
        Assert.Null(removeException);
        Assert.DoesNotContain(":pointerover", button.Classes);
    }

    [Fact]
    public void HeadlessPreview_SelectsRenderedControlAndCapturesAdorner()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="260"
                                                         Height="140"
                                                         Background="White"
                                                         x:Name="Root">
                                               <TextBlock x:Name="Title" Text="Preview selection" />
                                               <Button x:Name="Action" Content="Run" Width="120" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 360,
                Height = 220,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 380,
                Height = 260,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                var buttonTopLeft = actionButton.TranslatePoint(default, preview);
                Assert.NotNull(buttonTopLeft);
                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    actionButton,
                    new Rect(buttonTopLeft.Value, actionButton.Bounds.Size)));

                Assert.True(viewModel.VisualEditorPreviewSelectionVisible);
                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("Selected Button from preview.", viewModel.VisualEditorStatus);

                PumpLayout(window);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-preview-selection.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DesignerPointerSelectionFallsBackFromGeneratedControlParts()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                VisualEditorDesignerMode = true
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="280"
                                                         Height="160"
                                                         Background="White"
                                                         Spacing="8"
                                                         x:Name="Root">
                                               <TextBlock Text="Before" />
                                               <Button x:Name="Action" Content="Run" Width="120" />
                                               <CheckBox x:Name="Accept" Content="Accept terms" />
                                               <TextBlock Text="After" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 380,
                Height = 260,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 420,
                Height = 300,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                var acceptCheckBox = Assert.Single(
                    preview.GetVisualDescendants().OfType<CheckBox>(),
                    checkBox => checkBox.Name == "Accept");

                ClickPreviewControl(preview, actionButton);
                PumpLayout(window);

                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
                Assert.True(viewModel.VisualEditorPreviewSelectionVisible);

                ClickPreviewControl(preview, acceptCheckBox);
                PumpLayout(window);

                Assert.Equal("Accept", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("CheckBox", viewModel.SelectedVisualEditorNode?.Element.TypeName);
                Assert.True(viewModel.VisualEditorPreviewSelectionVisible);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-generated-control-part-selection.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DesignerSelectionCyclesNestedControlsAndUsesCurrentContainer()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                VisualEditorDesignerMode = true
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="320"
                                                         Height="180"
                                                         Background="White"
                                                         x:Name="Root">
                                               <StackPanel x:Name="Inner"
                                                           Spacing="4"
                                                           Background="Gainsboro">
                                                 <Button x:Name="Action" Content="Run" Width="120" />
                                                 <Button x:Name="Second" Content="Second" Width="120" />
                                               </StackPanel>
                                               <Button x:Name="Peer" Content="Peer" Width="120" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 420,
                Height = 280,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 460,
                Height = 320,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.Equal("StackPanel #Root", viewModel.VisualEditorCurrentContainerTitle);

                var actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                var actionPoint = GetPreviewControlCenter(preview, actionButton);

                ClickPreviewPoint(preview, actionPoint);
                PumpLayout(window);

                Assert.Equal("Inner", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("StackPanel #Inner", viewModel.VisualEditorCurrentContainerTitle);
                Assert.True(viewModel.VisualEditorPreviewCurrentContainerVisible);

                ClickPreviewPoint(preview, actionPoint);
                PumpLayout(window);

                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("StackPanel #Inner", viewModel.VisualEditorCurrentContainerTitle);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Tab, KeyModifiers.None));
                PumpLayout(window);

                Assert.Equal("Second", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("StackPanel #Inner", viewModel.VisualEditorCurrentContainerTitle);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Escape, KeyModifiers.None));
                PumpLayout(window);

                Assert.Equal("Inner", viewModel.SelectedVisualEditorNode?.Element.Name);

                viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Root");
                PumpLayout(window);
                actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                ClickPreviewControl(preview, actionButton, KeyModifiers.Control);
                PumpLayout(window);

                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);

                viewModel.SelectedVisualEditorToolboxItem = Assert.Single(
                    viewModel.VisualEditorToolboxItems,
                    item => item.TypeName == "TextBlock");
                viewModel.InsertSelectedToolboxItemCommand.Execute(null);

                var innerStart = viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Inner\"", StringComparison.Ordinal);
                var peerStart = viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Peer\"", StringComparison.Ordinal);
                var insertedStart = viewModel.ActiveXamlFile.Text.IndexOf("<TextBlock", innerStart, StringComparison.Ordinal);
                Assert.True(insertedStart > innerStart);
                Assert.True(insertedStart < peerStart);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-selection-cycle-container.png");
                Assert.NotNull(frame);
                frame.Save(path);
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_SelectionAdornerTracksPreviewLayoutResize()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                VisualEditorDesignerMode = true
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <Grid xmlns="https://github.com/avaloniaui"
                                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                   Background="White"
                                                   x:Name="Root">
                                               <Grid.ColumnDefinitions>
                                                 <ColumnDefinition Width="*" />
                                                 <ColumnDefinition Width="Auto" />
                                               </Grid.ColumnDefinitions>
                                               <Button Grid.Column="1"
                                                       x:Name="Action"
                                                       Width="100"
                                                       Height="32"
                                                       Content="Run" />
                                             </Grid>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 320,
                Height = 180,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 360,
                Height = 240,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                var initialTopLeft = actionButton.TranslatePoint(default, previewSurface);
                Assert.NotNull(initialTopLeft);

                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    actionButton,
                    new Rect(initialTopLeft.Value, actionButton.Bounds.Size)));
                PumpLayout(window);

                var initialSelectionLeft = viewModel.VisualEditorPreviewSelectionLeft;
                preview.Width = 520;
                window.Width = 560;
                PumpLayout(window);
                PumpLayout(window);

                var resizedTopLeft = actionButton.TranslatePoint(default, previewSurface);
                Assert.NotNull(resizedTopLeft);
                Assert.True(resizedTopLeft.Value.X > initialTopLeft.Value.X + 100);
                Assert.True(viewModel.VisualEditorPreviewSelectionLeft > initialSelectionLeft + 100);
                Assert.Equal(resizedTopLeft.Value.X, viewModel.VisualEditorPreviewSelectionLeft, precision: 1);
                Assert.Equal(resizedTopLeft.Value.Y, viewModel.VisualEditorPreviewSelectionTop, precision: 1);
                Assert.Equal(actionButton.Bounds.Width, viewModel.VisualEditorPreviewSelectionWidth, precision: 1);
                Assert.Equal(actionButton.Bounds.Height, viewModel.VisualEditorPreviewSelectionHeight, precision: 1);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-selection-layout-resize.png");
                Assert.NotNull(frame);
                frame.Save(path);
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_RendersResizeAdornerAndAppliesSelectionBounds()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     Width="320"
                                                     Height="180"
                                                     Background="White"
                                                     x:Name="Root">
                                               <Button x:Name="Action"
                                                       Canvas.Left="20"
                                                       Canvas.Top="30"
                                                       Width="120"
                                                       Height="40"
                                                       Content="Run" />
                                             </Canvas>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 420,
                Height = 260,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 440,
                Height = 300,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var actionButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Action");
                var buttonTopLeft = actionButton.TranslatePoint(default, previewSurface);
                Assert.NotNull(buttonTopLeft);
                var initialBounds = new Rect(buttonTopLeft.Value, actionButton.Bounds.Size);
                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    actionButton,
                    initialBounds));
                PumpLayout(window);

                var southEastThumb = Assert.Single(
                    preview.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "ResizeSouthEastThumb");
                var thumbTopLeft = southEastThumb.TranslatePoint(default, previewSurface);
                Assert.NotNull(thumbTopLeft);
                Assert.True(southEastThumb.IsHitTestVisible);

                Assert.DoesNotContain("Width=\"150\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("Height=\"54\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

                Assert.True(viewModel.ResizeVisualEditorSelectionToBounds(
                    initialBounds,
                    new Rect(initialBounds.Position, new Size(150, 54))));
                PumpLayout(window);

                Assert.Contains("Width=\"150\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Contains("Height=\"54\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Equal("Resized selection using Width and Height.", viewModel.VisualEditorStatus);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-resize-adorner.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_LiveResizeCandidateLookupReturnsNullWhenNoControlMatches()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var preview = new PreviewView
            {
                Width = 320,
                Height = 180
            };
            var window = new Window
            {
                Width = 360,
                Height = 240,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var result = typeof(PreviewView)
                    .GetMethod("FindPreviewControlNearSelectionBounds", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[]
                    {
                        previewSurface,
                        new Rect(10_000, 10_000, 20, 20),
                        "DefinitelyMissingControl"
                    });

                Assert.Null(result);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_LiveResizeIdentityFallbackRequiresStableName()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="220"
                                                         Height="140"
                                                         Background="White">
                                               <Button Width="120" Height="32" Content="First" />
                                               <Button Width="120" Height="32" Content="Second" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 360,
                Height = 240,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 380,
                Height = 280,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var selected = FindVisualEditorNodeByPath(viewModel.VisualEditorStructureNodes, 1)?.Element;
                Assert.NotNull(selected);
                var result = typeof(PreviewView)
                    .GetMethod("FindPreviewControlByElementIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[]
                    {
                        viewModel.Control,
                        selected
                    });

                Assert.Null(result);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DraggingChildShowsPanelPlacementAndGuides()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="220"
                                                         Height="140"
                                                         Background="White"
                                                         x:Name="Root">
                                               <Button x:Name="First" Width="120" Height="32" Content="First" />
                                               <Button x:Name="Second" Width="120" Height="32" Content="Second" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 360,
                Height = 240,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 380,
                Height = 280,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var firstButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "First");
                var secondButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "Second");
                var firstTopLeft = firstButton.TranslatePoint(default, previewSurface);
                var secondTopLeft = secondButton.TranslatePoint(default, previewSurface);
                Assert.NotNull(firstTopLeft);
                Assert.NotNull(secondTopLeft);

                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    firstButton,
                    new Rect(firstTopLeft.Value, firstButton.Bounds.Size)));
                PumpLayout(window);

                var pointer = new Avalonia.Input.Pointer(
                    Avalonia.Input.Pointer.GetNextFreeId(),
                    PointerType.Mouse,
                    isPrimary: true);
                var start = firstTopLeft.Value + new Vector(12, 12);
                var end = secondTopLeft.Value + new Vector(12, 12);
                preview.RaiseEvent(CreatePointerPressedArgs(preview, preview, pointer, start));
                preview.RaiseEvent(CreatePointerMovedArgs(preview, preview, pointer, end));
                PumpLayout(window);

                Assert.True(viewModel.VisualEditorPreviewDropTargetVisible);
                Assert.True(viewModel.VisualEditorPreviewInsertionVisible);
                Assert.True(viewModel.VisualEditorPreviewDropPlaceholderVisible);
                Assert.True(viewModel.VisualEditorPreviewVerticalGuideVisible);
                Assert.True(viewModel.VisualEditorPreviewMeasurementVisible);
                Assert.Contains("120 x 32", viewModel.VisualEditorPreviewMeasurementText, StringComparison.Ordinal);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-child-drag-guides.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);

                preview.RaiseEvent(CreatePointerReleasedArgs(preview, preview, pointer, end));
                PumpLayout(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DraggingIntoEmptyContainerDropsInsideInsteadOfBeside()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="260"
                                                         Height="180"
                                                         Background="White"
                                                         x:Name="Root">
                                               <Button x:Name="MoveMe" Width="120" Height="32" Content="Move" />
                                               <Border x:Name="Target"
                                                       Width="180"
                                                       Height="80"
                                                       Background="LightBlue" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 360,
                Height = 260,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 400,
                Height = 300,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var moveButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "MoveMe");
                var targetBorder = Assert.Single(
                    preview.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "Target");
                var moveTopLeft = moveButton.TranslatePoint(default, previewSurface);
                var targetTopLeft = targetBorder.TranslatePoint(default, previewSurface);
                Assert.NotNull(moveTopLeft);
                Assert.NotNull(targetTopLeft);

                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    moveButton,
                    new Rect(moveTopLeft.Value, moveButton.Bounds.Size)));
                PumpLayout(window);

                var pointer = new Avalonia.Input.Pointer(
                    Avalonia.Input.Pointer.GetNextFreeId(),
                    PointerType.Mouse,
                    isPrimary: true);
                var start = moveTopLeft.Value + new Vector(12, 12);
                var end = targetTopLeft.Value + new Vector(targetBorder.Bounds.Width / 2, targetBorder.Bounds.Height / 2);
                preview.RaiseEvent(CreatePointerPressedArgs(preview, preview, pointer, start));
                preview.RaiseEvent(CreatePointerMovedArgs(preview, preview, pointer, end));
                PumpLayout(window);

                Assert.True(viewModel.VisualEditorPreviewDropTargetVisible);
                Assert.True(viewModel.VisualEditorPreviewDropPlaceholderVisible);
                Assert.False(viewModel.VisualEditorPreviewInsertionVisible);

                preview.RaiseEvent(CreatePointerReleasedArgs(preview, preview, pointer, end));
                PumpLayout(window);

                var updated = viewModel.ActiveXamlFile.Text;
                var targetStart = updated.IndexOf("x:Name=\"Target\"", StringComparison.Ordinal);
                var targetEnd = updated.IndexOf("</Border>", targetStart, StringComparison.Ordinal);
                var moved = updated.IndexOf("x:Name=\"MoveMe\"", StringComparison.Ordinal);

                Assert.True(targetStart >= 0);
                Assert.True(targetEnd > targetStart);
                Assert.True(moved > targetStart && moved < targetEnd);
                Assert.Equal("Moved selection into Border.", viewModel.VisualEditorStatus);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_RuntimeModeDoesNotInterceptSelectionOrToolboxDrop()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="260"
                                                         Height="140"
                                                         Background="White"
                                                         x:Name="Root">
                                               <TextBlock x:Name="First" Text="First" />
                                               <TextBlock x:Name="Second" Text="Second" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 360,
                Height = 240,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 380,
                Height = 280,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var firstText = Assert.Single(
                    preview.GetVisualDescendants().OfType<TextBlock>(),
                    textBlock => textBlock.Name == "First");
                var secondText = Assert.Single(
                    preview.GetVisualDescendants().OfType<TextBlock>(),
                    textBlock => textBlock.Name == "Second");
                var firstTopLeft = firstText.TranslatePoint(default, previewSurface);
                var secondTopLeft = secondText.TranslatePoint(default, previewSurface);
                Assert.NotNull(firstTopLeft);
                Assert.NotNull(secondTopLeft);

                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    firstText,
                    new Rect(firstTopLeft.Value, firstText.Bounds.Size)));
                viewModel.VisualEditorDesignerMode = false;
                PumpLayout(window);

                var designerOverlay = Assert.Single(
                    preview.GetVisualDescendants().OfType<Canvas>(),
                    canvas => canvas.Name == "DesignerOverlay");
                Assert.False(designerOverlay.IsVisible);

                var pointer = new Avalonia.Input.Pointer(
                    Avalonia.Input.Pointer.GetNextFreeId(),
                    PointerType.Mouse,
                    isPrimary: true);
                var pressArgs = CreatePointerPressedArgs(
                    secondText,
                    preview,
                    pointer,
                    secondTopLeft.Value + new Vector(4, 4));
                secondText.RaiseEvent(pressArgs);
                PumpLayout(window);

                Assert.False(pressArgs.Handled);
                Assert.Equal("First", viewModel.SelectedVisualEditorNode?.Element.Name);

                var runtimeFrame = window.CaptureRenderedFrame();
                var runtimePath = GetScreenshotPath("visual-editing-runtime-mode-pass-through.png");
                Assert.NotNull(runtimeFrame);
                runtimeFrame.Save(runtimePath);
                Assert.True(File.Exists(runtimePath));
                Assert.True(new FileInfo(runtimePath).Length > 0);

                var buttonTool = Assert.Single(
                    viewModel.VisualEditorToolboxItems,
                    item => item.TypeName == "Button");
                var data = ToolboxDragPayload.CreateDataTransfer(buttonTool);
                var dropArgs = CreateDragEventArgs(
                    DragDrop.DropEvent,
                    previewSurface,
                    data,
                    new Point(20, 24));
                typeof(PreviewView)
                    .GetMethod("OnDrop", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[] { previewSurface, dropArgs });

                Assert.False(dropArgs.Handled);
                Assert.DoesNotContain("<Button", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Equal("First", viewModel.SelectedVisualEditorNode?.Element.Name);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DesignerKeyboardNudgesResizesReordersAndMovesBetweenPanels()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     Width="420"
                                                     Height="180"
                                                     Background="White"
                                                     x:Name="Root">
                                               <StackPanel x:Name="LeftPanel"
                                                           Canvas.Left="20"
                                                           Canvas.Top="20"
                                                           Width="150"
                                                           Height="120"
                                                           Background="LightGray">
                                                 <Button x:Name="MoveMe"
                                                         Width="100"
                                                         Height="32"
                                                         Content="Move" />
                                                 <Button x:Name="Second"
                                                         Width="100"
                                                         Height="32"
                                                         Content="Second" />
                                               </StackPanel>
                                               <StackPanel x:Name="RightPanel"
                                                           Canvas.Left="230"
                                                           Canvas.Top="20"
                                                           Width="150"
                                                           Height="120"
                                                           Background="Gainsboro">
                                                 <Button x:Name="Anchor"
                                                         Width="100"
                                                         Height="32"
                                                         Content="Anchor" />
                                               </StackPanel>
                                             </Canvas>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 520,
                Height = 300,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 540,
                Height = 340,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var moveButton = Assert.Single(
                    preview.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "MoveMe");
                var moveTopLeft = moveButton.TranslatePoint(default, previewSurface);
                Assert.NotNull(moveTopLeft);

                Assert.True(viewModel.SelectVisualEditorPreviewControl(
                    moveButton,
                    new Rect(moveTopLeft.Value, moveButton.Bounds.Size)));
                PumpLayout(window);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Right, KeyModifiers.Shift));
                PumpLayout(window);

                Assert.Contains("Margin=\"10,0,0,0\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.True(viewModel.VisualEditorPreviewMeasurementVisible);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift));
                PumpLayout(window);

                Assert.Contains("Width=\"110\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Contains("Height=\"32\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);

                var keyboardFrame = window.CaptureRenderedFrame();
                var keyboardPath = GetScreenshotPath("visual-editing-keyboard-designer.png");
                Assert.NotNull(keyboardFrame);
                keyboardFrame.Save(keyboardPath);
                Assert.True(File.Exists(keyboardPath));
                Assert.True(new FileInfo(keyboardPath).Length > 0);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Down, KeyModifiers.Control));
                PumpLayout(window);

                Assert.True(
                    viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"Second\"", StringComparison.Ordinal) <
                    viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"MoveMe\"", StringComparison.Ordinal));

                viewModel.Control = Assert.IsAssignableFrom<Control>(
                    AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
                viewModel.RefreshVisualEditorCommand.Execute(null);
                PumpLayout(window);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Right, KeyModifiers.Alt));
                PumpLayout(window);

                var rightPanelStart = viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"RightPanel\"", StringComparison.Ordinal);
                var moveMeStart = viewModel.ActiveXamlFile.Text.IndexOf("x:Name=\"MoveMe\"", StringComparison.Ordinal);
                Assert.True(rightPanelStart >= 0);
                Assert.True(moveMeStart > rightPanelStart);
                Assert.Equal("Moved selection into StackPanel.", viewModel.VisualEditorStatus);

                preview.RaiseEvent(CreateKeyDownArgs(preview, Key.Tab, KeyModifiers.None));
                PumpLayout(window);

                Assert.NotEqual("MoveMe", viewModel.SelectedVisualEditorNode?.Element.Name);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PreviewDockView_DesignModeToggleControlsDesignerOverlay()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="240"
                                                         Height="120"
                                                         Background="White"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var view = new PreviewDockView
            {
                DataContext = new PreviewDockViewModel(viewModel)
            };
            var window = new Window
            {
                Width = 420,
                Height = 260,
                Background = Brushes.White,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var toggle = Assert.Single(view.GetVisualDescendants().OfType<ToggleButton>());
                var designerOverlay = Assert.Single(
                    view.GetVisualDescendants().OfType<Canvas>(),
                    canvas => canvas.Name == "DesignerOverlay");
                var contentHost = Assert.Single(
                    view.GetVisualDescendants().OfType<XamlPlayground.Controls.ExclusiveContentControl>());

                Assert.True(viewModel.VisualEditorDesignerMode);
                Assert.True(toggle.IsChecked);
                Assert.True(designerOverlay.IsHitTestVisible);
                Assert.False(contentHost.IsHitTestVisible);

                toggle.IsChecked = false;
                PumpLayout(window);

                Assert.False(viewModel.VisualEditorDesignerMode);
                Assert.False(designerOverlay.IsHitTestVisible);
                Assert.True(contentHost.IsHitTestVisible);

                toggle.IsChecked = true;
                PumpLayout(window);

                Assert.True(viewModel.VisualEditorDesignerMode);
                Assert.True(designerOverlay.IsHitTestVisible);
                Assert.False(contentHost.IsHitTestVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_CapturesToolboxDropPlacementAdorner()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="260"
                                                         Height="160"
                                                         Background="White"
                                                         x:Name="Root">
                                               <TextBlock x:Name="First" Text="First" />
                                               <TextBlock x:Name="Second" Text="Second" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.UpdateVisualEditorPreviewDropFeedback(
                new Rect(20, 20, 260, 160),
                new Rect(28, 72, 224, 3),
                new Rect(24, 60, 252, 32));

            var preview = new PreviewView
            {
                Width = 360,
                Height = 240,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 380,
                Height = 280,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.True(viewModel.VisualEditorPreviewDropTargetVisible);
                Assert.True(viewModel.VisualEditorPreviewInsertionVisible);
                Assert.True(viewModel.VisualEditorPreviewDropPlaceholderVisible);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-toolbox-drop-adorner.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_DropSelectsInsertedToolPlaceholder()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <Canvas xmlns="https://github.com/avaloniaui"
                                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                     Width="320"
                                                     Height="180"
                                                     Background="White"
                                                     x:Name="Root" />
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 420,
                Height = 260,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 440,
                Height = 300,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var buttonTool = Assert.Single(
                    viewModel.VisualEditorToolboxItems,
                    item => item.TypeName == "Button");
                var data = ToolboxDragPayload.CreateDataTransfer(buttonTool);
                var rootCanvas = Assert.Single(
                    preview.GetVisualDescendants().OfType<Canvas>(),
                    canvas => canvas.Name == "Root");
                var rootTopLeft = rootCanvas.TranslatePoint(default, previewSurface);
                Assert.NotNull(rootTopLeft);
                var dropPoint = rootTopLeft.Value + new Vector(42, 55);

                var dropArgs = CreateDragEventArgs(
                    DragDrop.DropEvent,
                    previewSurface,
                    data,
                    dropPoint);
                typeof(PreviewView)
                    .GetMethod("OnDrop", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[] { previewSurface, dropArgs });
                PumpLayout(window);

                Assert.Contains("<Button", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Contains("Canvas.Left=\"42\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Contains("Canvas.Top=\"55\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
                Assert.True(viewModel.VisualEditorPreviewSelectionVisible);
                Assert.Equal(rootTopLeft.Value.X + 42, viewModel.VisualEditorPreviewSelectionLeft);
                Assert.Equal(rootTopLeft.Value.Y + 55, viewModel.VisualEditorPreviewSelectionTop);
                Assert.Equal(112, viewModel.VisualEditorPreviewSelectionWidth);
                Assert.Equal(32, viewModel.VisualEditorPreviewSelectionHeight);
                Assert.False(viewModel.VisualEditorPreviewDropPlaceholderVisible);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-toolbox-drop-selection.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessPreview_ToolboxDropUsesDisplayedNestedPanelTarget()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null)
            {
                EnableAutoRun = false
            };
            viewModel.ActiveXamlFile!.Text = """
                                             <Grid xmlns="https://github.com/avaloniaui"
                                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                   Width="420"
                                                   Height="200"
                                                   Background="White">
                                               <StackPanel Orientation="Horizontal"
                                                           Margin="20"
                                                           Spacing="20">
                                                 <StackPanel Width="150"
                                                             Height="120"
                                                             Background="Gainsboro">
                                                   <TextBlock Text="Left" />
                                                 </StackPanel>
                                                 <StackPanel Width="150"
                                                             Height="120"
                                                             Background="LightBlue">
                                                   <TextBlock Text="Right" />
                                                 </StackPanel>
                                               </StackPanel>
                                             </Grid>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var preview = new PreviewView
            {
                Width = 520,
                Height = 300,
                DataContext = viewModel
            };
            var window = new Window
            {
                Width = 540,
                Height = 340,
                Background = Brushes.White,
                Content = preview
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var previewSurface = Assert.Single(
                    preview.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Name == "PreviewSurface");
                var rightPanel = Assert.Single(
                    preview.GetVisualDescendants().OfType<StackPanel>(),
                    panel => panel.Children
                        .OfType<TextBlock>()
                        .Any(text => string.Equals(text.Text, "Right", StringComparison.Ordinal)));
                var rightTopLeft = rightPanel.TranslatePoint(default, previewSurface);
                Assert.NotNull(rightTopLeft);

                var buttonTool = Assert.Single(
                    viewModel.VisualEditorToolboxItems,
                    item => item.TypeName == "Button");
                var data = ToolboxDragPayload.CreateDataTransfer(buttonTool);
                var dropPoint = rightTopLeft.Value + new Vector(40, 50);

                var dragOverArgs = CreateDragEventArgs(
                    DragDrop.DragOverEvent,
                    previewSurface,
                    data,
                    dropPoint);
                typeof(PreviewView)
                    .GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[] { previewSurface, dragOverArgs });
                PumpLayout(window);

                Assert.True(viewModel.VisualEditorPreviewDropTargetVisible);
                Assert.True(Math.Abs(viewModel.VisualEditorPreviewDropTargetLeft - rightTopLeft.Value.X) < 0.5);
                Assert.True(Math.Abs(viewModel.VisualEditorPreviewDropTargetTop - rightTopLeft.Value.Y) < 0.5);

                var dropArgs = CreateDragEventArgs(
                    DragDrop.DropEvent,
                    previewSurface,
                    data,
                    dropPoint);
                typeof(PreviewView)
                    .GetMethod("OnDrop", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(preview, new object?[] { previewSurface, dropArgs });
                PumpLayout(window);

                var updated = viewModel.ActiveXamlFile.Text;
                var rightMarker = updated.IndexOf("Text=\"Right\"", StringComparison.Ordinal);
                var rightPanelEnd = updated.IndexOf("</StackPanel>", rightMarker, StringComparison.Ordinal);
                var inserted = updated.IndexOf("<Button", rightMarker, StringComparison.Ordinal);

                Assert.True(rightMarker >= 0);
                Assert.True(rightPanelEnd > rightMarker);
                Assert.True(inserted > rightMarker && inserted < rightPanelEnd);
                Assert.Equal("Button", viewModel.SelectedVisualEditorNode?.Element.TypeName);
                Assert.Equal(new[] { 0, 1, 1 }, viewModel.SelectedVisualEditorNode?.Element.Path);
                Assert.False(viewModel.VisualEditorPreviewDropPlaceholderVisible);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-toolbox-drop-nested-target.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WorkspaceEditor_SelectsXamlSpanWhenVisualEditorSelectionChanges()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <TextBlock x:Name="Title" Text="Designer" />
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            var dockable = new WorkspaceFileDocumentDockViewModel(viewModel, viewModel.ActiveXamlFile);
            var view = new WorkspaceFileEditorDockView
            {
                DataContext = dockable
            };
            var window = new Window
            {
                Width = 640,
                Height = 420,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var titleNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Title");
                Assert.NotNull(titleNode);
                viewModel.SelectedVisualEditorNode = titleNode;
                PumpLayout(window);

                var editor = Assert.Single(view.GetVisualDescendants().OfType<TextEditor>());
                Assert.Equal(titleNode.Element.Start, viewModel.VisualEditorSourceSelectionStart);
                Assert.True(TextEditorSourceSelection.GetIsEnabled(editor));
                Assert.Equal(viewModel.ActiveXamlFile.Path, TextEditorSourceSelection.GetSourcePath(editor));
                Assert.Equal(viewModel.ActiveXamlFile.Path, TextEditorSourceSelection.GetTargetPath(editor));
                Assert.Equal(titleNode.Element.Start, TextEditorSourceSelection.GetSourceStart(editor));
                Assert.Equal(titleNode.Element.Length, TextEditorSourceSelection.GetSourceLength(editor));
                Assert.Equal(titleNode.Element.Start, editor.SelectionStart);
                Assert.Equal(titleNode.Element.Length, editor.SelectionLength);
                Assert.Equal(titleNode.Element.Start + titleNode.Element.Length, editor.CaretOffset);
            }
            finally
            {
                window.Close();
                dockable.Dispose();
            }
        });
    }

    [Fact]
    public void WorkspaceEditor_DoesNotSelectWholeXamlOnStartup()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureAvaloniaEditTestResources();

            var viewModel = new MainViewModel(null);
            var activeXamlFile = viewModel.ActiveXamlFile;
            Assert.NotNull(activeXamlFile);
            var dockable = new WorkspaceFileDocumentDockViewModel(
                viewModel,
                activeXamlFile);
            var view = new WorkspaceFileEditorDockView
            {
                DataContext = dockable
            };
            var window = new Window
            {
                Width = 640,
                Height = 420,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var editor = Assert.Single(view.GetVisualDescendants().OfType<TextEditor>());
                Assert.Equal(0, viewModel.VisualEditorSourceSelectionLength);
                Assert.Equal(0, editor.SelectionLength);
                Assert.NotEqual(activeXamlFile.Text.Length, editor.SelectionLength);
            }
            finally
            {
                window.Close();
                dockable.Dispose();
            }
        });
    }

    [Fact]
    public void TextEditorSourceSelection_ClearsStaleSelectionWhenSourceSelectionClears()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var editor = new TextEditor
            {
                Document = new TextDocument { Text = "<StackPanel><TextBlock /></StackPanel>" }
            };

            TextEditorSourceSelection.SetIsEnabled(editor, true);
            TextEditorSourceSelection.SetTargetPath(editor, "Main.axaml");
            TextEditorSourceSelection.SetSourcePath(editor, "Main.axaml");
            TextEditorSourceSelection.SetSourceStart(editor, 12);
            TextEditorSourceSelection.SetSourceLength(editor, 11);
            TextEditorSourceSelection.SetSourceVersion(editor, 1);

            Assert.Equal(12, editor.SelectionStart);
            Assert.Equal(11, editor.SelectionLength);

            TextEditorSourceSelection.SetSourcePath(editor, null);
            TextEditorSourceSelection.SetSourceLength(editor, 0);
            TextEditorSourceSelection.SetSourceVersion(editor, 2);

            Assert.Equal(0, editor.SelectionLength);
            Assert.NotEqual(11, editor.SelectionLength);
        });
    }

    [Fact]
    public void WorkspaceEditor_TypingDoesNotReapplyVisualEditorSourceSelection()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureAvaloniaEditTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            var dockable = new WorkspaceFileDocumentDockViewModel(viewModel, viewModel.ActiveXamlFile);
            var view = new WorkspaceFileEditorDockView
            {
                DataContext = dockable
            };
            var window = new Window
            {
                Width = 640,
                Height = 420,
                Content = view
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var actionNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
                Assert.NotNull(actionNode);
                viewModel.SelectedVisualEditorNode = actionNode;
                PumpLayout(window);

                var editor = Assert.Single(view.GetVisualDescendants().OfType<TextEditor>());
                Assert.Equal(actionNode.Element.Start, editor.SelectionStart);
                Assert.Equal(actionNode.Element.Length, editor.SelectionLength);

                var typingOffset = viewModel.ActiveXamlFile.Text.IndexOf("Run", StringComparison.Ordinal) + 1;
                editor.Select(typingOffset, 0);
                editor.CaretOffset = typingOffset;
                PumpLayout(window);
                var sourceSelectionVersion = viewModel.VisualEditorSourceSelectionVersion;

                editor.TextArea.PerformTextInput("!");
                PumpLayout(window);

                Assert.Contains("R!un", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                Assert.Equal(0, editor.SelectionLength);
                Assert.Equal(typingOffset + 1, editor.CaretOffset);
                Assert.Equal(sourceSelectionVersion, viewModel.VisualEditorSourceSelectionVersion);
                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
            }
            finally
            {
                window.Close();
                dockable.Dispose();
            }
        });
    }

    [Fact]
    public void WorkspaceEditor_CaretSelectionSynchronizesVisualEditorPanelsAndPreview()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureAvaloniaEditTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         Width="260"
                                                         Height="140"
                                                         Background="White"
                                                         x:Name="Root">
                                               <TextBlock x:Name="Title" Text="Designer" />
                                               <Button x:Name="Action" Content="Run" Width="120" />
                                             </StackPanel>
                                             """;
            viewModel.Control = Assert.IsAssignableFrom<Control>(
                AvaloniaRuntimeXamlLoader.Load(viewModel.ActiveXamlFile.Text));
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var dockable = new WorkspaceFileDocumentDockViewModel(viewModel, viewModel.ActiveXamlFile);
            var editorView = new WorkspaceFileEditorDockView
            {
                DataContext = dockable
            };
            var preview = new PreviewView
            {
                DataContext = viewModel
            };
            var host = new Grid
            {
                Width = 920,
                Height = 420,
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(520)),
                    new ColumnDefinition(new GridLength(400))
                },
                Children =
                {
                    editorView,
                    preview
                }
            };
            Grid.SetColumn(preview, 1);

            var window = new Window
            {
                Width = 940,
                Height = 460,
                Background = Brushes.White,
                Content = host
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var actionNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
                Assert.NotNull(actionNode);

                var editor = Assert.Single(editorView.GetVisualDescendants().OfType<TextEditor>());
                var actionCaretOffset = actionNode.Element.Start + 1;
                editor.Select(actionCaretOffset, 0);
                editor.CaretOffset = actionCaretOffset;
                PumpLayout(window);

                Assert.Equal("Action", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal("Action", viewModel.SelectedVisualEditorStructureRow?.Element.Name);
                Assert.Contains(viewModel.VisualEditorProperties, property =>
                    property.Name == "Content" && property.Value == "Run");
                Assert.Equal(actionCaretOffset, editor.CaretOffset);
                Assert.Equal(0, editor.SelectionLength);
                Assert.True(viewModel.VisualEditorPreviewSelectionVisible);
                Assert.True(viewModel.VisualEditorPreviewSelectionWidth > 0);
                Assert.True(viewModel.VisualEditorPreviewSelectionHeight > 0);

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-source-selection-sync.png");
                Assert.NotNull(frame);
                frame.Save(path);
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
                dockable.Dispose();
            }
        });
    }

    [Fact]
    public void HeadlessStructureTree_ToolboxDropUsesHoveredRowTarget()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureVisualEditorTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Border x:Name="Target"
                                                       Width="120"
                                                       Height="80"
                                                       Background="LightBlue" />
                                               <Button x:Name="Peer" Content="Peer" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var structureView = new VisualStructureDockView
            {
                Width = 360,
                Height = 260,
                DataContext = new VisualStructureDockViewModel(viewModel)
            };
            var window = new Window
            {
                Width = 380,
                Height = 300,
                Background = Brushes.White,
                Content = structureView
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var targetRow = Assert.Single(
                    structureView.GetVisualDescendants().OfType<DataGridRow>(),
                    row => row.DataContext is HierarchicalNode { Item: VisualEditorNodeViewModel node } &&
                           node.Element.Name == "Target");
                var textBlockTool = Assert.Single(
                    viewModel.VisualEditorToolboxItems,
                    item => item.TypeName == "TextBlock");
                var data = ToolboxDragPayload.CreateDataTransfer(textBlockTool);
                var dropArgs = CreateDragEventArgs(
                    DragDrop.DropEvent,
                    targetRow,
                    data,
                    new Point(targetRow.Bounds.Width / 2, targetRow.Bounds.Height / 2));

                typeof(VisualStructureDockView)
                    .GetMethod("OnDrop", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(structureView, new object?[] { targetRow, dropArgs });

                Assert.True(dropArgs.Handled);
                Assert.Equal(DragDropEffects.Copy, dropArgs.DragEffects);

                var updated = viewModel.ActiveXamlFile.Text;
                var targetStart = updated.IndexOf("x:Name=\"Target\"", StringComparison.Ordinal);
                var targetEnd = updated.IndexOf("</Border>", targetStart, StringComparison.Ordinal);
                var inserted = updated.IndexOf("<TextBlock", targetStart, StringComparison.Ordinal);
                Assert.True(targetStart >= 0);
                Assert.True(targetEnd > targetStart);
                Assert.True(inserted > targetStart && inserted < targetEnd);
                Assert.Equal("TextBlock", viewModel.SelectedVisualEditorNode?.Element.TypeName);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessStructureTree_RowDropHandlerMovesXamlElement()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureVisualEditorTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <Button x:Name="MoveMe" Content="Move" />
                                               <StackPanel x:Name="TargetPanel" />
                                               <Button x:Name="Peer" Content="Peer" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);

            var structureView = new VisualStructureDockView
            {
                Width = 360,
                Height = 260,
                DataContext = new VisualStructureDockViewModel(viewModel)
            };
            var window = new Window
            {
                Width = 380,
                Height = 300,
                Background = Brushes.White,
                Content = structureView
            };

            try
            {
                window.Show();
                PumpLayout(window);

                var structureGrid = Assert.Single(
                    structureView.GetVisualDescendants().OfType<DataGrid>(),
                    grid => grid.Name == "StructureGrid");
                var sourceRow = Assert.Single(
                    structureView.GetVisualDescendants().OfType<DataGridRow>(),
                    row => row.DataContext is HierarchicalNode { Item: VisualEditorNodeViewModel node } &&
                           node.Element.Name == "MoveMe");
                var targetRow = Assert.Single(
                    structureView.GetVisualDescendants().OfType<DataGridRow>(),
                    row => row.DataContext is HierarchicalNode { Item: VisualEditorNodeViewModel node } &&
                           node.Element.Name == "TargetPanel");
                var dragArgs = CreateDragEventArgs(
                    DragDrop.DropEvent,
                    targetRow,
                    new DataTransfer(),
                    new Point(targetRow.Bounds.Width / 2, targetRow.Bounds.Height / 2));
                var dropArgs = new DataGridRowDropEventArgs(
                    structureGrid,
                    targetList: null,
                    items: new object[] { sourceRow.DataContext! },
                    sourceIndices: new[] { sourceRow.Index },
                    targetItem: targetRow.DataContext,
                    targetIndex: targetRow.Index,
                    insertIndex: 0,
                    targetRow,
                    DataGridRowDropPosition.Inside,
                    isSameGrid: true,
                    requestedEffect: DragDropEffects.Move,
                    dragArgs);

                Assert.True(structureGrid.RowDropHandler.Validate(dropArgs));
                Assert.True(structureGrid.RowDropHandler.Execute(dropArgs));

                var updated = viewModel.ActiveXamlFile.Text;
                var targetStart = updated.IndexOf("x:Name=\"TargetPanel\"", StringComparison.Ordinal);
                var targetEnd = updated.IndexOf("</StackPanel>", targetStart, StringComparison.Ordinal);
                var moved = updated.IndexOf("x:Name=\"MoveMe\"", StringComparison.Ordinal);
                var peer = updated.IndexOf("x:Name=\"Peer\"", StringComparison.Ordinal);
                Assert.True(targetStart >= 0);
                Assert.True(targetEnd > targetStart);
                Assert.True(moved > targetStart && moved < targetEnd);
                Assert.True(peer > targetEnd);
                Assert.Equal("MoveMe", viewModel.SelectedVisualEditorNode?.Element.Name);
                Assert.Equal(new[] { 0, 0 }, viewModel.SelectedVisualEditorNode?.Element.Path);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeadlessVisualEditorPanels_CaptureScreenshotWithLiveEditingData()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            EnsureVisualEditorTestResources();

            var viewModel = new MainViewModel(null);
            viewModel.ActiveXamlFile!.Text = """
                                             <StackPanel xmlns="https://github.com/avaloniaui"
                                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                         x:Name="Root">
                                               <TextBlock x:Name="Title" Text="Designer" />
                                               <Button x:Name="Action" Content="Run" />
                                             </StackPanel>
                                             """;
            viewModel.RefreshVisualEditorCommand.Execute(null);
            viewModel.SelectedVisualEditorNode = FindVisualEditorNode(viewModel.VisualEditorStructureNodes, "Action");
            viewModel.VisualEditorToolboxSearch = "button";

            var host = new Grid
            {
                Width = 960,
                Height = 520,
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(320)),
                    new ColumnDefinition(new GridLength(320)),
                    new ColumnDefinition(new GridLength(320))
                },
                Children =
                {
                    new VisualStructureDockView
                    {
                        DataContext = new VisualStructureDockViewModel(viewModel)
                    },
                    new VisualPropertiesDockView
                    {
                        DataContext = new VisualPropertiesDockViewModel(viewModel)
                    },
                    new VisualToolboxDockView
                    {
                        DataContext = new VisualToolboxDockViewModel(viewModel)
                    }
                }
            };
            Grid.SetColumn(host.Children[1], 1);
            Grid.SetColumn(host.Children[2], 2);

            var window = new Window
            {
                Width = 980,
                Height = 560,
                Background = Brushes.White,
                Content = host
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.Equal("Content", viewModel.SelectedVisualEditorAvailableProperty?.Name);
                Assert.True(host.GetVisualDescendants()
                    .OfType<Control>()
                    .Count(control => control.GetType().Name == "DataGrid") >= 2);
                var propertiesGrid = Assert.Single(
                    host.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType().Name == "DataGrid" &&
                               control.Name == "PropertiesGrid");
                Assert.Empty(propertiesGrid.GetVisualAncestors().OfType<ScrollViewer>());
                Assert.Same(
                    typeof(MainViewModel)
                        .GetProperty("VisualEditorPropertiesView")!
                        .GetValue(viewModel),
                    propertiesGrid.GetType()
                        .GetProperty("ItemsSource")!
                        .GetValue(propertiesGrid));
                Assert.False((bool)propertiesGrid.GetType()
                    .GetProperty("IsReadOnly")!
                    .GetValue(propertiesGrid)!);
                var propertyColumns = ((System.Collections.IEnumerable)propertiesGrid.GetType()
                        .GetProperty("Columns")!
                        .GetValue(propertiesGrid)!)
                    .Cast<object>()
                    .ToArray();
                Assert.Equal(new[] { "Property", "Value", "Type", "Priority" }, propertyColumns
                    .Select(column => column.GetType().GetProperty("Header")!.GetValue(column)?.ToString())
                    .ToArray());
                var propertyColumn = Assert.Single(propertyColumns, column =>
                    string.Equals(column.GetType().GetProperty("Header")!.GetValue(column)?.ToString(), "Property", StringComparison.Ordinal));
                Assert.Equal("Name", GetDataGridTextColumnBindingPath(propertyColumn));
                var valueColumn = Assert.Single(propertyColumns, column =>
                    string.Equals(column.GetType().GetProperty("Header")!.GetValue(column)?.ToString(), "Value", StringComparison.Ordinal));
                Assert.Equal("Value", GetDataGridTextColumnBindingPath(valueColumn));
                Assert.DoesNotContain(
                    host.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType().Name == "DataGrid" &&
                               control.Name == "EditorFieldsGrid");

                var contentProperty = Assert.Single(
                    viewModel.VisualEditorProperties,
                    property => property.Name == "Content");
                Assert.Equal("Assigned Values", contentProperty.Group);
                contentProperty.Value = "Apply";
                Assert.Contains("Content=\"Apply\"", viewModel.ActiveXamlFile.Text, StringComparison.Ordinal);
                PumpLayout(window);
                var structureGrid = Assert.Single(
                    host.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType().Name == "DataGrid" &&
                               control.Name == "StructureGrid");
                Assert.True((bool)structureGrid.GetType()
                    .GetProperty("HierarchicalRowsEnabled")!
                    .GetValue(structureGrid)!);
                Assert.Same(
                    typeof(MainViewModel)
                        .GetProperty("VisualEditorStructureModel")!
                        .GetValue(viewModel),
                    structureGrid.GetType()
                        .GetProperty("HierarchicalModel")!
                        .GetValue(structureGrid));
                var structureColumns = ((System.Collections.IEnumerable)structureGrid.GetType()
                        .GetProperty("Columns")!
                        .GetValue(structureGrid)!)
                    .Cast<object>()
                    .ToArray();
                Assert.Contains(structureColumns, column =>
                    column.GetType().Name == "DataGridHierarchicalColumn");
                Assert.Equal(new[] { "Element", "Path" }, structureColumns
                    .Select(column => column.GetType().GetProperty("Header")!.GetValue(column)?.ToString())
                    .ToArray());

                var structureButtons = new[]
                {
                    "StructureRefreshButton",
                    "StructureWrapButton",
                    "StructureUnwrapButton",
                    "StructureDuplicateButton",
                    "StructureMoveUpButton",
                    "StructureMoveDownButton",
                    "StructureDeleteButton"
                }
                    .Select(name => Assert.Single(
                        host.GetVisualDescendants().OfType<Button>(),
                        button => button.Name == name))
                    .ToArray();
                Assert.All(structureButtons, button =>
                {
                    Assert.Equal(30, button.Width);
                    Assert.Equal(30, button.Height);
                    Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                    Assert.NotNull(ToolTip.GetTip(button));
                });

                var frame = window.CaptureRenderedFrame();
                var path = GetScreenshotPath("visual-editing-panels.png");
                Assert.NotNull(frame);
                frame.Save(path);

                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void ClickPreviewControl(PreviewView preview, Control control)
    {
        ClickPreviewControl(preview, control, KeyModifiers.None);
    }

    private static void ClickPreviewControl(PreviewView preview, Control control, KeyModifiers modifiers)
    {
        var point = GetPreviewControlCenter(preview, control);
        ClickPreviewPoint(preview, point, modifiers);
    }

    private static void ClickPreviewPoint(PreviewView preview, Point point)
    {
        ClickPreviewPoint(preview, point, KeyModifiers.None);
    }

    private static void ClickPreviewPoint(PreviewView preview, Point point, KeyModifiers modifiers)
    {
        var previewSurface = Assert.Single(
            preview.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Name == "PreviewSurface");
        var pointer = new Avalonia.Input.Pointer(
            Avalonia.Input.Pointer.GetNextFreeId(),
            PointerType.Mouse,
            isPrimary: true);

        preview.RaiseEvent(CreatePointerPressedArgs(preview, previewSurface, pointer, point, modifiers));
        preview.RaiseEvent(CreatePointerReleasedArgs(preview, previewSurface, pointer, point, modifiers));
    }

    private static Point GetPreviewControlCenter(
        PreviewView preview,
        Control control)
    {
        var previewSurface = Assert.Single(
            preview.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Name == "PreviewSurface");
        return GetPreviewControlCenter(preview, previewSurface, control);
    }

    private static Point GetPreviewControlCenter(
        PreviewView preview,
        Control previewSurface,
        Control control)
    {
        var topLeft = control.TranslatePoint(default, previewSurface);
        if (topLeft is { } point)
        {
            return point + new Vector(control.Bounds.Width / 2, control.Bounds.Height / 2);
        }

        var controlTopLeftInPreview = control.TranslatePoint(default, preview);
        var surfaceTopLeftInPreview = previewSurface.TranslatePoint(default, preview);
        if (controlTopLeftInPreview is { } controlPoint &&
            surfaceTopLeftInPreview is { } surfacePoint)
        {
            return controlPoint - surfacePoint + new Vector(control.Bounds.Width / 2, control.Bounds.Height / 2);
        }

        var controlBounds = control.GetTransformedBounds();
        var surfaceBounds = previewSurface.GetTransformedBounds();
        if (controlBounds is { } transformedControlBounds &&
            surfaceBounds is { } transformedSurfaceBounds)
        {
            var center = transformedControlBounds.Bounds.Center;
            var origin = transformedSurfaceBounds.Bounds.Position;
            return new Point(center.X - origin.X, center.Y - origin.Y);
        }

        Assert.Fail($"Could not map {control.GetType().Name} '{control.Name}' to the preview surface.");
        return default;
    }

    private static PointerPressedEventArgs CreatePointerPressedArgs(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        return new PointerPressedEventArgs(
            source,
            pointer,
            root,
            position,
            0,
            properties,
            modifiers);
    }

    private static PointerEventArgs CreatePointerMovedArgs(Control source, Visual root, IPointer pointer, Point position)
    {
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.Other);
        return new PointerEventArgs(
            InputElement.PointerMovedEvent,
            source,
            pointer,
            root,
            position,
            0,
            properties,
            KeyModifiers.None);
    }

    private static PointerReleasedEventArgs CreatePointerReleasedArgs(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var properties = new PointerPointProperties(
            RawInputModifiers.None,
            PointerUpdateKind.LeftButtonReleased);
        return new PointerReleasedEventArgs(
            source,
            pointer,
            root,
            position,
            0,
            properties,
            modifiers,
            MouseButton.Left);
    }

    private static DragEventArgs CreateDragEventArgs(
        RoutedEvent<DragEventArgs> routedEvent,
        Interactive source,
        IDataTransfer dataTransfer,
        Point position)
    {
        return new DragEventArgs(
            routedEvent,
            dataTransfer,
            source,
            position,
            KeyModifiers.None);
    }

    private static KeyEventArgs CreateKeyDownArgs(
        Interactive source,
        Key key,
        KeyModifiers modifiers)
    {
        return new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = source,
            Key = key,
            KeyModifiers = modifiers,
            Route = RoutingStrategies.Bubble
        };
    }

    private static VisualEditorNodeViewModel? FindVisualEditorNode(
        IEnumerable<VisualEditorNodeViewModel> nodes,
        string name)
    {
        return nodes
            .SelectMany(FlattenVisualEditorNodes)
            .FirstOrDefault(node => string.Equals(node.Element.Name, name, StringComparison.Ordinal));
    }

    private static VisualEditorNodeViewModel? FindVisualEditorNodeByPath(
        IEnumerable<VisualEditorNodeViewModel> nodes,
        params int[] path)
    {
        return nodes
            .SelectMany(FlattenVisualEditorNodes)
            .FirstOrDefault(node => node.Element.Path.SequenceEqual(path));
    }

    private static string? GetDataGridTextColumnBindingPath(object column)
    {
        var binding = column.GetType()
            .GetProperty("Binding")!
            .GetValue(column);

        return binding?.GetType()
            .GetProperty("Path")
            ?.GetValue(binding)
            ?.ToString();
    }

    private static void SetDiagnosticsPreviewXamlFile(
        MainViewModel viewModel,
        InMemoryProjectFile xamlFile,
        string? previewSourceXaml = null)
    {
        var previewSource = previewSourceXaml ?? xamlFile.Text;
        var fileField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewXamlFile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(fileField);
        fileField.SetValue(viewModel, xamlFile);

        var textField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewSourceXamlText",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(textField);
        textField.SetValue(viewModel, previewSource);

        var acceptedTextField = typeof(MainViewModel).GetField(
            "_diagnosticsAcceptedXamlText",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(acceptedTextField);
        acceptedTextField.SetValue(viewModel, previewSource);

        var generatedSourceField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewUsesGeneratedSource",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(generatedSourceField);
        generatedSourceField.SetValue(viewModel, false);
    }

    private static void SetGeneratedDiagnosticsPreviewXamlFile(MainViewModel viewModel, InMemoryProjectFile xamlFile)
    {
        var fileField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewXamlFile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(fileField);
        fileField.SetValue(viewModel, xamlFile);

        var textField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewSourceXamlText",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(textField);
        textField.SetValue(viewModel, null);

        var acceptedTextField = typeof(MainViewModel).GetField(
            "_diagnosticsAcceptedXamlText",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(acceptedTextField);
        acceptedTextField.SetValue(viewModel, null);

        var generatedSourceField = typeof(MainViewModel).GetField(
            "_diagnosticsPreviewUsesGeneratedSource",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(generatedSourceField);
        generatedSourceField.SetValue(viewModel, true);
    }

    private static IEnumerable<VisualEditorNodeViewModel> FlattenVisualEditorNodes(VisualEditorNodeViewModel node)
    {
        yield return node;

        foreach (var child in node.Children.SelectMany(FlattenVisualEditorNodes))
        {
            yield return child;
        }
    }

    private static void EnsureVisualEditorTestResources()
    {
        var application = Application.Current!;
        application.Resources["DockSurfacePanelBrush"] = Brushes.WhiteSmoke;
        application.Resources["DockTabHoverBackgroundBrush"] = Brushes.Gainsboro;
        application.Resources["EditorBorderBrush"] = Brushes.LightGray;
    }

    private static void EnsureAvaloniaEditTestResources()
    {
        var application = Application.Current!;
        const string avaloniaEditTheme = "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml";
        if (application.Styles.OfType<StyleInclude>().Any(style =>
                style.Source?.OriginalString == avaloniaEditTheme))
        {
            return;
        }

        var include = new StyleInclude(new Uri("avares://XamlPlayground.Tests/"))
        {
            Source = new Uri(avaloniaEditTheme)
        };

        try
        {
            application.Styles.Add(include);
        }
        catch (XamlLoadException)
        {
            if (application.Styles.Contains(include))
            {
                application.Styles.Remove(include);
            }
        }
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string GetScreenshotPath(string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("AVALONIA_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "..",
                "..",
                "..",
                "..",
                "..",
                "artifacts",
                "headless-screenshots");
        }

        Directory.CreateDirectory(directory);
        return Path.GetFullPath(Path.Combine(directory, fileName));
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }
}
