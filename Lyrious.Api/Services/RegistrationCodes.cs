using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Lyrious.Api.Services;

/// <summary>
/// The codes emailed to verify an address before an account is made with it (there's no member yet
/// to keep them with). In memory: a code lasts <see cref="Lifetime"/>, allows
/// <see cref="MaxAttempts"/> wrong tries, and works once. A new one for the same address can be
/// asked for after <see cref="ResendDelay"/>, and replaces the old one.
/// </summary>
public sealed class RegistrationCodes
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
	public static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(30);
	public const int MaxAttempts = 5;

	private sealed record Pending(string Code, DateTime IssuedAt, int Attempts);

	private readonly ConcurrentDictionary<string, Pending> codes = new(StringComparer.Ordinal);

	/// <summary>A new 6-digit code for the (normalized) address; null if one was issued too recently.</summary>
	public string? Issue(string normalizedEmail)
	{
		var now = DateTime.UtcNow;
		Prune(now);
		if (codes.TryGetValue(normalizedEmail, out var pending) && now - pending.IssuedAt < ResendDelay)
		{
			return null;
		}

		var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
		codes[normalizedEmail] = new Pending(code, now, 0);
		return code;
	}

	/// <summary>Whether the code is the address's (and still valid); a right code is used up, wrong ones count.</summary>
	public bool Verify(string normalizedEmail, string? code)
	{
		var now = DateTime.UtcNow;
		if (string.IsNullOrWhiteSpace(code) || !codes.TryGetValue(normalizedEmail, out var pending)
		    || now - pending.IssuedAt > Lifetime || pending.Attempts >= MaxAttempts)
		{
			return false;
		}

		if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(code.Trim()), System.Text.Encoding.ASCII.GetBytes(pending.Code)))
		{
			codes.TryRemove(normalizedEmail, out _);
			return true;
		}

		codes[normalizedEmail] = pending with { Attempts = pending.Attempts + 1 };
		return false;
	}

	private void Prune(DateTime now)
	{
		foreach (var (email, pending) in codes)
		{
			if (now - pending.IssuedAt > Lifetime)
			{
				codes.TryRemove(email, out _);
			}
		}
	}
}
