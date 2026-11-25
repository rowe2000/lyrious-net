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
	ISmsSender smsSender,
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

		var language = AccountTexts.Language(model.Language);
		await emailSender.SendAsync(
			email,
			AccountTexts.T(language, "Your Lyrious verification code"),
			AccountTexts.T(language, "Your code for creating your Lyrious account is {0}. It expires in {1} minutes.\n\nIf you didn't ask for this, you can ignore this email.", code, RegistrationCodes.Lifetime.TotalMinutes.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
			cancellationToken);
		return Ok();
	}

	/// <summary>
	/// Texts a 6-digit code to the phone number someone wants to register with instead of an email
	/// address; registering needs it (see <see cref="Register"/>), so the number is known to be theirs.
	/// </summary>
	[HttpPost("register/send-sms-code")]
	public async Task<IActionResult> SendRegistrationSmsCode([FromBody] PhoneCodeModel model, CancellationToken cancellationToken)
	{
		if (PhoneNumbers.Clean(model.PhoneNumber) is not { } phone)
		{
			return Problem("That isn't a phone number.");
		}

		if (PhoneTaken(phone, exceptMemberId: null))
		{
			return Problem("That phone number belongs to another account. Log in with it, or use \"Forgot my password\".");
		}

		if (registrationCodes.Issue(PhoneKey(phone)) is not { } code)
		{
			return Problem("A code was sent a moment ago; wait a little before asking for another.");
		}

		var language = AccountTexts.Language(model.Language);
		await smsSender.SendAsync(
			phone,
			AccountTexts.T(language, "Your code for creating your Lyrious account is {0}. It expires in {1} minutes.", code, RegistrationCodes.Lifetime.TotalMinutes.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
			cancellationToken);
		return Ok();
	}

	// A phone number's registration code, kept apart from email addresses' (see RegistrationCodes).
	private static string PhoneKey(string phone) => "phone:" + phone;

	/// <summary>
	/// Creates a member, with the code emailed to their address (see <see cref="SendRegistrationCode"/>),
	/// or texted to their phone number when they register with that instead (see <see cref="SendRegistrationSmsCode"/>).
	/// </summary>
	[HttpPost("register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model)
	{
		// With a phone number instead of an email address: verified by its texted code.
		if (string.IsNullOrWhiteSpace(model.Email) && !string.IsNullOrWhiteSpace(model.PhoneNumber))
		{
			return await RegisterWithPhoneAsync(model);
		}

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
		// The language chosen when registering (the device's, unless changed): the app's and the emails'.
		member.Settings = AccountTexts.WithLanguage(member.Settings, AccountTexts.Language(model.Language));
		var result = await userManager.CreateAsync(member, model.Password);

		if (result.Succeeded)
		{
			// Registering doesn't log in; the client calls login to get a token.
			return Ok(new TokenModel { Token = "", Member = PublicProfile(member) });
		}

		return BadRequest(result.Errors);
	}

	/// <summary>A member with a phone number instead of an email address, the number verified by its texted code (and so able to get codes).</summary>
	private async Task<IActionResult> RegisterWithPhoneAsync(RegisterModel model)
	{
		if (PhoneNumbers.Clean(model.PhoneNumber) is not { } phone)
		{
			return Problem("That isn't a phone number.");
		}

		if (!registrationCodes.Verify(PhoneKey(phone), model.Code))
		{
			return Problem("The code is wrong or has expired. Ask for a new one.");
		}

		if (PhoneTaken(phone, exceptMemberId: null))
		{
			return Problem("That phone number belongs to another account.");
		}

		var member = Member.Create(model.UserName, model.Name, email: null, phone);
		member.PhoneNumberConfirmed = true;
		member.Settings = AccountTexts.WithLanguage(member.Settings, AccountTexts.Language(model.Language));
		var result = await userManager.CreateAsync(member, model.Password);
		return result.Succeeded
			? Ok(new TokenModel { Token = "", Member = PublicProfile(member) })
			: BadRequest(result.Errors);
	}

	/// <summary>
	/// Logs in with the username, or the email address or phone number of the account (when only
	/// one account has it), and the password.
	/// </summary>
	[HttpPost("login")]
	public async Task<IActionResult> Login([FromBody] LoginModel model)
	{
		var (member, _) = await FindAccountAsync(model.Username, forLogin: true);
		if (member == null)
		{
			return Unauthorized("Invalid username or password.");
		}

		// Verifies the password (and lockout state) without creating a cookie session.
		var result = await signInManager.CheckPasswordSignInAsync(member, model.Password, lockoutOnFailure: false);
		if (result.IsLockedOut)
		{
			// Disabled by a site manager. Not 401 (a wrong password, which the app would also forget
			// offline) nor 403 (taken for a server that isn't there): a refusal that says why.
			return BadRequest("This account has been disabled. Ask the site's managers to enable it again.");
		}

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
		// A normal login: a member's rights, also for a listed site manager (who elevates with api/auth/elevate).
		return Ok(new TokenModel { Token = jwtSettings.CreateToken(member), Member = PublicProfile(member), CanElevate = LyriousRepository.IsSiteManagerAccount(member) });
	}

	/// <summary>
	/// The elevated login of a member listed as a site manager (SITE_MANAGERS): with their password
	/// again, a token with site-manager rights (admin of every band). Refused for anyone not listed.
	/// </summary>
	[Authorize]
	[HttpPost("elevate")]
	public async Task<IActionResult> Elevate([FromBody] ElevateModel model)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (!LyriousRepository.IsSiteManagerAccount(member))
		{
			return StatusCode(StatusCodes.Status403Forbidden, "You aren't a site manager.");
		}

		var result = await signInManager.CheckPasswordSignInAsync(member, model.Password, lockoutOnFailure: false);
		if (!result.Succeeded)
		{
			return Problem("Wrong password.");
		}

		return Ok(new TokenModel { Token = jwtSettings.CreateToken(member, siteManager: true), Member = PublicProfile(member), IsSiteManager = true, CanElevate = true });
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
	public async Task<IActionResult> SendPasswordChangeCode([FromQuery] string? by, CancellationToken cancellationToken)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		// By text instead, to the member's verified phone number (?by=sms).
		if (by == "sms")
		{
			if (string.IsNullOrWhiteSpace(member.PhoneNumber) || !member.PhoneNumberConfirmed)
			{
				return Problem("Your account has no verified phone number. Add one first (Profile → Phone).");
			}

			var smsCode = await userManager.GenerateUserTokenAsync(member, TokenOptions.DefaultEmailProvider, ChangePasswordPurpose);
			await smsSender.SendAsync(member.PhoneNumber, AccountTexts.T(AccountTexts.LanguageOf(member), "Your Lyrious code for changing your password is {0}. It expires in a few minutes.", smsCode), cancellationToken);
			return Ok();
		}

		if (string.IsNullOrWhiteSpace(member.Email))
		{
			return Problem("Your account has no email address. Add one first (Profile → Email), to receive the code.");
		}

		var code = await userManager.GenerateUserTokenAsync(member, TokenOptions.DefaultEmailProvider, ChangePasswordPurpose);
		var language = AccountTexts.LanguageOf(member);
		await emailSender.SendAsync(
			member.Email,
			AccountTexts.T(language, "Your Lyrious code for changing your password"),
			AccountTexts.T(language, "Hi {0},\n\nYour code for changing your Lyrious password is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, someone may know your password: change it.", member.Name, code),
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
		var language = AccountTexts.LanguageOf(member);
		await emailSender.SendAsync(
			email,
			AccountTexts.T(language, "Your Lyrious code for changing your email address"),
			AccountTexts.T(language, "Hi {0},\n\nYour code for making this your Lyrious email address is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email.", member.Name, code),
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

	/// <summary>
	/// A band's admin makes an account for someone new: no password (they set their own, with "Forgot
	/// my password?" and the code sent to them, so the admin never knows it), told their username and
	/// how to start by email, or by text when a phone number is given instead. The admin's app then
	/// invites the account to the band, as any member. Answers the new member's public profile.
	/// </summary>
	[Authorize]
	[HttpPost("create-member")]
	public async Task<IActionResult> CreateMember([FromBody] CreateMemberModel model, CancellationToken cancellationToken)
	{
		repo.SetIdentity(User.Identity);
		if (!repo.IsAdminOf(model.GroupId) || repo.FindGroup(model.GroupId) is not { IsPrivate: false } group)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only the band's admins can make accounts for it.");
		}

		var userName = model.UserName.Trim();
		var name = model.Name.Trim();
		if (userName.Length == 0 || name.Length == 0)
		{
			return Problem("Enter their name and a username.");
		}

		// Told by email, or else by text: one of the two, and where it can be sent.
		var byPhone = string.IsNullOrWhiteSpace(model.Email);
		string? email = null, phone = null;
		if (!byPhone)
		{
			if ((email = CleanEmail(model.Email)) == null)
			{
				return Problem("That isn't an email address.");
			}

			if (await EmailTakenAsync(email, exceptMemberId: null))
			{
				return Problem("That email address belongs to an account already: invite them with it instead.");
			}

			// Without the email they couldn't set a password, and the account would be of no use.
			if (!emailSender.CanSend)
			{
				return Problem("The server isn't set up to send email, so it can't tell them about the account.");
			}
		}
		else
		{
			if ((phone = PhoneNumbers.Clean(model.PhoneNumber)) == null)
			{
				return Problem("Enter their email address or phone number.");
			}

			if (PhoneTaken(phone, exceptMemberId: null))
			{
				return Problem("That phone number belongs to an account already: invite them with it instead.");
			}

			if (!smsSender.CanSend)
			{
				return Problem("The server isn't set up to send texts, so it can't tell them about the account. Use their email address instead.");
			}
		}

		if (await userManager.FindByNameAsync(userName) != null)
		{
			return Problem("That username is taken. Choose another.");
		}

		// Their language (the admin's, unless chosen otherwise): kept as theirs, and the invitation's.
		var language = AccountTexts.Language(model.Language);
		var member = Member.Create(userName, name, email, phone);
		member.Settings = AccountTexts.WithLanguage(member.Settings, language);
		var result = await userManager.CreateAsync(member);
		if (!result.Succeeded)
		{
			return BadRequest(result.Errors);
		}

		var inviter = !string.IsNullOrWhiteSpace(repo.Me?.Name) ? repo.Me.Name : repo.Me?.UserName ?? AccountTexts.T(language, "A band admin");
		// Where the caller reached the API is where the web app is, too.
		var appUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/app/";
		try
		{
			if (byPhone)
			{
				// Short: a text of more than 160 characters is sent (and paid) as several. The address
				// without "https://" (phones make "www.lyrious.se/app" a link all the same).
				await smsSender.SendAsync(
					phone!,
					AccountTexts.T(language, "{0} has made you a Lyrious account in {1}. Username: {2}. Set your password at {3} with \"Forgot my password?\" and this number.", inviter, group.Name, userName, $"{Request.Host}{Request.PathBase}/app"),
					cancellationToken);
			}
			else
			{
				await emailSender.SendAsync(
					email!,
					AccountTexts.T(language, "{0} invites you to {1} on Lyrious", inviter, group.Name),
					AccountTexts.T(language, "Hi {0},\n\n{1} has made you an account on Lyrious and invites you to the band {2}, where the band keeps its setlists and lyrics.\n\nYour username: {3}\n\nTo start, set your password: open {4}, choose \"Forgot my password?\" and enter your username or this email address. You'll be emailed a code to set it with. Then log in and join {2}.\n\nIf you don't know who this is from, you can ignore this email.", name, inviter, group.Name, userName, appUrl),
					cancellationToken);
			}
		}
		catch (Exception ex)
		{
			// They'd never hear of an account they can't use: take it away again, so the admin can try once more.
			logger.LogError(ex, "Couldn't tell a new member about their account; the account was removed again");
			await userManager.DeleteAsync(member);
			return Problem(byPhone
				? "The text to them couldn't be sent, so no account was made. Try again in a while."
				: "The email to them couldn't be sent, so no account was made. Try again in a while.");
		}
		logger.LogInformation("An admin made an account for a new member of a band");
		return Ok(PublicProfile(member));
	}

	/// <summary>
	/// Texts a code to the phone number the member wants as theirs; changing to it needs the code
	/// (see <see cref="ChangePhone"/>), so the number is known to be theirs and can get their codes.
	/// </summary>
	[Authorize]
	[HttpPost("change-phone/send-code")]
	public async Task<IActionResult> SendPhoneChangeCode([FromBody] PhoneCodeModel model, CancellationToken cancellationToken)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (PhoneNumbers.Clean(model.PhoneNumber) is not { } phone)
		{
			return Problem("That isn't a phone number.");
		}

		if (PhoneTaken(phone, member.Id))
		{
			return Problem("That phone number belongs to another account.");
		}

		var code = await userManager.GenerateChangePhoneNumberTokenAsync(member, phone);
		await smsSender.SendAsync(phone, AccountTexts.T(AccountTexts.LanguageOf(member), "Your Lyrious code for adding this phone number is {0}. It expires in a few minutes.", code), cancellationToken);
		return Ok();
	}

	/// <summary>
	/// Sets the member's phone number, with the code texted to it (see <see cref="SendPhoneChangeCode"/>),
	/// marked verified; an empty number removes theirs. Answers the number as kept (empty when removed).
	/// </summary>
	[Authorize]
	[HttpPost("change-phone")]
	public async Task<IActionResult> ChangePhone([FromBody] ChangePhoneModel model)
	{
		var member = await userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
		if (member == null)
		{
			return Unauthorized();
		}

		if (string.IsNullOrWhiteSpace(model.PhoneNumber))
		{
			var removed = await userManager.SetPhoneNumberAsync(member, null);
			return removed.Succeeded ? Ok(new PhoneCodeModel { PhoneNumber = "" }) : BadRequest(removed.Errors);
		}

		if (PhoneNumbers.Clean(model.PhoneNumber) is not { } phone)
		{
			return Problem("That isn't a phone number.");
		}

		if (PhoneTaken(phone, member.Id))
		{
			return Problem("That phone number belongs to another account.");
		}

		// Sets the number and marks it verified, if the code is the one texted to it.
		var result = await userManager.ChangePhoneNumberAsync(member, phone, model.Code?.Trim() ?? "");
		if (result.Succeeded)
		{
			// The number as kept (+46701740610), for the app to show.
			return Ok(new PhoneCodeModel { PhoneNumber = phone });
		}

		var errors = result.Errors.Select(error => error.Code == "InvalidToken"
			? new IdentityError { Code = error.Code, Description = "The code is wrong or has expired. Ask for a new one." }
			: error);
		return BadRequest(errors);
	}

	/// <summary>Whether another member (not <paramref name="exceptMemberId"/>) has this phone number (in its clean form).</summary>
	private bool PhoneTaken(string phone, Guid? exceptMemberId)
		=> userManager.Users.Any(m => m.PhoneNumber == phone && m.Id != exceptMemberId);

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
	/// Sends a short-lived 6-digit code for resetting the password: by text when the member entered
	/// their phone number (or has no email address, only a phone number), else by email. Always
	/// answers 200, whether or not the account exists or can be reached, so it can't be used to find
	/// out who has an account.
	/// </summary>
	[HttpPost("forgot-password")]
	public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordModel model, CancellationToken cancellationToken)
	{
		var (member, byPhone) = await FindAccountAsync(model.UserNameOrEmail);
		if (member == null)
		{
			logger.LogInformation("Password reset requested for an unknown account");
			return Ok();
		}

		var bySms = byPhone || (string.IsNullOrWhiteSpace(member.Email) && CanTextCodesTo(member));
		if (!bySms && string.IsNullOrWhiteSpace(member.Email))
		{
			logger.LogInformation("Password reset requested for an account without an email address or a phone number to text");
			return Ok();
		}

		var code = await userManager.GeneratePasswordResetTokenAsync(member);
		var language = AccountTexts.LanguageOf(member);
		if (bySms)
		{
			await smsSender.SendAsync(member.PhoneNumber!, AccountTexts.T(language, "Your Lyrious code for a new password is {0}. It expires in a few minutes.", code), cancellationToken);
		}
		else
		{
			await emailSender.SendAsync(
				member.Email!,
				AccountTexts.T(language, "Your Lyrious password reset code"),
				AccountTexts.T(language, "Hi {0},\n\nYour code for resetting your Lyrious password is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email.", member.Name, code),
				cancellationToken);
		}

		return Ok();
	}

	/// <summary>Sets a new password using the code from <see cref="ForgotPassword"/>.</summary>
	[HttpPost("reset-password")]
	public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordModel model)
	{
		var (member, _) = await FindAccountAsync(model.UserNameOrEmail);
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
	/// Finds a member by username, else by email, else by phone number (<c>ByPhone</c> then true).
	/// Emails aren't required to be unique, so an email shared by several members matches none of
	/// them; likewise a phone number. Only a number codes may be texted to counts (see <see cref="CanTextCodesTo"/>),
	/// except <paramref name="forLogin"/>, where the password is checked next.
	/// </summary>
	private async Task<(Member? Member, bool ByPhone)> FindAccountAsync(string userNameOrEmailOrPhone, bool forLogin = false)
	{
		var value = userNameOrEmailOrPhone.Trim();
		var member = await userManager.FindByNameAsync(value);
		if (member != null)
		{
			return (member, false);
		}

		if (value.Contains('@'))
		{
			var normalizedEmail = userManager.NormalizeEmail(value);
			var matches = userManager.Users.Where(m => m.NormalizedEmail == normalizedEmail).Take(2).ToList();
			return (matches.Count == 1 ? matches[0] : null, false);
		}

		if (PhoneNumbers.Clean(value) is { } phone)
		{
			var matches = userManager.Users.Where(m => m.PhoneNumber == phone).Take(2).ToList();
			return matches.Count == 1 && (forLogin || CanTextCodesTo(matches[0])) ? (matches[0], true) : (null, false);
		}

		return (null, false);
	}

	/// <summary>
	/// Whether codes may be texted to the member's phone number: it's verified (see <see cref="ChangePhone"/>),
	/// or the account has no password yet (an admin made it with that number, and its owner sets the
	/// first password). Else whoever typed the number in could take over the account.
	/// </summary>
	private static bool CanTextCodesTo(Member member)
		=> !string.IsNullOrWhiteSpace(member.PhoneNumber) && (member.PhoneNumberConfirmed || member.PasswordHash == null);

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
		PhoneNumberConfirmed = member.PhoneNumberConfirmed,
		JoinedGroupId = member.JoinedGroupId,
		CreatedAt = member.CreatedAt,
		ChangedAt = member.ChangedAt,
	};
}
