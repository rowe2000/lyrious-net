using Lyrious.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
	SignInManager<Member> signInManager,
	UserManager<Member> userManager,
	JwtSettings jwtSettings,
	IEmailSender emailSender,
	ILogger<AuthController> logger) : ControllerBase
{
	[HttpPost("register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model)
	{
		var member = Member.Create(model.UserName, model.Name, model.Email, model.PhoneNumber);
		var result = await userManager.CreateAsync(member, model.Password);

		if (result.Succeeded)
		{
			// Registering doesn't log in; the client calls login to get a token.
			return Ok(new TokenModel { Token = "", Member = PublicProfile(member) });
		}

		return BadRequest(result.Errors);
	}

	[HttpPost("login")]
	public async Task<IActionResult> Login([FromBody] LoginModel model)
	{
		var member = await userManager.FindByNameAsync(model.Username);
		if (member == null)
		{
			return Unauthorized("Invalid username or password.");
		}

		// Verifies the password (and lockout state) without creating a cookie session.
		var result = await signInManager.CheckPasswordSignInAsync(member, model.Password, lockoutOnFailure: false);
		if (!result.Succeeded)
		{
			return Unauthorized("Invalid username or password.");
		}

		return Ok(new TokenModel { Token = jwtSettings.CreateToken(member), Member = PublicProfile(member) });
	}

	[Authorize]
	[HttpPost("change-password")]
	public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		// Checks the current password and applies the password rules; errors come back as Identity errors.
		var result = await userManager.ChangePasswordAsync(member, model.CurrentPassword, model.NewPassword);
		return result.Succeeded ? Ok() : BadRequest(result.Errors);
	}

	/// <summary>
	/// Emails a short-lived 6-digit code for resetting the password. Always answers 200, whether or not
	/// the account exists or has an email address, so it can't be used to find out who has an account.
	/// </summary>
	[HttpPost("forgot-password")]
	public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordModel model, CancellationToken cancellationToken)
	{
		var member = await FindByUserNameOrEmailAsync(model.UserNameOrEmail);
		if (member == null || string.IsNullOrWhiteSpace(member.Email))
		{
			logger.LogInformation("Password reset requested for an unknown account or one without an email address");
			return Ok();
		}

		var code = await userManager.GeneratePasswordResetTokenAsync(member);
		await emailSender.SendAsync(
			member.Email,
			"Your Lyrious password reset code",
			$"Hi {member.Name},\n\nYour code for resetting your Lyrious password is {code}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email.",
			cancellationToken);
		return Ok();
	}

	/// <summary>Sets a new password using the code from <see cref="ForgotPassword"/>.</summary>
	[HttpPost("reset-password")]
	public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordModel model)
	{
		var member = await FindByUserNameOrEmailAsync(model.UserNameOrEmail);
		if (member == null)
		{
			// Same answer as a wrong code, so this doesn't reveal which accounts exist.
			return BadRequest(new[] { new IdentityError { Code = "InvalidToken", Description = "The code is wrong or has expired." } });
		}

		var result = await userManager.ResetPasswordAsync(member, model.Code.Trim(), model.NewPassword);
		if (result.Succeeded)
		{
			return Ok();
		}

		// Identity reports a wrong or expired code as "Invalid token."; say it in the user's terms.
		var errors = result.Errors.Select(error => error.Code == "InvalidToken"
			? new IdentityError { Code = error.Code, Description = "The code is wrong or has expired." }
			: error);
		return BadRequest(errors);
	}

	/// <summary>
	/// Finds a member by username, else by email. Emails aren't required to be unique, so an email
	/// shared by several members matches none of them.
	/// </summary>
	private async Task<Member?> FindByUserNameOrEmailAsync(string userNameOrEmail)
	{
		var value = userNameOrEmail.Trim();
		var member = await userManager.FindByNameAsync(value);
		if (member != null || !value.Contains('@'))
		{
			return member;
		}

		var normalizedEmail = userManager.NormalizeEmail(value);
		var matches = userManager.Users.Where(m => m.NormalizedEmail == normalizedEmail).Take(2).ToList();
		return matches.Count == 1 ? matches[0] : null;
	}

	[HttpPost("logout")]
	public IActionResult Logout()
	{
		// Tokens are stateless: logging out means the client discards its token.
		// A token stays valid until it expires (Jwt:LifetimeDays).
		return Ok("Logged out successfully.");
	}

	// Identity fields (password hash, security stamp, ...) never leave the server.
	private static Member PublicProfile(Member member) => new()
	{
		Id = member.Id,
		UserName = member.UserName,
		Name = member.Name,
		Email = member.Email,
		PhoneNumber = member.PhoneNumber,
		JoinedGroupId = member.JoinedGroupId,
		CreatedAt = member.CreatedAt,
		ChangedAt = member.ChangedAt,
	};
}
