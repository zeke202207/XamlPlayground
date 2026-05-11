using Avalonia.Controls;
using Avalonia.Input;
using XamlPlayground.Services.VisualEditing;

namespace XamlPlayground.Views.Docking;

public partial class VisualToolboxDockView : UserControl
{
    public VisualToolboxDockView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control { DataContext: ToolboxItemDescriptor item } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        await DragDrop.DoDragDropAsync(e, ToolboxDragPayload.CreateDataTransfer(item), DragDropEffects.Copy);
    }
}
