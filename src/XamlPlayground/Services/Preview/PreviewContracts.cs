using System;
using System.Collections.Generic;
using System.Linq;
using XamlPlayground.Workspace;

namespace XamlPlayground.Services.Preview;

public enum PreviewExecutionMode
{
    InlineDesign,
    IsolatedRemoteDesigner,
    IsolatedFullHost,
    BrowserIframe
}

public enum PreviewUpdateKind
{
    Initial,
    None,
    XamlOnly,
    ResourcesOnly,
    XamlAndResources,
    CodeOrProject,
    References,
    Mixed
}

public enum PreviewReloadStrategy
{
    NoOp,
    InlineReload,
    RemoteLiveXamlUpdate,
    HostResourceUpdate,
    HostRecompile,
    RestartHost
}

public enum PreviewDiagnosticKind
{
    Compiler,
    Xaml,
    Resource,
    Runtime,
    Host,
    Protocol
}

public sealed record PreviewSourceFile(
    string Path,
    ProjectFileKind Kind,
    string Text,
    bool IncludeInRuntimePreview,
    bool IncludeInCompilation,
    string? SourcePath)
{
    public bool IsLooseXaml => Kind is ProjectFileKind.Xaml or ProjectFileKind.Resource;
}

public sealed record PreviewAssemblyReference(
    string Name,
    string? FilePath,
    bool IsReferenceAssembly,
    bool IsRuntimeAssembly,
    bool HasImage,
    string? Fingerprint);

public sealed record PreviewDiagnostic(
    PreviewDiagnosticKind Kind,
    string Message,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? ExceptionType = null,
    string? StackTrace = null);

public sealed record PreviewMessage(
    string Type,
    Guid RequestId,
    string? SessionId,
    string? JsonPayload);

public sealed record PreviewSnapshot(
    Guid RequestId,
    string SolutionName,
    string ProjectName,
    string RootNamespace,
    string AssemblyName,
    string ActiveXamlPath,
    string? ActiveCodeBehindPath,
    string? AppXamlPath,
    string? TargetFramework,
    bool IsMsBuildWorkspace,
    string? ProjectFilePath,
    string? WorkspaceRootPath,
    string? OutputAssemblyPath,
    IReadOnlyList<PreviewSourceFile> Files,
    IReadOnlyList<PreviewAssemblyReference> AssemblyReferences)
{
    public PreviewSourceFile? ActiveFile =>
        Files.FirstOrDefault(file => string.Equals(file.Path, ActiveXamlPath, StringComparison.OrdinalIgnoreCase));

    public PreviewSourceFile? AppXamlFile =>
        AppXamlPath is null
            ? null
            : Files.FirstOrDefault(file => string.Equals(file.Path, AppXamlPath, StringComparison.OrdinalIgnoreCase));
}

public sealed record PreviewChangeSet(
    PreviewUpdateKind Kind,
    IReadOnlyList<string> ChangedPaths,
    bool ReferencesChanged)
{
    public bool RequiresCompilation =>
        ReferencesChanged ||
        Kind is PreviewUpdateKind.Initial or PreviewUpdateKind.CodeOrProject or PreviewUpdateKind.References or PreviewUpdateKind.Mixed;
}

public sealed record PreviewHostCapabilities(
    PreviewExecutionMode Mode,
    bool IsIsolated,
    bool SupportsLiveXamlUpdates,
    bool SupportsResourceUpdates,
    bool SupportsHostCompilation)
{
    public static PreviewHostCapabilities InlineDesign { get; } = new(
        PreviewExecutionMode.InlineDesign,
        IsIsolated: false,
        SupportsLiveXamlUpdates: true,
        SupportsResourceUpdates: true,
        SupportsHostCompilation: false);

    public static PreviewHostCapabilities RemoteDesigner { get; } = new(
        PreviewExecutionMode.IsolatedRemoteDesigner,
        IsIsolated: true,
        SupportsLiveXamlUpdates: true,
        SupportsResourceUpdates: false,
        SupportsHostCompilation: false);

    public static PreviewHostCapabilities IsolatedFullHost { get; } = new(
        PreviewExecutionMode.IsolatedFullHost,
        IsIsolated: true,
        SupportsLiveXamlUpdates: true,
        SupportsResourceUpdates: true,
        SupportsHostCompilation: true);

    public static PreviewHostCapabilities BrowserIframe { get; } = new(
        PreviewExecutionMode.BrowserIframe,
        IsIsolated: true,
        SupportsLiveXamlUpdates: true,
        SupportsResourceUpdates: true,
        SupportsHostCompilation: false);
}

public sealed record PreviewReloadPlan(
    Guid RequestId,
    PreviewExecutionMode Mode,
    PreviewReloadStrategy Strategy,
    PreviewChangeSet Changes,
    bool IsIsolated,
    string Description);
