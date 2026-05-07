using XamlPlayground.ViewModels;

namespace XamlPlayground.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void NewFileCommand_CreatesUntitledHelloWorldUserControl()
    {
        TestApplication.EnsureAvaloniaInitialized();

        var viewModel = new MainViewModel(null);
        var previousCount = viewModel.Samples.Count;

        viewModel.NewFileCommand.Execute(null);

        var sample = Assert.IsType<SampleViewModel>(viewModel.CurrentSample);
        Assert.Equal(previousCount + 1, viewModel.Samples.Count);
        Assert.Equal("Untitled", sample.Name);
        Assert.Contains("<UserControl", sample.Xaml.Text, StringComparison.Ordinal);
        Assert.Contains("TextBlock", sample.Xaml.Text, StringComparison.Ordinal);
        Assert.Contains("Hello, world!", sample.Xaml.Text, StringComparison.Ordinal);
        Assert.True(string.IsNullOrEmpty(sample.Code.Text));
    }
}
