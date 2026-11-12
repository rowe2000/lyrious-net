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

	public static JwtSettings FromConfiguration(IConfiguration configuration)
	{
		var section = configuration.GetSection("Jwt");

		var key = section["Key"];
		if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
		{
			throw new InvalidOperationException("Jwt:Key must be configured and at least 32 bytes long.");
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

	public string CreateToken(Member member)
	{
		var claims = new List<Claim>
		{
			new(JwtRegisteredClaimNames.Sub, member.Id.ToString()),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
			new(ClaimTypes.Name, member.UserName ?? string.Empty),
		};

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
