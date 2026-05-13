using Avalonia;
using Avalonia.Controls;
using System;

namespace XamlPlayground.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
    }
}
