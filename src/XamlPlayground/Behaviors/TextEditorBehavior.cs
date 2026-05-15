using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using TextMateSharp.Grammars;
using XamlPlayground;
using XamlPlayground.Editor.Minimap;
using XamlPlayground.Services.Editing;
using XamlPlayground.Services.Editing.InlineFeatures;
using XamlPlayground.Services.IntelliSense;
using XamlPlayground.ViewModels;
using XamlPlayground.Workspace;

namespace XamlPlayground.Behaviors;

public class TextEditorBehavior : Behavior<TextEditor>
{
    public static readonly StyledProperty<string?> ExtensionProperty = 
        AvaloniaProperty.Register<TextEditorBehavior, string?>(nameof(Extension));

    public static readonly StyledProperty<InMemoryProject?> ProjectProperty =
        AvaloniaProperty.Register<TextEditorBehavior, InMemoryProject?>(nameof(Project));

    public static readonly StyledProperty<InMemoryProjectFile?> FileProperty =
        AvaloniaProperty.Register<TextEditorBehavior, InMemoryProjectFile?>(nameof(File));

    public static readonly StyledProperty<MainViewModel?> ShellProperty =
        AvaloniaProperty.Register<TextEditorBehavior, MainViewModel?>(nameof(Shell));

    private static readonly TimeSpan FoldingUpdateDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DiagnosticsUpdateDelay = TimeSpan.FromMilliseconds(250);

    private TextEditor? _textEditor;
    private FoldingManager? _foldingManager;
    private DispatcherTimer? _foldingTimer;
    private XmlFoldingStrategy? _xmlFoldingStrategy;
    private CSharpFoldingStrategy? _csharpFoldingStrategy;
    private IEditorIntelliSenseService? _intelliSenseService;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _insightWindow;
    private EditorDiagnosticRenderer? _diagnosticRenderer;
    private AvaloniaEdit.TextMate.TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _textMateRegistryOptions;
    private TextDocument? _subscribedDocument;
    private ContextMenu? _editorContextMenu;
    private int? _contextMenuOffset;
    private bool _ownsEditorContextMenu;
    private bool _isApplyingXamlEdit;
    private int _completionRequestVersion;
    private int _quickInfoRequestVersion;
    private int _diagnosticsRequestVersion;
    private int _foldingRefreshVersion;
    private DispatcherTimer? _diagnosticsTimer;

    public string? Extension
    {
        get => GetValue(ExtensionProperty);
        set => SetValue(ExtensionProperty, value);
    }

    public InMemoryProject? Project
    {
        get => GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public InMemoryProjectFile? File
    {
        get => GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    public MainViewModel? Shell
    {
        get => GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public static void PrepareForDocumentReplacement(TextEditor textEditor)
    {
        foreach (var behavior in Interaction.GetBehaviors(textEditor).OfType<TextEditorBehavior>())
        {
            behavior.PrepareForDocumentReplacementCore(textEditor);
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is not { } textEditor)
        {
            return;
        }

        _textEditor = textEditor;

        _textEditor.TextArea.SelectionCornerRadius = 0;
        _textEditor.TextArea.KeyDown += TextAreaOnKeyDown;
        _textEditor.TextArea.TextEntered += TextAreaOnTextEntered;
        _textEditor.AttachedToVisualTree += TextEditorOnAttachedToVisualTree;
        _textEditor.PropertyChanged += TextEditorOnPropertyChanged;
        _textEditor.TextChanged += TextEditorOnTextChanged;
        _textEditor.DocumentChanged += TextEditorOnDocumentChanged;
        _textEditor.PointerPressed += TextEditorOnPointerPressed;
        _textEditor.PointerHover += TextEditorOnPointerHover;
        _textEditor.PointerHoverStopped += TextEditorOnPointerHoverStopped;
        SubscribeToDocument(_textEditor.Document);

        InstallFoldingManager();
        _foldingTimer = new DispatcherTimer { Interval = FoldingUpdateDelay };
        _foldingTimer.Tick += FoldingTimerOnTick;
        _diagnosticsTimer = new DispatcherTimer { Interval = DiagnosticsUpdateDelay };
        _diagnosticsTimer.Tick += DiagnosticsTimerOnTick;
        _diagnosticRenderer = new EditorDiagnosticRenderer();
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);

        ApplyExtensionMode();
        ApplyEditorTheme();
        InstallContextMenu(_textEditor);
    }

    protected override void OnDetaching()
    {
        if (_textEditor is { } textEditor)
        {
            textEditor.TextArea.KeyDown -= TextAreaOnKeyDown;
            textEditor.TextArea.TextEntered -= TextAreaOnTextEntered;
            textEditor.AttachedToVisualTree -= TextEditorOnAttachedToVisualTree;
            textEditor.PropertyChanged -= TextEditorOnPropertyChanged;
            textEditor.TextChanged -= TextEditorOnTextChanged;
            textEditor.DocumentChanged -= TextEditorOnDocumentChanged;
            textEditor.PointerPressed -= TextEditorOnPointerPressed;
            textEditor.PointerHover -= TextEditorOnPointerHover;
            textEditor.PointerHoverStopped -= TextEditorOnPointerHoverStopped;
        }

        if (_foldingTimer is { } foldingTimer)
        {
            foldingTimer.Stop();
            foldingTimer.Tick -= FoldingTimerOnTick;
        }

        if (_diagnosticsTimer is { } diagnosticsTimer)
        {
            diagnosticsTimer.Stop();
            diagnosticsTimer.Tick -= DiagnosticsTimerOnTick;
        }

        if (_diagnosticRenderer is not null && _textEditor is not null)
        {
            _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_diagnosticRenderer);
        }

        _foldingRefreshVersion++;
        _diagnosticsRequestVersion++;
        UninstallFoldingManager();
        SubscribeToDocument(null);
        CloseCompletionWindow();
        CloseInsightWindow();
        CloseQuickInfo();
        UninstallContextMenu();
        DisposeTextMateInstallation();

        _foldingTimer = null;
        _foldingManager = null;
        _xmlFoldingStrategy = null;
        _csharpFoldingStrategy = null;
        _intelliSenseService = null;
        _diagnosticRenderer = null;
        _subscribedDocument = null;
        _contextMenuOffset = null;
        _diagnosticsTimer = null;
        _textEditor = null;

        base.OnDetaching();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if ((change.Property == ExtensionProperty ||
             change.Property == ProjectProperty ||
             change.Property == FileProperty) &&
            _textEditor is not null)
        {
            ApplyExtensionMode();
        }
    }

    private void TextEditorOnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyEditorTheme();
    }

