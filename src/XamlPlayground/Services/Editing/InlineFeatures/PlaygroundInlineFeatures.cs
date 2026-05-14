using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using XamlPlayground.Editor.Minimap;
using XamlPlayground.Editor.Minimap.Inline;
using XamlPlayground.ViewModels;
using XamlPlayground.Workspace;

namespace XamlPlayground.Services.Editing.InlineFeatures;

public sealed class PlaygroundInlineFeatures : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PlaygroundInlineFeatures, MinimapTextEditor, bool>("IsEnabled", true);

    public static readonly AttachedProperty<MainViewModel?> ShellProperty =
        AvaloniaProperty.RegisterAttached<PlaygroundInlineFeatures, MinimapTextEditor, MainViewModel?>("Shell");

    public static readonly AttachedProperty<InMemoryProjectFile?> FileProperty =
        AvaloniaProperty.RegisterAttached<PlaygroundInlineFeatures, MinimapTextEditor, InMemoryProjectFile?>("File");

    public static readonly AttachedProperty<PlaygroundInlineFeatureMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<PlaygroundInlineFeatures, MinimapTextEditor, PlaygroundInlineFeatureMode>(
            "Mode",
            PlaygroundInlineFeatureMode.Auto);

    private static readonly AttachedProperty<InlineFeatureAttachment?> AttachmentProperty =
        AvaloniaProperty.RegisterAttached<PlaygroundInlineFeatures, MinimapTextEditor, InlineFeatureAttachment?>("Attachment");

    private PlaygroundInlineFeatures()
    {
    }

    static PlaygroundInlineFeatures()
    {
        IsEnabledProperty.Changed.AddClassHandler<MinimapTextEditor>(OnFeaturePropertyChanged);
        ShellProperty.Changed.AddClassHandler<MinimapTextEditor>(OnFeaturePropertyChanged);
        FileProperty.Changed.AddClassHandler<MinimapTextEditor>(OnFeaturePropertyChanged);
        ModeProperty.Changed.AddClassHandler<MinimapTextEditor>(OnFeaturePropertyChanged);
    }

    public static bool GetIsEnabled(MinimapTextEditor editor)
    {
        return editor.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(MinimapTextEditor editor, bool value)
    {
        editor.SetValue(IsEnabledProperty, value);
    }

    public static MainViewModel? GetShell(MinimapTextEditor editor)
    {
        return editor.GetValue(ShellProperty);
    }

    public static void SetShell(MinimapTextEditor editor, MainViewModel? value)
    {
        editor.SetValue(ShellProperty, value);
    }

    public static InMemoryProjectFile? GetFile(MinimapTextEditor editor)
    {
        return editor.GetValue(FileProperty);
    }

    public static void SetFile(MinimapTextEditor editor, InMemoryProjectFile? value)
    {
        editor.SetValue(FileProperty, value);
    }

    public static PlaygroundInlineFeatureMode GetMode(MinimapTextEditor editor)
    {
        return editor.GetValue(ModeProperty);
    }

    public static void SetMode(MinimapTextEditor editor, PlaygroundInlineFeatureMode value)
    {
        editor.SetValue(ModeProperty, value);
    }

    private static void OnFeaturePropertyChanged(MinimapTextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        UpdateAttachment(editor);
    }

    private static void UpdateAttachment(MinimapTextEditor editor)
    {
        editor.GetValue(AttachmentProperty)?.Dispose();
        editor.SetValue(AttachmentProperty, null);

        if (!GetIsEnabled(editor) || GetShell(editor) is not { } shell)
        {
            return;
        }

        var file = GetFile(editor);
        var mode = GetMode(editor);
        if (CreateExtension(shell, file, mode) is not { } extension)
        {
            return;
        }

        editor.InlineExtensions.Add(extension);
        editor.SetValue(AttachmentProperty, new InlineFeatureAttachment(editor, extension));
    }

    internal static PlaygroundInlineFeatureSnapshot? TryCreateSnapshot(MinimapTextEditor editor)
    {
        if (!GetIsEnabled(editor) || GetShell(editor) is not { } shell)
        {
            return null;
        }

        var file = GetFile(editor);
        var resolvedMode = ResolveMode(file, GetMode(editor));
        return resolvedMode switch
        {
            PlaygroundInlineFeatureMode.SampleXaml => CreateSampleXamlSnapshot(shell),
            PlaygroundInlineFeatureMode.SampleCode => CreateSampleCodeSnapshot(shell),
            PlaygroundInlineFeatureMode.WorkspaceFile when file is not null => CreateWorkspaceSnapshot(shell, file),
            _ => null
        };
    }

    private static IEditorInlineExtension? CreateExtension(
        MainViewModel shell,
        InMemoryProjectFile? file,
        PlaygroundInlineFeatureMode mode)
    {
        var resolvedMode = ResolveMode(file, mode);
        return resolvedMode switch
        {
            PlaygroundInlineFeatureMode.SampleXaml => new PlaygroundXamlInlineExtension(() => CreateSampleXamlSnapshot(shell)),
            PlaygroundInlineFeatureMode.SampleCode => new PlaygroundCSharpInlineExtension(() => CreateSampleCodeSnapshot(shell)),
            PlaygroundInlineFeatureMode.WorkspaceFile when file is { IsXaml: true } =>
                new PlaygroundXamlInlineExtension(() => CreateWorkspaceSnapshot(shell, file)),
            PlaygroundInlineFeatureMode.WorkspaceFile when file is { IsCSharp: true } =>
                new PlaygroundCSharpInlineExtension(() => CreateWorkspaceSnapshot(shell, file)),
            _ => null
        };
    }

    private static PlaygroundInlineFeatureMode ResolveMode(
        InMemoryProjectFile? file,
        PlaygroundInlineFeatureMode mode)
    {
        if (mode != PlaygroundInlineFeatureMode.Auto)
        {
            return mode;
        }

        if (file is { IsXaml: true } or { IsCSharp: true })
        {
            return PlaygroundInlineFeatureMode.WorkspaceFile;
        }

        return PlaygroundInlineFeatureMode.SampleXaml;
    }

    private static PlaygroundInlineFeatureSnapshot CreateSampleXamlSnapshot(MainViewModel shell)
    {
        const string fallbackPath = "Main.axaml";
        var sample = shell.CurrentSample;
        var project = shell.ActiveProject;
        var currentPath = FindProjectFilePath(project, sample?.Xaml) ?? fallbackPath;
        var documents = new List<PlaygroundInlineDocumentSnapshot>();

        if (project is not null)
        {
            documents.AddRange(CreateProjectDocuments(project));
        }

        if (sample is not null && documents.All(document => !string.Equals(document.Path, currentPath, StringComparison.OrdinalIgnoreCase)))
        {
            documents.Add(new PlaygroundInlineDocumentSnapshot(
                currentPath,
                sample.Xaml.Text,
                IsXaml: true,
                IsResource: LooksLikeResourceDictionary(sample.Xaml.Text),
                IsCSharp: false));
        }

        return new PlaygroundInlineFeatureSnapshot(currentPath, NormalizeDocuments(documents));
    }

    private static PlaygroundInlineFeatureSnapshot CreateSampleCodeSnapshot(MainViewModel shell)
    {
        const string fallbackPath = "Main.axaml.cs";
        var sample = shell.CurrentSample;
        var project = shell.ActiveProject;
        var currentPath = FindProjectFilePath(project, sample?.Code) ?? fallbackPath;
        var documents = new List<PlaygroundInlineDocumentSnapshot>();

        if (project is not null)
        {
            documents.AddRange(CreateProjectDocuments(project));
        }

        if (sample is not null && documents.All(document => !string.Equals(document.Path, currentPath, StringComparison.OrdinalIgnoreCase)))
        {
            documents.Add(new PlaygroundInlineDocumentSnapshot(
                currentPath,
                sample.Code.Text,
                IsXaml: false,
                IsResource: false,
                IsCSharp: true));
        }

        return new PlaygroundInlineFeatureSnapshot(currentPath, NormalizeDocuments(documents));
    }

    private static PlaygroundInlineFeatureSnapshot CreateWorkspaceSnapshot(
        MainViewModel shell,
        InMemoryProjectFile file)
    {
        var project = FindProject(shell, file) ?? shell.ActiveProject;
        var documents = project is null
            ? new[] { CreateDocumentSnapshot(file) }
            : CreateProjectDocuments(project);

        return new PlaygroundInlineFeatureSnapshot(file.Path, NormalizeDocuments(documents));
    }

    private static IEnumerable<PlaygroundInlineDocumentSnapshot> CreateProjectDocuments(InMemoryProject project)
    {
        return project.Files
            .Where(static file => file.IsXaml || file.IsCSharp)
            .Select(CreateDocumentSnapshot);
    }

    private static PlaygroundInlineDocumentSnapshot CreateDocumentSnapshot(InMemoryProjectFile file)
    {
        var text = file.Text;
        return new PlaygroundInlineDocumentSnapshot(
            file.Path,
            text,
            file.IsXaml,
            file.Kind == ProjectFileKind.Resource || LooksLikeResourceDictionary(text),
            file.IsCSharp);
    }

    private static IReadOnlyList<PlaygroundInlineDocumentSnapshot> NormalizeDocuments(
        IEnumerable<PlaygroundInlineDocumentSnapshot> documents)
    {
        return documents
            .GroupBy(static document => document.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static document => document.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static InMemoryProject? FindProject(MainViewModel shell, InMemoryProjectFile file)
    {
        return shell.Solution?.Projects.FirstOrDefault(project => project.Files.Contains(file));
    }

    private static string? FindProjectFilePath(InMemoryProject? project, AvaloniaEdit.Document.TextDocument? document)
    {
        if (project is null || document is null)
        {
            return null;
        }

        return project.Files.FirstOrDefault(file => ReferenceEquals(file.Document, document))?.Path;
    }

    private static bool LooksLikeResourceDictionary(string text)
    {
        return text.Contains("<ResourceDictionary", StringComparison.Ordinal) ||
               text.Contains(":ResourceDictionary", StringComparison.Ordinal);
    }

    private sealed class InlineFeatureAttachment : IDisposable
    {
        private readonly MinimapTextEditor _editor;
        private readonly IEditorInlineExtension _extension;
        private bool _disposed;

        public InlineFeatureAttachment(MinimapTextEditor editor, IEditorInlineExtension extension)
        {
            _editor = editor;
            _extension = extension;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _editor.InlineExtensions.Remove(_extension);
            _editor.CloseInlinePeek();
        }
    }
}
