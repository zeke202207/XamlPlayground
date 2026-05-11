using System;
using System.Collections.Generic;

namespace XamlPlayground.Services.VisualEditing;

public sealed record XamlElementSelector(string? Name = null, IReadOnlyList<int>? Path = null, string? TypeName = null)
{
    public static XamlElementSelector ByName(string name)
    {
        return new XamlElementSelector(Name: name);
    }

    public static XamlElementSelector ByPath(params int[] path)
    {
        return new XamlElementSelector(Path: path);
    }
}

public sealed record XamlAttributeSnapshot(
    string Name,
    string Value,
    int Start,
    int Length);

public sealed record XamlElementSnapshot(
    XamlElementSelector Selector,
    string TypeName,
    string? Name,
    IReadOnlyList<int> Path,
    int Start,
    int Length,
    IReadOnlyDictionary<string, string> Attributes,
    int ChildElementCount);

public sealed record XamlDocumentSnapshot(
    string Text,
    XamlElementSnapshot? Root,
    IReadOnlyList<XamlElementSnapshot> Elements,
    IReadOnlyList<string> Diagnostics);

public sealed record XamlMutationResult(
    string Text,
    XamlDocumentSnapshot Snapshot,
    IReadOnlyList<string> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;
}

public enum XamlMutationKind
{
    SetProperty,
    RemoveProperty,
    InsertChild,
    RemoveElement,
    RenameElement,
    ReplaceElement,
    DuplicateElement,
    MoveElement,
    ReorderElement,
    WrapElement,
    UnwrapElement,
    SetMemberElement,
    RemoveMemberElement
}

public sealed record XamlMutationRequest(
    XamlMutationKind Kind,
    XamlElementSelector Selector,
    string? PropertyName = null,
    string? Value = null,
    string? Xaml = null,
    string? TypeName = null,
    XamlElementSelector? TargetParentSelector = null,
    int? ChildIndex = null);

public interface IXamlMutationEngine
{
    XamlDocumentSnapshot Analyze(string xaml);

    XamlMutationResult SetProperty(
        string xaml,
        XamlElementSelector selector,
        string propertyName,
        string value);

    XamlMutationResult RemoveProperty(
        string xaml,
        XamlElementSelector selector,
        string propertyName);

    XamlMutationResult InsertChild(
        string xaml,
        XamlElementSelector parentSelector,
        string childXaml,
        int? childIndex = null);

    XamlMutationResult RemoveElement(
        string xaml,
        XamlElementSelector selector);

    XamlMutationResult RenameElement(
        string xaml,
        XamlElementSelector selector,
        string typeName);

    XamlMutationResult ReplaceElement(
        string xaml,
        XamlElementSelector selector,
        string replacementXaml);

    XamlMutationResult DuplicateElement(
        string xaml,
        XamlElementSelector selector);

    XamlMutationResult MoveElement(
        string xaml,
        XamlElementSelector selector,
        XamlElementSelector targetParentSelector,
        int? childIndex = null);

    XamlMutationResult ReorderElement(
        string xaml,
        XamlElementSelector selector,
        int childIndex);

    XamlMutationResult WrapElement(
        string xaml,
        XamlElementSelector selector,
        string wrapperXaml);

    XamlMutationResult UnwrapElement(
        string xaml,
        XamlElementSelector selector);

    XamlMutationResult SetMemberElement(
        string xaml,
        XamlElementSelector selector,
        string propertyName,
        string valueXaml);

    XamlMutationResult RemoveMemberElement(
        string xaml,
        XamlElementSelector selector,
        string propertyName);

    XamlMutationResult Batch(
        string xaml,
        IEnumerable<XamlMutationRequest> requests);
}

public sealed class XamlMutationException : InvalidOperationException
{
    public XamlMutationException(string message)
        : base(message)
    {
    }
}
