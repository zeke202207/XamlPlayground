using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XamlPlayground.Workspace;

namespace XamlPlayground.ViewModels.Workspace;

public sealed partial class NewProjectWizardViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _solutionName = "App1";
    [ObservableProperty] private AvaloniaProjectTemplate? _selectedTemplate;

    public NewProjectWizardViewModel()
    {
        Templates = new ObservableCollection<AvaloniaProjectTemplate>(AvaloniaProjectTemplates.All);
        SelectedTemplate = Templates.Count > 0 ? Templates[0] : null;
    }

    public ObservableCollection<AvaloniaProjectTemplate> Templates { get; }
}