    private void TextEditorOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(StyledElement.ActualThemeVariant))
        {
            ApplyTextMateTheme();
            ApplyEditorTheme();
            return;
        }

        if (e.Property.Name == nameof(TextEditor.Background) ||
            e.Property.Name == nameof(TextEditor.Foreground))
        {
            ApplyEditorTheme();
        }
    }

    private void ApplyEditorTheme()
    {
        if (_textEditor is not { } textEditor)
        {
            return;
        }

        textEditor.Options.HighlightCurrentLine = true;

        if (FindBrush(textEditor, "EditorSelectionBrush") is { } selectionBrush)
        {
            textEditor.TextArea.SelectionBrush = selectionBrush;
        }

        if (FindBrush(textEditor, "EditorSelectionForegroundBrush") is { } selectionForegroundBrush)
        {
            textEditor.TextArea.SelectionForeground = selectionForegroundBrush;
        }

        if (FindBrush(textEditor, "EditorCaretBrush") is { } caretBrush)
        {
            textEditor.TextArea.Caret.CaretBrush = caretBrush;
        }

        if (FindBrush(textEditor, "EditorCurrentLineBackgroundBrush") is { } currentLineBackgroundBrush)
        {
            textEditor.TextArea.TextView.CurrentLineBackground = currentLineBackgroundBrush;
        }
    }

    private static IBrush? FindBrush(TextEditor textEditor, string resourceKey)
    {
        return textEditor.TryFindResource(resourceKey, textEditor.ActualThemeVariant, out var value)
            ? value as IBrush
            : null;
    }

    private async void TextAreaOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textEditor is null)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var hasControlOrMeta = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

        if (e.Key == Key.Escape)
        {
            CloseCompletionWindow();
            CloseInsightWindow();
            CloseQuickInfo();
            if (_textEditor is MinimapTextEditor minimapTextEditor &&
                minimapTextEditor.InlineViewZones.Count > 0)
            {
                minimapTextEditor.CloseInlinePeek();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Space && hasControlOrMeta)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                await ShowSignatureHelpAsync();
            }
            else
            {
                await ShowCompletionAsync(explicitInvocation: true, triggerCharacter: null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            IsXmlExtension(Extension) &&
            modifiers == KeyModifiers.None &&
            TryInsertXamlElementBreak())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A)
        {
            var hasOtherModifiers = (modifiers & ~(KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (hasControlOrMeta && !hasOtherModifiers)
            {
                _textEditor.SelectAll();
                e.Handled = true;
            }
        }
    }

    private async void TextAreaOnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_textEditor is null ||
            _textEditor.IsReadOnly ||
            string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var trigger = e.Text[^1];
        if (IsXmlExtension(Extension))
        {
            TryHandleXamlTextEntered(e.Text);
        }

        if (_intelliSenseService is null)
        {
            return;
        }

        if (trigger is '(' or ',')
        {
            await ShowSignatureHelpAsync();
        }
        else if (trigger is ')' or ';' or '}')
        {
            CloseInsightWindow();
        }

        if (_completionWindow is not null && trigger != '.')
        {
            return;
        }

        await ShowCompletionAsync(explicitInvocation: false, trigger);
    }

    private async void TextEditorOnPointerHover(object? sender, PointerEventArgs e)
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        var textEditor = _textEditor;
        var position = textEditor.GetPositionFromPoint(e.GetPosition(textEditor));
        if (position is null)
        {
            return;
        }

        var line = Math.Clamp(position.Value.Line, 1, textEditor.Document.LineCount);
        var documentLine = textEditor.Document.GetLineByNumber(line);
        var column = Math.Clamp(position.Value.Column, 1, documentLine.Length + 1);
        var offset = textEditor.Document.GetOffset(line, column);
        if (_diagnosticRenderer?.GetDiagnosticAt(offset) is { } diagnostic)
        {
            ToolTip.SetTip(_textEditor, FormatDiagnostic(diagnostic));
            ToolTip.SetIsOpen(_textEditor, true);
            return;
        }

        var requestVersion = ++_quickInfoRequestVersion;

        try
        {
            var quickInfo = await _intelliSenseService.GetQuickInfoAsync(
                textEditor.Document.Text,
                offset,
                default);

            if (requestVersion != _quickInfoRequestVersion || quickInfo is null || _textEditor is null)
            {
                return;
            }

            ToolTip.SetTip(_textEditor, quickInfo.Text);
            ToolTip.SetIsOpen(_textEditor, true);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void TextEditorOnPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        CloseQuickInfo();
    }

    private void TextEditorOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_textEditor?.Document is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_textEditor);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (TryGetOffsetFromPoint(e.GetPosition(_textEditor), out var offset))
        {
            _contextMenuOffset = offset;
            if (!IsOffsetInsideSelection(offset))
            {
                _textEditor.Select(offset, 0);
                _textEditor.CaretOffset = offset;
            }
        }
        else
        {
            _contextMenuOffset = _textEditor.CaretOffset;
        }
    }

    private void InstallContextMenu(TextEditor textEditor)
    {
        if (textEditor.ContextMenu is not null)
        {
            return;
        }

        _editorContextMenu = new ContextMenu();
        _editorContextMenu.Opening += EditorContextMenuOnOpening;
        textEditor.ContextMenu = _editorContextMenu;
        _ownsEditorContextMenu = true;
    }

    private void UninstallContextMenu()
    {
        if (_editorContextMenu is not null)
        {
            _editorContextMenu.Opening -= EditorContextMenuOnOpening;
        }

        if (_ownsEditorContextMenu &&
            _textEditor is not null &&
            ReferenceEquals(_textEditor.ContextMenu, _editorContextMenu))
        {
            _textEditor.ContextMenu = null;
        }

        _editorContextMenu = null;
        _ownsEditorContextMenu = false;
    }

    private void EditorContextMenuOnOpening(object? sender, CancelEventArgs e)
    {
        if (_editorContextMenu is null || _textEditor?.Document is null)
        {
            return;
        }

        var offset = Math.Clamp(_contextMenuOffset ?? _textEditor.CaretOffset, 0, _textEditor.Document.TextLength);
        var hasSelection = _textEditor.SelectionLength > 0;
        var isEditable = !_textEditor.IsReadOnly;
        var canUseIntelliSense = _intelliSenseService is not null;
        var canResolveResource = _textEditor is MinimapTextEditor minimapTextEditor &&
                                 PlaygroundEditorContextActions.CanResolveResourceAt(minimapTextEditor, offset);

        var menu = _editorContextMenu;
        menu.Items.Clear();

        AddMenuItem("Cut", async () => await CutSelectionAsync(), isEditable && hasSelection);
        AddMenuItem("Copy", async () => await CopySelectionAsync(), hasSelection);
        AddMenuItem("Paste", async () => await PasteAsync(), isEditable);
        AddSeparator();
        AddMenuItem("Peek Definition", async () => await TryPeekDefinitionAsync(offset), canResolveResource || canUseIntelliSense);
        AddMenuItem("Go to Definition", async () => await TryGoToDefinitionAsync(offset), canResolveResource || canUseIntelliSense);
        AddMenuItem("Peek References", async () => await TryPeekReferencesAsync(offset), canResolveResource || canUseIntelliSense);
        AddMenuItem("Close Peek", CloseInlinePeek, _textEditor is MinimapTextEditor { InlineViewZones.Count: > 0 });
        AddSeparator();
        AddMenuItem("Show Suggestions", async () => await ShowCompletionAsync(explicitInvocation: true, triggerCharacter: null), canUseIntelliSense);
        AddMenuItem("Parameter Hints", async () => await ShowSignatureHelpAsync(), canUseIntelliSense);
        AddMenuItem("Quick Info", async () => await ShowQuickInfoAtOffsetAsync(offset), canUseIntelliSense);
        AddSeparator();
        AddMenuItem("Format Document", FormatDocument, isEditable && canUseIntelliSense);
        AddMenuItem("Toggle Line Comment", ToggleLineComment, isEditable && (IsXmlExtension(Extension) || IsCSharpExtension(Extension)));
        AddMenuItem("Indent Selection", () => IndentSelection(outdent: false), isEditable);
        AddMenuItem("Outdent Selection", () => IndentSelection(outdent: true), isEditable);
        AddSeparator();
        AddMenuItem("Fold", () => SetCurrentFold(isFolded: true), _foldingManager?.AllFoldings.Any(static folding => !folding.IsFolded) == true);
        AddMenuItem("Unfold", () => SetCurrentFold(isFolded: false), _foldingManager?.AllFoldings.Any(static folding => folding.IsFolded) == true);
        AddMenuItem("Fold All", () => SetAllFoldings(isFolded: true), _foldingManager?.AllFoldings.Any(static folding => !folding.IsFolded) == true);
        AddMenuItem("Unfold All", () => SetAllFoldings(isFolded: false), _foldingManager?.AllFoldings.Any(static folding => folding.IsFolded) == true);
        AddSeparator();
        AddMenuItem("Select All", () => _textEditor.SelectAll(), _textEditor.Document.TextLength > 0);
        return;

        void AddMenuItem(string header, System.Action action, bool isEnabled = true)
        {
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = isEnabled
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void AddSeparator()
        {
            menu.Items.Add(new Separator());
        }
    }

    private bool TryGetOffsetFromPoint(Point point, out int offset)
    {
        offset = 0;
        if (_textEditor?.Document is not { } document)
        {
            return false;
        }

        var position = _textEditor.GetPositionFromPoint(point);
        if (position is null)
        {
            return false;
        }

        var lineNumber = Math.Clamp(position.Value.Line, 1, document.LineCount);
        var line = document.GetLineByNumber(lineNumber);
        var column = Math.Clamp(position.Value.Column, 1, line.Length + 1);
        offset = Math.Clamp(document.GetOffset(lineNumber, column), 0, document.TextLength);
        return true;
    }

    private bool IsOffsetInsideSelection(int offset)
    {
        if (_textEditor is null || _textEditor.SelectionLength <= 0)
        {
            return false;
        }

        var selectionStart = Math.Min(_textEditor.SelectionStart, _textEditor.SelectionStart + _textEditor.SelectionLength);
        var selectionEnd = Math.Max(_textEditor.SelectionStart, _textEditor.SelectionStart + _textEditor.SelectionLength);
        return offset >= selectionStart && offset <= selectionEnd;
    }

    private async System.Threading.Tasks.Task CopySelectionAsync()
    {
        if (_textEditor is null || _textEditor.SelectionLength <= 0)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(_textEditor)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(_textEditor.SelectedText);
        }
    }

    private async System.Threading.Tasks.Task CutSelectionAsync()
    {
        if (_textEditor?.Document is null || _textEditor.IsReadOnly || _textEditor.SelectionLength <= 0)
        {
            return;
        }

        await CopySelectionAsync();
        ReplaceSelection(string.Empty);
    }

    private async System.Threading.Tasks.Task PasteAsync()
    {
        if (_textEditor?.Document is null || _textEditor.IsReadOnly)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(_textEditor)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            ReplaceSelection(text);
        }
    }

    private void ReplaceSelection(string text)
    {
        if (_textEditor?.Document is not { } document)
        {
            return;
        }

        var start = Math.Clamp(_textEditor.SelectionStart, 0, document.TextLength);
        var length = Math.Clamp(_textEditor.SelectionLength, 0, document.TextLength - start);
        document.Replace(start, length, text);
        var caretOffset = Math.Clamp(start + text.Length, 0, document.TextLength);
        _textEditor.Select(caretOffset, 0);
        _textEditor.CaretOffset = caretOffset;
    }

    private async System.Threading.Tasks.Task ShowQuickInfoAtOffsetAsync(int offset)
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        var requestVersion = ++_quickInfoRequestVersion;
        try
        {
            var quickInfo = await _intelliSenseService.GetQuickInfoAsync(
                _textEditor.Document.Text,
                Math.Clamp(offset, 0, _textEditor.Document.TextLength),
                default);

            if (requestVersion != _quickInfoRequestVersion || quickInfo is null || _textEditor is null)
            {
                return;
            }

            ToolTip.SetTip(_textEditor, quickInfo.Text);
            ToolTip.SetIsOpen(_textEditor, true);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private async System.Threading.Tasks.Task TryPeekDefinitionAsync(int offset)
    {
        if (_textEditor is MinimapTextEditor minimapTextEditor &&
            PlaygroundEditorContextActions.TryShowResourceDefinitionPeek(minimapTextEditor, offset))
        {
            return;
        }

        await ShowLanguageDefinitionAsync(offset, navigate: false);
    }

    private async System.Threading.Tasks.Task TryGoToDefinitionAsync(int offset)
    {
        if (_textEditor is MinimapTextEditor minimapTextEditor &&
            PlaygroundEditorContextActions.TryGoToResourceDefinition(minimapTextEditor, offset))
        {
            return;
        }

        await ShowLanguageDefinitionAsync(offset, navigate: true);
    }

    private async System.Threading.Tasks.Task TryPeekReferencesAsync(int offset)
    {
        if (_textEditor is MinimapTextEditor minimapTextEditor &&
            PlaygroundEditorContextActions.TryShowResourceReferencesPeek(minimapTextEditor, offset))
        {
            return;
        }

        await ShowLanguageReferencesAsync(offset);
    }

    private async System.Threading.Tasks.Task ShowLanguageDefinitionAsync(int offset, bool navigate)
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        try
        {
            var definition = await _intelliSenseService.GetDefinitionAsync(
                _textEditor.Document.Text,
                Math.Clamp(offset, 0, _textEditor.Document.TextLength),
                default);
            if (definition is null)
            {
                return;
            }

            if (navigate && TryNavigateToLocation(definition))
            {
                return;
            }

            ShowLanguagePeek(
                offset,
                "Definition",
                FormatLocation(definition),
                string.IsNullOrWhiteSpace(definition.PreviewText) ? FormatLocation(definition) : definition.PreviewText,
                IsCSharpExtension(Extension) ? "csharp" : "xaml",
                180);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private async System.Threading.Tasks.Task ShowLanguageReferencesAsync(int offset)
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        try
        {
            var references = await _intelliSenseService.GetReferencesAsync(
                _textEditor.Document.Text,
                Math.Clamp(offset, 0, _textEditor.Document.TextLength),
                default);
            if (references.Count == 0)
            {
                return;
            }

            var lines = references
                .Take(12)
                .Select(reference => $"{(reference.IsDefinition ? "def" : "ref")} {FormatLocation(reference.Location)}");
            var suffix = references.Count > 12 ? $"{Environment.NewLine}+ {references.Count - 12} more" : string.Empty;
            ShowLanguagePeek(
                offset,
                "References",
                $"{references.Count} reference(s)",
                string.Join(Environment.NewLine, lines) + suffix,
                "text",
                220);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private bool TryNavigateToLocation(EditorLocation location)
    {
        if (location.EndOffset <= location.StartOffset)
        {
            return false;
        }

        if (!IsCurrentDocumentLocation(location))
        {
            return TryOpenWorkspaceLocation(location);
        }

        if (_textEditor?.Document is not { } document)
        {
            return false;
        }

        var start = Math.Clamp(location.StartOffset, 0, document.TextLength);
        var length = Math.Clamp(location.EndOffset - location.StartOffset, 0, document.TextLength - start);
        _textEditor.Select(start, length);
        _textEditor.CaretOffset = start;
        _textEditor.TextArea.Caret.BringCaretToView();
        return true;
    }

    private bool IsCurrentDocumentLocation(EditorLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.FilePath) ||
            File is null)
        {
            return true;
        }

        return string.Equals(NormalizePath(location.FilePath), NormalizePath(File.Path), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryOpenWorkspaceLocation(EditorLocation location)
    {
        if (Shell is not { } shell ||
            string.IsNullOrWhiteSpace(location.FilePath) ||
            FindWorkspaceFile(location.FilePath) is not { CanEdit: true } file)
        {
            return false;
        }

        shell.OpenWorkspaceFileLocation(file, location.StartOffset, location.EndOffset - location.StartOffset);
        return true;
    }

    private InMemoryProjectFile? FindWorkspaceFile(string path)
    {
        return Project?.FindFile(path) ??
               Shell?.ActiveProject?.FindFile(path) ??
               Shell?.Solution?.Projects
                   .Select(project => project.FindFile(path))
                   .FirstOrDefault(static file => file is not null);
    }

    private void ShowLanguagePeek(
        int offset,
        string title,
        string subtitle,
        string text,
        string language,
        double height)
    {
        if (_textEditor?.Document is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_textEditor is MinimapTextEditor minimapTextEditor)
        {
            var line = minimapTextEditor.Document.GetLineByOffset(
                Math.Clamp(offset, 0, minimapTextEditor.Document.TextLength));
            minimapTextEditor.ShowInlinePeek(line.LineNumber, title, subtitle, text, language, height);
            return;
        }

        ToolTip.SetTip(_textEditor, $"{subtitle}{Environment.NewLine}{text}");
        ToolTip.SetIsOpen(_textEditor, true);
    }

    private void CloseInlinePeek()
    {
        if (_textEditor is MinimapTextEditor minimapTextEditor)
        {
            minimapTextEditor.CloseInlinePeek();
        }
    }

    private async void FormatDocument()
    {
        if (_textEditor?.Document is not { } document ||
            _textEditor.IsReadOnly ||
            _intelliSenseService is null)
        {
            return;
        }

        try
        {
            var formatted = await _intelliSenseService.FormatDocumentAsync(document.Text, default);
            if (formatted is null)
            {
                return;
            }

            document.Replace(0, document.TextLength, formatted);
            _textEditor.CaretOffset = Math.Clamp(_textEditor.CaretOffset, 0, document.TextLength);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void ToggleLineComment()
    {
        if (_textEditor?.Document is null || _textEditor.IsReadOnly)
        {
            return;
        }

        if (IsXmlExtension(Extension))
        {
            ToggleXmlComment();
            return;
        }

        if (IsCSharpExtension(Extension))
        {
            ToggleLineComment("//");
        }
    }

    private void ToggleXmlComment()
    {
        if (_textEditor?.Document is not { } document)
        {
            return;
        }

        var (start, length) = GetSelectedOrCurrentLineRange();
        var selected = document.GetText(start, length);
        var trimmed = selected.Trim();
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal) &&
            trimmed.EndsWith("-->", StringComparison.Ordinal))
        {
            var commentStart = selected.IndexOf("<!--", StringComparison.Ordinal);
            var commentEnd = selected.LastIndexOf("-->", StringComparison.Ordinal);
            if (commentStart >= 0 && commentEnd >= commentStart)
            {
                var uncommented = selected.Remove(commentEnd, 3).Remove(commentStart, 4);
                document.Replace(start, length, uncommented);
                _textEditor.Select(start, uncommented.Length);
            }

            return;
        }

        var commented = $"<!--{selected}-->";
        document.Replace(start, length, commented);
        _textEditor.Select(start, commented.Length);
    }

    private void ToggleLineComment(string marker)
    {
        if (_textEditor?.Document is not { } document)
        {
            return;
        }

        var (startLine, endLine) = GetSelectedLineRange();
        var lines = Enumerable.Range(startLine, endLine - startLine + 1)
            .Select(document.GetLineByNumber)
            .ToArray();
        var uncomment = lines
            .Where(line => line.Length > 0)
            .All(line => document.GetText(line.Offset, line.Length).TrimStart().StartsWith(marker, StringComparison.Ordinal));

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            var lineText = document.GetText(line.Offset, line.Length);
            var indent = lineText.Length - lineText.TrimStart().Length;
            if (uncomment)
            {
                var markerOffset = lineText.IndexOf(marker, StringComparison.Ordinal);
                if (markerOffset >= 0)
                {
                    document.Remove(line.Offset + markerOffset, marker.Length);
                }
            }
            else
            {
                document.Insert(line.Offset + indent, marker);
            }
        }
    }

    private void IndentSelection(bool outdent)
    {
        if (_textEditor?.Document is not { } document || _textEditor.IsReadOnly)
        {
            return;
        }

        var (startLine, endLine) = GetSelectedLineRange();
        for (var lineNumber = endLine; lineNumber >= startLine; lineNumber--)
        {
            var line = document.GetLineByNumber(lineNumber);
            if (outdent)
            {
                if (line.Length > 0 && document.GetCharAt(line.Offset) == '\t')
                {
                    document.Remove(line.Offset, 1);
                    continue;
                }

                var remove = 0;
                while (remove < Math.Min(4, line.Length) && document.GetCharAt(line.Offset + remove) == ' ')
                {
                    remove++;
                }

                if (remove > 0)
                {
                    document.Remove(line.Offset, remove);
                }
            }
            else
            {
                document.Insert(line.Offset, "    ");
            }
        }
    }

    private (int Start, int Length) GetSelectedOrCurrentLineRange()
    {
        if (_textEditor?.Document is not { } document)
        {
            return (0, 0);
        }

        if (_textEditor.SelectionLength > 0)
        {
            var start = Math.Clamp(_textEditor.SelectionStart, 0, document.TextLength);
            var length = Math.Clamp(_textEditor.SelectionLength, 0, document.TextLength - start);
            return (start, length);
        }

        var line = document.GetLineByOffset(Math.Clamp(_textEditor.CaretOffset, 0, document.TextLength));
        return (line.Offset, line.Length);
    }

    private (int StartLine, int EndLine) GetSelectedLineRange()
    {
        if (_textEditor?.Document is not { } document)
        {
            return (1, 1);
        }

        var startOffset = Math.Clamp(_textEditor.SelectionStart, 0, document.TextLength);
        var endOffset = _textEditor.SelectionLength > 0
            ? Math.Clamp(_textEditor.SelectionStart + _textEditor.SelectionLength, 0, document.TextLength)
            : Math.Clamp(_textEditor.CaretOffset, 0, document.TextLength);
        if (endOffset > startOffset && endOffset < document.TextLength)
        {
            endOffset--;
        }

        return (
            document.GetLineByOffset(startOffset).LineNumber,
            document.GetLineByOffset(Math.Max(startOffset, endOffset)).LineNumber);
    }

    private void SetCurrentFold(bool isFolded)
    {
        if (_textEditor is null || _foldingManager is null)
        {
            return;
        }

        var caretOffset = Math.Clamp(_textEditor.CaretOffset, 0, _textEditor.Document?.TextLength ?? 0);
        var folding = _foldingManager.GetFoldingsContaining(caretOffset).FirstOrDefault(fold => fold.IsFolded != isFolded) ??
                      _foldingManager.GetNextFolding(caretOffset);
        if (folding is not null)
        {
            folding.IsFolded = isFolded;
        }
    }

    private void SetAllFoldings(bool isFolded)
    {
        if (_foldingManager is null)
        {
            return;
        }

        foreach (var folding in _foldingManager.AllFoldings)
        {
            folding.IsFolded = isFolded;
        }
    }

    private void TextEditorOnTextChanged(object? sender, EventArgs e)
    {
        ClearFoldings();
        ScheduleFoldingUpdate();
        ScheduleDiagnosticsUpdate();
    }

    private void TextEditorOnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        _foldingTimer?.Stop();
        _diagnosticsTimer?.Stop();
        _foldingRefreshVersion++;
        _diagnosticsRequestVersion++;
        UninstallFoldingManager();
        ClearDiagnostics();
        SubscribeToDocument(e.NewDocument);
        QueueFoldingRefresh();
        ScheduleDiagnosticsUpdate();
    }

    private void PrepareForDocumentReplacementCore(TextEditor textEditor)
    {
        if (!ReferenceEquals(_textEditor, textEditor))
        {
            return;
        }

        _foldingTimer?.Stop();
        _diagnosticsTimer?.Stop();
        _foldingRefreshVersion++;
        _diagnosticsRequestVersion++;
        UninstallFoldingManager();
        ClearDiagnostics();
        SubscribeToDocument(null);
    }

    private void DocumentOnChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_isApplyingXamlEdit ||
            _textEditor is null ||
            !IsXmlExtension(Extension))
        {
            return;
        }

        var caretOffset = _textEditor.CaretOffset;
        var editOffset = e.Offset + e.InsertionLength;

        try
        {
            _isApplyingXamlEdit = true;
            if (XamlEditorTypingService.TrySynchronizeTagRename(_textEditor.Document, editOffset, ref caretOffset))
            {
                _textEditor.CaretOffset = Math.Clamp(caretOffset, 0, _textEditor.Document.TextLength);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
        finally
        {
            _isApplyingXamlEdit = false;
        }
    }

    private void FoldingTimerOnTick(object? sender, EventArgs e)
    {
        _foldingTimer?.Stop();
        UpdateFoldings();
    }

    private async void DiagnosticsTimerOnTick(object? sender, EventArgs e)
    {
        _diagnosticsTimer?.Stop();
        await UpdateDiagnosticsAsync();
    }

    private void ScheduleDiagnosticsUpdate()
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            ClearDiagnostics();
            return;
        }

        if (_diagnosticsTimer is null)
        {
            _ = UpdateDiagnosticsAsync();
            return;
        }

        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();
    }

    private async System.Threading.Tasks.Task UpdateDiagnosticsAsync()
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            ClearDiagnostics();
            return;
        }

        var requestVersion = ++_diagnosticsRequestVersion;
        var text = _textEditor.Document.Text;
        try
        {
            var diagnostics = await _intelliSenseService.GetDiagnosticsAsync(text, default);
            if (requestVersion != _diagnosticsRequestVersion || _textEditor is null)
            {
                return;
            }

            _diagnosticRenderer?.SetDiagnostics(diagnostics);
            _textEditor.TextArea.TextView.InvalidateLayer(EditorDiagnosticRenderer.RenderLayer);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            ClearDiagnostics();
        }
    }

    private void ClearDiagnostics()
    {
        _diagnosticRenderer?.SetDiagnostics([]);
        _textEditor?.TextArea.TextView.InvalidateLayer(EditorDiagnosticRenderer.RenderLayer);
    }

    private void SubscribeToDocument(TextDocument? document)
    {
        if (ReferenceEquals(_subscribedDocument, document))
        {
            return;
        }

        if (_subscribedDocument is not null)
        {
            _subscribedDocument.Changed -= DocumentOnChanged;
        }

        _subscribedDocument = document;

        if (_subscribedDocument is not null)
        {
            _subscribedDocument.Changed += DocumentOnChanged;
        }
    }

    private bool TryInsertXamlElementBreak()
    {
        if (_textEditor?.Document is not { } document)
        {
            return false;
        }

        try
        {
            _isApplyingXamlEdit = true;
            if (!XamlEditorTypingService.TryInsertElementBreak(document, _textEditor.CaretOffset, out var newCaretOffset))
            {
                return false;
            }

            _textEditor.CaretOffset = Math.Clamp(newCaretOffset, 0, document.TextLength);
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return false;
        }
        finally
        {
            _isApplyingXamlEdit = false;
        }
    }

    private void TryHandleXamlTextEntered(string text)
    {
        if (_textEditor?.Document is not { } document)
        {
            return;
        }

        try
        {
            _isApplyingXamlEdit = true;
            if (XamlEditorTypingService.TryHandleTextEntered(document, _textEditor.CaretOffset, text, out var newCaretOffset))
            {
                _textEditor.CaretOffset = Math.Clamp(newCaretOffset, 0, document.TextLength);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
        finally
        {
            _isApplyingXamlEdit = false;
        }
    }

    private void ApplyExtensionMode()
    {
        if (_textEditor is null)
        {
            return;
        }

        ApplySyntaxHighlighting();

        if (IsXmlExtension(Extension))
        {
            _xmlFoldingStrategy ??= new XmlFoldingStrategy();
            _csharpFoldingStrategy = null;
            _intelliSenseService = new XamlIntelliSenseService();
        }
        else if (IsCSharpExtension(Extension))
        {
            _csharpFoldingStrategy ??= new CSharpFoldingStrategy();
            _xmlFoldingStrategy = null;
            _intelliSenseService = new CSharpIntelliSenseService(Project, File);
        }
        else
        {
            _xmlFoldingStrategy = null;
            _csharpFoldingStrategy = null;
            _intelliSenseService = null;
        }

        ClearDiagnostics();
        CloseCompletionWindow();
        CloseInsightWindow();
        CloseQuickInfo();
        if (Utilities.IsBrowser())
        {
            ScheduleFoldingUpdate();
        }
        else
        {
            QueueFoldingRefresh();
        }

        ScheduleDiagnosticsUpdate();
    }

    private void ApplySyntaxHighlighting()
    {
        if (_textEditor is null)
        {
            return;
        }

        var extension = NormalizeTextMateExtension(Extension);
        if (Utilities.IsBrowser())
        {
            // TextMateSharp uses Onigwrap; in the published browser app this can block
            // startup before the editor renders. The built-in highlighters are safe on WASM.
            DisposeTextMateInstallation();
            ApplyBuiltInSyntaxHighlighting(extension ?? Extension);
            return;
        }

        if (extension is null)
        {
            DisposeTextMateInstallation();
            ApplyBuiltInSyntaxHighlighting(Extension);
            return;
        }

        try
        {
            _textMateRegistryOptions ??= new RegistryOptions(GetTextMateThemeName(_textEditor));

            _textMateInstallation ??= AvaloniaEdit.TextMate.TextMate.InstallTextMate(
                _textEditor,
                _textMateRegistryOptions,
                true,
                HandleTextMateException);

            ApplyTextMateTheme();

            var scopeName = _textMateRegistryOptions.GetScopeByExtension(extension);
            if (!string.IsNullOrWhiteSpace(scopeName))
            {
                _textMateInstallation.SetGrammar(scopeName);
                _textEditor.SyntaxHighlighting = null;
                return;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            DisposeTextMateInstallation();
        }

        ApplyBuiltInSyntaxHighlighting(extension ?? Extension);
    }

    private void ApplyTextMateTheme()
    {
        if (_textEditor is null ||
            _textMateInstallation is null ||
            _textMateRegistryOptions is null)
        {
            return;
        }

        try
        {
            _textMateInstallation.SetTheme(_textMateRegistryOptions.LoadTheme(GetTextMateThemeName(_textEditor)));
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void DisposeTextMateInstallation()
    {
        if (_textMateInstallation is null)
        {
            return;
        }

        _textMateInstallation.Dispose();
        _textMateInstallation = null;
    }

    private static ThemeName GetTextMateThemeName(TextEditor textEditor)
    {
        return textEditor.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeName.VisualStudioDark
            : ThemeName.VisualStudioLight;
    }

    private static string? NormalizeTextMateExtension(string? extension)
    {
        if (IsXmlExtension(extension))
        {
            return ".xaml";
        }

        if (IsCSharpExtension(extension))
        {
            return ".cs";
        }

        return null;
    }

    private void HandleTextMateException(Exception exception)
    {
        Console.WriteLine(exception);
        if (_textEditor is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_textEditor is null)
            {
                return;
            }

            DisposeTextMateInstallation();
            ApplyBuiltInSyntaxHighlighting(NormalizeTextMateExtension(Extension) ?? Extension);
        });
    }

    private void ApplyBuiltInSyntaxHighlighting(string? extension)
    {
        if (_textEditor is null)
        {
            return;
        }

        _textEditor.SyntaxHighlighting = string.IsNullOrWhiteSpace(extension)
            ? null
            : HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    private async System.Threading.Tasks.Task ShowCompletionAsync(bool explicitInvocation, char? triggerCharacter)
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        var textEditor = _textEditor;
        var requestVersion = ++_completionRequestVersion;

        try
        {
            var result = await _intelliSenseService.GetCompletionsAsync(
                textEditor.Document.Text,
                textEditor.CaretOffset,
                explicitInvocation,
                triggerCharacter,
                default);

            if (requestVersion != _completionRequestVersion || result is null || result.Items.Count == 0 || _textEditor is null)
            {
                if (explicitInvocation)
                {
                    CloseCompletionWindow();
                }

                return;
            }

            CloseCompletionWindow();

            var completionWindow = new CompletionWindow(textEditor.TextArea)
            {
                StartOffset = Math.Clamp(result.ReplacementStart, 0, textEditor.Document.TextLength),
                EndOffset = Math.Clamp(result.ReplacementEnd, 0, textEditor.Document.TextLength),
                CloseAutomatically = true,
                CloseWhenCaretAtBeginning = false
            };

            completionWindow.CompletionList.IsFiltering = true;
            foreach (var item in result.Items)
            {
                completionWindow.CompletionList.CompletionData.Add(new EditorCompletionData(item));
            }

            completionWindow.Closed += CompletionWindowOnClosed;
            _completionWindow = completionWindow;
            completionWindow.Show();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private async System.Threading.Tasks.Task ShowSignatureHelpAsync()
    {
        if (_textEditor?.Document is null || _intelliSenseService is null)
        {
            return;
        }

        try
        {
            var signatureHelp = await _intelliSenseService.GetSignatureHelpAsync(
                _textEditor.Document.Text,
                _textEditor.CaretOffset,
                default);

            if (signatureHelp is null || signatureHelp.Items.Count == 0 || _textEditor is null)
            {
                CloseInsightWindow();
                return;
            }

            if (_insightWindow is null)
            {
                _insightWindow = new OverloadInsightWindow(_textEditor.TextArea);
                _insightWindow.Closed += InsightWindowOnClosed;
                _insightWindow.Show();
            }

            _insightWindow.Provider = new EditorOverloadProvider(signatureHelp);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void CompletionWindowOnClosed(object? sender, EventArgs e)
    {
        if (_completionWindow is not null)
        {
            _completionWindow.Closed -= CompletionWindowOnClosed;
            _completionWindow = null;
        }
    }

    private void InsightWindowOnClosed(object? sender, EventArgs e)
    {
        if (_insightWindow is not null)
        {
            _insightWindow.Closed -= InsightWindowOnClosed;
            _insightWindow = null;
        }
    }

    private void CloseCompletionWindow()
    {
        if (_completionWindow is null)
        {
            return;
        }

        _completionWindow.Closed -= CompletionWindowOnClosed;
        _completionWindow.Close();
        _completionWindow = null;
    }

    private void CloseInsightWindow()
    {
        if (_insightWindow is null)
        {
            return;
        }

        _insightWindow.Closed -= InsightWindowOnClosed;
        _insightWindow.Close();
        _insightWindow = null;
    }

    private void CloseQuickInfo()
    {
        _quickInfoRequestVersion++;
        if (_textEditor is not null)
        {
            ToolTip.SetIsOpen(_textEditor, false);
        }
    }

    private void ScheduleFoldingUpdate()
    {
        if (_foldingTimer is null)
        {
            QueueFoldingRefresh();
            return;
        }

        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    private void UpdateFoldings()
    {
        if (_textEditor?.Document is null ||
            _textEditor.TextArea.Document is null ||
            !ReferenceEquals(_textEditor.Document, _textEditor.TextArea.Document))
        {
            return;
        }

        InstallFoldingManager();
        if (_foldingManager is null)
        {
            return;
        }

        try
        {
            if (_xmlFoldingStrategy is not null)
            {
                _xmlFoldingStrategy.UpdateFoldings(_foldingManager, _textEditor.Document);
                return;
            }

            if (_csharpFoldingStrategy is not null)
            {
                _csharpFoldingStrategy.UpdateFoldings(_foldingManager, _textEditor.Document);
                return;
            }

            _foldingManager.UpdateFoldings([], -1);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            UninstallFoldingManager();
        }
    }

    private void QueueFoldingRefresh()
    {
        var version = ++_foldingRefreshVersion;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _foldingRefreshVersion)
                {
                    return;
                }

                UpdateFoldings();
            },
            DispatcherPriority.Loaded);
    }

    private void ClearFoldings()
    {
        if (_foldingManager is null)
        {
            return;
        }

        try
        {
            _foldingManager.UpdateFoldings([], -1);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            UninstallFoldingManager();
        }
    }

    private void InstallFoldingManager()
    {
        if (_textEditor?.TextArea.Document is null)
        {
            return;
        }

        _foldingManager ??= FoldingManager.Install(_textEditor.TextArea);
    }

    private void UninstallFoldingManager()
    {
        if (_foldingManager is not { } foldingManager)
        {
            return;
        }

        try
        {
            FoldingManager.Uninstall(foldingManager);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
        finally
        {
            _foldingManager = null;
        }
    }

    private static bool IsXmlExtension(string? extension)
    {
        return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCSharpExtension(string? extension)
    {
        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDiagnostic(EditorDiagnostic diagnostic)
    {
        var code = string.IsNullOrWhiteSpace(diagnostic.Code) ? string.Empty : $"{diagnostic.Code}: ";
        return $"{diagnostic.Severity}: {code}{diagnostic.Message}";
    }

    private static string FormatLocation(EditorLocation location)
    {
        var path = string.IsNullOrWhiteSpace(location.FilePath) ? "current document" : location.FilePath;
        var preview = string.IsNullOrWhiteSpace(location.PreviewText)
            ? string.Empty
            : $"{Environment.NewLine}{location.PreviewText}";
        return $"{path}:{location.Line}:{location.Column}{preview}";
    }

    private static string? NormalizePath(string? path)
    {
        var normalized = path?.Replace('\\', '/').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.Trim('/');
    }

    private sealed class EditorDiagnosticRenderer : IBackgroundRenderer
    {
        public const KnownLayer RenderLayer = KnownLayer.Text;

        private IReadOnlyList<EditorDiagnostic> _diagnostics = [];

        public KnownLayer Layer => RenderLayer;

        public void SetDiagnostics(IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public EditorDiagnostic? GetDiagnosticAt(int offset)
        {
            return _diagnostics
                .Where(diagnostic => diagnostic.StartOffset <= offset && offset < diagnostic.EndOffset)
                .OrderByDescending(static diagnostic => diagnostic.Severity)
                .ThenBy(static diagnostic => diagnostic.EndOffset - diagnostic.StartOffset)
                .FirstOrDefault();
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_diagnostics.Count == 0 || textView.Document is null)
            {
                return;
            }

            textView.EnsureVisualLines();
            foreach (var diagnostic in _diagnostics)
            {
                var start = Math.Clamp(diagnostic.StartOffset, 0, textView.Document.TextLength);
                var end = Math.Clamp(diagnostic.EndOffset, start, textView.Document.TextLength);
                if (start == end)
                {
                    end = Math.Min(textView.Document.TextLength, start + 1);
                }

                if (start == end)
                {
                    continue;
                }

                var segment = new TextSegment { StartOffset = start, EndOffset = end };
                var brush = GetDiagnosticBrush(diagnostic.Severity);
                var pen = new Pen(brush, 1);
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    DrawSquiggle(drawingContext, pen, rect);
                }
            }
        }

        private static IBrush GetDiagnosticBrush(EditorDiagnosticSeverity severity)
        {
            return severity switch
            {
                EditorDiagnosticSeverity.Error => Brushes.IndianRed,
                EditorDiagnosticSeverity.Warning => Brushes.Goldenrod,
                EditorDiagnosticSeverity.Information => Brushes.DeepSkyBlue,
                _ => Brushes.Gray
            };
        }

        private static void DrawSquiggle(DrawingContext drawingContext, Pen pen, Rect rect)
        {
            const double waveWidth = 4;
            const double waveHeight = 2;
            var y = Math.Max(rect.Top, rect.Bottom - waveHeight - 1);
            var x = rect.Left;
            while (x < rect.Right)
            {
                var mid = Math.Min(x + waveWidth / 2, rect.Right);
                var end = Math.Min(x + waveWidth, rect.Right);
                drawingContext.DrawLine(pen, new Point(x, y + waveHeight), new Point(mid, y));
                drawingContext.DrawLine(pen, new Point(mid, y), new Point(end, y + waveHeight));
                x += waveWidth;
            }
        }
    }

    private sealed class EditorCompletionData : ICompletionData
    {
        private readonly EditorCompletionItem _item;

        public EditorCompletionData(EditorCompletionItem item)
        {
            _item = item;
        }

        public IImage? Image => null;

        public string Text => _item.Text;

        public object Content => _item.Text;

        public object Description => string.IsNullOrWhiteSpace(_item.Description)
            ? _item.Kind.ToString()
            : $"{_item.Kind}{Environment.NewLine}{_item.Description}";

        public double Priority => _item.Priority;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            var document = textArea.Document;
            var offset = Math.Clamp(completionSegment.Offset, 0, document.TextLength);
            var length = Math.Clamp(completionSegment.Length, 0, document.TextLength - offset);
            document.Replace(offset, length, _item.InsertionText);

            var caretOffset = offset + (_item.CaretOffset ?? _item.InsertionText.Length);
            textArea.Caret.Offset = Math.Clamp(caretOffset, 0, document.TextLength);
        }
    }

    private sealed class EditorOverloadProvider : IOverloadProvider, INotifyPropertyChanged
    {
        private readonly EditorSignatureHelp _signatureHelp;
        private int _selectedIndex;

        public EditorOverloadProvider(EditorSignatureHelp signatureHelp)
        {
            _signatureHelp = signatureHelp;
            _selectedIndex = Math.Clamp(signatureHelp.SelectedIndex, 0, Math.Max(0, signatureHelp.Items.Count - 1));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var selectedIndex = Math.Clamp(value, 0, Math.Max(0, Count - 1));
                if (_selectedIndex == selectedIndex)
                {
                    return;
                }

                _selectedIndex = selectedIndex;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
            }
        }

        public int Count => _signatureHelp.Items.Count;

        public string CurrentIndexText => Count == 0 ? string.Empty : $"{SelectedIndex + 1} of {Count}";

        public object CurrentHeader => Count == 0 ? string.Empty : _signatureHelp.Items[SelectedIndex].Header;

        public object CurrentContent => Count == 0 ? string.Empty : _signatureHelp.Items[SelectedIndex].Content;
    }
}
