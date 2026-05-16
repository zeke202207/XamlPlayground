using System;
using System.Collections.Generic;

namespace XamlPlayground.Services.Preview;

public enum PreviewToolingCommandKind
{
    InspectTree,
    SelectElement,
    SetPropertyValue,
    SetResourceReference,
    SetResourceValue,
    BeginAnimationPreview,
    StopAnimationPreview,
    RequestDiagnostics
}

public enum PreviewPropertyValueKind
{
    Literal,
    StaticResource,
    DynamicResource,
    Binding,
    Animation
}

public sealed record PreviewElementIdentity(
    string? Name,
    string? TypeName,
    string? XamlPath,
    int? SourceLine,
    int? SourceColumn,
    string? RuntimePath);

public sealed record PreviewPropertyMutation(
    PreviewElementIdentity Target,
    string PropertyName,
    PreviewPropertyValueKind ValueKind,
    string? Value,
    string? ResourceKey = null);

public sealed record PreviewResourceMutation(
    string ResourcePath,
    string ResourceKey,
    string? Value,
    bool IsDynamicReference);

public sealed record PreviewAnimationMutation(
    PreviewElementIdentity Target,
    string PropertyName,
    string? TimelineKey,
    string? SerializedTimeline,
    bool IsPlaying);

public sealed record PreviewToolingCommand(
    Guid RequestId,
    string SessionId,
    PreviewToolingCommandKind Kind,
    PreviewElementIdentity? Target = null,
    PreviewPropertyMutation? PropertyMutation = null,
    PreviewResourceMutation? ResourceMutation = null,
    PreviewAnimationMutation? AnimationMutation = null);

public sealed record PreviewToolingElement(
    PreviewElementIdentity Identity,
    string DisplayName,
    IReadOnlyList<PreviewToolingProperty> Properties,
    IReadOnlyList<PreviewToolingElement> Children);

public sealed record PreviewToolingProperty(
    string Name,
    string TypeName,
    string? Value,
    PreviewPropertyValueKind ValueKind,
    bool CanWrite);

public sealed record PreviewToolingSnapshot(
    Guid RequestId,
    string SessionId,
    PreviewToolingElement? Root,
    IReadOnlyList<PreviewDiagnostic> Diagnostics);
