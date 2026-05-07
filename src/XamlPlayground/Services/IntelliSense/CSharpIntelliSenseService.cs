using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace XamlPlayground.Services.IntelliSense;

public sealed class CSharpIntelliSenseService : IEditorIntelliSenseService
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly CSharpCompilationOptions s_compilationOptions =
        new(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug);

    private static readonly SymbolDisplayFormat s_memberDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat s_signatureDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeType,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly string[] s_keywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
        "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var",
        "virtual", "void", "volatile", "while", "with", "yield"
    ];

    private static readonly EditorCompletionItem[] s_snippets =
    [
        new("ctor", "public SampleView()\n{\n    \n}", "Constructor snippet", EditorCompletionKind.Snippet, 20, 26),
        new("prop", "public string? Property { get; set; }", "Auto-property snippet", EditorCompletionKind.Snippet, 20, 15),
        new("override", "protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)\n{\n    base.OnAttachedToVisualTree(e);\n}", "Override OnAttachedToVisualTree", EditorCompletionKind.Snippet, 15, 86)
    ];

    public async Task<EditorCompletionResult?> GetCompletionsAsync(
        string text,
        int position,
        bool explicitInvocation,
        char? triggerCharacter,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        if (!explicitInvocation && triggerCharacter is { } trigger && !IsAutomaticTrigger(trigger))
        {
            return null;
        }

        var wordSpan = GetIdentifierSpan(text, position);
        var replacementStart = wordSpan.Start;
        var replacementEnd = wordSpan.End;
        var compilationContext = await CreateCompilationContextAsync(text, cancellationToken);
        var memberAccess = TryGetMemberAccess(compilationContext.Root, replacementStart, out var memberAccessExpression);

        var items = memberAccess && memberAccessExpression is not null
            ? GetMemberCompletions(compilationContext, memberAccessExpression, cancellationToken)
            : GetGlobalCompletions(compilationContext, position, explicitInvocation, triggerCharacter, cancellationToken);

        if (items.Count == 0)
        {
            return null;
        }

        return new EditorCompletionResult(replacementStart, replacementEnd, items);
    }

    public async Task<EditorQuickInfo?> GetQuickInfoAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        if (text.Length == 0)
        {
            return null;
        }

        var context = await CreateCompilationContextAsync(text, cancellationToken);
        var token = context.Root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));
        if (token.IsKind(SyntaxKind.None))
        {
            return null;
        }

        var symbol = FindSymbol(context.SemanticModel, token, cancellationToken);
        if (symbol is null)
        {
            return null;
        }

        var span = token.Span;
        var description = FormatSymbolDescription(symbol);
        return string.IsNullOrWhiteSpace(description)
            ? null
            : new EditorQuickInfo(span.Start, span.End, description);
    }

    public async Task<EditorSignatureHelp?> GetSignatureHelpAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        var context = await CreateCompilationContextAsync(text, cancellationToken);

        if (FindInvocation(context.Root, position) is { } invocation)
        {
            var methods = GetCandidateMethods(context.SemanticModel, invocation.Expression, cancellationToken);
            var selectedParameter = GetSelectedParameterIndex(invocation.ArgumentList, position);
            return CreateSignatureHelp(methods, selectedParameter);
        }

        if (FindObjectCreation(context.Root, position) is { } objectCreation)
        {
            var methods = GetCandidateMethods(context.SemanticModel, objectCreation, cancellationToken);
            var selectedParameter = objectCreation.ArgumentList is null
                ? 0
                : GetSelectedParameterIndex(objectCreation.ArgumentList, position);
            return CreateSignatureHelp(methods, selectedParameter);
        }

        return null;
    }

    private static async Task<CSharpCompilationContext> CreateCompilationContextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var sourceText = SourceText.From(text, Encoding.UTF8);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, s_parseOptions, "SampleView.cs", cancellationToken);
        var references = await CompilerService.GetMetadataReferences();
        var compilation = CSharpCompilation.Create(
            "XamlPlayground.IntelliSense",
            [syntaxTree],
            references,
            s_compilationOptions);
        var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: false);
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        return new CSharpCompilationContext(compilation, semanticModel, root);
    }

    private static IReadOnlyList<EditorCompletionItem> GetMemberCompletions(
        CSharpCompilationContext context,
        MemberAccessExpressionSyntax memberAccessExpression,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpression.Expression, cancellationToken).Symbol;
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccessExpression.Expression, cancellationToken);
        var targetType = targetSymbol as ITypeSymbol ?? typeInfo.Type;
        if (targetType is null)
        {
            return [];
        }

        var includeStatic = targetSymbol is ITypeSymbol ||
                            memberAccessExpression.Expression is IdentifierNameSyntax identifier &&
                            char.IsUpper(identifier.Identifier.ValueText.FirstOrDefault());

        var items = new List<EditorCompletionItem>();
        foreach (var group in GetAllMembers(targetType)
                     .Where(symbol => IsCompletionSymbol(symbol) && symbol.IsStatic == includeStatic)
                     .GroupBy(symbol => (symbol.Name, symbol.Kind))
                     .OrderBy(group => group.Key.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var symbol = group.First();
            if (!IsAccessible(context.Compilation, symbol, targetType))
            {
                continue;
            }

            var description = FormatSymbolGroupDescription(group);
            items.Add(new EditorCompletionItem(
                symbol.Name,
                symbol.Name,
                description,
                GetCompletionKind(symbol),
                GetSymbolPriority(symbol)));
        }

        return items;
    }

    private static IReadOnlyList<EditorCompletionItem> GetGlobalCompletions(
        CSharpCompilationContext context,
        int position,
        bool explicitInvocation,
        char? triggerCharacter,
        CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, EditorCompletionItem>(StringComparer.Ordinal);

        foreach (var keyword in s_keywords)
        {
            items.TryAdd(keyword, new EditorCompletionItem(
                keyword,
                keyword,
                "C# keyword",
                EditorCompletionKind.Keyword,
                2));
        }

        foreach (var snippet in s_snippets)
        {
            items.TryAdd(snippet.Text, snippet);
        }

        foreach (var symbol in context.SemanticModel.LookupSymbols(position)
                     .Concat<ISymbol>(context.SemanticModel.LookupNamespacesAndTypes(position)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCompletionSymbol(symbol) || string.IsNullOrWhiteSpace(symbol.Name))
            {
                continue;
            }

            items.TryAdd(symbol.Name, new EditorCompletionItem(
                symbol.Name,
                symbol.Name,
                FormatSymbolDescription(symbol),
                GetCompletionKind(symbol),
                GetSymbolPriority(symbol)));
        }

        if (explicitInvocation || triggerCharacter is not ' ')
        {
            foreach (var type in GetCommonAvaloniaTypeCompletions())
            {
                items.TryAdd(type.Text, type);
            }
        }

        return items.Values
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<EditorCompletionItem> GetCommonAvaloniaTypeCompletions()
    {
        string[] names =
        [
            "Application", "UserControl", "Window", "Grid", "StackPanel", "DockPanel", "Canvas", "Border",
            "Button", "TextBlock", "TextBox", "ListBox", "ComboBox", "CheckBox", "RadioButton", "Slider",
            "Image", "Path", "Rectangle", "Ellipse", "SolidColorBrush", "Thickness", "HorizontalAlignment",
            "VerticalAlignment", "VisualTreeAttachmentEventArgs"
        ];

        foreach (var name in names)
        {
            yield return new EditorCompletionItem(name, name, "Common Avalonia type", EditorCompletionKind.Type, 3);
        }
    }

    private static bool TryGetMemberAccess(
        SyntaxNode root,
        int replacementStart,
        out MemberAccessExpressionSyntax? memberAccessExpression)
    {
        var dotOffset = replacementStart - 1;
        memberAccessExpression = null;
        if (dotOffset < 0)
        {
            return false;
        }

        memberAccessExpression = root
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.OperatorToken.SpanStart == dotOffset ||
                           node.OperatorToken.Span.End == dotOffset + 1 ||
                           node.Span.Contains(replacementStart))
            .OrderBy(node => node.Span.Length)
            .FirstOrDefault();

        return memberAccessExpression is not null;
    }

    private static ISymbol? FindSymbol(
        SemanticModel semanticModel,
        SyntaxToken token,
        CancellationToken cancellationToken)
    {
        foreach (var node in token.Parent?.AncestorsAndSelf() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            var declared = node switch
            {
                ClassDeclarationSyntax classDeclaration => semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken),
                StructDeclarationSyntax structDeclaration => semanticModel.GetDeclaredSymbol(structDeclaration, cancellationToken),
                InterfaceDeclarationSyntax interfaceDeclaration => semanticModel.GetDeclaredSymbol(interfaceDeclaration, cancellationToken),
                EnumDeclarationSyntax enumDeclaration => semanticModel.GetDeclaredSymbol(enumDeclaration, cancellationToken),
                MethodDeclarationSyntax methodDeclaration => semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken),
                PropertyDeclarationSyntax propertyDeclaration => semanticModel.GetDeclaredSymbol(propertyDeclaration, cancellationToken),
                EventDeclarationSyntax eventDeclaration => semanticModel.GetDeclaredSymbol(eventDeclaration, cancellationToken),
                FieldDeclarationSyntax fieldDeclaration => fieldDeclaration.Declaration.Variables.Count == 1
                    ? semanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], cancellationToken)
                    : null,
                VariableDeclaratorSyntax variableDeclarator => semanticModel.GetDeclaredSymbol(variableDeclarator, cancellationToken),
                ParameterSyntax parameter => semanticModel.GetDeclaredSymbol(parameter, cancellationToken),
                _ => null
            };

            if (declared is not null)
            {
                return declared;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            if (symbolInfo.Symbol is not null)
            {
                return symbolInfo.Symbol;
            }

            if (!symbolInfo.CandidateSymbols.IsDefaultOrEmpty)
            {
                return symbolInfo.CandidateSymbols[0];
            }
        }

        return null;
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, int position)
    {
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(node => node.ArgumentList.Span.Start <= position && position <= node.ArgumentList.Span.End)
            .OrderBy(node => node.Span.Length)
            .FirstOrDefault();
    }

    private static ObjectCreationExpressionSyntax? FindObjectCreation(SyntaxNode root, int position)
    {
        return root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.ArgumentList is not null &&
                           node.ArgumentList.Span.Start <= position &&
                           position <= node.ArgumentList.Span.End)
            .OrderBy(node => node.Span.Length)
            .FirstOrDefault();
    }

    private static IReadOnlyList<IMethodSymbol> GetCandidateMethods(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
        var methods = new List<IMethodSymbol>();

        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            methods.Add(method);
        }

        methods.AddRange(symbolInfo.CandidateSymbols.OfType<IMethodSymbol>());
        return methods
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<IMethodSymbol>()
            .Where(method => method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension)
            .ToArray();
    }

    private static IReadOnlyList<IMethodSymbol> GetCandidateMethods(
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax objectCreation,
        CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(objectCreation, cancellationToken).Type as INamedTypeSymbol;
        return type?.Constructors
            .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public)
            .Cast<IMethodSymbol>()
            .ToArray() ?? [];
    }

    private static EditorSignatureHelp? CreateSignatureHelp(
        IReadOnlyList<IMethodSymbol> methods,
        int selectedParameter)
    {
        if (methods.Count == 0)
        {
            return null;
        }

        var items = methods
            .OrderBy(method => method.Parameters.Length)
            .ThenBy(method => method.ToDisplayString(s_signatureDisplayFormat), StringComparer.Ordinal)
            .Select(method =>
            {
                var header = method.ToDisplayString(s_signatureDisplayFormat);
                var parameter = selectedParameter < method.Parameters.Length
                    ? method.Parameters[selectedParameter].ToDisplayString(s_memberDisplayFormat)
                    : string.Empty;
                var content = string.IsNullOrWhiteSpace(parameter)
                    ? method.ContainingType?.ToDisplayString(s_memberDisplayFormat) ?? string.Empty
                    : $"Parameter: {parameter}";
                return new EditorSignatureItem(header, content);
            })
            .ToArray();

        return new EditorSignatureHelp(items, 0, selectedParameter);
    }

    private static int GetSelectedParameterIndex(ArgumentListSyntax argumentList, int position)
    {
        var selected = 0;
        foreach (var separator in argumentList.Arguments.GetSeparators())
        {
            if (separator.SpanStart < position)
            {
                selected++;
            }
        }

        return selected;
    }

    private static bool IsCompletionSymbol(ISymbol symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol.Name) ||
            symbol.Name.StartsWith('<') ||
            symbol.IsImplicitlyDeclared)
        {
            return false;
        }

        return symbol.Kind switch
        {
            SymbolKind.Method => symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension },
            SymbolKind.NamedType or
            SymbolKind.Namespace or
            SymbolKind.Property or
            SymbolKind.Field or
            SymbolKind.Event or
            SymbolKind.Local or
            SymbolKind.Parameter => true,
            _ => false
        };
    }

    private static IEnumerable<ISymbol> GetAllMembers(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (!member.Name.Contains('.', StringComparison.Ordinal))
                {
                    yield return member;
                }
            }
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            foreach (var member in interfaceType.GetMembers())
            {
                if (!member.Name.Contains('.', StringComparison.Ordinal))
                {
                    yield return member;
                }
            }
        }
    }

    private static bool IsAccessible(Compilation compilation, ISymbol symbol, ITypeSymbol within)
    {
        return symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedOrInternal ||
               compilation.IsSymbolAccessibleWithin(symbol, within);
    }

    private static EditorCompletionKind GetCompletionKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.NamedType => EditorCompletionKind.Type,
            SymbolKind.Namespace => EditorCompletionKind.Namespace,
            SymbolKind.Method => EditorCompletionKind.Method,
            SymbolKind.Property => EditorCompletionKind.Property,
            SymbolKind.Field => EditorCompletionKind.Field,
            SymbolKind.Event => EditorCompletionKind.Event,
            SymbolKind.Local or SymbolKind.Parameter => EditorCompletionKind.Value,
            _ => EditorCompletionKind.Value
        };
    }

    private static double GetSymbolPriority(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Local or SymbolKind.Parameter => 10,
            SymbolKind.Property or SymbolKind.Method or SymbolKind.Event => 8,
            SymbolKind.NamedType => 5,
            SymbolKind.Namespace => 4,
            _ => 1
        };
    }

    private static string FormatSymbolGroupDescription(IEnumerable<ISymbol> symbols)
    {
        var list = symbols.ToArray();
        var first = list[0];
        var description = FormatSymbolDescription(first);
        return list.Length <= 1
            ? description
            : $"{description}{Environment.NewLine}+ {list.Length - 1} overload(s)";
    }

    private static string FormatSymbolDescription(ISymbol symbol)
    {
        var declaration = symbol.ToDisplayString(s_memberDisplayFormat);
        var containingNamespace = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        return string.IsNullOrWhiteSpace(containingNamespace)
            ? declaration
            : $"{declaration}{Environment.NewLine}{containingNamespace}";
    }

    private static TextSpan GetIdentifierSpan(string text, int position)
    {
        var start = position;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < text.Length && IsIdentifierPart(text[end]))
        {
            end++;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    private static bool IsAutomaticTrigger(char ch)
    {
        return ch == '.' ||
               ch == ' ' ||
               ch == '(' ||
               ch == ',' ||
               ch == '_' ||
               char.IsLetterOrDigit(ch);
    }

    private static int ClampPosition(string text, int position)
    {
        return Math.Clamp(position, 0, text.Length);
    }

    private sealed record CSharpCompilationContext(
        Compilation Compilation,
        SemanticModel SemanticModel,
        SyntaxNode Root);
}
