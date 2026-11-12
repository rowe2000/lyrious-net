using System.Text;
using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
// Add MQTT namespaces
using MQTTnet.Server;
using Lyrious.Api.Services; // We'll create this folder and class shortly

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

		// Configure MQTT broker
		builder.Services.AddSingleton<MqttServerFactory>();

		// Setup MQTT server options
		builder.Services.AddSingleton(sp =>
		{
			var mqttFactory = sp.GetRequiredService<MqttServerFactory>();
			return mqttFactory.CreateServerOptionsBuilder()
				.WithDefaultEndpoint()
				.WithDefaultEndpointPort(1883) // Standard MQTT port
				//.WithConnectionValidator(context =>
				//{
				//	// You can implement authentication logic here if needed
				//	// For development, we'll allow all connections
				//	context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.Success;
				//})
				.Build();
		});

		// Register MQTT server
		builder.Services.AddSingleton(sp =>
		{
			var mqttFactory = sp.GetRequiredService<MqttServerFactory>();
			var options = sp.GetRequiredService<MqttServerOptions>();
			return mqttFactory.CreateMqttServer(options);
		});

		// Register MQTT service
		builder.Services.AddSingleton<MqttService>();
		builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());

		// Add WebSocket support for MQTT
		builder.Services.AddConnections();

		// Configure Kestrel for WebSocket endpoints
		builder.WebHost.ConfigureKestrel(options =>
		{
			// Regular HTTP endpoint
			options.ListenAnyIP(5000);

			// MQTT over WebSockets endpoint will be handled by middleware
		});

		// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();

		builder.Services.AddSingleton<Cache>();
		builder.Services.AddScoped<LyriousRepository>();
		
		builder.Services.AddDbContext<LyriousContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
		
		// Add Identity services
		builder.Services.AddIdentity<Member, Role>()
			.AddEntityFrameworkStores<LyriousContext>()
			.AddDefaultTokenProviders();


		builder.Services.AddAuthentication("Bearer")
			.AddJwtBearer("Bearer", options =>
			{
				var jwtConfig = builder.Configuration.GetSection("Jwt");
				var jwtKey = jwtConfig["Key"] ?? throw new InvalidOperationException("JWT Key is not configured.");
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidAudience = jwtConfig["Audience"] ?? "lyrious-api",
					ValidIssuer = jwtConfig["Issuer"] ?? "lyrious-client",
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
				};
			});

		builder.Services.AddAuthorization();
		
		
		// Configure authentication
		builder.Services.ConfigureApplicationCookie(options =>
		{
			options.LoginPath = "/api/login";
			options.LogoutPath = "/api/logout";
			options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // Session timeout
		});


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
		app.UseWebSockets();

		// Configure the HTTP request pipeline.
		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		// Set up MQTT WebSocket handler
		
		app.MapControllers();

		// Map a custom middleware that handles MQTT over WebSockets
		app.Map("/mqtt", async context =>
		{
			try
			{
				if (!context.WebSockets.IsWebSocketRequest)
				{
					context.Response.StatusCode = 400;
					return;
				}

				using var webSocket = await context.WebSockets.AcceptWebSocketAsync("mqtt");
				var mqttService = context.RequestServices.GetRequiredService<MqttService>();
				await mqttService.HandleWebSocketConnectionAsync(webSocket, context.RequestAborted);
			}
			catch (Exception ex)
			{
				var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
				logger.LogError(ex, "Error handling WebSocket connection");
				if (!context.Response.HasStarted)
					context.Response.StatusCode = 500;
			}
		});


		app.Run();
	}
}