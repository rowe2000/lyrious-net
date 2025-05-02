using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Lyrious.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
	private readonly SignInManager<Member> signInManager;
	private readonly UserManager<Member> userManager;

	public AuthController(SignInManager<Member> signInManager, UserManager<Member> userManager)
	{
		this.signInManager = signInManager;
		this.userManager = userManager;
	}

	[HttpPost("register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model)
	{
		var member = Member.Create(model.UserName, model.Name, model.Email, model.PhoneNumber);
		var result = await userManager.CreateAsync(member, model.Password);

		if (result.Succeeded)
		{
			return Ok(new TokenModel { Token = "", Member = member });
		}

		return BadRequest(result.Errors);
	}

	[HttpPost("login")]
	public async Task<IActionResult> Login([FromBody] LoginModel model)
	{
		var result = await signInManager.PasswordSignInAsync(model.Username, model.Password, isPersistent: false, lockoutOnFailure: false);
		
		if (result.Succeeded)
		{
			// Retrieve the user
			var member = await userManager.FindByNameAsync(model.Username);
			if (member == null)
			{
				return Unauthorized("Invalid username or password.");
			}

			// Generate the JWT token
			var token = GenerateJwtToken(member);

			return Ok(new TokenModel { Token = token, Member = member });
		}

		return Unauthorized("Invalid username or password.");
	}

	[HttpPost("logout")]
	public async Task<IActionResult> Logout()
	{
		await signInManager.SignOutAsync();
		return Ok("Logged out successfully.");
	}
	private string GenerateJwtToken(Member user)
	{
		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("YourSuperSecureKeyThatIsAtLeast32CharactersLong")); // Replace with a secure key
		var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, user.UserName)
		};

		var token = new JwtSecurityToken(
			//issuer: "your-issuer", // Replace with your issuer
			//audience: "your-audience", // Replace with your audience
			//claims: claims,
			expires: DateTime.UtcNow.AddDays(30), // Token expiration
			signingCredentials: creds
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}