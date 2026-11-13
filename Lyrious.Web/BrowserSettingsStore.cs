using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>Settings in the browser's local storage (functions in wwwroot/main.js), so they last between visits.</summary>
/// <param name="prefix">Keeps different stores apart, e.g. settings and the offline cache.</param>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserSettingsStore(string prefix = "lyrious.") : ISettingsStore
{
    public string? Get(string key) => GetItem(prefix + key);

    public void Set(string key, string? value) => SetItem(prefix + key, value);

    [JSImport("globalThis.lyriousSettings.get")]
    private static partial string? GetItem(string key);

    [JSImport("globalThis.lyriousSettings.set")]
    private static partial void SetItem(string key, string? value);
}
