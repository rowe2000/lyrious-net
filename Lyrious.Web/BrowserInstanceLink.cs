using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.Client;

namespace Lyrious.Web;

/// <summary>
/// The app's other tabs and windows on the same address, which share its stored settings and cache (functions
/// in wwwroot/main.js): messages go over a BroadcastChannel, and the owner is the one holding a
/// Web Lock, which the browser hands to the next when its tab is closed. Where the browser lacks
/// either (Web Locks need https or localhost), the app is alone and its own owner, as before.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserInstanceLink : IInstanceLink
{
    public bool IsOwner => IsOwnerNow();

    public void Join(string name, Action<string> received, Action becameOwner) => JoinNow(name, received, becameOwner);

    public void Leave() => LeaveNow();

    public void Post(string message) => PostNow(message);

    [JSImport("globalThis.lyriousInstances.join")]
    private static partial void JoinNow(
        string name,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> received,
        [JSMarshalAs<JSType.Function>] Action becameOwner);

    [JSImport("globalThis.lyriousInstances.leave")]
    private static partial void LeaveNow();

    [JSImport("globalThis.lyriousInstances.isOwner")]
    private static partial bool IsOwnerNow();

    [JSImport("globalThis.lyriousInstances.post")]
    private static partial void PostNow(string message);
}
