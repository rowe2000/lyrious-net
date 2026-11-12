using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>
/// Hosts the Lyrious.UILib app in the browser (WebAssembly). All UI lives in Lyrious.UILib;
/// this project only starts it inside the "out" element of wwwroot/index.html.
/// </summary>
internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
        .StartBrowserAppAsync("out");

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
