using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlPlayground.Services.Theming;

public sealed record ControlThemeAnalysis(
    string Key,
    string TargetType,
    IReadOnlyList<ControlThemeTemplatePart> Parts,
    IReadOnlyList<ControlThemeTemplateBinding> TemplateBindings,
    IReadOnlyList<ControlThemeStateSelector> StateSelectors,
    IReadOnlyList<string> AvailableStates)
{
    public static ControlThemeAnalysis Empty { get; } = new(
        string.Empty,
        string.Empty,
        Array.Empty<ControlThemeTemplatePart>(),
        Array.Empty<ControlThemeTemplateBinding>(),
        Array.Empty<ControlThemeStateSelector>(),
        Array.Empty<string>());
}

public sealed record ControlThemeTemplatePart(
    string Name,
    string Type,
    int? Line);

public sealed record ControlThemeTemplateBinding(
    string Property,
    int Line,
    string Snippet);

public sealed record ControlThemeStateSelector(
    string State,
    string Selector,
    int? Line);

public static class ControlThemeAnalyzer
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] CommonStates =
    {
        "normal",
        "pointerover",
        "pressed",
        "disabled",
        "focus",
        "checked"
    };
    private static readonly Regex PseudoClassRegex = new(
        ":(?<state>[A-Za-z][A-Za-z0-9_-]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TemplateBindingRegex = new(
        "\\{\\s*TemplateBinding\\s+(?<property>[^,}\\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ControlThemeAnalysis Analyze(string xaml, string themeKey)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return ControlThemeAnalysis.Empty;
        }

        var theme = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ControlTheme" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, themeKey, StringComparison.Ordinal));
        if (theme is null)
        {
            return ControlThemeAnalysis.Empty;
        }

        var stateSelectors = FindStateSelectors(theme).ToArray();
        var availableStates = stateSelectors
            .Select(static selector => selector.State)
            .Concat(CommonStates)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static state => state, ThemeStateComparer.Instance)
            .ToArray();

        return new ControlThemeAnalysis(
            themeKey,
            FluentControlThemeCatalog.GetLocalName(theme.Attribute("TargetType")?.Value ?? string.Empty),
            FindTemplateParts(theme).ToArray(),
            FindTemplateBindings(theme).ToArray(),
            stateSelectors,
            availableStates);
    }

    private static IEnumerable<ControlThemeTemplatePart> FindTemplateParts(XElement theme)
    {
        return theme
            .Descendants()
            .Select(element => new
            {
                Element = element,
                Name = element.Attribute("Name")?.Value ?? element.Attribute(XamlNamespace + "Name")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ControlThemeTemplatePart(
                item.Name!,
                item.Element.Name.LocalName,
                GetLineNumber(item.Element)));
    }

    private static IEnumerable<ControlThemeTemplateBinding> FindTemplateBindings(XElement theme)
    {
        foreach (var element in theme.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                foreach (Match match in TemplateBindingRegex.Matches(attribute.Value))
                {
                    yield return new ControlThemeTemplateBinding(
                        match.Groups["property"].Value,
                        GetLineNumber(element) ?? 0,
                        $"{attribute.Name.LocalName}=\"{attribute.Value}\"");
                }
            }
        }
    }

    private static IEnumerable<ControlThemeStateSelector> FindStateSelectors(XElement theme)
    {
        foreach (var style in theme.Descendants().Where(static element => element.Name.LocalName == "Style"))
        {
            var selector = style.Attribute("Selector")?.Value;
            if (string.IsNullOrWhiteSpace(selector))
            {
                continue;
            }

            foreach (Match match in PseudoClassRegex.Matches(selector))
            {
                yield return new ControlThemeStateSelector(
                    match.Groups["state"].Value,
                    selector,
                    GetLineNumber(style));
            }
        }
    }

    private static int? GetLineNumber(XObject value)
    {
        return value is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : null;
    }

    private sealed class ThemeStateComparer : IComparer<string>
    {
        public static ThemeStateComparer Instance { get; } = new();

        private static readonly string[] s_order =
        {
            "normal",
            "pointerover",
            "pressed",
            "disabled",
            "focus",
            "focus-visible",
            "checked",
            "unchecked",
            "indeterminate"
        };

        public int Compare(string? x, string? y)
        {
            var xIndex = Array.IndexOf(s_order, x ?? string.Empty);
            var yIndex = Array.IndexOf(s_order, y ?? string.Empty);
            if (xIndex >= 0 || yIndex >= 0)
            {
                if (xIndex < 0)
                {
                    return 1;
                }

                if (yIndex < 0)
                {
                    return -1;
                }

                return xIndex.CompareTo(yIndex);
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}
