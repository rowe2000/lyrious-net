using System.Net.Mail;
using Lyrious.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Lyrious.CoreLib;
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
	RegistrationCodes registrationCodes,
	LyriousRepository repo,
	ILogger<AuthController> logger) : ControllerBase
{
	// The purpose of the codes that confirm a password change (see ChangePassword).
	private const string ChangePasswordPurpose = "ChangePassword";

	/// <summary>
	/// Emails a 6-digit code to the address someone wants to register with; registering needs it
	/// (see <see cref="Register"/>), so every account's email address is known to be theirs.
	/// </summary>
	[HttpPost("register/send-code")]
	public async Task<IActionResult> SendRegistrationCode([FromBody] EmailCodeModel model, CancellationToken cancellationToken)
	{
		if (CleanEmail(model.Email) is not { } email)
		{
			return Problem("That isn't an email address.");
		}

		if (await EmailTakenAsync(email, exceptMemberId: null))
		{
			return Problem("That email address belongs to another account. Log in with it, or use \"Forgot my password\".");
		}

		if (registrationCodes.Issue(userManager.NormalizeEmail(email)) is not { } code)
		{
			return Problem("A code was sent a moment ago; wait a little before asking for another.");
		}

		await emailSender.SendAsync(
			email,
			"Your Lyrious verification code",
			$"Your code for creating your Lyrious account is {code}. It expires in {RegistrationCodes.Lifetime.TotalMinutes:0} minutes.\n\nIf you didn't ask for this, you can ignore this email.",
			cancellationToken);
		return Ok();
	}

	/// <summary>Creates a member, with the code emailed to their address (see <see cref="SendRegistrationCode"/>).</summary>
	[HttpPost("register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model)
	{
		if (CleanEmail(model.Email) is not { } email)
		{
			return Problem("An email address is needed, to verify the account and to reset a forgotten password.");
		}

		if (!registrationCodes.Verify(userManager.NormalizeEmail(email), model.Code))
		{
			return Problem("The code is wrong or has expired. Ask for a new one.");
		}

		if (await EmailTakenAsync(email, exceptMemberId: null))
		{
			return Problem("That email address belongs to another account.");
		}

		var member = Member.Create(model.UserName, model.Name, email, model.PhoneNumber);
		member.EmailConfirmed = true;
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

		// Every member has a private band, joined if they haven't joined another, so a new member
		// can add songs and setlists straight away.
		repo.SetIdentity(new System.Security.Principal.GenericIdentity(member.UserName ?? ""));
		if (repo.EnsurePrivateGroup() is { } privateGroup && member.JoinedGroupId == null)
		{
			member.JoinedGroupId = privateGroup.Id;
		}

		member.LastLoginAt = DateTime.Now;
		await userManager.UpdateAsync(member);
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

		// The code emailed to the member's address proves it's them, as well as the current password.
		if (string.IsNullOrWhiteSpace(member.Email))
		{
			return Problem("Your account has no email address. Add one first (Profile → Email), to receive the code.");
		}

		if (string.IsNullOrWhiteSpace(model.Code)
		    || !await userManager.VerifyUserTokenAsync(member, TokenOptions.DefaultEmailProvider, ChangePasswordPurpose, model.Code.Trim()))
		{
			return Problem("The code is wrong or has expired. Ask for a new one.");
		}

		// Checks the current password and applies the password rules; errors come back as Identity errors.
		var result = await userManager.ChangePasswordAsync(member, model.CurrentPassword, model.NewPassword);
		return result.Succeeded ? Ok() : BadRequest(result.Errors);
	}

	/// <summary>Emails a 6-digit code to the member's address, needed to change the password (see <see cref="ChangePassword"/>).</summary>
	[Authorize]
	[HttpPost("change-password/send-code")]
	public async Task<IActionResult> SendPasswordChangeCode(CancellationToken cancellationToken)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (string.IsNullOrWhiteSpace(member.Email))
		{
			return Problem("Your account has no email address. Add one first (Profile → Email), to receive the code.");
		}

		var code = await userManager.GenerateUserTokenAsync(member, TokenOptions.DefaultEmailProvider, ChangePasswordPurpose);
		await emailSender.SendAsync(
			member.Email,
			"Your Lyrious code for changing your password",
			$"Hi {member.Name},\n\nYour code for changing your Lyrious password is {code}. It expires in a few minutes.\n\nIf you didn't ask for this, someone may know your password: change it.",
			cancellationToken);
		return Ok();
	}

	/// <summary>
	/// Emails a 6-digit code to the address the member wants instead of theirs; changing to it
	/// needs the code (see <see cref="ChangeEmail"/>), so the new address is known to be theirs.
	/// </summary>
	[Authorize]
	[HttpPost("change-email/send-code")]
	public async Task<IActionResult> SendEmailChangeCode([FromBody] EmailCodeModel model, CancellationToken cancellationToken)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (CleanEmail(model.Email) is not { } email)
		{
			return Problem("That isn't an email address.");
		}

		if (string.Equals(userManager.NormalizeEmail(email), member.NormalizedEmail, StringComparison.Ordinal))
		{
			return Problem("That's your email address already.");
		}

		if (await EmailTakenAsync(email, member.Id))
		{
			return Problem("That email address belongs to another account.");
		}

		var code = await userManager.GenerateChangeEmailTokenAsync(member, email);
		await emailSender.SendAsync(
			email,
			"Your Lyrious code for changing your email address",
			$"Hi {member.Name},\n\nYour code for making this your Lyrious email address is {code}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email.",
			cancellationToken);
		return Ok();
	}

	/// <summary>Changes the member's email address, with the code emailed to it (see <see cref="SendEmailChangeCode"/>).</summary>
	[Authorize]
	[HttpPost("change-email")]
	public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailModel model)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (CleanEmail(model.NewEmail) is not { } email)
		{
			return Problem("That isn't an email address.");
		}

		if (await EmailTakenAsync(email, member.Id))
		{
			return Problem("That email address belongs to another account.");
		}

		// Sets the address and marks it confirmed, if the code is the one sent to it.
		var result = await userManager.ChangeEmailAsync(member, email, model.Code.Trim());
		if (result.Succeeded)
		{
			return Ok();
		}

		var errors = result.Errors.Select(error => error.Code == "InvalidToken"
			? new IdentityError { Code = error.Code, Description = "The code is wrong or has expired. Ask for a new one." }
			: error);
		return BadRequest(errors);
	}

	/// <summary>The address trimmed, if it's an email address; else null.</summary>
	private static string? CleanEmail(string? value)
	{
		var email = value?.Trim();
		return !string.IsNullOrEmpty(email) && email.Contains('@') && MailAddress.TryCreate(email, out var address) && address.Address == email
			? email
			: null;
	}

	/// <summary>Whether another member (not <paramref name="exceptMemberId"/>) has this email address.</summary>
	private async Task<bool> EmailTakenAsync(string email, Guid? exceptMemberId)
	{
		var normalized = userManager.NormalizeEmail(email);
		return await Task.FromResult(userManager.Users.Any(m => m.NormalizedEmail == normalized && m.Id != exceptMemberId));
	}

	/// <summary>A refusal the client shows as it is: a list with one error, as Identity's refusals are.</summary>
	private BadRequestObjectResult Problem(string message)
		=> BadRequest(new[] { new IdentityError { Code = "Refused", Description = message } });

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
