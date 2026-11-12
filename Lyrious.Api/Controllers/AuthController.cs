using Lyrious.Api.Services;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(SignInManager<Member> signInManager, UserManager<Member> userManager, JwtSettings jwtSettings) : ControllerBase
{
	[HttpPost("register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model)
	{
		var member = Member.Create(model.UserName, model.Name, model.Email, model.PhoneNumber);
		var result = await userManager.CreateAsync(member, model.Password);

		if (result.Succeeded)
		{
			// Registering doesn't log in; the client calls login to get a token.
			return Ok(new TokenModel { Token = "", Member = member });
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

		return Ok(new TokenModel { Token = jwtSettings.CreateToken(member), Member = member });
	}

	[HttpPost("logout")]
	public IActionResult Logout()
	{
		// Tokens are stateless: logging out means the client discards its token.
		// A token stays valid until it expires (Jwt:LifetimeDays).
		return Ok("Logged out successfully.");
	}
}
