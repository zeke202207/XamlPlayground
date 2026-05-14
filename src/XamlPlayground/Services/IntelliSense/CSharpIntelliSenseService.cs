using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using XamlPlayground.Workspace;

namespace XamlPlayground.Services.IntelliSense;

public sealed class CSharpIntelliSenseService : IEditorIntelliSenseService
{
    private const string DefaultFilePath = "SampleView.cs";

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

    private readonly InMemoryProject? _project;
    private readonly InMemoryProjectFile? _file;

    public CSharpIntelliSenseService(InMemoryProject? project = null, InMemoryProjectFile? file = null)
    {
        _project = project;
        _file = file;
    }

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

    public async Task<IReadOnlyList<EditorDiagnostic>> GetDiagnosticsAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var context = await CreateCompilationContextAsync(text, cancellationToken);
        var diagnostics = context.Compilation
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Location.SourceTree == context.CurrentTree)
            .Select(diagnostic => CreateDiagnostic(text, diagnostic))
            .Where(static diagnostic => diagnostic is not null)
            .Cast<EditorDiagnostic>()
            .ToArray();

        return diagnostics;
    }

    public async Task<EditorLocation?> GetDefinitionAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        var context = await CreateCompilationContextAsync(text, cancellationToken);
        var symbol = FindSymbolAtPosition(context, position, cancellationToken);
        if (symbol is null)
        {
            return null;
        }

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = await reference.GetSyntaxAsync(cancellationToken);
            var span = GetDeclarationSelectionSpan(node);
            var root = await reference.SyntaxTree.GetRootAsync(cancellationToken);
            return CreateLocation(reference.SyntaxTree.FilePath, root, span);
        }

        return null;
    }

    public async Task<IReadOnlyList<EditorReference>> GetReferencesAsync(
        string text,
        int position,
        CancellationToken cancellationToken)
    {
        position = ClampPosition(text, position);
        var context = await CreateCompilationContextAsync(text, cancellationToken);
        var symbol = FindSymbolAtPosition(context, position, cancellationToken);
        if (symbol is null || string.IsNullOrWhiteSpace(symbol.Name))
        {
            return [];
        }

        var references = new List<EditorReference>();
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var root = await tree.GetRootAsync(cancellationToken);
            var semanticModel = context.Compilation.GetSemanticModel(tree);
            foreach (var token in root.DescendantTokens(descendIntoTrivia: false)
                         .Where(token => string.Equals(token.ValueText, symbol.Name, StringComparison.Ordinal)))
            {
                var candidate = FindSymbol(semanticModel, token, cancellationToken);
                if (candidate is null || !SymbolEqualityComparer.Default.Equals(candidate, symbol))
                {
                    continue;
                }

                var location = CreateLocation(tree.FilePath, root, token.Span);
                references.Add(new EditorReference(location, IsDeclarationToken(token)));
            }
        }

        return references
            .OrderBy(reference => reference.Location.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Location.StartOffset)
            .ToArray();
    }

    public Task<string?> FormatDocumentAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceText = SourceText.From(text, Encoding.UTF8);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            _project?.CSharpParseOptions ?? s_parseOptions,
            _file?.Path ?? DefaultFilePath,
            cancellationToken);
        var root = syntaxTree.GetRoot(cancellationToken);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var formatted = root.NormalizeWhitespace(indentation: "    ", eol: newLine, elasticTrivia: true).ToFullString();

        return Task.FromResult<string?>(formatted);
    }

    public async Task<IReadOnlyList<EditorDocumentSymbol>> GetDocumentSymbolsAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var context = await CreateCompilationContextAsync(text, cancellationToken);
        return context.Root
            .DescendantNodes()
            .Select(node => CreateDocumentSymbol(node, cancellationToken))
            .Where(static symbol => symbol is not null)
            .Cast<EditorDocumentSymbol>()
            .OrderBy(static symbol => symbol.StartOffset)
            .ToArray();
    }

    private async Task<CSharpCompilationContext> CreateCompilationContextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var filePath = NormalizePath(_file?.Path) ?? DefaultFilePath;
        var parseOptions = _project?.CSharpParseOptions ?? s_parseOptions;
        var codeFiles = GetProjectCodeFiles(text, filePath);
        var syntaxTrees = codeFiles
            .Select(file => CSharpSyntaxTree.ParseText(
                SourceText.From(file.Text, Encoding.UTF8),
                parseOptions,
                file.Path,
                cancellationToken))
            .ToArray();

        var currentTree = syntaxTrees.FirstOrDefault(tree =>
            string.Equals(NormalizePath(tree.FilePath), filePath, StringComparison.OrdinalIgnoreCase)) ??
                          syntaxTrees.First();
        var workspaceReferences = _project?.AssemblyReferences
            .Where(reference =>
                string.IsNullOrWhiteSpace(_project.AssemblyName) ||
                !string.Equals(reference.Name, _project.AssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        var baseReferences = workspaceReferences.Any(static reference => reference.IsReferenceAssembly)
            ? Array.Empty<PortableExecutableReference>()
            : await CompilerService.GetMetadataReferences();
        var references = MergeReferences(baseReferences, workspaceReferences);
        var compilationOptions = (_project?.CSharpCompilationOptions ?? s_compilationOptions)
            .WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Debug);
        var compilation = CSharpCompilation.Create(
            string.IsNullOrWhiteSpace(_project?.AssemblyName) ? "XamlPlayground.IntelliSense" : _project.AssemblyName,
            syntaxTrees,
            references,
            compilationOptions);
        var semanticModel = compilation.GetSemanticModel(currentTree, ignoreAccessibility: false);
        var root = await currentTree.GetRootAsync(cancellationToken);
        return new CSharpCompilationContext(compilation, currentTree, semanticModel, root);
    }

    private IReadOnlyList<(string Path, string Text)> GetProjectCodeFiles(string currentText, string currentFilePath)
    {
        if (_project is null || _file?.IsCSharp != true)
        {
            return [(currentFilePath, currentText)];
        }

        var files = new List<(string Path, string Text)>();
        var includedCurrentFile = false;
        foreach (var file in _project.GetCSharpFiles())
        {
            var path = NormalizePath(file.Path) ?? file.Path;
            var isCurrent = ReferenceEquals(file, _file) ||
                            string.Equals(path, currentFilePath, StringComparison.OrdinalIgnoreCase);
            files.Add((path, isCurrent ? currentText : file.Text));
            includedCurrentFile |= isCurrent;
        }

        if (!includedCurrentFile)
        {
            files.Add((currentFilePath, currentText));
        }

        return files.Count == 0 ? [(currentFilePath, currentText)] : files;
    }

    private static IReadOnlyList<PortableExecutableReference> MergeReferences(
        IReadOnlyList<PortableExecutableReference> baseReferences,
        IReadOnlyList<WorkspaceAssemblyReference> workspaceReferences)
    {
        if (workspaceReferences.Count == 0)
        {
            return baseReferences;
        }

        var references = new List<PortableExecutableReference>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in workspaceReferences
                     .Select(static (reference, index) => (Reference: reference, Index: index))
                     .OrderByDescending(static item => item.Reference.IsReferenceAssembly)
                     .ThenBy(static item => item.Index))
        {
            var workspaceReference = item.Reference;
            var metadataReference = workspaceReference.CreateMetadataReference();
            if (metadataReference is null)
            {
                continue;
            }

            var key = workspaceReference.Image is { Length: > 0 }
                ? workspaceReference.Name
                : GetReferenceKey(metadataReference) ?? workspaceReference.Name;
            if (keys.Add(key))
            {
                references.Add(metadataReference);
            }
        }

        foreach (var baseReference in baseReferences)
        {
            var key = GetReferenceKey(baseReference);
            if (!string.IsNullOrWhiteSpace(key) && !keys.Add(key))
            {
                continue;
            }

            references.Add(baseReference);
        }

        return references;
    }

    private static string? GetReferenceKey(PortableExecutableReference reference)
    {
        var path = reference.FilePath ?? reference.Display;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".dll".Length]
            : fileName;
    }

    private static EditorDiagnostic? CreateDiagnostic(string text, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return null;
        }

        var span = diagnostic.Location.SourceSpan;
        var start = Math.Clamp(span.Start, 0, text.Length);
        var end = Math.Clamp(span.End, start, text.Length);
        if (start == end && start < text.Length)
        {
            end++;
        }

        return new EditorDiagnostic(
            start,
            end,
            diagnostic.GetMessage(),
            GetDiagnosticSeverity(diagnostic.Severity),
            diagnostic.Id);
    }

    private static EditorDiagnosticSeverity GetDiagnosticSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => EditorDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => EditorDiagnosticSeverity.Warning,
            DiagnosticSeverity.Info => EditorDiagnosticSeverity.Information,
            _ => EditorDiagnosticSeverity.Hint
        };
    }

    private static ISymbol? FindSymbolAtPosition(
        CSharpCompilationContext context,
        int position,
        CancellationToken cancellationToken)
    {
        var token = context.Root.FindToken(Math.Clamp(position, 0, Math.Max(0, context.Root.FullSpan.End - 1)));
        return token.IsKind(SyntaxKind.None)
            ? null
            : FindSymbol(context.SemanticModel, token, cancellationToken);
    }

    private static EditorLocation CreateLocation(string? filePath, SyntaxNode root, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, root.FullSpan.End);
        var end = Math.Clamp(span.End, start, root.FullSpan.End);
        var lineSpan = root.SyntaxTree.GetLineSpan(TextSpan.FromBounds(start, end));
        var lineText = GetLinePreview(root, start);
        return new EditorLocation(
            NormalizePath(filePath),
            start,
            end,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            lineText);
    }

    private static string GetLinePreview(SyntaxNode root, int offset)
    {
        var text = root.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(Math.Clamp(offset, 0, Math.Max(0, text.Length)));
        return line.ToString().Trim();
    }

    private static TextSpan GetDeclarationSelectionSpan(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.Span,
            DelegateDeclarationSyntax declaration => declaration.Identifier.Span,
            MethodDeclarationSyntax declaration => declaration.Identifier.Span,
            ConstructorDeclarationSyntax declaration => declaration.Identifier.Span,
            PropertyDeclarationSyntax declaration => declaration.Identifier.Span,
            EventDeclarationSyntax declaration => declaration.Identifier.Span,
            VariableDeclaratorSyntax declaration => declaration.Identifier.Span,
            ParameterSyntax declaration => declaration.Identifier.Span,
            _ => node.Span
        };
    }

    private static bool IsDeclarationToken(SyntaxToken token)
    {
        return token.Parent switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier == token,
            DelegateDeclarationSyntax declaration => declaration.Identifier == token,
            MethodDeclarationSyntax declaration => declaration.Identifier == token,
            ConstructorDeclarationSyntax declaration => declaration.Identifier == token,
            PropertyDeclarationSyntax declaration => declaration.Identifier == token,
            EventDeclarationSyntax declaration => declaration.Identifier == token,
            VariableDeclaratorSyntax declaration => declaration.Identifier == token,
            ParameterSyntax declaration => declaration.Identifier == token,
            _ => false
        };
    }

    private static EditorDocumentSymbol? CreateDocumentSymbol(
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return node switch
        {
            BaseTypeDeclarationSyntax declaration => new EditorDocumentSymbol(
                declaration.Identifier.ValueText,
                GetTypeDeclarationDetail(declaration),
                EditorCompletionKind.Type,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Identifier.SpanStart,
                declaration.Identifier.Span.End,
                []),
            MethodDeclarationSyntax declaration => new EditorDocumentSymbol(
                declaration.Identifier.ValueText,
                declaration.ReturnType.ToString(),
                EditorCompletionKind.Method,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Identifier.SpanStart,
                declaration.Identifier.Span.End,
                []),
            ConstructorDeclarationSyntax declaration => new EditorDocumentSymbol(
                declaration.Identifier.ValueText,
                "constructor",
                EditorCompletionKind.Method,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Identifier.SpanStart,
                declaration.Identifier.Span.End,
                []),
            PropertyDeclarationSyntax declaration => new EditorDocumentSymbol(
                declaration.Identifier.ValueText,
                declaration.Type.ToString(),
                EditorCompletionKind.Property,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Identifier.SpanStart,
                declaration.Identifier.Span.End,
                []),
            EventDeclarationSyntax declaration => new EditorDocumentSymbol(
                declaration.Identifier.ValueText,
                declaration.Type.ToString(),
                EditorCompletionKind.Event,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Identifier.SpanStart,
                declaration.Identifier.Span.End,
                []),
            FieldDeclarationSyntax declaration when declaration.Declaration.Variables.Count == 1 => new EditorDocumentSymbol(
                declaration.Declaration.Variables[0].Identifier.ValueText,
                declaration.Declaration.Type.ToString(),
                EditorCompletionKind.Field,
                declaration.SpanStart,
                declaration.Span.End,
                declaration.Declaration.Variables[0].Identifier.SpanStart,
                declaration.Declaration.Variables[0].Identifier.Span.End,
                []),
            _ => null
        };
    }

    private static string GetTypeDeclarationDetail(BaseTypeDeclarationSyntax declaration)
    {
        return declaration switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax record => record.ClassOrStructKeyword.ValueText is { Length: > 0 } keyword
                ? $"record {keyword}"
                : "record",
            EnumDeclarationSyntax => "enum",
            _ => "type"
        };
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

    private static string? NormalizePath(string? path)
    {
        var normalized = path?.Replace('\\', '/').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.Trim('/');
    }

    private sealed record CSharpCompilationContext(
        Compilation Compilation,
        SyntaxTree CurrentTree,
        SemanticModel SemanticModel,
        SyntaxNode Root);
}
