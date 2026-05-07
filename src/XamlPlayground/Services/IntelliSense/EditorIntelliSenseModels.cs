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
}
