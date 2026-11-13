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

    /// <param name="args">
    /// [0] is the page URL (see wwwroot/main.js); <c>?api=...</c> sets the API address. Else the
    /// host chosen on the Sync page last time (in the browser's local storage), else the default.
    /// </param>
    private static Task Main(string[] args)
    {
        var settings = new BrowserSettingsStore();
        App.Settings = settings;
        var apiBaseUrl = GetApiBaseUrl(args.FirstOrDefault(), settings.Get(SettingKeys.Host));
        var auth = new HttpAuthService(new HttpClient { BaseAddress = apiBaseUrl });
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth);
        App.CredentialStore = new BrowserCredentialStore();

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();

    private static Uri GetApiBaseUrl(string? pageUrl, string? savedHost)
    {
        var query = Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) ? page.Query.TrimStart('?') : "";
        var api = query.Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "api")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

        // Relative API paths ("api/auth/login") resolve under the base address, so it must end with '/'.
        return Uri.TryCreate(api, UriKind.Absolute, out var uri) || Uri.TryCreate(savedHost, UriKind.Absolute, out uri)
            ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/")
            : new Uri(DefaultApiBaseUrl);
    }
}
