using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Lyrious.Client;
using Lyrious.UILib;

namespace Lyrious.Webapp;

/// <summary>
/// Hosts the Lyrious.UILib app in the browser (WebAssembly). All UI lives in Lyrious.UILib;
/// this project only starts it inside the "out" element of wwwroot/index.html and connects it to the API.
/// </summary>
internal sealed partial class Program
{
    // Only when the page has no web address of its own to take the API's from.
    private const string DefaultApiBaseUrl = SettingKeys.DefaultServer + "/";

    /// <param name="args">
    /// [0] is the page URL (see wwwroot/main.js); <c>?api=...</c> sets the API address. [1] is
    /// "own-server" when the page's address is a Lyrious server itself (the API serving the web app,
    /// or a desktop app sharing it on the network): then that address is used, whatever host was
    /// saved. Else the host chosen on the Sync page last time (in the browser's IndexedDB), else
    /// where the page itself comes from.
    /// </param>
    private static Task Main(string[] args)
    {
        var settings = new BrowserSettingsStore();
        App.Settings = settings;
        var ownServer = args.Length > 1 && args[1] == "own-server";
        var apiBaseUrl = GetApiBaseUrl(args.FirstOrDefault(), ownServer ? null : settings.Get(SettingKeys.Host), out var fromQuery);
        if (fromQuery)
        {
            // Given in the address: remembered as the host, so it's needed only once.
            settings.Set(SettingKeys.Host, apiBaseUrl.AbsoluteUri.TrimEnd('/'));
        }
        // The offline cache and offline logins, kept apart from the settings.
        var offline = new BrowserSettingsStore("lyrious.offline.");
        var auth = new HttpAuthService(new HttpClient { BaseAddress = apiBaseUrl }, offline, new BrowserPasswordHasher());
        App.AuthService = auth;
        // The other tabs on this address share the offline cache: they keep each other up to date.
        App.SetlistService = new HttpSetlistService(auth, offline, new BrowserInstanceLink());
        App.CredentialStore = new BrowserCredentialStore();
        // The login page's fields as a real login form, for the browser's password manager.
        App.LoginForm = new BrowserLoginForm();
        App.SoundStore = new BrowserSoundStore();
        // The MIDI tags in songs go to every MIDI output the browser offers.
        App.Midi = new BrowserMidiOutput();
        // Full screen as the member chose (Settings), at their first tap or key when it can't be at once.
        FullScreen.Host = new BrowserFullScreen();

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();

    /// <param name="fromQuery">Whether the address came from <c>?api=...</c>.</param>
    private static Uri GetApiBaseUrl(string? pageUrl, string? savedHost, out bool fromQuery)
    {
        fromQuery = false;
        var query = Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) ? page.Query.TrimStart('?') : "";
        var api = query.Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "api")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

        // Relative API paths ("api/auth/login") resolve under the base address, so it must end with '/'.
        if (Uri.TryCreate(api, UriKind.Absolute, out var uri))
        {
            fromQuery = true;
            return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        }

        if (Uri.TryCreate(savedHost, UriKind.Absolute, out uri))
        {
            return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        }

        return page is { Scheme: "http" or "https" } ? new Uri(page.GetLeftPart(UriPartial.Authority) + "/") : new Uri(DefaultApiBaseUrl);
    }
}
