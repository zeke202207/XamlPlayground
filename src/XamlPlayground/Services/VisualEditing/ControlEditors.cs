using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace XamlPlayground.Services.VisualEditing;

public enum ControlEditorValueKind
{
    String,
    Number,
    Boolean,
    Brush,
    Thickness,
    Enum,
    Command,
    Content,
    Collection
}

public sealed record ControlEditorProperty(
    string PropertyName,
    string DisplayName,
    ControlEditorValueKind ValueKind,
    string? Group = null,
    bool IsAdvanced = false,
    Type? ValueType = null);

public sealed record ControlEditorDescriptor(
    string ControlTypeName,
    string DisplayName,
    IReadOnlyList<ControlEditorProperty> Properties,
    IReadOnlyDictionary<string, string> Metadata);

public interface IControlEditorProvider
{
    int Priority { get; }

    bool CanEdit(Type controlType);

    ControlEditorDescriptor CreateDescriptor(Type controlType);
}

public sealed class ControlEditorRegistry
{
    private readonly List<IControlEditorProvider> _providers = new();

    public ControlEditorRegistry()
    {
        Register(new TextBlockControlEditorProvider());
        Register(new ButtonControlEditorProvider());
        Register(new PanelControlEditorProvider());
        Register(new FallbackControlEditorProvider());
    }

    public IReadOnlyList<IControlEditorProvider> Providers => _providers;

    public void Register(IControlEditorProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        _providers.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
    }

    public ControlEditorDescriptor Resolve(Type controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);

        var provider = _providers.FirstOrDefault(provider => provider.CanEdit(controlType));
        if (provider is null)
        {
            throw new InvalidOperationException($"No visual editor provider is registered for {controlType.FullName}.");
        }

        return provider.CreateDescriptor(controlType);
    }
}

