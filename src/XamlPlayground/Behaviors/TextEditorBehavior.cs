using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;

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
        _textEditor.TextChanged += TextEditorOnTextChanged;
        _textEditor.DocumentChanged += TextEditorOnDocumentChanged;

        InstallFoldingManager();
        _foldingTimer = new DispatcherTimer { Interval = FoldingUpdateDelay };
        _foldingTimer.Tick += FoldingTimerOnTick;

        ApplyExtensionMode();
    }

    protected override void OnDetaching()
    {
        if (_textEditor is { } textEditor)
        {
            textEditor.TextArea.KeyDown -= TextAreaOnKeyDown;
            textEditor.TextChanged -= TextEditorOnTextChanged;
            textEditor.DocumentChanged -= TextEditorOnDocumentChanged;
        }

        if (_foldingTimer is { } foldingTimer)
        {
            foldingTimer.Stop();
            foldingTimer.Tick -= FoldingTimerOnTick;
        }

        UninstallFoldingManager();

        _foldingTimer = null;
        _foldingManager = null;
        _xmlFoldingStrategy = null;
        _csharpFoldingStrategy = null;
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

    private void TextAreaOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textEditor is null || e.Key != Key.A)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var isSelectAllGesture = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        var hasOtherModifiers = (modifiers & ~(KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (!isSelectAllGesture || hasOtherModifiers)
        {
            return;
        }

        _textEditor.SelectAll();
        e.Handled = true;
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

        _textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Extension);

        if (IsXmlExtension(Extension))
        {
            _xmlFoldingStrategy ??= new XmlFoldingStrategy();
            _csharpFoldingStrategy = null;
        }
        else if (IsCSharpExtension(Extension))
        {
            _csharpFoldingStrategy ??= new CSharpFoldingStrategy();
            _xmlFoldingStrategy = null;
        }
        else
        {
            _xmlFoldingStrategy = null;
            _csharpFoldingStrategy = null;
        }

        UpdateFoldings();
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
}
