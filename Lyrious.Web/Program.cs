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
    // Only when the page has no web address of its own to take the API's from.
    private const string DefaultApiBaseUrl = "http://localhost:5180/";

    /// <param name="args">
    /// [0] is the page URL (see wwwroot/main.js); <c>?api=...</c> sets the API address. Else the
    /// host chosen on the Sync page last time (in the browser's local storage), else where the page
    /// itself comes from: the API serves the web app, on the same address.
    /// </param>
    private static Task Main(string[] args)
    {
        var settings = new BrowserSettingsStore();
        App.Settings = settings;
        var apiBaseUrl = GetApiBaseUrl(args.FirstOrDefault(), settings.Get(SettingKeys.Host));
        // The offline cache and offline logins, kept apart from the settings.
        var offline = new BrowserSettingsStore("lyrious.offline.");
        var auth = new HttpAuthService(new HttpClient { BaseAddress = apiBaseUrl }, offline, new BrowserPasswordHasher());
        App.AuthService = auth;
        // The other tabs on this address share the offline cache: they keep each other up to date.
        App.SetlistService = new HttpSetlistService(auth, offline, new BrowserInstanceLink());
        App.CredentialStore = new BrowserCredentialStore();
        App.SoundStore = new BrowserSoundStore();

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
        if (Uri.TryCreate(api, UriKind.Absolute, out var uri) || Uri.TryCreate(savedHost, UriKind.Absolute, out uri))
        {
            return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        }

        return page is { Scheme: "http" or "https" } ? new Uri(page.GetLeftPart(UriPartial.Authority) + "/") : new Uri(DefaultApiBaseUrl);
    }
}
