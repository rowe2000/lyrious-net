using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Lyrious.UILib.Login;

namespace Lyrious.Web;

/// <summary>
/// Saves the login in the browser's password manager through the Credential Management API
/// (<c>navigator.credentials</c>, functions in wwwroot/main.js). The browser asks the user itself
/// before saving, and only the browser's settings can remove a saved password. Browsers without
/// <c>PasswordCredential</c> (Firefox, Safari) aren't offered anything.
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

    public Task SaveAsync(SavedCredential credential)
        => IsSupported ? Store(credential.UserName, credential.Password) : Task.CompletedTask;

    public Task ForgetAsync() => Task.CompletedTask;

    [JSImport("globalThis.lyriousCredentials.isSupported")]
    private static partial bool IsAvailable();

    [JSImport("globalThis.lyriousCredentials.store")]
    private static partial Task Store(string userName, string password);

    /// <summary>The saved login as JSON (<c>{"id","password"}</c>), or null.</summary>
    [JSImport("globalThis.lyriousCredentials.load")]
    private static partial Task<string?> Load();
}
