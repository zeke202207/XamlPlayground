using System.Collections.ObjectModel;

namespace XamlPlayground.Workspace;

public sealed class InMemorySolution
{
    public InMemorySolution(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public ObservableCollection<InMemoryProject> Projects { get; } = new();
}
