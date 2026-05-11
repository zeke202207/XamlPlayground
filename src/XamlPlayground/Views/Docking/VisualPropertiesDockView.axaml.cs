using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XamlPlayground.ViewModels;
using XamlPlayground.ViewModels.Docking;

namespace XamlPlayground.Views.Docking;

public partial class VisualPropertiesDockView : UserControl
{
    public VisualPropertiesDockView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != DataContextProperty)
        {
            return;
        }

        if (change.GetOldValue<object?>() is VisualPropertiesDockViewModel oldViewModel)
        {
            oldViewModel.Shell.PropertyChanged -= OnShellPropertyChanged;
        }

        if (change.GetNewValue<object?>() is VisualPropertiesDockViewModel newViewModel)
        {
            newViewModel.Shell.PropertyChanged += OnShellPropertyChanged;
            QueueSelectedPropertyIntoView(newViewModel.Shell);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is VisualPropertiesDockViewModel viewModel)
        {
            viewModel.Shell.PropertyChanged -= OnShellPropertyChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedVisualEditorProperty) &&
            sender is MainViewModel viewModel)
        {
            QueueSelectedPropertyIntoView(viewModel);
        }
    }

    private void QueueSelectedPropertyIntoView(MainViewModel viewModel)
    {
        if (viewModel.SelectedVisualEditorProperty is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => PropertiesGrid.ScrollIntoView(viewModel.SelectedVisualEditorProperty, null),
            DispatcherPriority.Loaded);
    }
}
