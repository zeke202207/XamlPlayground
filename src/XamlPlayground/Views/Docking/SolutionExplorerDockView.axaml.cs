using Avalonia.Controls;
using Avalonia.Input;
using XamlPlayground.ViewModels.Docking;

namespace XamlPlayground.Views.Docking;

public partial class SolutionExplorerDockView : UserControl
{
    public SolutionExplorerDockView()
    {
        InitializeComponent();
    }

    private void TreeViewOnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SolutionExplorerDockViewModel viewModel ||
            viewModel.Shell.SelectedSolutionExplorerNode is not { } node ||
            !node.OpenCommand.CanExecute(null))
        {
            return;
        }

        node.OpenCommand.Execute(null);
        e.Handled = true;
    }
}
