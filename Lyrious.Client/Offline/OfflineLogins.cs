using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lyrious.UILib;

namespace Lyrious.Client.Offline;

/// <summary>A login that can be checked without the server: who the member is.</summary>
internal sealed record OfflineLogin(Guid MemberId, string UserName, string DisplayName, Guid? JoinedGroupId);

/// <summary>
/// Lets members who have logged in online on this device log in again without the server. After
/// each online login a salted, slow hash of the password is kept (never the password itself), per
/// server and username, with who the member is. A failed online login removes it, so a password
/// changed elsewhere stops working offline too.
/// </summary>
/// <param name="hasher">Computes PBKDF2; the browser's is native and doesn't block the page.</param>
internal sealed class OfflineLogins(ISettingsStore store, IPasswordHasher hasher)
{
    private const string Key = "offline-logins";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // PBKDF2 with SHA-256 from the hasher; if it isn't available, a managed one with fewer rounds
    // (slow in the browser, so it's only a fallback; still read for logins saved with it earlier).
    private const string Pbkdf2 = "pbkdf2-sha256";
    private const string ManualPbkdf2 = "pbkdf2-hmacsha256-managed";
    private const int Iterations = 100_000;
    private const int ManualIterations = 20_000;

    public async Task SaveAsync(string server, string userName, string password, OfflineLogin login)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (algorithm, iterations, hash) = await HashAsync(password, salt);
        var entry = new JsonObject
        {
            ["memberId"] = login.MemberId.ToString(),
            ["userName"] = login.UserName,
            ["displayName"] = login.DisplayName,
            ["joinedGroupId"] = login.JoinedGroupId?.ToString(),
            ["algorithm"] = algorithm,
            ["iterations"] = iterations,
            ["salt"] = Convert.ToBase64String(salt),
            ["hash"] = Convert.ToBase64String(hash),
        };

        var all = ReadAll();
        all[EntryKey(server, userName)] = entry;
        store.Set(Key, all.ToJsonString());
    }

    /// <summary>Who the member is, if the password matches the one from their last online login here.</summary>
    public async Task<OfflineLogin?> VerifyAsync(string server, string userName, string password)
    {
        if (ReadAll()[EntryKey(server, userName)] is not JsonObject entry)
        {
            return null;
        }

        try
        {
            var salt = Convert.FromBase64String((string)entry["salt"]!);
            var expected = Convert.FromBase64String((string)entry["hash"]!);
            var actual = await HashAsync(password, salt, (string)entry["algorithm"]!, (int)entry["iterations"]!);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return null;
            }

            return new OfflineLogin(
                Guid.Parse((string)entry["memberId"]!),
                (string?)entry["userName"] ?? userName,
                (string?)entry["displayName"] ?? userName,
                Guid.TryParse((string?)entry["joinedGroupId"], out var joined) ? joined : null);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or NullReferenceException or CryptographicException or PlatformNotSupportedException or System.Runtime.InteropServices.JavaScript.JSException)
        {
            return null;
        }
    }

    public bool Has(string server, string userName) => ReadAll()[EntryKey(server, userName)] is JsonObject;

    public void Remove(string server, string userName)
    {
        var all = ReadAll();
        if (all.Remove(EntryKey(server, userName)))
        {
            store.Set(Key, all.ToJsonString());
        }
    }

    /// <summary>Remembers the member's newly joined group, so an offline login opens on it.</summary>
    public void SetJoinedGroup(string server, string userName, Guid? groupId)
    {
        var all = ReadAll();
        if (all[EntryKey(server, userName)] is JsonObject entry)
        {
            entry["joinedGroupId"] = groupId?.ToString();
            store.Set(Key, all.ToJsonString());
        }
    }

    private static string EntryKey(string server, string userName) => $"{server.TrimEnd('/').ToLowerInvariant()}|{userName.Trim().ToLowerInvariant()}";

    private JsonObject ReadAll()
    {
        try
        {
            return store.Get(Key) is { } json && JsonNode.Parse(json) is JsonObject all ? all : new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
    }

    private async Task<(string Algorithm, int Iterations, byte[] Hash)> HashAsync(string password, byte[] salt)
    {
        try
        {
            return (Pbkdf2, Iterations, await HashAsync(password, salt, Pbkdf2, Iterations));
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or System.Runtime.InteropServices.JavaScript.JSException)
        {
            // E.g. the browser has no Web Crypto (a page served over plain http from another machine).
            return (ManualPbkdf2, ManualIterations, await HashAsync(password, salt, ManualPbkdf2, ManualIterations));
        }
    }

    private async Task<byte[]> HashAsync(string password, byte[] salt, string algorithm, int iterations) => algorithm switch
    {
        Pbkdf2 => await hasher.Pbkdf2Sha256Async(password, salt, iterations, HashSize),
        ManualPbkdf2 => ManagedPbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations),
        _ => throw new CryptographicException($"Unknown password hash '{algorithm}'."),
    };

    /// <summary>PBKDF2 (RFC 8018) with HMAC-SHA256, one 32-byte block, for platforms without the built-in one.</summary>
    private static byte[] ManagedPbkdf2(byte[] password, byte[] salt, int iterations)
    {
        using var hmac = new HMACSHA256(password);
        var block = new byte[salt.Length + 4];
        salt.CopyTo(block, 0);
        block[^1] = 1; // Block index 1, big-endian.

        var u = hmac.ComputeHash(block);
        var result = (byte[])u.Clone();
        for (var i = 1; i < iterations; i++)
        {
            u = hmac.ComputeHash(u);
            for (var j = 0; j < result.Length; j++)
            {
                result[j] ^= u[j];
            }
        }

        return result;
    }
}
