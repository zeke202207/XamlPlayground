using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Language.Xml;
using XamlPlayground.Services.Editing;
using XamlPlayground.Services.VisualEditing;

namespace XamlPlayground.Services.Animation;

public sealed record AnimationTimelineKeyFrameDefinition(
    int CuePercent,
    string PropertyName,
    string Value,
    string KeySpline);

public sealed record AnimationTimelineTrackDefinition(
    string TargetSelector,
    string PropertyName,
    IReadOnlyList<AnimationTimelineKeyFrameDefinition> KeyFrames);

public sealed record AnimationTimelineDefinition(
    string TargetSelector,
    string Duration,
    string Delay,
    string IterationCount,
    string PlaybackDirection,
    string FillMode,
    string Easing,
    IReadOnlyList<AnimationTimelineTrackDefinition> Tracks)
{
    public static AnimationTimelineDefinition CreateEmpty(string targetSelector)
    {
        return new AnimationTimelineDefinition(
            targetSelector,
            "0:0:0.3",
            string.Empty,
            string.Empty,
            "Normal",
            "Both",
            "CubicEaseOut",
            Array.Empty<AnimationTimelineTrackDefinition>());
    }
}

public sealed record AnimationDocumentStyleTargetDefinition(
    int Index,
    string Selector);

public sealed record AnimationTimelineEditResult(bool Changed, string Text, string? Error = null);

