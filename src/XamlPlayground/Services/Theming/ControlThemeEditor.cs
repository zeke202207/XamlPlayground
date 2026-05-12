using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Language.Xml;
using XamlPlayground.Services.Editing;

namespace XamlPlayground.Services.Theming;

public static class ControlThemeEditor
{
    public static ThemeResourceEditResult SetStateSetter(
        string xaml,
        string themeKey,
        string state,
        string propertyName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(themeKey) ||
            string.IsNullOrWhiteSpace(state) ||
            string.Equals(state, "normal", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(propertyName))
        {
            return new ThemeResourceEditResult(false, xaml, "Select a non-normal state and a property.");
        }

        return SetSelectorSetter(
            xaml,
            themeKey,
            $"^:{state.TrimStart(':')}",
            propertyName,
            value);
    }

    public static ThemeResourceEditResult SetSelectorSetter(
        string xaml,
        string themeKey,
        string selector,
        string propertyName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(themeKey) ||
            string.IsNullOrWhiteSpace(selector) ||
            string.IsNullOrWhiteSpace(propertyName))
        {
            return new ThemeResourceEditResult(false, xaml, "Select a selector and a property.");
        }

        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new ThemeResourceEditResult(false, xaml, parseError);
        }

        var theme = FindTheme(document.RootSyntax, themeKey);
        if (theme is null)
        {
            return new ThemeResourceEditResult(false, xaml, $"ControlTheme '{themeKey}' was not found.");
        }

        selector = selector.Trim();
        var style = FindDirectStyle(theme, selector);
        if (style is null)
        {
            var insertedStyle = XamlTextEditor.InsertChild(
                xaml,
                theme,
                CreateStyleText(xaml, theme, selector, CreateSetterText(propertyName, value ?? string.Empty)));
            return new ThemeResourceEditResult(true, insertedStyle);
        }

        var setter = XamlTextEditor.DirectChildElements(style)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "Setter", StringComparison.Ordinal) &&
                string.Equals(XamlTextEditor.GetAttributeValue(element, "Property"), propertyName, StringComparison.Ordinal));
        if (setter is null)
        {
            var insertedSetter = XamlTextEditor.InsertChild(
                xaml,
                style,
                CreateSetterText(propertyName, value ?? string.Empty));
            return new ThemeResourceEditResult(true, insertedSetter);
        }

        string edited;
        if (setter.AsNode is XmlElementSyntax)
        {
            edited = XamlTextEditor.ReplaceElement(
                xaml,
                setter,
                CreateSetterText(propertyName, value ?? string.Empty));
        }
        else
        {
            edited = XamlTextEditor.SetAttributeValue(xaml, setter, "Value", value ?? string.Empty);
        }

        return new ThemeResourceEditResult(true, edited);
    }

    public static ThemeResourceEditResult SetDesignPreview(
        string xaml,
        string previewXaml)
    {
        if (!XamlTextEditor.TryParse(xaml, out var document, out var parseError) ||
            document.RootSyntax is null)
        {
            return new ThemeResourceEditResult(false, xaml, parseError ?? "Resource dictionary has no root element.");
        }

        try
        {
            XElement.Parse(previewXaml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            return new ThemeResourceEditResult(false, xaml, exception.Message);
        }

        var edited = xaml;
        var previews = XamlTextEditor.DirectChildElements(document.RootSyntax)
            .Where(static element => string.Equals(element.NameNode.LocalName, "Design.PreviewWith", StringComparison.Ordinal))
            .OrderByDescending(static element => element.AsNode.Span.Start)
            .ToArray();
        foreach (var preview in previews)
        {
            edited = XamlTextEditor.RemoveElement(edited, preview);
        }

        if (!XamlTextEditor.TryParse(edited, out document, out _) ||
            document.RootSyntax is null)
        {
            return new ThemeResourceEditResult(false, xaml, "Resource dictionary could not be updated.");
        }

        edited = XamlTextEditor.InsertChild(
            edited,
            document.RootSyntax,
            XamlTextEditor.NormalizeBlock(previewXaml),
            insertFirst: true);
        return new ThemeResourceEditResult(true, edited);
    }

    private static IXmlElementSyntax? FindTheme(IXmlElementSyntax root, string themeKey)
    {
        return XamlTextEditor
            .DescendantsAndSelf(root)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "ControlTheme", StringComparison.Ordinal) &&
                string.Equals(
                    XamlTextEditor.GetAttributeValue(element, "x:Key") ??
                    XamlTextEditor.GetAttributeValueByLocalName(element, "Key"),
                    themeKey,
                    StringComparison.Ordinal));
    }

    private static IXmlElementSyntax? FindDirectStyle(IXmlElementSyntax theme, string selector)
    {
        return XamlTextEditor.DirectChildElements(theme)
            .FirstOrDefault(element =>
                string.Equals(element.NameNode.LocalName, "Style", StringComparison.Ordinal) &&
                string.Equals(XamlTextEditor.GetAttributeValue(element, "Selector"), selector, StringComparison.Ordinal));
    }

    private static string CreateStyleText(
        string xaml,
        IXmlElementSyntax context,
        string selector,
        string childXaml)
    {
        var newLine = XamlTextEditor.GetPreferredNewLine(xaml);
        var indentUnit = XamlTextEditor.GetIndentUnit(xaml, context);
        return $"<Style Selector=\"{XamlTextEditor.EscapeAttributeValue(selector)}\">{newLine}" +
               XamlTextEditor.IndentBlock(childXaml, indentUnit, newLine) +
               $"{newLine}</Style>";
    }

    private static string CreateSetterText(string propertyName, string value)
    {
        return $"<Setter Property=\"{XamlTextEditor.EscapeAttributeValue(propertyName)}\" Value=\"{XamlTextEditor.EscapeAttributeValue(value)}\" />";
    }
}
