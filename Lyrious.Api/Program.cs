using System.Text;
using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Lyrious.Api;
internal class Program
{
	private static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		// Add services to the container.
		builder.Services.AddControllers()
			.AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Ignore case for property names
				options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; // Use camelCase for JSON
				options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles; // Handle circular references
				options.JsonSerializerOptions.WriteIndented = true;
			});


		// Add services to the container.

		// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();
		builder.Services.AddSingleton<Cache>();
		builder.Logging.AddConsole();

		builder.Services.AddAuthorization();

		builder.Services.AddDbContext<LyriousContext>(options =>
			options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

		builder.Services.AddScoped<LyriousRepository>();

		var jwtConfig = builder.Configuration.GetSection("Jwt");

		builder.Services.AddAuthentication("Bearer")
			.AddJwtBearer("Bearer", options =>
			{
				options.Authority = jwtConfig["Issuer"];
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = false,
					ValidateAudience = false,
					ValidAudience = jwtConfig["Audience"],
					ValidIssuer = jwtConfig["Issuer"],
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig["Key"]))
				};
			});

		// Add Identity services
		builder.Services.AddIdentity<Member, Role>()
			.AddEntityFrameworkStores<LyriousContext>()
			.AddDefaultTokenProviders();

		builder.Services.AddScoped<LyriousRepository>();
		
		// Configure authentication
		builder.Services.ConfigureApplicationCookie(options =>
		{
			options.LoginPath = "/api/login";
			options.LogoutPath = "/api/logout";
			options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // Session timeout
		});

		var app = builder.Build();

		app.UseAuthorization();     
		
		using (var scope = app.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<LyriousContext>();
			if (context.Database.EnsureCreated())
				Console.WriteLine("Database has been created.");
			else
				Console.WriteLine("Database already exists.");
		}

		// Configure the HTTP request pipeline.
		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		app.UseHttpsRedirection();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		app.Run();
	}
}