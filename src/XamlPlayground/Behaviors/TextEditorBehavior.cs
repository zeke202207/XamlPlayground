using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
using TextMateSharp.Grammars;
using XamlPlayground.Services.IntelliSense;

namespace XamlPlayground.Behaviors;

public class TextEditorBehavior : Behavior<TextEditor>
{
    public static readonly StyledProperty<string?> ExtensionProperty = 
        AvaloniaProperty.Register<TextEditorBehavior, string?>(nameof(Extension));

    private static readonly TimeSpan FoldingUpdateDelay = TimeSpan.FromMilliseconds(150);

    private TextEditor? _textEditor;
    private FoldingManager? _foldingManager;
    private DispatcherTimer? _foldingTimer;
    private XmlFoldingStrategy? _xmlFoldingStrategy;
    private CSharpFoldingStrategy? _csharpFoldingStrategy;
    private IEditorIntelliSenseService? _intelliSenseService;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _insightWindow;
    private AvaloniaEdit.TextMate.TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _textMateRegistryOptions;
    private int _completionRequestVersion;
    private int _quickInfoRequestVersion;

    public string? Extension
    {
        get => GetValue(ExtensionProperty);
        set => SetValue(ExtensionProperty, value);
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
        _textEditor.PointerHover += TextEditorOnPointerHover;
        _textEditor.PointerHoverStopped += TextEditorOnPointerHoverStopped;

        InstallFoldingManager();
        _foldingTimer = new DispatcherTimer { Interval = FoldingUpdateDelay };
        _foldingTimer.Tick += FoldingTimerOnTick;

        ApplyExtensionMode();
        ApplyEditorTheme();
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
            textEditor.PointerHover -= TextEditorOnPointerHover;
            textEditor.PointerHoverStopped -= TextEditorOnPointerHoverStopped;
        }

        if (_foldingTimer is { } foldingTimer)
        {
            foldingTimer.Stop();
            foldingTimer.Tick -= FoldingTimerOnTick;
        }

        UninstallFoldingManager();
        CloseCompletionWindow();
        CloseInsightWindow();
        CloseQuickInfo();
        DisposeTextMateInstallation();

        _foldingTimer = null;
        _foldingManager = null;
        _xmlFoldingStrategy = null;
        _csharpFoldingStrategy = null;
        _intelliSenseService = null;
        _textEditor = null;

        base.OnDetaching();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ExtensionProperty && _textEditor is not null)
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
            string.IsNullOrEmpty(e.Text) ||
            _intelliSenseService is null)
        {
            return;
        }

        var trigger = e.Text[^1];
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

    private void TextEditorOnTextChanged(object? sender, EventArgs e)
    {
        ScheduleFoldingUpdate();
    }

    private void TextEditorOnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        _foldingTimer?.Stop();
        UninstallFoldingManager();
        InstallFoldingManager();
        UpdateFoldings();
    }

    private void FoldingTimerOnTick(object? sender, EventArgs e)
    {
        _foldingTimer?.Stop();
        UpdateFoldings();
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
            _intelliSenseService = new CSharpIntelliSenseService();
        }
        else
        {
            _xmlFoldingStrategy = null;
            _csharpFoldingStrategy = null;
            _intelliSenseService = null;
        }

        CloseCompletionWindow();
        CloseInsightWindow();
        CloseQuickInfo();
        UpdateFoldings();
    }

    private void ApplySyntaxHighlighting()
    {
        if (_textEditor is null)
        {
            return;
        }

        var extension = NormalizeTextMateExtension(Extension);
        if (extension is null)
        {
            DisposeTextMateInstallation();
            _textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Extension);
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

        _textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Extension);
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

    private static void HandleTextMateException(Exception exception)
    {
        Console.WriteLine(exception);
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
            UpdateFoldings();
            return;
        }

        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    private void UpdateFoldings()
    {
        if (_textEditor?.Document is null || _foldingManager is null)
        {
            return;
        }

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

        FoldingManager.Uninstall(foldingManager);
        _foldingManager = null;
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
