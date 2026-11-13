using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Lyrious.Client;

/// <summary>
/// PBKDF2 with HMAC-SHA256, for the offline login's password hash. A host can supply a faster or
/// non-blocking one: the browser's Web Crypto does it natively without freezing the page, where
/// .NET in WebAssembly would compute it on the page's only thread.
/// </summary>
public interface IPasswordHasher
{
    Task<byte[]> Pbkdf2Sha256Async(string password, byte[] salt, int iterations, int length);
}

/// <summary>.NET's own PBKDF2 (native and quick on desktop and mobile).</summary>
public sealed class DotNetPasswordHasher : IPasswordHasher
{
    public Task<byte[]> Pbkdf2Sha256Async(string password, byte[] salt, int iterations, int length)
        => Task.FromResult(Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length));
}
