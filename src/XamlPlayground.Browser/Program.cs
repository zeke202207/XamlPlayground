using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using XamlPlayground;
using XamlPlayground.Services;

[assembly: SupportedOSPlatform("browser")]

internal partial class Program
{
    private static void Initialize(string id, string baseUri, string browserReferenceAssets)
    {
        CompilerService.BaseUri = baseUri;
        CompilerService.SetBrowserReferenceAssets(browserReferenceAssets);

        id = id.Replace("XamlPlayground/", "").Replace("gist/", "").Replace("?gist=", "").Replace("/", "");

        if (Application.Current is App app)
        {
            app.InitialGist = id;
        }
    }

    private static Task Main(string[] args) =>
        BuildAvaloniaApp()
            .AfterSetup(_ => Initialize(
                args.ElementAtOrDefault(0) ?? string.Empty,
                args.ElementAtOrDefault(1) ?? string.Empty,
                args.ElementAtOrDefault(2) ?? string.Empty))
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithInterFont();
}
