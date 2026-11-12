using System.Text.Json;
using Lyrious.Api.Hubs;
using Lyrious.Api.Services;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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

		builder.WebHost.ConfigureKestrel(options =>
		{
			options.ListenLocalhost(7000);
		});

		// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();

		// Real-time change notifications. Hub users are identified by the JWT "sub" claim (member id).
		builder.Services.AddSignalR();
		builder.Services.AddSingleton<IUserIdProvider, MemberIdUserIdProvider>();
		builder.Services.AddSingleton<HubConnectionRegistry>();
		builder.Services.AddScoped<ChangeNotifier>();

		builder.Services.AddSingleton<Cache>();
		builder.Services.AddScoped<LyriousRepository>();

		builder.Services.AddDbContext<LyriousContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

		// Identity for users, roles and password checks only. AddIdentityCore (unlike AddIdentity)
		// registers no cookie schemes, so JWT bearer is the API's only authentication.
		builder.Services.AddIdentityCore<Member>()
			.AddRoles<Role>()
			.AddEntityFrameworkStores<LyriousContext>()
			.AddSignInManager()
			.AddDefaultTokenProviders();

		var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);
		builder.Services.AddSingleton(jwtSettings);

		builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = jwtSettings.ValidationParameters;
				options.Events = new JwtBearerEvents
				{
					// WebSocket and server-sent-event clients can't set headers, so SignalR sends the token
					// as ?access_token=. Accept it only on the hub path.
					OnMessageReceived = context =>
					{
						var accessToken = context.Request.Query["access_token"];
						if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments(LyriousHub.Path))
						{
							context.Token = accessToken;
						}

						return Task.CompletedTask;
					}
				};
			});

		builder.Services.AddAuthorization();

		builder.Logging.AddConsole();

		var app = builder.Build();

		using (var scope = app.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<LyriousContext>();
			Console.WriteLine(context.Database.EnsureCreated()
				? "Database has been created."
				: "Database already exists.");
		}

		app.UseHttpsRedirection();
		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();

		// Configure the HTTP request pipeline.
		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		app.MapControllers();
		app.MapHub<LyriousHub>(LyriousHub.Path);

		app.Run();
	}
}
