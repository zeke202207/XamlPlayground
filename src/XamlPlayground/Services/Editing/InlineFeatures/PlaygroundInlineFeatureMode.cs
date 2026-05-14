using System;
using System.Collections.Generic;
using System.Linq;

namespace XamlPlayground.Services.Editing.InlineFeatures;

public enum PlaygroundInlineFeatureMode
{
    Auto,
    SampleXaml,
    SampleCode,
    WorkspaceFile
}

internal sealed record PlaygroundInlineDocumentSnapshot(
    string Path,
    string Text,
    bool IsXaml,
    bool IsResource,
    bool IsCSharp);

internal sealed record PlaygroundInlineFeatureSnapshot(
    string CurrentPath,
    IReadOnlyList<PlaygroundInlineDocumentSnapshot> Documents)
{
    public PlaygroundInlineDocumentSnapshot? CurrentDocument =>
        Documents.FirstOrDefault(document => string.Equals(document.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));
}
