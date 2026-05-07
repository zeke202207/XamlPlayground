namespace XamlPlayground.Workspace;

public sealed record AvaloniaProjectTemplate(
    string Name,
    string ShortName,
    string Description,
    bool SupportsBrowser);
