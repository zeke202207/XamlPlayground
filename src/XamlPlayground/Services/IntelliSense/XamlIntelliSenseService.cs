using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;

namespace XamlPlayground.Services.IntelliSense;

public sealed partial class XamlIntelliSenseService : IEditorIntelliSenseService
{
    private const string AvaloniaXmlns = "https://github.com/avaloniaui";
    private const string XamlXmlns = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly object s_catalogLock = new();
    private static readonly HashSet<string> s_workspaceAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
    private static XamlTypeCatalog? s_catalog;

    private static readonly string[] s_commonColorValues =
    [
        "Transparent", "Black", "White", "Red", "Green", "Blue", "Yellow", "Orange", "Purple",
        "Gray", "LightGray", "DarkGray", "WhiteSmoke", "DodgerBlue", "CornflowerBlue"
    ];

    public Task<EditorCompletionResult?> GetCompletionsAsync(
        string text,
        int position,
        bool explicitInvocation,
        char? triggerCharacter,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        if (!explicitInvocation && triggerCharacter is { } trigger && !IsAutomaticTrigger(trigger))
        {
            return Task.FromResult<EditorCompletionResult?>(null);
        }

        var context = XamlCompletionContext.Create(text, position);
        if (context is null)
        {
            return Task.FromResult<EditorCompletionResult?>(null);
        }

        var result = context.Kind switch
        {
            XamlCompletionContextKind.ClosingTag => GetClosingTagCompletions(text, position, context),
            XamlCompletionContextKind.ElementName => GetElementNameCompletions(text, position, context),
            XamlCompletionContextKind.AttributeName => GetAttributeNameCompletions(text, context),
            XamlCompletionContextKind.AttributeValue => GetAttributeValueCompletions(text, context),
            _ => null
        };

        return Task.FromResult(result);
    }

    public Task<EditorQuickInfo?> GetQuickInfoAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        var wordSpan = GetXmlNameSpan(text, position);
        if (wordSpan.Start == wordSpan.End)
        {
            return Task.FromResult<EditorQuickInfo?>(null);
        }

        var token = text[wordSpan.Start..wordSpan.End];
        var context = XamlCompletionContext.Create(text, position);
        if (context is null)
        {
            return Task.FromResult<EditorQuickInfo?>(null);
        }

        var namespaces = GetXmlNamespaces(text);
        var catalog = GetCatalog();

        if (context.Kind is XamlCompletionContextKind.ElementName or XamlCompletionContextKind.ClosingTag &&
            ResolveType(token, namespaces, catalog) is { } type)
        {
            return Task.FromResult<EditorQuickInfo?>(new EditorQuickInfo(
                wordSpan.Start,
                wordSpan.End,
                $"{type.Type.FullName}{Environment.NewLine}{type.Type.Assembly.GetName().Name}"));
        }

        var ownerElementName = context.TagName ?? GetOpenElementStack(text, position).LastOrDefault();
        if (ownerElementName is not null &&
            ResolveType(ownerElementName, namespaces, catalog) is { } ownerType &&
            ResolveMember(ownerType.Type, token, catalog) is { } member)
        {
            return Task.FromResult<EditorQuickInfo?>(new EditorQuickInfo(
                wordSpan.Start,
                wordSpan.End,
                member.Description));
        }

