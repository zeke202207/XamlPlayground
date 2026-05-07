using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using XamlPlayground.Controls;

namespace XamlPlayground.Tests;

public sealed class ExclusiveContentControlTests
{
    [Fact]
    public void Source_IsHostedByOneAttachedHostAtATime()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var source = new Border
            {
                Name = "GeneratedSampleScope"
            };
            var first = new ExclusiveContentControl
            {
                Source = source
            };
            var second = new ExclusiveContentControl
            {
                Source = source
            };
            var third = new ExclusiveContentControl
            {
                Source = source
            };
            var panel = new StackPanel
            {
                Children =
                {
                    first
                }
            };
            var window = new Window
            {
                Width = 320,
                Height = 240,
                Content = panel
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.Same(source, first.Content);

                panel.Children.Add(second);
                PumpLayout(window);

                Assert.Same(source, first.Content);
                Assert.Null(second.Content);
                Assert.Equal(1, new[] { first, second }.Count(host => ReferenceEquals(host.Content, source)));

                panel.Children.Add(third);
                PumpLayout(window);

                Assert.Same(source, first.Content);
                Assert.Null(second.Content);
                Assert.Null(third.Content);

                first.Source = null;
                PumpLayout(window);

                Assert.Null(first.Content);
                Assert.Same(source, second.Content);
                Assert.Null(third.Content);

                second.Source = null;
                PumpLayout(window);

                Assert.Null(first.Content);
                Assert.Null(second.Content);
                Assert.Same(source, third.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Source_Grid_RemainsStableDuringHostResize()
    {
        TestApplication.EnsureAvaloniaInitialized();

        Dispatcher.UIThread.Invoke(() =>
        {
            var source = CreateSampleGrid();
            var first = new ExclusiveContentControl
            {
                Source = source
            };
            var second = new ExclusiveContentControl
            {
                Source = source
            };
            var panel = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(1, GridUnitType.Star),
                    new ColumnDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    first,
                    second
                }
            };
            Grid.SetColumn(second, 1);

            var window = new Window
            {
                Width = 640,
                Height = 360,
                Content = panel
            };

            try
            {
                window.Show();
                PumpLayout(window);

                Assert.Same(source, first.Content);
                Assert.Null(second.Content);

                ResizeWindow(window, 520, 340);
                ResizeWindow(window, 780, 420);

                Assert.Same(source, first.Content);
                Assert.Null(second.Content);

                first.Source = null;
                PumpLayout(window);

                Assert.Null(first.Content);
                Assert.Same(source, second.Content);

                ResizeWindow(window, 600, 380);
                ResizeWindow(window, 820, 460);

                Assert.Same(source, second.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ResizeWindow(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        PumpLayout(window);
    }

    private static Grid CreateSampleGrid()
    {
        var grid = new Grid
        {
            Name = "GeneratedSampleScopeGrid",
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(2, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            }
        };

        var header = new TextBlock
        {
            Text = "Preview",
            Margin = new Thickness(8)
        };
        Grid.SetColumnSpan(header, 2);

        var left = new Border
        {
            Background = Brushes.SteelBlue,
            MinHeight = 64,
            Margin = new Thickness(8)
        };
        Grid.SetRow(left, 1);

        var right = new Border
        {
            Background = Brushes.DarkSlateGray,
            MinHeight = 64,
            Margin = new Thickness(8)
        };
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);

        grid.Children.Add(header);
        grid.Children.Add(left);
        grid.Children.Add(right);

        return grid;
    }
}
