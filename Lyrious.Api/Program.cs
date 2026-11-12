using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
// Add MQTT namespaces
using MQTTnet.Server;
using Lyrious.Api.Services;

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
			options.ListenLocalhost(7000);

			// MQTT over WebSockets endpoint will be handled by middleware
		});

		// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();

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
			.AddJwtBearer(options => options.TokenValidationParameters = jwtSettings.ValidationParameters);

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