using System;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XamlPlayground.Workspace;

public sealed partial class InMemoryProjectFile : ObservableObject
{
    private readonly Action<InMemoryProjectFile>? _changed;

    [ObservableProperty] private string _path;
    [ObservableProperty] private TextDocument _document;
    [ObservableProperty] private bool _isDirty;

    public InMemoryProjectFile(
        string path,
        string text,
        ProjectFileKind kind,
        Action<InMemoryProjectFile>? changed = null,
        TextDocument? document = null,
        bool includeInRuntimePreview = true)
    {
        _path = NormalizePath(path);
        _document = document ?? new TextDocument { Text = text };
        Kind = kind;
        IncludeInRuntimePreview = includeInRuntimePreview;
        _changed = changed;
        _document.TextChanged += DocumentOnTextChanged;
    }

    public ProjectFileKind Kind { get; }

    public bool IncludeInRuntimePreview { get; }

    public string Name => GetFileName(Path);

    public string Extension
    {
        get
        {
            var index = Path.LastIndexOf('.');
            return index < 0 ? string.Empty : Path[index..];
        }
    }

    public bool CanEdit => Kind is ProjectFileKind.Xaml or ProjectFileKind.CSharp or ProjectFileKind.Resource or ProjectFileKind.ProjectFile or ProjectFileKind.Text;

    public bool IsXaml => Kind is ProjectFileKind.Xaml or ProjectFileKind.Resource;

    public bool IsCSharp => Kind == ProjectFileKind.CSharp;

    public string Text
    {
        get => Document.Text;
        set => Document.Text = value;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private void DocumentOnTextChanged(object? sender, EventArgs e)
    {
        IsDirty = true;
        _changed?.Invoke(this);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string GetFileName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }
}
