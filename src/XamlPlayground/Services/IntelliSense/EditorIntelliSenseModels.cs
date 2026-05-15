using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XamlPlayground.Services.IntelliSense;

public enum EditorCompletionKind
{
    Keyword,
    Type,
    Namespace,
    Property,
    Method,
    Event,
    Field,
    EnumValue,
    Value,
    Snippet
}

public sealed record EditorCompletionItem(
    string Text,
    string InsertionText,
    string? Description,
    EditorCompletionKind Kind,
    double Priority = 0,
    int? CaretOffset = null);

public sealed record EditorCompletionResult(
    int ReplacementStart,
    int ReplacementEnd,
    IReadOnlyList<EditorCompletionItem> Items);

public sealed record EditorQuickInfo(int StartOffset, int EndOffset, string Text);

public sealed record EditorSignatureItem(string Header, string Content);

public sealed record EditorSignatureHelp(
    IReadOnlyList<EditorSignatureItem> Items,
    int SelectedIndex,
    int SelectedParameterIndex);

public enum EditorDiagnosticSeverity
{
    Hint,
    Information,
    Warning,
    Error
}

public sealed record EditorDiagnostic(
    int StartOffset,
    int EndOffset,
    string Message,
    EditorDiagnosticSeverity Severity,
    string? Code = null);

public sealed record EditorLocation(
    string? FilePath,
    int StartOffset,
    int EndOffset,
    int Line,
    int Column,
    string PreviewText);

public sealed record EditorReference(
    EditorLocation Location,
    bool IsDefinition);

public sealed record EditorDocumentSymbol(
    string Name,
    string Detail,
    EditorCompletionKind Kind,
    int StartOffset,
    int EndOffset,
    int SelectionStartOffset,
    int SelectionEndOffset,
    IReadOnlyList<EditorDocumentSymbol> Children);

public interface IEditorIntelliSenseService
{
    Task<EditorCompletionResult?> GetCompletionsAsync(
        string text,
        int position,
        bool explicitInvocation,
        char? triggerCharacter,
        CancellationToken cancellationToken);

    Task<EditorQuickInfo?> GetQuickInfoAsync(
        string text,
        int position,
        CancellationToken cancellationToken);

    Task<EditorSignatureHelp?> GetSignatureHelpAsync(
        string text,
        int position,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EditorDiagnostic>> GetDiagnosticsAsync(
        string text,
        CancellationToken cancellationToken);

    Task<EditorLocation?> GetDefinitionAsync(
        string text,
        int position,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EditorReference>> GetReferencesAsync(
        string text,
        int position,
        CancellationToken cancellationToken);

    Task<string?> FormatDocumentAsync(
        string text,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EditorDocumentSymbol>> GetDocumentSymbolsAsync(
        string text,
        CancellationToken cancellationToken);
}
