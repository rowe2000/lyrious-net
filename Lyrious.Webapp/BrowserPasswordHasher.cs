using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lyrious.Client;

namespace Lyrious.Webapp;

/// <summary>
/// PBKDF2 through the browser's Web Crypto (<c>crypto.subtle</c>, function in wwwroot/main.js): native
/// and asynchronous, so hashing the offline login's password doesn't freeze the page as .NET's own
/// would in WebAssembly (about ten seconds). Throws a JSException where Web Crypto isn't available
/// (pages served over plain http from another machine); a slower fallback is used then.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserPasswordHasher : IPasswordHasher
{
    public async Task<byte[]> Pbkdf2Sha256Async(string password, byte[] salt, int iterations, int length)
        => Convert.FromBase64String(await Pbkdf2(password, Convert.ToBase64String(salt), iterations, length));

    [JSImport("globalThis.lyriousCrypto.pbkdf2")]
    private static partial Task<string> Pbkdf2(string password, string saltBase64, int iterations, int length);
}
