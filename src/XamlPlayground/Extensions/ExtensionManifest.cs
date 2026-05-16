using System.Collections.Generic;

namespace XamlPlayground.Extensions;

public sealed class ExtensionManifest
{
    public ExtensionManifest(
        ExtensionIdentity identity,
        IEnumerable<string>? activationEvents = null,
        ExtensionContributions? contributions = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Identity = identity ?? throw new System.ArgumentNullException(nameof(identity));
        ActivationEvents = ExtensionCollections.CopyList(activationEvents);
        Contributions = contributions ?? ExtensionContributions.Empty;
        Metadata = ExtensionCollections.CopyDictionary(metadata);
    }

    public ExtensionIdentity Identity { get; }

    public IReadOnlyList<string> ActivationEvents { get; }

    public ExtensionContributions Contributions { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class ExtensionIdentity
{
    public ExtensionIdentity(string id, string displayName, string version, string? publisher = null, string? description = null)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        DisplayName = ExtensionCollections.RequireText(displayName, nameof(displayName));
        Version = ExtensionCollections.RequireText(version, nameof(version));
        Publisher = publisher;
        Description = description;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public string? Publisher { get; }

    public string? Description { get; }
}
