using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Lyrious.Client;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>
/// Hosts the Lyrious.UILib app in the browser (WebAssembly). All UI lives in Lyrious.UILib;
/// this project only starts it inside the "out" element of wwwroot/index.html and connects it to the API.
/// </summary>
internal sealed partial class Program
{
    private const string DefaultApiBaseUrl = "http://localhost:7000/";

    /// <param name="args">[0] is the page URL (see wwwroot/main.js); <c>?api=...</c> overrides the API address.</param>
    private static Task Main(string[] args)
    {
        var apiBaseUrl = GetApiBaseUrl(args.FirstOrDefault());
        var auth = new HttpAuthService(new HttpClient { BaseAddress = apiBaseUrl });
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth);

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();

    private static Uri GetApiBaseUrl(string? pageUrl)
    {
        var query = Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) ? page.Query.TrimStart('?') : "";
        var api = query.Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "api")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

        // Relative API paths ("api/auth/login") resolve under the base address, so it must end with '/'.
        return Uri.TryCreate(api, UriKind.Absolute, out var uri)
            ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/")
            : new Uri(DefaultApiBaseUrl);
    }
}