public abstract class ControlEditorProviderBase<TControl> : IControlEditorProvider
    where TControl : Control
{
    public virtual int Priority => 100;

    public bool CanEdit(Type controlType)
    {
        return typeof(TControl).IsAssignableFrom(controlType);
    }

    public abstract ControlEditorDescriptor CreateDescriptor(Type controlType);

    protected static IReadOnlyList<ControlEditorProperty> CommonLayoutProperties()
    {
        return new[]
        {
            new ControlEditorProperty("Name", "Name", ControlEditorValueKind.String, "Identity"),
            new ControlEditorProperty("Classes", "Classes", ControlEditorValueKind.String, "Identity", IsAdvanced: true),
            new ControlEditorProperty("Width", "Width", ControlEditorValueKind.Number, "Layout"),
            new ControlEditorProperty("Height", "Height", ControlEditorValueKind.Number, "Layout"),
            new ControlEditorProperty("MinWidth", "Min width", ControlEditorValueKind.Number, "Layout", IsAdvanced: true),
            new ControlEditorProperty("MinHeight", "Min height", ControlEditorValueKind.Number, "Layout", IsAdvanced: true),
            new ControlEditorProperty("Margin", "Margin", ControlEditorValueKind.Thickness, "Layout"),
            new ControlEditorProperty("HorizontalAlignment", "Horizontal alignment", ControlEditorValueKind.Enum, "Layout"),
            new ControlEditorProperty("VerticalAlignment", "Vertical alignment", ControlEditorValueKind.Enum, "Layout"),
            new ControlEditorProperty("IsVisible", "Visible", ControlEditorValueKind.Boolean, "State")
        };
    }

    protected static ControlEditorDescriptor CreateDescriptor(
        Type controlType,
        string displayName,
        IEnumerable<ControlEditorProperty> properties)
    {
        return new ControlEditorDescriptor(
            controlType.FullName ?? controlType.Name,
            displayName,
            CompleteProperties(controlType, properties),
            new Dictionary<string, string>
            {
                ["provider"] = typeof(TControl).Name
            });
    }

    private static IReadOnlyList<ControlEditorProperty> CompleteProperties(
        Type controlType,
        IEnumerable<ControlEditorProperty> curatedProperties)
    {
        var properties = new Dictionary<string, ControlEditorProperty>(StringComparer.Ordinal);

        foreach (var property in curatedProperties)
        {
            properties[property.PropertyName] = property.ValueType is null
                ? property with { ValueType = ResolveAvaloniaPropertyType(controlType, property.PropertyName) }
                : property;
        }

        foreach (var property in EnumerateEditableAvaloniaProperties(controlType))
        {
            var propertyName = GetXamlPropertyName(controlType, property);
            if (string.IsNullOrWhiteSpace(propertyName) ||
                properties.ContainsKey(propertyName))
            {
                continue;
            }

            properties[propertyName] = new ControlEditorProperty(
                propertyName,
                CreateDisplayName(propertyName),
                ResolveValueKind(property.PropertyType, propertyName),
                ResolveGroup(propertyName, property.PropertyType, property.IsAttached),
                IsAdvancedProperty(propertyName, property.PropertyType),
                property.PropertyType);
        }

        return properties.Values
            .OrderBy(static property => GetGroupSortIndex(property.Group))
            .ThenBy(static property => property.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetGroupSortIndex(string? group)
    {
        return group switch
        {
            "Identity" => 0,
            "Content" => 1,
            "Layout" => 2,
            "Typography" => 3,
            "Brushes" => 4,
            "State" => 5,
            "Behavior" => 6,
            "Input" => 7,
            "Styling" => 8,
            "Transform" => 9,
            "Effects" => 10,
            "Structure" => 11,
            "Data" => 12,
            "Advanced" => 99,
            _ => 50
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The visual designer builds runtime property metadata for editor tooling.")]
    private static Type? ResolveAvaloniaPropertyType(Type controlType, string propertyName)
    {
        return EnumerateEditableAvaloniaProperties(controlType)
            .FirstOrDefault(property => string.Equals(GetXamlPropertyName(controlType, property), propertyName, StringComparison.Ordinal))
            ?.PropertyType;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The visual designer intentionally reflects Avalonia property fields for design-time metadata.")]
    private static IEnumerable<AvaloniaProperty> EnumerateEditableAvaloniaProperties(Type controlType)
    {
        var properties = new Dictionary<string, AvaloniaProperty>(StringComparer.Ordinal);

        foreach (var property in EnumerateRegistryProperties(controlType)
                     .Concat(EnumeratePropertyFields(controlType, includeInherited: true))
                     .Concat(EnumerateCommonAttachedProperties()))
        {
            if (property.IsReadOnly)
            {
                continue;
            }

            var name = GetXamlPropertyName(controlType, property);
            if (!string.IsNullOrWhiteSpace(name))
            {
                properties.TryAdd(name, property);
            }
        }

        return properties.Values;
    }

    private static IEnumerable<AvaloniaProperty> EnumerateRegistryProperties(Type controlType)
    {
        var registry = AvaloniaPropertyRegistry.Instance;
        return registry.GetRegistered(controlType)
            .Concat(registry.GetRegisteredInherited(controlType))
            .Concat(registry.GetRegisteredDirect(controlType))
            .Concat(registry.GetRegisteredAttached(controlType));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The visual designer intentionally reflects Avalonia property fields for design-time metadata.")]
    private static IEnumerable<AvaloniaProperty> EnumeratePropertyFields(Type type, bool includeInherited)
    {
        var flags = BindingFlags.Public | BindingFlags.Static;
        if (includeInherited)
        {
            flags |= BindingFlags.FlattenHierarchy;
        }

        foreach (var field in type.GetFields(flags))
        {
            if (typeof(AvaloniaProperty).IsAssignableFrom(field.FieldType) &&
                field.GetValue(null) is AvaloniaProperty property)
            {
                yield return property;
            }
        }
    }

    private static IEnumerable<AvaloniaProperty> EnumerateCommonAttachedProperties()
    {
        return EnumeratePropertyFields(typeof(Grid), includeInherited: false)
            .Concat(EnumeratePropertyFields(typeof(Canvas), includeInherited: false))
            .Concat(EnumeratePropertyFields(typeof(DockPanel), includeInherited: false))
            .Concat(EnumeratePropertyFields(typeof(ToolTip), includeInherited: false))
            .Where(static property => property.IsAttached);
    }

    private static string GetXamlPropertyName(Type controlType, AvaloniaProperty property)
    {
        if (property.IsAttached &&
            !property.OwnerType.IsAssignableFrom(controlType))
        {
            return $"{property.OwnerType.Name}.{property.Name}";
        }

        return property.Name;
    }

    private static ControlEditorValueKind ResolveValueKind(Type propertyType, string propertyName)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(bool))
        {
            return ControlEditorValueKind.Boolean;
        }

        if (type.IsEnum)
        {
            return ControlEditorValueKind.Enum;
        }

        if (IsNumericType(type))
        {
            return ControlEditorValueKind.Number;
        }

        if (type == typeof(Thickness) ||
            type.Name == "CornerRadius")
        {
            return ControlEditorValueKind.Thickness;
        }

        if (typeof(IBrush).IsAssignableFrom(type) ||
            type.Name.Contains("Brush", StringComparison.Ordinal))
        {
            return ControlEditorValueKind.Brush;
        }

        if (typeof(ICommand).IsAssignableFrom(type) ||
            propertyName.Contains("Command", StringComparison.Ordinal))
        {
            return ControlEditorValueKind.Command;
        }

        if (typeof(Control).IsAssignableFrom(type) ||
            propertyName.EndsWith("Content", StringComparison.Ordinal) ||
            string.Equals(propertyName, "ToolTip.Tip", StringComparison.Ordinal) ||
            type == typeof(object))
        {
            return ControlEditorValueKind.Content;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) &&
            type != typeof(string))
        {
            return ControlEditorValueKind.Collection;
        }

        return ControlEditorValueKind.String;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    private static string ResolveGroup(string propertyName, Type propertyType, bool isAttached)
    {
        var localName = GetLocalPropertyName(propertyName);

        if (string.Equals(localName, "Name", StringComparison.Ordinal) ||
            string.Equals(localName, "Classes", StringComparison.Ordinal) ||
            string.Equals(localName, "Tag", StringComparison.Ordinal) ||
            propertyName.StartsWith("AutomationProperties.", StringComparison.Ordinal))
        {
            return "Identity";
        }

        if (string.Equals(localName, "DataContext", StringComparison.Ordinal) ||
            string.Equals(localName, "ItemsSource", StringComparison.Ordinal) ||
            string.Equals(localName, "SelectedItem", StringComparison.Ordinal) ||
            localName.EndsWith("Template", StringComparison.Ordinal))
        {
            return "Data";
        }

        if (string.Equals(localName, "Content", StringComparison.Ordinal) ||
            string.Equals(localName, "Header", StringComparison.Ordinal) ||
            string.Equals(localName, "Text", StringComparison.Ordinal) ||
            localName.Contains("Placeholder", StringComparison.Ordinal) ||
            string.Equals(propertyName, "ToolTip.Tip", StringComparison.Ordinal))
        {
            return "Content";
        }

        if (localName.StartsWith("Font", StringComparison.Ordinal) ||
            localName.StartsWith("Line", StringComparison.Ordinal))
        {
            return "Typography";
        }

        if (localName.Contains("Brush", StringComparison.Ordinal) ||
            localName.Contains("Foreground", StringComparison.Ordinal) ||
            localName.Contains("Background", StringComparison.Ordinal) ||
            localName is "Fill" or "Stroke" or "CaretBrush" or "SelectionBrush")
        {
            return "Brushes";
        }

        if (localName.Contains("Transform", StringComparison.Ordinal) ||
            localName.Contains("Origin", StringComparison.Ordinal))
        {
            return "Transform";
        }

        if (localName is "Opacity" or "Clip" or "ClipToBounds" or "Effect" or "ZIndex")
        {
            return "Effects";
        }

        if (localName.StartsWith("Is", StringComparison.Ordinal) ||
            localName.EndsWith("State", StringComparison.Ordinal) ||
            localName.EndsWith("Visible", StringComparison.Ordinal) ||
            localName.EndsWith("Enabled", StringComparison.Ordinal))
        {
            return "State";
        }

        if (localName.Contains("Command", StringComparison.Ordinal) ||
            localName.Contains("Click", StringComparison.Ordinal) ||
            localName.Contains("Selection", StringComparison.Ordinal))
        {
            return "Behavior";
        }

        if (propertyType == typeof(KeyGesture) ||
            localName.Contains("Gesture", StringComparison.Ordinal) ||
            localName.Contains("Focus", StringComparison.Ordinal) ||
            localName.Contains("Cursor", StringComparison.Ordinal) ||
            localName.Contains("Pointer", StringComparison.Ordinal) ||
            localName.Contains("Tab", StringComparison.Ordinal))
        {
            return "Input";
        }

        if (localName.Contains("Style", StringComparison.Ordinal) ||
            localName.Contains("Theme", StringComparison.Ordinal) ||
            localName.Contains("Resource", StringComparison.Ordinal))
        {
            return "Styling";
        }

        if (isAttached ||
            localName is "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight" or
                "Margin" or "Padding" or "HorizontalAlignment" or "VerticalAlignment" or
                "Row" or "Column" or "RowSpan" or "ColumnSpan" or "Dock" or
                "Left" or "Top" or "Right" or "Bottom" or "Bounds" or "DesiredSize")
        {
            return "Layout";
        }

        if (localName is "Children" or "Items")
        {
            return "Structure";
        }

        return "Advanced";
    }

    private static bool IsAdvancedProperty(string propertyName, Type propertyType)
    {
        return ResolveGroup(propertyName, propertyType, propertyName.Contains('.', StringComparison.Ordinal)) == "Advanced";
    }

    private static string GetLocalPropertyName(string propertyName)
    {
        var index = propertyName.LastIndexOf('.');
        return index < 0 ? propertyName : propertyName[(index + 1)..];
    }

    private static string CreateDisplayName(string propertyName)
    {
        var builder = new StringBuilder(propertyName.Length + 8);

        for (var i = 0; i < propertyName.Length; i++)
        {
            var current = propertyName[i];
            if (i > 0 &&
                current != '.' &&
                char.IsUpper(current) &&
                (char.IsLower(propertyName[i - 1]) ||
                 (i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]))))
            {
                builder.Append(' ');
            }

            if (current == '.')
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(i == 0 ? current : char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}

public sealed class TextBlockControlEditorProvider : ControlEditorProviderBase<TextBlock>
{
    public override int Priority => 500;

    public override ControlEditorDescriptor CreateDescriptor(Type controlType)
    {
        return CreateDescriptor(
            controlType,
            "Text",
            new[]
            {
                new ControlEditorProperty("Text", "Text", ControlEditorValueKind.String, "Content"),
                new ControlEditorProperty("FontSize", "Font size", ControlEditorValueKind.Number, "Typography"),
                new ControlEditorProperty("FontWeight", "Font weight", ControlEditorValueKind.Enum, "Typography"),
                new ControlEditorProperty("Foreground", "Foreground", ControlEditorValueKind.Brush, "Brushes"),
                new ControlEditorProperty("TextWrapping", "Wrapping", ControlEditorValueKind.Enum, "Text")
            }.Concat(CommonLayoutProperties()));
    }
}

public sealed class ButtonControlEditorProvider : ControlEditorProviderBase<Button>
{
    public override int Priority => 500;

    public override ControlEditorDescriptor CreateDescriptor(Type controlType)
    {
        return CreateDescriptor(
            controlType,
            "Button",
            new[]
            {
                new ControlEditorProperty("Content", "Content", ControlEditorValueKind.Content, "Content"),
                new ControlEditorProperty("Command", "Command", ControlEditorValueKind.Command, "Behavior"),
                new ControlEditorProperty("CommandParameter", "Command parameter", ControlEditorValueKind.String, "Behavior", IsAdvanced: true),
                new ControlEditorProperty("IsEnabled", "Enabled", ControlEditorValueKind.Boolean, "State")
            }.Concat(CommonLayoutProperties()));
    }
}

public sealed class PanelControlEditorProvider : ControlEditorProviderBase<Panel>
{
    public override int Priority => 300;

    public override ControlEditorDescriptor CreateDescriptor(Type controlType)
    {
        return CreateDescriptor(
            controlType,
            "Panel",
            new[]
            {
                new ControlEditorProperty("Children", "Children", ControlEditorValueKind.Collection, "Structure"),
                new ControlEditorProperty("ClipToBounds", "Clip to bounds", ControlEditorValueKind.Boolean, "Layout")
            }.Concat(CommonLayoutProperties()));
    }
}

public sealed class FallbackControlEditorProvider : ControlEditorProviderBase<Control>
{
    public override int Priority => 0;

    public override ControlEditorDescriptor CreateDescriptor(Type controlType)
    {
        return CreateDescriptor(controlType, controlType.Name, CommonLayoutProperties());
    }
}