public sealed class AnimationTimelineEditor
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public AnimationTimelineDefinition ReadElementAnimation(
        string xaml,
        XamlElementSelector selector,
        string targetSelector)
    {
        if (!TryParse(xaml, out var document, out _) ||
            document.Root is null ||
            FindElement(document.Root, selector) is not { } target)
        {
            return AnimationTimelineDefinition.CreateEmpty(targetSelector);
        }

        var elementSelector = NormalizeElementStyleSelector(target, targetSelector);
        var ownerSelector = GetOwnerScopedElementStyleSelector(target, elementSelector);
        var style = FindElementStyle(target, elementSelector) ??
                    (string.Equals(elementSelector, targetSelector, StringComparison.Ordinal)
                        ? null
                        : FindElementStyle(target, targetSelector)) ??
                    (ownerSelector is null
                        ? null
                        : FindElementStyle(target, ownerSelector));
        return style is null
            ? AnimationTimelineDefinition.CreateEmpty(elementSelector)
            : ReadStyleAnimation(style, elementSelector);
    }

    public AnimationTimelineDefinition ReadControlThemeAnimation(
        string xaml,
        string themeKey,
        string targetSelector)
    {
        if (!TryParse(xaml, out var document, out _) ||
            FindTheme(document, themeKey) is not { } theme)
        {
            return AnimationTimelineDefinition.CreateEmpty(targetSelector);
        }

        var style = FindDirectStyle(theme, targetSelector);
        return style is null
            ? AnimationTimelineDefinition.CreateEmpty(targetSelector)
            : ReadStyleAnimation(style, targetSelector);
    }

    public IReadOnlyList<AnimationDocumentStyleTargetDefinition> GetDocumentStyleTargets(string xaml)
    {
        if (!TryParse(xaml, out var document, out _) ||
            document.Root is null)
        {
            return Array.Empty<AnimationDocumentStyleTargetDefinition>();
        }

        return EnumerateStyleTargets(document)
            .Select(static item => new AnimationDocumentStyleTargetDefinition(
                item.Index,
                item.Style.Attribute("Selector")?.Value ?? string.Empty))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Selector))
            .ToArray();
    }

    public AnimationTimelineDefinition ReadDocumentStyleAnimation(
        string xaml,
        int styleIndex,
        string originalSelector,
        string targetSelector)
    {
        if (!TryParse(xaml, out var document, out _) ||
            document.Root is null ||
            FindDocumentStyle(document, styleIndex, originalSelector) is not { } style)
        {
            return AnimationTimelineDefinition.CreateEmpty(targetSelector);
        }

        var selector = style.Attribute("Selector")?.Value ?? targetSelector;
        return ReadStyleAnimation(style, selector);
    }

    public AnimationTimelineEditResult SetElementAnimation(
        string xaml,
        XamlElementSelector selector,
        AnimationTimelineDefinition timeline)
    {
        if (timeline.Tracks.Count == 0)
        {
            return new AnimationTimelineEditResult(false, xaml, "Add at least one animation track.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new AnimationTimelineEditResult(false, xaml, parseError ?? "XAML document has no root element.");
        }

        var target = XamlTextEditor.FindVisualElement(document.RootSyntax, selector);
        if (target is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected element was not found.");
        }

        var styleSelector = NormalizeElementStyleSelector(target, timeline.TargetSelector);
        var ownerSelector = GetOwnerScopedElementStyleSelector(target, styleSelector);
        var memberName = $"{target.NameNode.FullName}.Styles";
        var member = XamlTextEditor.FindDirectChildByFullName(target, memberName);
        if (member is null)
        {
            var memberText = CreateElementText(
                memberName,
                CreateStyleText(xaml, target, styleSelector, timeline with { TargetSelector = styleSelector }),
                XamlTextEditor.GetIndentUnit(xaml, target),
                XamlTextEditor.GetPreferredNewLine(xaml));
            var inserted = XamlTextEditor.InsertChild(xaml, target, memberText, insertFirst: true);
            return new AnimationTimelineEditResult(true, inserted);
        }

        var style = FindDirectStyle(member, styleSelector) ??
                    (string.Equals(styleSelector, timeline.TargetSelector, StringComparison.Ordinal)
                        ? null
                        : FindDirectStyle(member, timeline.TargetSelector)) ??
                    (ownerSelector is null
                        ? null
                        : FindDirectStyle(member, ownerSelector)) ??
                    null;

        if (style is null)
        {
            var inserted = XamlTextEditor.InsertChild(
                xaml,
                member,
                CreateStyleText(xaml, member, styleSelector, timeline with { TargetSelector = styleSelector }));
            return new AnimationTimelineEditResult(true, inserted);
        }

        var edited = EnsureStyleSelector(xaml, selector, style, styleSelector, out style);
        if (style is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected style could not be updated.");
        }

        edited = SetStyleAnimation(edited, style, timeline with { TargetSelector = styleSelector });
        return new AnimationTimelineEditResult(true, edited);
    }

    public AnimationTimelineEditResult SetDocumentStyleAnimation(
        string xaml,
        int styleIndex,
        string originalSelector,
        AnimationTimelineDefinition timeline)
    {
        if (timeline.Tracks.Count == 0)
        {
            return new AnimationTimelineEditResult(false, xaml, "Add at least one animation track.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new AnimationTimelineEditResult(false, xaml, parseError ?? "XAML document has no root element.");
        }

        var style = FindDocumentStyle(document.RootSyntax, styleIndex, originalSelector);
        if (style is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected style was not found.");
        }

        var edited = SetStyleSelectorAndReparse(xaml, style, styleIndex, originalSelector, timeline.TargetSelector, out style);
        if (style is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected style could not be updated.");
        }

        edited = SetStyleAnimation(edited, style, timeline);
        return new AnimationTimelineEditResult(true, edited);
    }

    public AnimationTimelineEditResult SetDocumentStyleSetter(
        string xaml,
        int styleIndex,
        string originalSelector,
        string targetSelector,
        string propertyName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return new AnimationTimelineEditResult(false, xaml, "Setter property name is required.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new AnimationTimelineEditResult(false, xaml, parseError ?? "XAML document has no root element.");
        }

        var style = FindDocumentStyle(document.RootSyntax, styleIndex, originalSelector);
        if (style is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected style was not found.");
        }

        var edited = SetStyleSelectorAndReparse(xaml, style, styleIndex, originalSelector, targetSelector, out style);
        if (style is null)
        {
            return new AnimationTimelineEditResult(false, xaml, "The selected style could not be updated.");
        }

        edited = SetDirectSetter(edited, style, propertyName.Trim(), value);
        return new AnimationTimelineEditResult(true, edited);
    }

    public AnimationTimelineEditResult SetControlThemeAnimation(
        string xaml,
        string themeKey,
        AnimationTimelineDefinition timeline)
    {
        if (timeline.Tracks.Count == 0)
        {
            return new AnimationTimelineEditResult(false, xaml, "Add at least one animation track.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new AnimationTimelineEditResult(false, xaml, parseError ?? "Theme XAML could not be parsed.");
        }

        var theme = FindTheme(document.RootSyntax, themeKey);
        if (theme is null)
        {
            return new AnimationTimelineEditResult(false, xaml, $"ControlTheme '{themeKey}' was not found.");
        }

        var style = FindDirectStyle(theme, timeline.TargetSelector);
        if (style is null)
        {
            var inserted = XamlTextEditor.InsertChild(
                xaml,
                theme,
                CreateStyleText(xaml, theme, timeline.TargetSelector, timeline));
            return new AnimationTimelineEditResult(true, inserted);
        }

        var edited = SetStyleAnimation(xaml, style, timeline);
        return new AnimationTimelineEditResult(true, edited);
    }

    private static AnimationTimelineDefinition ReadStyleAnimation(XElement style, string targetSelector)
    {
        var animation = style
            .Elements()
            .FirstOrDefault(static element => element.Name.LocalName == "Style.Animations")
            ?.Elements()
            .FirstOrDefault(static element => element.Name.LocalName == "Animation");
        if (animation is null)
        {
            return AnimationTimelineDefinition.CreateEmpty(targetSelector);
        }

        var frames = animation
            .Elements()
            .Where(static element => element.Name.LocalName == "KeyFrame")
            .SelectMany(frame =>
            {
                var cue = ParseCuePercent(
                    frame.Attribute("Cue")?.Value ??
                    ConvertKeyTimeToCue(frame.Attribute("KeyTime")?.Value, animation.Attribute("Duration")?.Value));
                var keySpline = frame.Attribute("KeySpline")?.Value ?? string.Empty;
                return frame
                    .Elements()
                    .Where(static element => element.Name.LocalName == "Setter")
                    .Select(setter => new AnimationTimelineKeyFrameDefinition(
                        cue,
                        setter.Attribute("Property")?.Value ?? string.Empty,
                        setter.Attribute("Value")?.Value ?? string.Empty,
                        keySpline));
            })
            .Where(static frame => !string.IsNullOrWhiteSpace(frame.PropertyName))
            .Select((frame, index) => new
            {
                Frame = frame with
                {
                    CuePercent = ClampCue(frame.CuePercent),
                    PropertyName = frame.PropertyName.Trim()
                },
                Index = index
            })
            .ToArray();

        var keyFrames = frames
            .GroupBy(static item => item.Frame.PropertyName, StringComparer.Ordinal)
            .Select(group => new AnimationTimelineTrackDefinition(
                targetSelector,
                group.Key,
                group
                    .GroupBy(static item => item.Frame.CuePercent)
                    .Select(static cueGroup => cueGroup
                        .OrderByDescending(static item => item.Index)
                        .First()
                        .Frame)
                    .OrderBy(static frame => frame.CuePercent)
                    .ToArray()))
            .OrderBy(static track => track.PropertyName, StringComparer.Ordinal)
            .ToArray();

        return new AnimationTimelineDefinition(
            targetSelector,
            animation.Attribute("Duration")?.Value ?? "0:0:0.3",
            animation.Attribute("Delay")?.Value ?? string.Empty,
            animation.Attribute("IterationCount")?.Value ?? string.Empty,
            animation.Attribute("PlaybackDirection")?.Value ?? "Normal",
            animation.Attribute("FillMode")?.Value ?? "Both",
            animation.Attribute("Easing")?.Value ?? "CubicEaseOut",
            keyFrames);
    }

    private static string SetStyleAnimation(
        string xaml,
        IXmlElementSyntax style,
        AnimationTimelineDefinition timeline)
    {
        var animations = XamlTextEditor.FindDirectChildByLocalName(style, "Style.Animations");
        var animationText = CreateAnimationText(xaml, style, timeline);

        if (animations is null)
        {
            var memberText = CreateElementText(
                "Style.Animations",
                animationText,
                XamlTextEditor.GetIndentUnit(xaml, style),
                XamlTextEditor.GetPreferredNewLine(xaml));
            return XamlTextEditor.InsertChild(xaml, style, memberText);
        }

        var animation = XamlTextEditor.FindDirectChildByLocalName(animations, "Animation");
        return animation is null
            ? XamlTextEditor.InsertChild(xaml, animations, animationText)
            : XamlTextEditor.ReplaceElement(xaml, animation, animationText);
    }

    private static string SetDirectSetter(
        string xaml,
        IXmlElementSyntax style,
        string propertyName,
        string value)
    {
        var setter = XamlTextEditor.DirectChildElements(style)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "Setter", StringComparison.Ordinal) &&
                string.Equals(XamlTextEditor.GetAttributeValue(element, "Property"), propertyName, StringComparison.Ordinal));

        if (setter is null)
        {
            return XamlTextEditor.InsertChild(
                xaml,
                style,
                CreateSetterText(propertyName, value));
        }

        if (setter.AsNode is XmlElementSyntax)
        {
            return XamlTextEditor.ReplaceElement(
                xaml,
                setter,
                CreateSetterText(propertyName, value));
        }

        return XamlTextEditor.SetAttributeValue(xaml, setter, "Value", value);
    }

    private static string EnsureStyleSelector(
        string xaml,
        XamlElementSelector selector,
        IXmlElementSyntax style,
        string styleSelector,
        out IXmlElementSyntax? updatedStyle)
    {
        if (string.Equals(XamlTextEditor.GetAttributeValue(style, "Selector"), styleSelector, StringComparison.Ordinal))
        {
            updatedStyle = style;
            return xaml;
        }

        var edited = XamlTextEditor.SetAttributeValue(xaml, style, "Selector", styleSelector);
        if (!XamlTextEditor.TryParse(edited, out var document, out _) ||
            document.RootSyntax is null ||
            XamlTextEditor.FindVisualElement(document.RootSyntax, selector) is not { } target)
        {
            updatedStyle = null;
            return edited;
        }

        var member = XamlTextEditor.FindDirectChildByFullName(target, $"{target.NameNode.FullName}.Styles");
        updatedStyle = member is null
            ? null
            : FindDirectStyle(member, styleSelector);
        return edited;
    }

    private static string SetStyleSelectorAndReparse(
        string xaml,
        IXmlElementSyntax style,
        int styleIndex,
        string originalSelector,
        string selector,
        out IXmlElementSyntax? updatedStyle)
    {
        if (string.Equals(XamlTextEditor.GetAttributeValue(style, "Selector"), selector, StringComparison.Ordinal))
        {
            updatedStyle = style;
            return xaml;
        }

        var edited = XamlTextEditor.SetAttributeValue(xaml, style, "Selector", selector);
        if (!XamlTextEditor.TryParse(edited, out var document, out _) ||
            document.RootSyntax is null)
        {
            updatedStyle = null;
            return edited;
        }

        updatedStyle = FindDocumentStyle(document.RootSyntax, styleIndex, selector) ??
                       FindDocumentStyle(document.RootSyntax, styleIndex, originalSelector);
        return edited;
    }

    private static string CreateStyleText(
        string xaml,
        IXmlElementSyntax context,
        string selector,
        AnimationTimelineDefinition timeline)
    {
        var newLine = XamlTextEditor.GetPreferredNewLine(xaml);
        var indentUnit = XamlTextEditor.GetIndentUnit(xaml, context);
        var animationText = CreateAnimationText(xaml, context, timeline);
        var animationsText = CreateElementText("Style.Animations", animationText, indentUnit, newLine);
        return CreateElementText(
            $"Style Selector=\"{XamlTextEditor.EscapeAttributeValue(selector)}\"",
            animationsText,
            indentUnit,
            newLine);
    }

    private static string CreateAnimationText(
        string xaml,
        IXmlElementSyntax context,
        AnimationTimelineDefinition timeline)
    {
        var newLine = XamlTextEditor.GetPreferredNewLine(xaml);
        var indentUnit = XamlTextEditor.GetIndentUnit(xaml, context);
        var attributes = new List<(string Name, string? Value)>
        {
            ("Duration", string.IsNullOrWhiteSpace(timeline.Duration) ? "0:0:0.3" : timeline.Duration),
            ("Delay", timeline.Delay),
            ("IterationCount", timeline.IterationCount),
            ("PlaybackDirection", timeline.PlaybackDirection),
            ("FillMode", timeline.FillMode),
            ("Easing", timeline.Easing)
        };
        var attributeText = string.Concat(attributes
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(static item => $" {item.Name}=\"{XamlTextEditor.EscapeAttributeValue(item.Value!.Trim())}\""));

        var builder = new StringBuilder();
        builder.Append("<Animation");
        builder.Append(attributeText);
        builder.Append('>');

        var keyFrames = NormalizeKeyFrames(timeline);
        foreach (var group in keyFrames.GroupBy(static frame => frame.CuePercent).OrderBy(static group => group.Key))
        {
            var firstKeySpline = group
                .Select(static frame => frame.KeySpline)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            builder.Append(newLine);
            builder.Append(indentUnit);
            builder.Append("<KeyFrame Cue=\"");
            builder.Append(FormattableString.Invariant($"{group.Key}%"));
            builder.Append('"');
            if (!string.IsNullOrWhiteSpace(firstKeySpline))
            {
                builder.Append(" KeySpline=\"");
                builder.Append(XamlTextEditor.EscapeAttributeValue(firstKeySpline.Trim()));
                builder.Append('"');
            }

            builder.Append('>');

            foreach (var frame in group.OrderBy(static frame => frame.PropertyName, StringComparer.Ordinal))
            {
                builder.Append(newLine);
                builder.Append(indentUnit);
                builder.Append(indentUnit);
                builder.Append(CreateSetterText(frame.PropertyName, frame.Value ?? string.Empty));
            }

            builder.Append(newLine);
            builder.Append(indentUnit);
            builder.Append("</KeyFrame>");
        }

        builder.Append(newLine);
        builder.Append("</Animation>");
        return builder.ToString();
    }

    private static string CreateElementText(
        string elementNameAndAttributes,
        string childXaml,
        string indentUnit,
        string newLine)
    {
        return $"<{elementNameAndAttributes}>{newLine}" +
               XamlTextEditor.IndentBlock(childXaml, indentUnit, newLine) +
               $"{newLine}</{GetElementName(elementNameAndAttributes)}>";
    }

    private static string CreateSetterText(string propertyName, string value)
    {
        return $"<Setter Property=\"{XamlTextEditor.EscapeAttributeValue(propertyName)}\" Value=\"{XamlTextEditor.EscapeAttributeValue(value)}\" />";
    }

    private static string GetElementName(string elementNameAndAttributes)
    {
        var index = elementNameAndAttributes.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        return index < 0
            ? elementNameAndAttributes
            : elementNameAndAttributes[..index];
    }

    private static IReadOnlyList<AnimationTimelineKeyFrameDefinition> NormalizeKeyFrames(AnimationTimelineDefinition timeline)
    {
        return timeline.Tracks
            .SelectMany(static track => track.KeyFrames)
            .Where(static frame => !string.IsNullOrWhiteSpace(frame.PropertyName))
            .Select(static frame => frame with
            {
                CuePercent = ClampCue(frame.CuePercent),
                PropertyName = frame.PropertyName.Trim()
            })
            .GroupBy(static frame => (frame.CuePercent, frame.PropertyName))
            .Select(static group => group.Last())
            .ToArray();
    }

    private static IXmlElementSyntax? FindDirectStyle(IXmlElementSyntax owner, string selector)
    {
        return XamlTextEditor.DirectChildElements(owner)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "Style", StringComparison.Ordinal) &&
                string.Equals(XamlTextEditor.GetAttributeValue(element, "Selector"), selector, StringComparison.Ordinal));
    }

    private static IXmlElementSyntax? FindTheme(IXmlElementSyntax root, string themeKey)
    {
        return XamlTextEditor.DescendantsAndSelf(root)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "ControlTheme", StringComparison.Ordinal) &&
                string.Equals(
                    XamlTextEditor.GetAttributeValue(element, "x:Key") ??
                    XamlTextEditor.GetAttributeValueByLocalName(element, "Key"),
                    themeKey,
                    StringComparison.Ordinal));
    }

    private static IXmlElementSyntax? FindDocumentStyle(
        IXmlElementSyntax root,
        int styleIndex,
        string originalSelector)
    {
        var styles = EnumerateStyleTargets(root).ToArray();
        if (styleIndex >= 0 &&
            styleIndex < styles.Length)
        {
            return styles[styleIndex].Style;
        }

        return styles
            .Select(static item => item.Style)
            .FirstOrDefault(style =>
                string.Equals(XamlTextEditor.GetAttributeValue(style, "Selector"), originalSelector, StringComparison.Ordinal));
    }

    private static IEnumerable<(int Index, IXmlElementSyntax Style)> EnumerateStyleTargets(IXmlElementSyntax root)
    {
        var index = 0;
        foreach (var style in XamlTextEditor
                     .DescendantsAndSelf(root)
                     .Where(static element =>
                         string.Equals(element.NameNode.LocalName, "Style", StringComparison.Ordinal) &&
                         !string.IsNullOrWhiteSpace(XamlTextEditor.GetAttributeValue(element, "Selector"))))
        {
            yield return (index, style);
            index++;
        }
    }

    private static string NormalizeElementStyleSelector(IXmlElementSyntax target, string? selector)
    {
        var typeSelector = GetElementStyleTypeSelector(target);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return typeSelector;
        }

        var trimmed = selector.Trim();
        if (!trimmed.StartsWith('^'))
        {
            return trimmed;
        }

        var suffix = trimmed[1..];
        if (suffix.Length == 0)
        {
            return typeSelector;
        }

        return suffix[0] is ':' or ' '
            ? typeSelector + suffix
            : $"{typeSelector} {suffix}";
    }

    private static string GetElementStyleTypeSelector(IXmlElementSyntax target)
    {
        var fullName = target.NameNode.FullName;
        var separator = fullName.IndexOf(':', StringComparison.Ordinal);
        return separator > 0
            ? $"{fullName[..separator]}|{target.NameNode.LocalName}"
            : target.NameNode.LocalName;
    }

    private static string? GetOwnerScopedElementStyleSelector(IXmlElementSyntax target, string selector)
    {
        var typeSelector = GetElementStyleTypeSelector(target);
        if (!selector.StartsWith(typeSelector, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = selector[typeSelector.Length..];
        return suffix.Length == 0 || suffix[0] is ':' or ' '
            ? "^" + suffix
            : null;
    }

    private static XElement? FindElementStyle(XElement target, string selector)
    {
        var memberName = $"{target.Name.LocalName}.Styles";
        return target
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == memberName)
            ?.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Style" &&
                string.Equals(element.Attribute("Selector")?.Value, selector, StringComparison.Ordinal));
    }

    private static XElement? FindDirectStyle(XElement owner, string selector)
    {
        return owner
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Style" &&
                string.Equals(element.Attribute("Selector")?.Value, selector, StringComparison.Ordinal));
    }

    private static string NormalizeElementStyleSelector(XElement target, string? selector)
    {
        var typeSelector = GetElementStyleTypeSelector(target);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return typeSelector;
        }

        var trimmed = selector.Trim();
        if (!trimmed.StartsWith('^'))
        {
            return trimmed;
        }

        var suffix = trimmed[1..];
        if (suffix.Length == 0)
        {
            return typeSelector;
        }

        return suffix[0] is ':' or ' '
            ? typeSelector + suffix
            : $"{typeSelector} {suffix}";
    }

    private static string GetElementStyleTypeSelector(XElement target)
    {
        var localName = target.Name.LocalName;
        var prefix = target.GetPrefixOfNamespace(target.Name.Namespace);
        return string.IsNullOrWhiteSpace(prefix)
            ? localName
            : $"{prefix}|{localName}";
    }

    private static string? GetOwnerScopedElementStyleSelector(XElement target, string selector)
    {
        var typeSelector = GetElementStyleTypeSelector(target);
        if (!selector.StartsWith(typeSelector, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = selector[typeSelector.Length..];
        return suffix.Length == 0 || suffix[0] is ':' or ' '
            ? "^" + suffix
            : null;
    }

    private static XElement? FindTheme(XDocument document, string themeKey)
    {
        return document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ControlTheme" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, themeKey, StringComparison.Ordinal));
    }

    private static XElement? FindDocumentStyle(
        XDocument document,
        int styleIndex,
        string originalSelector)
    {
        var styles = EnumerateStyleTargets(document).ToArray();
        if (styleIndex >= 0 &&
            styleIndex < styles.Length)
        {
            return styles[styleIndex].Style;
        }

        return styles
            .Select(static item => item.Style)
            .FirstOrDefault(style =>
                string.Equals(style.Attribute("Selector")?.Value, originalSelector, StringComparison.Ordinal));
    }

    private static IEnumerable<(int Index, XElement Style)> EnumerateStyleTargets(XDocument document)
    {
        var index = 0;
        foreach (var style in document
                     .Descendants()
                     .Where(static element =>
                         element.Name.LocalName == "Style" &&
                         !string.IsNullOrWhiteSpace(element.Attribute("Selector")?.Value)))
        {
            yield return (index, style);
            index++;
        }
    }

    private static XElement? FindElement(XElement root, XamlElementSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            return root
                .DescendantsAndSelf()
                .FirstOrDefault(element =>
                    !IsMemberElement(element) &&
                    (string.Equals(element.Attribute(XamlNamespace + "Name")?.Value, selector.Name, StringComparison.Ordinal) ||
                     string.Equals(element.Attribute("Name")?.Value, selector.Name, StringComparison.Ordinal)));
        }

        var path = selector.Path ?? Array.Empty<int>();
        var current = root;
        foreach (var index in path)
        {
            var children = EnumerateVisualChildren(current).ToArray();
            if (index < 0 || index >= children.Length)
            {
                return null;
            }

            current = children[index];
        }

        return current;
    }

    private static IEnumerable<XElement> EnumerateVisualChildren(XElement element)
    {
        foreach (var child in element.Elements())
        {
            if (!IsMemberElement(child))
            {
                yield return child;
                continue;
            }

            if (!IsVisualContentMemberElement(child))
            {
                continue;
            }

            foreach (var memberChild in EnumerateMemberVisualChildren(child))
            {
                yield return memberChild;
            }
        }
    }

    private static IEnumerable<XElement> EnumerateMemberVisualChildren(XElement element)
    {
        foreach (var child in element.Elements())
        {
            if (IsMemberElement(child))
            {
                if (!IsVisualContentMemberElement(child))
                {
                    continue;
                }

                foreach (var nested in EnumerateMemberVisualChildren(child))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return child;
            }
        }
    }

    private static bool IsMemberElement(XElement element)
    {
        return element.Parent is not null &&
               element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsVisualContentMemberElement(XElement element)
    {
        var propertyName = GetMemberPropertyName(element);
        if (propertyName.Length == 0 ||
            IsNonVisualMemberProperty(propertyName))
        {
            return false;
        }

        return propertyName is "Child" or "Children" or "Content" or "Footer" or "Header" or "Icon" or "Items" or
                   "Pane" ||
               propertyName.EndsWith("Child", StringComparison.Ordinal) ||
               propertyName.EndsWith("Children", StringComparison.Ordinal) ||
               propertyName.EndsWith("Content", StringComparison.Ordinal);
    }

    private static bool IsNonVisualMemberProperty(string propertyName)
    {
        return propertyName.EndsWith("Bindings", StringComparison.Ordinal) ||
               propertyName.EndsWith("Definitions", StringComparison.Ordinal) ||
               propertyName.EndsWith("Dictionaries", StringComparison.Ordinal) ||
               propertyName.EndsWith("Resources", StringComparison.Ordinal) ||
               propertyName.EndsWith("Styles", StringComparison.Ordinal) ||
               propertyName.EndsWith("Template", StringComparison.Ordinal) ||
               propertyName.EndsWith("Templates", StringComparison.Ordinal) ||
               propertyName.EndsWith("Transitions", StringComparison.Ordinal) ||
               propertyName is "DataContext" or "RenderTransform" or "Resources" or "Styles" or "Transitions";
    }

    private static string GetMemberPropertyName(XElement element)
    {
        var name = element.Name.LocalName;
        var dotIndex = name.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < name.Length - 1
            ? name[(dotIndex + 1)..]
            : string.Empty;
    }

    private static bool TryParse(string xaml, out XDocument document, out string? error)
    {
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.None);
            error = null;
            return true;
        }
        catch (XmlException exception)
        {
            document = new XDocument();
            error = exception.Message;
            return false;
        }
    }

    private static int ParseCuePercent(string? cue)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            return 100;
        }

        var text = cue.Trim().TrimEnd('%');
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? ClampCue(value)
            : 100;
    }

    private static string? ConvertKeyTimeToCue(string? keyTime, string? duration)
    {
        if (string.IsNullOrWhiteSpace(keyTime) ||
            string.IsNullOrWhiteSpace(duration) ||
            !TimeSpan.TryParse(keyTime, CultureInfo.InvariantCulture, out var keyTimeValue) ||
            !TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var durationValue) ||
            durationValue.TotalMilliseconds <= 0)
        {
            return null;
        }

        return FormattableString.Invariant($"{ClampCue((int)Math.Round(keyTimeValue.TotalMilliseconds / durationValue.TotalMilliseconds * 100))}%");
    }

    private static int ClampCue(int value)
    {
        return Math.Clamp(value, 0, 100);
    }
}
