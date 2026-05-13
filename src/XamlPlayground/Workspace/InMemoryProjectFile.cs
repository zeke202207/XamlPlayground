using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XamlPlayground.Workspace;

public sealed partial class InMemoryProjectFile : ObservableObject
{
    private readonly Action<InMemoryProjectFile>? _changed;
    private string _textSnapshot;

    [ObservableProperty] private string _path;
    [ObservableProperty] private TextDocument _document;
    [ObservableProperty] private bool _isDirty;

    public InMemoryProjectFile(
        string path,
        string text,
        ProjectFileKind kind,
        Action<InMemoryProjectFile>? changed = null,
        TextDocument? document = null,
        bool includeInRuntimePreview = true,
        bool includeInCompilation = true,
        string? sourcePath = null,
        IStorageFile? sourceStorageFile = null)
    {
        _path = NormalizePath(path);
        _textSnapshot = text;
        if (document is { })
        {
            try
            {
                _textSnapshot = document.Text;
            }
            catch (InvalidOperationException)
            {
            }
        }

        _document = document ?? new TextDocument { Text = _textSnapshot };
        Kind = kind;
        IncludeInRuntimePreview = includeInRuntimePreview;
        IncludeInCompilation = includeInCompilation;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath;
        SourceStorageFile = sourceStorageFile;
        _changed = changed;
        _document.TextChanged += DocumentOnTextChanged;
    }

    public ProjectFileKind Kind { get; }

    public bool IncludeInRuntimePreview { get; }

    public bool IncludeInCompilation { get; }

    public string? SourcePath { get; }

    public IStorageFile? SourceStorageFile { get; }

    public bool CanSaveToSource => SourcePath is not null || SourceStorageFile is not null;

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
        get
        {
            return TryGetDocumentText(out var text)
                ? text
                : _textSnapshot;
        }
        set
        {
            _textSnapshot = value;
            try
            {
                Document.Text = value;
            }
            catch (InvalidOperationException)
            {
                ReplaceDocument(new TextDocument { Text = value });
                IsDirty = true;
                _changed?.Invoke(this);
            }
        }
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    public void EnsureDocumentOnCurrentThread()
    {
        if (TryGetDocumentText(out var text))
        {
            _textSnapshot = text;
            return;
        }

        ReplaceDocument(new TextDocument { Text = _textSnapshot });
    }

    public async Task SaveToSourceAsync()
    {
        if (SourcePath is { } sourcePath)
        {
            await File.WriteAllTextAsync(sourcePath, Text);
            MarkClean();
            return;
        }

        if (SourceStorageFile is { } sourceStorageFile)
        {
            await using var stream = await sourceStorageFile.OpenWriteAsync();
            try
            {
                if (!stream.CanSeek)
                {
                    throw new NotSupportedException("The storage file write stream does not support truncation.");
                }

                stream.SetLength(0);
                stream.Position = 0;
            }
            catch (NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Unable to safely save {Path} because the storage provider does not support truncating the existing file.");
            }

            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(Text);
            }

            MarkClean();
        }
    }

    private void DocumentOnTextChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, Document))
        {
            return;
        }

        _textSnapshot = Document.Text;
        IsDirty = true;
        _changed?.Invoke(this);
    }

    private bool TryGetDocumentText(out string text)
    {
        try
        {
            text = Document.Text;
            return true;
        }
        catch (InvalidOperationException)
        {
            text = _textSnapshot;
            return false;
        }
    }

    private void ReplaceDocument(TextDocument document)
    {
        try
        {
            Document.TextChanged -= DocumentOnTextChanged;
        }
        catch (InvalidOperationException)
        {
        }

        document.TextChanged += DocumentOnTextChanged;
        Document = document;
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