        return Task.FromResult<EditorQuickInfo?>(null);
    }

    public Task<EditorSignatureHelp?> GetSignatureHelpAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<EditorSignatureHelp?>(null);
    }

    public Task<IReadOnlyList<EditorDiagnostic>> GetDiagnosticsAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<EditorDiagnostic>();
        try
        {
            _ = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            diagnostics.Add(CreateXmlExceptionDiagnostic(text, exception));
            return Task.FromResult<IReadOnlyList<EditorDiagnostic>>(diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new EditorDiagnostic(
                0,
                Math.Min(1, text.Length),
                exception.Message,
                EditorDiagnosticSeverity.Error));
            return Task.FromResult<IReadOnlyList<EditorDiagnostic>>(diagnostics);
        }

        var catalog = GetCatalog();
        var namespaces = GetXmlNamespaces(text);
        foreach (Match match in ElementRegex().Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (match.Groups["closing"].Success)
            {
                continue;
            }

            var tagName = match.Groups["name"].Value;
            if (IsPropertyElement(tagName))
            {
                continue;
            }

            var resolvedType = ResolveType(tagName, namespaces, catalog);
            if (resolvedType is null)
            {
                diagnostics.Add(new EditorDiagnostic(
                    match.Groups["name"].Index,
                    match.Groups["name"].Index + tagName.Length,
                    $"Unknown XAML type '{tagName}'.",
                    EditorDiagnosticSeverity.Warning,
                    "XAML1001"));
                continue;
            }

            var rest = match.Groups["rest"];
            foreach (Match attributeMatch in AttributeRegex().Matches(rest.Value))
            {
                var attributeName = attributeMatch.Groups["name"].Value;
                if (ShouldSkipAttributeDiagnostic(attributeName))
                {
                    continue;
                }

                if (ResolveMember(resolvedType.Type, attributeName, catalog) is null)
                {
                    var start = rest.Index + attributeMatch.Groups["name"].Index;
                    diagnostics.Add(new EditorDiagnostic(
                        start,
                        start + attributeName.Length,
                        $"Unknown member '{attributeName}' on '{tagName}'.",
                        EditorDiagnosticSeverity.Warning,
                        "XAML1002"));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<EditorDiagnostic>>(diagnostics);
    }

    public Task<EditorLocation?> GetDefinitionAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        position = ClampPosition(text, position);
        var token = GetXmlNameSpan(text, position);
        if (token.Start == token.End)
        {
            return Task.FromResult<EditorLocation?>(null);
        }

        var tokenText = text[token.Start..token.End];
        var key = TryReadResourceKeyAt(text, token.Start, token.End);
        if (key is not null && FindResourceDefinition(text, key) is { } definition)
        {
            return Task.FromResult<EditorLocation?>(definition);
        }

        var context = XamlCompletionContext.Create(text, position);
        if (context is null)
        {
            return Task.FromResult<EditorLocation?>(null);
        }

        var namespaces = GetXmlNamespaces(text);
        var catalog = GetCatalog();
        if (context.Kind is XamlCompletionContextKind.ElementName or XamlCompletionContextKind.ClosingTag &&
            ResolveType(tokenText, namespaces, catalog) is { } type)
        {
            return Task.FromResult<EditorLocation?>(CreateMetadataLocation(type.Type.FullName ?? type.Name));
        }

        var ownerElementName = context.TagName ?? GetOpenElementStack(text, position).LastOrDefault();
        if (ownerElementName is not null &&
            ResolveType(ownerElementName, namespaces, catalog) is { } ownerType &&
            ResolveMember(ownerType.Type, tokenText, catalog) is { } member)
        {
            return Task.FromResult<EditorLocation?>(CreateMetadataLocation(member.Description));
        }

        return Task.FromResult<EditorLocation?>(null);
    }

    public Task<IReadOnlyList<EditorReference>> GetReferencesAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        position = ClampPosition(text, position);
        var token = GetXmlNameSpan(text, position);
        if (token.Start == token.End)
        {
            return Task.FromResult<IReadOnlyList<EditorReference>>([]);
        }

        var key = TryReadResourceKeyAt(text, token.Start, token.End);
        if (key is null)
        {
            return Task.FromResult<IReadOnlyList<EditorReference>>([]);
        }

        var references = new List<EditorReference>();
        if (FindResourceDefinition(text, key) is { } definition)
        {
            references.Add(new EditorReference(definition, true));
        }

        foreach (Match match in ResourceReferenceRegex().Matches(text))
        {
            var value = match.Groups["key"].Value;
            if (!string.Equals(value, key, StringComparison.Ordinal))
            {
                continue;
            }

            var start = match.Groups["key"].Index;
            references.Add(new EditorReference(
                CreateLocation(text, start, start + value.Length, filePath: null),
                false));
        }

        return Task.FromResult<IReadOnlyList<EditorReference>>(references
            .OrderBy(static reference => reference.Location.StartOffset)
            .ToArray());
    }

    public Task<string?> FormatDocumentAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var formatted = XDocument.Parse(text, LoadOptions.PreserveWhitespace).ToString(SaveOptions.None);
            return Task.FromResult<string?>(formatted);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task<IReadOnlyList<EditorDocumentSymbol>> GetDocumentSymbolsAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbols = ElementRegex()
            .Matches(text)
            .Where(static match => !match.Groups["closing"].Success)
            .Select(match => CreateDocumentSymbol(text, match))
            .Where(static symbol => symbol is not null)
            .Cast<EditorDocumentSymbol>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<EditorDocumentSymbol>>(symbols);
    }

    public static void RegisterWorkspaceAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (s_catalogLock)
        {
            var changed = false;
            foreach (var assembly in assemblies.Where(static assembly => !assembly.IsDynamic))
            {
                var name = assembly.GetName().Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    s_workspaceAssemblyNames.Add(name);
                    changed = true;
                }
            }

            if (changed)
            {
                s_catalog = null;
            }
        }
    }

    private static EditorDiagnostic CreateXmlExceptionDiagnostic(string text, XmlException exception)
    {
        var start = GetOffsetFromLineColumn(text, exception.LineNumber, exception.LinePosition);
        var end = start < text.Length ? start + 1 : start;
        return new EditorDiagnostic(
            start,
            end,
            exception.Message,
            EditorDiagnosticSeverity.Error,
            "XML");
    }

    private static bool IsPropertyElement(string tagName)
    {
        return GetLocalName(tagName).Contains('.', StringComparison.Ordinal);
    }

    private static bool ShouldSkipAttributeDiagnostic(string attributeName)
    {
        return attributeName.StartsWith("xmlns", StringComparison.Ordinal) ||
               attributeName.StartsWith("x:", StringComparison.Ordinal) ||
               attributeName.StartsWith("xml:", StringComparison.Ordinal) ||
               attributeName.StartsWith("d:", StringComparison.Ordinal) ||
               attributeName.StartsWith("mc:", StringComparison.Ordinal);
    }

    private static string? TryReadResourceKeyAt(string text, int startOffset, int endOffset)
    {
        foreach (Match match in ResourceReferenceRegex().Matches(text))
        {
            var group = match.Groups["key"];
            if (startOffset >= group.Index && endOffset <= group.Index + group.Length)
            {
                return group.Value;
            }
        }

        foreach (Match match in ResourceDefinitionRegex().Matches(text))
        {
            var group = match.Groups["key"];
            if (startOffset >= group.Index && endOffset <= group.Index + group.Length)
            {
                return group.Value;
            }
        }

        return null;
    }

    private static EditorLocation? FindResourceDefinition(string text, string key)
    {
        foreach (Match match in ResourceDefinitionRegex().Matches(text))
        {
            var group = match.Groups["key"];
            if (!string.Equals(group.Value, key, StringComparison.Ordinal))
            {
                continue;
            }

            return CreateLocation(text, group.Index, group.Index + group.Length, filePath: null);
        }

        return null;
    }

    private static EditorLocation CreateMetadataLocation(string previewText)
    {
        return new EditorLocation(
            null,
            0,
            0,
            1,
            1,
            previewText);
    }

    private static EditorLocation CreateLocation(string text, int start, int end, string? filePath)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var (line, column) = GetLineColumn(text, start);
        return new EditorLocation(
            filePath,
            start,
            end,
            line,
            column,
            GetLinePreview(text, start));
    }

    private static EditorDocumentSymbol? CreateDocumentSymbol(string text, Match match)
    {
        var name = match.Groups["name"].Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var rest = match.Groups["rest"].Value;
        var detail = string.Empty;
        foreach (Match attribute in AttributeRegex().Matches(rest))
        {
            var attributeName = attribute.Groups["name"].Value;
            if (attributeName is "Name" or "x:Name" or "x:Key")
            {
                detail = attribute.Groups["value"].Value;
                break;
            }
        }

        var start = match.Groups["name"].Index;
        var end = start + name.Length;
        return new EditorDocumentSymbol(
            string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}",
            detail,
            EditorCompletionKind.Type,
            match.Index,
            match.Index + match.Length,
            start,
            end,
            []);
    }

    private static int GetOffsetFromLineColumn(string text, int lineNumber, int column)
    {
        if (lineNumber <= 1)
        {
            return Math.Clamp(Math.Max(0, column - 1), 0, text.Length);
        }

        var line = 1;
        var offset = 0;
        while (offset < text.Length && line < lineNumber)
        {
            if (text[offset] == '\n')
            {
                line++;
            }

            offset++;
        }

        return Math.Clamp(offset + Math.Max(0, column - 1), 0, text.Length);
    }

    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static string GetLinePreview(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var searchStart = offset - 1;
        var lineStart = searchStart >= 0
            ? text.LastIndexOf('\n', searchStart)
            : -1;
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', offset);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        return text[lineStart..lineEnd].Trim();
    }

    private static XamlTypeCatalog GetCatalog()
    {
        lock (s_catalogLock)
        {
            return s_catalog ??= BuildCatalog();
        }
    }

    private static EditorCompletionResult? GetClosingTagCompletions(
        string text,
        int position,
        XamlCompletionContext context)
    {
        var stack = GetOpenElementStack(text, position);
        var items = stack
            .Reverse()
            .Take(4)
            .Select(name => new EditorCompletionItem(
                name,
                name,
                $"Close {name}",
                EditorCompletionKind.Value,
                20))
            .ToArray();

        return items.Length == 0
            ? null
            : new EditorCompletionResult(context.ReplacementStart, context.ReplacementEnd, items);
    }

    private static EditorCompletionResult? GetElementNameCompletions(
        string text,
        int position,
        XamlCompletionContext context)
    {
        var catalog = GetCatalog();
        var namespaces = GetXmlNamespaces(text);
        var prefix = GetPrefix(context.CurrentToken);
        var xmlNamespace = namespaces.GetValueOrDefault(prefix);
        var parentName = GetOpenElementStack(text, position).LastOrDefault();
        var items = new List<EditorCompletionItem>();

        if (parentName is not null &&
            ResolveType(parentName, namespaces, catalog) is { } parentType &&
            string.IsNullOrEmpty(prefix))
        {
            items.AddRange(GetPropertyElementCompletions(parentType.Type));
        }

        items.AddRange(catalog.Types
            .Where(type => IsTypeInXmlNamespace(type, prefix, xmlNamespace))
            .Select(type => new EditorCompletionItem(
                GetQualifiedName(prefix, type.Name),
                GetQualifiedName(prefix, type.Name),
                $"{type.Type.FullName}{Environment.NewLine}{type.Type.Assembly.GetName().Name}",
                EditorCompletionKind.Type,
                10)));

        var distinct = items
            .GroupBy(item => item.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 0
            ? null
            : new EditorCompletionResult(context.ReplacementStart, context.ReplacementEnd, distinct);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML metadata remains available for IntelliSense.")]
    private static EditorCompletionResult? GetAttributeNameCompletions(
        string text,
        XamlCompletionContext context)
    {
        var catalog = GetCatalog();
        var namespaces = GetXmlNamespaces(text);
        if (context.TagName is null || ResolveType(context.TagName, namespaces, catalog) is not { } ownerType)
        {
            return null;
        }

        var items = new List<EditorCompletionItem>
        {
            CreateAttributeCompletion("Name", "Avalonia control name", EditorCompletionKind.Property, 30),
            CreateAttributeCompletion("Classes", "Space-separated style classes", EditorCompletionKind.Property, 28),
            CreateAttributeCompletion("x:Name", "XAML name directive", EditorCompletionKind.Property, 26),
            CreateAttributeCompletion("x:Key", "XAML resource key directive", EditorCompletionKind.Property, 22),
            CreateAttributeCompletion("x:DataType", "Compiled binding data type directive", EditorCompletionKind.Property, 20)
        };

        items.AddRange(ownerType.Type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.SetMethod is not null)
            .Select(property => CreateAttributeCompletion(
                property.Name,
                $"{property.PropertyType.Name} {ownerType.Name}.{property.Name}",
                EditorCompletionKind.Property,
                18)));

        items.AddRange(ownerType.Type
            .GetEvents(BindingFlags.Public | BindingFlags.Instance)
            .Select(evt => CreateAttributeCompletion(
                evt.Name,
                $"{evt.EventHandlerType?.Name ?? "event"} {ownerType.Name}.{evt.Name}",
                EditorCompletionKind.Event,
                16)));

        items.AddRange(catalog.AttachedProperties
            .Select(property => CreateAttributeCompletion(
                property.Name,
                property.Description,
                EditorCompletionKind.Property,
                8)));

        var distinct = items
            .GroupBy(item => item.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EditorCompletionResult(context.ReplacementStart, context.ReplacementEnd, distinct);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Browser publish roots Avalonia and playground assemblies so reflected XAML property metadata remains available for IntelliSense.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Browser publish roots Avalonia and playground assemblies so reflected XAML property metadata remains available for IntelliSense.")]
    private static EditorCompletionResult? GetAttributeValueCompletions(
        string text,
        XamlCompletionContext context)
    {
        if (context.AttributeName is null || context.TagName is null)
        {
            return null;
        }

        var catalog = GetCatalog();
        var namespaces = GetXmlNamespaces(text);
        if (ResolveType(context.TagName, namespaces, catalog) is not { } ownerType)
        {
            return null;
        }

        var member = ResolveMember(ownerType.Type, context.AttributeName, catalog);
        var valueType = member?.ValueType;
        var items = new List<EditorCompletionItem>
        {
            new("{Binding}", "{Binding }", "Binding markup extension", EditorCompletionKind.Snippet, 30, 9),
            new("{CompiledBinding}", "{CompiledBinding }", "CompiledBinding markup extension", EditorCompletionKind.Snippet, 29, 17),
            new("{DynamicResource}", "{DynamicResource }", "Dynamic resource lookup", EditorCompletionKind.Snippet, 28, 17),
            new("{StaticResource}", "{StaticResource }", "Static resource lookup", EditorCompletionKind.Snippet, 27, 16)
        };

        if (valueType is not null)
        {
            var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (underlyingType == typeof(bool))
            {
                items.Add(new EditorCompletionItem("True", "True", "Boolean true", EditorCompletionKind.Value, 25));
                items.Add(new EditorCompletionItem("False", "False", "Boolean false", EditorCompletionKind.Value, 25));
            }
            else if (underlyingType.IsEnum)
            {
                items.AddRange(Enum.GetNames(underlyingType)
                    .Select(value => new EditorCompletionItem(
                        value,
                        value,
                        $"{underlyingType.Name}.{value}",
                        EditorCompletionKind.EnumValue,
                        25)));
            }
            else if (typeof(IBrush).IsAssignableFrom(underlyingType) || underlyingType == typeof(Color))
            {
                items.AddRange(s_commonColorValues.Select(value => new EditorCompletionItem(
                    value,
                    value,
                    "Color value",
                    EditorCompletionKind.Value,
                    20)));
            }
            else if (underlyingType == typeof(Thickness) || underlyingType.Name is "CornerRadius")
            {
                items.Add(new EditorCompletionItem("0", "0", underlyingType.Name, EditorCompletionKind.Value, 20));
                items.Add(new EditorCompletionItem("4", "4", underlyingType.Name, EditorCompletionKind.Value, 20));
                items.Add(new EditorCompletionItem("8", "8", underlyingType.Name, EditorCompletionKind.Value, 20));
                items.Add(new EditorCompletionItem("12", "12", underlyingType.Name, EditorCompletionKind.Value, 20));
            }
        }

        var distinct = items
            .GroupBy(item => item.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 0
            ? null
            : new EditorCompletionResult(context.ReplacementStart, context.ReplacementEnd, distinct);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML property metadata remains available for IntelliSense.")]
    private static IEnumerable<EditorCompletionItem> GetPropertyElementCompletions(Type parentType)
    {
        foreach (var property in parentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.GetIndexParameters().Length == 0 && property.SetMethod is not null)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var text = $"{GetXamlTypeName(parentType)}.{property.Name}";
            yield return new EditorCompletionItem(
                text,
                text,
                $"{property.PropertyType.Name} property element",
                EditorCompletionKind.Property,
                12);
        }
    }

    private static EditorCompletionItem CreateAttributeCompletion(
        string name,
        string description,
        EditorCompletionKind kind,
        double priority)
    {
        var insertion = $"{name}=\"\"";
        return new EditorCompletionItem(name, insertion, description, kind, priority, name.Length + 2);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML member metadata remains available for IntelliSense.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML member metadata remains available for IntelliSense.")]
    private static XamlMemberInfo? ResolveMember(Type ownerType, string memberName, XamlTypeCatalog catalog)
    {
        if (TryResolveDirective(memberName, out var directive))
        {
            return directive;
        }

        if (memberName.Contains('.', StringComparison.Ordinal))
        {
            var attached = catalog.AttachedProperties.FirstOrDefault(property =>
                string.Equals(property.Name, memberName, StringComparison.Ordinal));
            if (attached is not null)
            {
                return new XamlMemberInfo(attached.Name, attached.ValueType, attached.Description);
            }
        }

        var property = ownerType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is not null)
        {
            return new XamlMemberInfo(
                property.Name,
                property.PropertyType,
                $"{property.PropertyType.FullName}{Environment.NewLine}{ownerType.FullName}.{property.Name}");
        }

        var evt = ownerType.GetEvent(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (evt is not null)
        {
            return new XamlMemberInfo(
                evt.Name,
                evt.EventHandlerType,
                $"{evt.EventHandlerType?.FullName ?? "event"}{Environment.NewLine}{ownerType.FullName}.{evt.Name}");
        }

        return null;
    }

    private static bool TryResolveDirective(string memberName, out XamlMemberInfo member)
    {
        memberName = memberName.Trim();
        member = memberName switch
        {
            "Name" or "x:Name" => new XamlMemberInfo(memberName, typeof(string), "XAML element name"),
            "x:Key" => new XamlMemberInfo(memberName, typeof(object), "XAML resource key"),
            "x:Class" => new XamlMemberInfo(memberName, typeof(string), "XAML backing class"),
            "x:DataType" => new XamlMemberInfo(memberName, typeof(Type), "Compiled binding data type"),
            "Classes" => new XamlMemberInfo(memberName, typeof(string), "Avalonia style classes"),
            _ => null!
        };

        return member is not null;
    }

    private static XamlTypeInfo? ResolveType(
        string xmlName,
        IReadOnlyDictionary<string, string> namespaces,
        XamlTypeCatalog catalog)
    {
        var prefix = GetPrefix(xmlName);
        var localName = GetLocalName(xmlName);
        var xmlNamespace = namespaces.GetValueOrDefault(prefix);
        return catalog.Types.FirstOrDefault(type =>
            string.Equals(type.Name, localName, StringComparison.Ordinal) &&
            IsTypeInXmlNamespace(type, prefix, xmlNamespace));
    }

    private static bool IsTypeInXmlNamespace(XamlTypeInfo type, string prefix, string? xmlNamespace)
    {
        if (string.Equals(prefix, "x", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(xmlNamespace) || string.Equals(xmlNamespace, AvaloniaXmlns, StringComparison.Ordinal))
        {
            return type.IsAvaloniaNamespace;
        }

        if (xmlNamespace.StartsWith("using:", StringComparison.Ordinal))
        {
            return string.Equals(type.Namespace, xmlNamespace["using:".Length..], StringComparison.Ordinal);
        }

        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var clrNamespace = xmlNamespace["clr-namespace:".Length..];
            var assemblyName = default(string);
            var separator = clrNamespace.IndexOf(";assembly=", StringComparison.Ordinal);
            if (separator >= 0)
            {
                assemblyName = clrNamespace[(separator + ";assembly=".Length)..];
                clrNamespace = clrNamespace[..separator];
            }

            return string.Equals(type.Namespace, clrNamespace, StringComparison.Ordinal) &&
                   (assemblyName is null || string.Equals(type.AssemblyName, assemblyName, StringComparison.Ordinal));
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> GetXmlNamespaces(string text)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [""] = AvaloniaXmlns,
            ["x"] = XamlXmlns
        };

        foreach (Match match in XmlnsRegex().Matches(text))
        {
            var prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : string.Empty;
            var value = match.Groups["value"].Value;
            namespaces[prefix] = value;
        }

        return namespaces;
    }

    private static IReadOnlyList<string> GetOpenElementStack(string text, int position)
    {
        position = ClampPosition(text, position);
        var stack = new List<string>();
        foreach (Match match in ElementRegex().Matches(text[..position]))
        {
            var full = match.Value;
            if (full.StartsWith("<!--", StringComparison.Ordinal) ||
                full.StartsWith("<!", StringComparison.Ordinal) ||
                full.StartsWith("<?", StringComparison.Ordinal))
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (match.Groups["closing"].Success)
            {
                var index = stack.FindLastIndex(item => string.Equals(item, name, StringComparison.Ordinal));
                if (index >= 0)
                {
                    stack.RemoveRange(index, stack.Count - index);
                }
            }
            else if (!full.EndsWith("/>", StringComparison.Ordinal))
            {
                stack.Add(name);
            }
        }

        return stack;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML type and property metadata remains available for IntelliSense.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Browser publish roots Avalonia and playground assemblies so public XAML type and property metadata remains available for IntelliSense.")]
    private static XamlTypeCatalog BuildCatalog()
    {
        var types = new List<XamlTypeInfo>();
        var attachedProperties = new List<XamlAttachedPropertyInfo>();

        foreach (var type in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(static assembly => !assembly.IsDynamic)
                     .SelectMany(GetLoadableTypes))
        {
            if (!IsCatalogType(type))
            {
                continue;
            }

            if (IsCandidateType(type))
            {
                var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
                types.Add(new XamlTypeInfo(
                    GetXamlTypeName(type),
                    type.Namespace ?? string.Empty,
                    assemblyName,
                    type,
                    IsAvaloniaNamespace(type)));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!field.Name.EndsWith("Property", StringComparison.Ordinal) ||
                    !typeof(AvaloniaProperty).IsAssignableFrom(field.FieldType) ||
                    field.GetValue(null) is not AvaloniaProperty avaloniaProperty)
                {
                    continue;
                }

                var propertyName = field.Name[..^"Property".Length];
                attachedProperties.Add(new XamlAttachedPropertyInfo(
                    $"{GetXamlTypeName(type)}.{propertyName}",
                    avaloniaProperty.PropertyType,
                    $"{avaloniaProperty.PropertyType.Name} attached property"));
            }
        }

        return new XamlTypeCatalog(
            types
                .GroupBy(type => (type.Name, type.Namespace, type.AssemblyName))
                .Select(group => group.First())
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray(),
            attachedProperties
                .GroupBy(property => property.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray());
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Browser publish roots Avalonia and playground assemblies so loadable public XAML types remain available for IntelliSense.")]
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
        catch
        {
            return [];
        }
    }

    private static bool IsCandidateType(Type type)
    {
        return type is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false } &&
               IsCatalogType(type);
    }

    private static bool IsCatalogType(Type type)
    {
        return type is { IsPublic: true, IsGenericTypeDefinition: false } &&
               type.Namespace is not null &&
               type.Assembly.GetName().Name is { } assemblyName &&
               (assemblyName.StartsWith("Avalonia", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Xaml.Behaviors", StringComparison.Ordinal) ||
                assemblyName.StartsWith("XamlPlayground", StringComparison.Ordinal) ||
                s_workspaceAssemblyNames.Contains(assemblyName));
    }

    private static bool IsAvaloniaNamespace(Type type)
    {
        return type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("XamlPlayground", StringComparison.Ordinal) == true ||
               type.Assembly.GetName().Name is { } assemblyName &&
               s_workspaceAssemblyNames.Contains(assemblyName);
    }

    private static string GetXamlTypeName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    private static string GetQualifiedName(string prefix, string localName)
    {
        return string.IsNullOrEmpty(prefix) ? localName : $"{prefix}:{localName}";
    }

    private static string GetPrefix(string xmlName)
    {
        var colon = xmlName.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? string.Empty : xmlName[..colon];
    }

    private static string GetLocalName(string xmlName)
    {
        var colon = xmlName.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? xmlName : xmlName[(colon + 1)..];
    }

    private static TextSpan GetXmlNameSpan(string text, int position)
    {
        var start = position;
        while (start > 0 && IsXmlNamePart(text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < text.Length && IsXmlNamePart(text[end]))
        {
            end++;
        }

        return new TextSpan(start, end);
    }

    private static bool IsXmlNamePart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-';
    }

    private static bool IsAutomaticTrigger(char ch)
    {
        return ch == '<' ||
               ch == '/' ||
               ch == ' ' ||
               ch == '=' ||
               ch == '"' ||
               ch == '\'' ||
               ch == '.' ||
               ch == ':' ||
               char.IsLetterOrDigit(ch);
    }

    private static int ClampPosition(string text, int position)
    {
        return Math.Clamp(position, 0, text.Length);
    }

    [GeneratedRegex("xmlns(?::(?<prefix>[A-Za-z_][\\w.-]*))?\\s*=\\s*[\"'](?<value>[^\"']+)[\"']", RegexOptions.Compiled)]
    private static partial Regex XmlnsRegex();

    [GeneratedRegex("<(?<closing>/)?(?<name>[A-Za-z_][\\w:.-]*)(?<rest>[^<>]*?)(?<selfclosing>/)?>", RegexOptions.Compiled)]
    private static partial Regex ElementRegex();

    [GeneratedRegex("(?<name>[A-Za-z_][\\w:.-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)')", RegexOptions.Compiled)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("\\{(?:StaticResource|DynamicResource)(?:\\s+ResourceKey\\s*=)?\\s*(?<key>[^\\s,}]+)", RegexOptions.Compiled)]
    private static partial Regex ResourceReferenceRegex();

    [GeneratedRegex("(?<![A-Za-z0-9_.:-])(?:x:Key|Key)\\s*=\\s*(?:\"(?<key>[^\"]+)\"|'(?<key>[^']+)')", RegexOptions.Compiled)]
    private static partial Regex ResourceDefinitionRegex();

    private sealed record XamlTypeCatalog(
        IReadOnlyList<XamlTypeInfo> Types,
        IReadOnlyList<XamlAttachedPropertyInfo> AttachedProperties);

    private sealed record XamlTypeInfo(
        string Name,
        string Namespace,
        string AssemblyName,
        Type Type,
        bool IsAvaloniaNamespace);

    private sealed record XamlAttachedPropertyInfo(
        string Name,
        Type ValueType,
        string Description);

    private sealed record XamlMemberInfo(
        string Name,
        Type? ValueType,
        string Description);

    private sealed class XamlCompletionContext
    {
        private XamlCompletionContext(
            XamlCompletionContextKind kind,
            string? tagName,
            string? attributeName,
            string currentToken,
            int replacementStart,
            int replacementEnd)
        {
            Kind = kind;
            TagName = tagName;
            AttributeName = attributeName;
            CurrentToken = currentToken;
            ReplacementStart = replacementStart;
            ReplacementEnd = replacementEnd;
        }

        public XamlCompletionContextKind Kind { get; }

        public string? TagName { get; }

        public string? AttributeName { get; }

        public string CurrentToken { get; }

        public int ReplacementStart { get; }

        public int ReplacementEnd { get; }

        public static XamlCompletionContext? Create(string text, int position)
        {
            var searchStart = Math.Max(0, position - 1);
            var lastLt = text.LastIndexOf("<", searchStart, StringComparison.Ordinal);
            var lastGt = text.LastIndexOf(">", searchStart, StringComparison.Ordinal);
            if (lastLt < 0 || lastLt <= lastGt)
            {
                return null;
            }

            var wordSpan = GetXmlNameSpan(text, position);
            var currentToken = text[wordSpan.Start..wordSpan.End];
            var tagText = text[lastLt..position];
            var isClosing = tagText.StartsWith("</", StringComparison.Ordinal);
            var tagName = ReadTagName(tagText);

            if (TryGetAttributeValueContext(text, position, lastLt, wordSpan, tagName, out var valueContext))
            {
                return valueContext;
            }

            if (isClosing)
            {
                return new XamlCompletionContext(
                    XamlCompletionContextKind.ClosingTag,
                    tagName,
                    null,
                    currentToken,
                    wordSpan.Start,
                    wordSpan.End);
            }

            if (IsInElementName(tagText))
            {
                return new XamlCompletionContext(
                    XamlCompletionContextKind.ElementName,
                    tagName,
                    null,
                    currentToken,
                    wordSpan.Start,
                    wordSpan.End);
            }

            return new XamlCompletionContext(
                XamlCompletionContextKind.AttributeName,
                tagName,
                null,
                currentToken,
                wordSpan.Start,
                wordSpan.End);
        }

        private static bool TryGetAttributeValueContext(
            string text,
            int position,
            int tagStart,
            TextSpan wordSpan,
            string? tagName,
            out XamlCompletionContext? context)
        {
            context = null;
            var quoteStart = -1;
            var quoteChar = '\0';

            for (var i = position - 1; i > tagStart; i--)
            {
                if (text[i] is '"' or '\'')
                {
                    quoteStart = i;
                    quoteChar = text[i];
                    break;
                }
            }

            if (quoteStart < 0)
            {
                return false;
            }

            var quoteCount = 0;
            for (var i = tagStart; i < position; i++)
            {
                if (text[i] == quoteChar)
                {
                    quoteCount++;
                }
            }

            if (quoteCount % 2 == 0)
            {
                return false;
            }

            var equals = quoteStart - 1;
            while (equals > tagStart && char.IsWhiteSpace(text[equals]))
            {
                equals--;
            }

            if (equals <= tagStart || text[equals] != '=')
            {
                return false;
            }

            var attrEnd = equals;
            while (attrEnd > tagStart && char.IsWhiteSpace(text[attrEnd - 1]))
            {
                attrEnd--;
            }

            var attrStart = attrEnd;
            while (attrStart > tagStart && IsXmlNamePart(text[attrStart - 1]))
            {
                attrStart--;
            }

            if (attrStart == attrEnd)
            {
                return false;
            }

            var attributeName = text[attrStart..attrEnd];
            context = new XamlCompletionContext(
                XamlCompletionContextKind.AttributeValue,
                tagName,
                attributeName,
                text[wordSpan.Start..wordSpan.End],
                wordSpan.Start,
                wordSpan.End);
            return true;
        }

        private static string? ReadTagName(string tagText)
        {
            var index = tagText.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
            while (index < tagText.Length && char.IsWhiteSpace(tagText[index]))
            {
                index++;
            }

            var start = index;
            while (index < tagText.Length && IsXmlNamePart(tagText[index]))
            {
                index++;
            }

            return index > start ? tagText[start..index] : null;
        }

        private static bool IsInElementName(string tagText)
        {
            var index = tagText.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
            while (index < tagText.Length && char.IsWhiteSpace(tagText[index]))
            {
                index++;
            }

            while (index < tagText.Length && IsXmlNamePart(tagText[index]))
            {
                index++;
            }

            return index >= tagText.Length;
        }
    }

    private enum XamlCompletionContextKind
    {
        ElementName,
        ClosingTag,
        AttributeName,
        AttributeValue
    }

    private readonly record struct TextSpan(int Start, int End);
}
