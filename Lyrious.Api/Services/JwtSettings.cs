using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lyrious.CoreLib.Models;
using Microsoft.IdentityModel.Tokens;

namespace Lyrious.Api.Services;

/// <summary>
/// JWT settings from the "Jwt" configuration section. The same instance is used to issue tokens
/// (AuthController) and to validate them (JwtBearer), so the two can't drift apart.
/// </summary>
public sealed class JwtSettings
{
	public required SymmetricSecurityKey SigningKey { get; init; }
	public required string Issuer { get; init; }
	public required string Audience { get; init; }
	public required TimeSpan Lifetime { get; init; }

	/// <summary>The key in appsettings.json: public (it's in the repository), so for local use only.</summary>
	private const string DevelopmentKey = "YourSuperSecureKeyThatIsAtLeast32CharactersLong";

	/// <param name="requireOwnKey">
	/// Refuse the key of appsettings.json: for a server open to others, where anyone knowing the
	/// key could sign themselves in as any member.
	/// </param>
	public static JwtSettings FromConfiguration(IConfiguration configuration, bool requireOwnKey = false)
	{
		var section = configuration.GetSection("Jwt");

		var key = section["Key"];
		if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
		{
			throw new InvalidOperationException("Jwt:Key must be configured and at least 32 bytes long.");
		}

		if (requireOwnKey && key == DevelopmentKey)
		{
			throw new InvalidOperationException("Jwt:Key is still the key from appsettings.json. Set a secret key of your own (the Jwt__Key environment variable).");
		}

		return new JwtSettings
		{
			SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
			Issuer = ValueOrDefault(section["Issuer"], "lyrious-api"),
			Audience = ValueOrDefault(section["Audience"], "lyrious-client"),
			Lifetime = TimeSpan.FromDays(section.GetValue("LifetimeDays", 30)),
		};
	}

	public TokenValidationParameters ValidationParameters => new()
	{
		ValidateIssuer = true,
		ValidateAudience = true,
		ValidateLifetime = true,
		ValidateIssuerSigningKey = true,
		ValidIssuer = Issuer,
		ValidAudience = Audience,
		IssuerSigningKey = SigningKey,
		// Controllers resolve the caller through User.Identity.Name.
		NameClaimType = ClaimTypes.Name,
	};

	/// <param name="siteManager">Marks the token as an elevated login (see LyriousRepository.SiteManagerClaim): site-manager rights.</param>
	public string CreateToken(Member member, bool siteManager = false)
	{
		var claims = new List<Claim>
		{
			new(JwtRegisteredClaimNames.Sub, member.Id.ToString()),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
			new(ClaimTypes.Name, member.UserName ?? string.Empty),
		};
		if (siteManager)
		{
			claims.Add(new Claim(Lyrious.CoreLib.LyriousRepository.SiteManagerClaim, "true"));
		}

		var token = new JwtSecurityToken(
			issuer: Issuer,
			audience: Audience,
			claims: claims,
			expires: DateTime.UtcNow.Add(Lifetime),
			signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	private static string ValueOrDefault(string? value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}
}
