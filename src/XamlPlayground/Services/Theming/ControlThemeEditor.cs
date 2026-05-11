using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace XamlPlayground.Services.Theming;

public static class ControlThemeEditor
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

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

        var parse = TryParse(xaml);
        if (parse.Error is not null)
        {
            return new ThemeResourceEditResult(false, xaml, parse.Error);
        }

        var theme = FindTheme(parse.Document, themeKey);
        if (theme is null)
        {
            return new ThemeResourceEditResult(false, xaml, $"ControlTheme '{themeKey}' was not found.");
        }

        selector = selector.Trim();
        var style = theme
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Style" &&
                string.Equals(element.Attribute("Selector")?.Value, selector, StringComparison.Ordinal));
        if (style is null)
        {
            var ns = theme.GetDefaultNamespace();
            style = new XElement(
                ns + "Style",
                new XAttribute("Selector", selector));
            theme.Add(new XText(Environment.NewLine), style);
        }

        var setter = style
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Setter" &&
                string.Equals(element.Attribute("Property")?.Value, propertyName, StringComparison.Ordinal));
        if (setter is null)
        {
            var ns = style.GetDefaultNamespace();
            setter = new XElement(
                ns + "Setter",
                new XAttribute("Property", propertyName),
                new XAttribute("Value", value ?? string.Empty));
            style.Add(new XText(Environment.NewLine), setter);
        }
        else
        {
            setter.SetAttributeValue("Value", value ?? string.Empty);
        }

        return new ThemeResourceEditResult(true, Serialize(parse.Document));
    }

    public static ThemeResourceEditResult SetDesignPreview(
        string xaml,
        string previewXaml)
    {
        var parse = TryParse(xaml);
        if (parse.Error is not null)
        {
            return new ThemeResourceEditResult(false, xaml, parse.Error);
        }

        if (parse.Document.Root is null)
        {
            return new ThemeResourceEditResult(false, xaml, "Resource dictionary has no root element.");
        }

        XElement preview;
        try
        {
            preview = XElement.Parse(previewXaml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            return new ThemeResourceEditResult(false, xaml, exception.Message);
        }

        parse.Document.Root
            .Elements()
            .Where(static element => element.Name.LocalName == "Design.PreviewWith")
            .Remove();
        parse.Document.Root.AddFirst(new XText(Environment.NewLine), preview);
        return new ThemeResourceEditResult(true, Serialize(parse.Document));
    }

    private static XElement? FindTheme(XDocument document, string themeKey)
    {
        return document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ControlTheme" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, themeKey, StringComparison.Ordinal));
    }

    private static (XDocument Document, string? Error) TryParse(string xaml)
    {
        try
        {
            return (XDocument.Parse(xaml, LoadOptions.PreserveWhitespace), null);
        }
        catch (XmlException exception)
        {
            return (new XDocument(), exception.Message);
        }
    }

    private static string Serialize(XDocument document)
    {
        return document.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
    }
}
