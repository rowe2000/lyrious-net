using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>Settings in the browser's local storage (functions in wwwroot/main.js), so they last between visits.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserSettingsStore : ISettingsStore
{
    private const string Prefix = "lyrious.";

    public string? Get(string key) => GetItem(Prefix + key);

    public void Set(string key, string? value) => SetItem(Prefix + key, value);

    [JSImport("globalThis.lyriousSettings.get")]
    private static partial string? GetItem(string key);

    [JSImport("globalThis.lyriousSettings.set")]
    private static partial void SetItem(string key, string? value);
}
