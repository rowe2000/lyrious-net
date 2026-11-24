using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Lyrious.UILib.Login;

namespace Lyrious.Webapp;

/// <summary>
/// Leaves the login (username and password) to the browser's own password manager, in every browser:
/// the login page's fields are a real login form (see BrowserLoginForm), so the browser offers to
/// save the login when the member logs in, and fills it in. Here it's only read, to log in by itself
/// at start (functions in wwwroot/main.js): from Chromium's password manager (the Credential
/// Management API), else from what the browser filled into the form. The browser asks the user
/// itself before saving, and only the browser's settings can remove a saved password.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserCredentialStore : ICredentialStore
{
    public bool IsSupported { get; } = IsAvailable();
    public bool AsksUserItself => true;
    public bool CanForget => false;

    public async Task<SavedCredential?> LoadAsync()
    {
        if (!IsSupported)
        {
            return null;
        }

        var json = await Load();
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString();
        var password = root.GetProperty("password").GetString();
        return string.IsNullOrEmpty(id) || password == null ? null : new SavedCredential(id, password);
    }

    /// <summary>Nothing to do: the browser offers to save the login itself, when it's submitted in the login form.</summary>
    public Task SaveAsync(SavedCredential credential) => Task.CompletedTask;

    public Task ForgetAsync() => Task.CompletedTask;

    [JSImport("globalThis.lyriousCredentials.isSupported")]
    private static partial bool IsAvailable();

    /// <summary>The saved login as JSON (<c>{"id","password"}</c>), or null.</summary>
    [JSImport("globalThis.lyriousCredentials.load")]
    private static partial Task<string?> Load();
}
