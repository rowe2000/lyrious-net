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
	private const string WebClientsCorsPolicy = "WebClients";

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

		// One address for everything: the API, the hub and the web app (see the end of Main).
		// In a container (see the Dockerfile) a hosting platform such as Railway says which port to
		// serve in PORT, and reaches the app from outside the container. Elsewhere it's the local
		// address, whatever PORT other tools may have left in the environment.
		var hostPort = GetHostPort();
		builder.WebHost.ConfigureKestrel(options =>
		{
			if (hostPort is { } publicPort)
			{
				options.ListenAnyIP(publicPort);
			}
			else
			{
				options.ListenLocalhost(5180);
			}
		});
		// The web app's files come from the Lyrious.Web project while running from a build
		// (Development does this by itself; a publish copies them into wwwroot instead).
		builder.WebHost.UseStaticWebAssets();

		// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();

		// Real-time change notifications. Hub users are identified by the JWT "sub" claim (member id).
		builder.Services.AddSignalR();
		builder.Services.AddSingleton<IUserIdProvider, MemberIdUserIdProvider>();
		builder.Services.AddSingleton<HubConnectionRegistry>();
		builder.Services.AddScoped<ChangeNotifier>();

		// Account emails (verification and reset codes, invitations); only logged until Gmail or Smtp:Host is configured.
		// Through the Gmail API when its environment variables are set (see GmailEmailSender), else SMTP.
		builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpSettings>() ?? new SmtpSettings());
		if (GmailEmailSender.IsConfigured)
		{
			builder.Services.AddSingleton<IEmailSender, GmailEmailSender>();
		}
		else
		{
			builder.Services.AddSingleton<IEmailSender, EmailSender>();
		}
		// Reset codes are the short 6-digit codes of the email token provider (valid for a few minutes),
		// which people can type, rather than the default long token.
		// So are the codes for changing the email address and the password (see AuthController).
		builder.Services.Configure<IdentityOptions>(options =>
		{
			options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultEmailProvider;
			options.Tokens.ChangeEmailTokenProvider = TokenOptions.DefaultEmailProvider;
		});
		// The codes verifying an address before an account is made with it.
		builder.Services.AddSingleton<RegistrationCodes>();

		// Songs' sound files (see SoundController): a folder per band, next to the project unless
		// Media:Folder says where (e.g. a volume that outlives the container).
		var mediaFolder = builder.Configuration["Media:Folder"];
		builder.Services.AddSingleton(new Lyrious.Api.Controllers.SoundFolder(
			Path.Combine(builder.Environment.ContentRootPath, string.IsNullOrWhiteSpace(mediaFolder) ? "media" : mediaFolder)));

		builder.Services.AddSingleton<Cache>();
		builder.Services.AddScoped<LyriousRepository>();

		// Database: SQL Server or SQLite, chosen by Database:Provider. When it isn't set: SQL Server on
		// Windows (the SQL Express setup) and SQLite elsewhere, so the API runs on a Mac or Linux with
		// no database install. The SQLite file lives next to the project, whatever the working directory.
		var provider = builder.Configuration["Database:Provider"];
		var useSqlite = string.IsNullOrWhiteSpace(provider)
			? !OperatingSystem.IsWindows()
			: provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
		// The values in logged queries and errors only in development: in production they'd put
		// members' data (emails, password hashes, ...) in the host's logs.
		var development = builder.Environment.IsDevelopment();
		if (useSqlite)
		{
			var file = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Database:SqliteFile"] ?? "lyrious-api.db");
			Console.WriteLine($"Database: SQLite ({file})");
			builder.Services.AddDbContext<LyriousContext>(options => options.UseSqlite($"Data Source={file}")
				.EnableSensitiveDataLogging(development)
				.EnableDetailedErrors(development));
		}
		else
		{
			Console.WriteLine("Database: SQL Server");
			builder.Services.AddDbContext<LyriousContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
				.EnableSensitiveDataLogging(development)
				.EnableDetailedErrors(development));
		}

		// Identity for users, roles and password checks only. AddIdentityCore (unlike AddIdentity)
		// registers no cookie schemes, so JWT bearer is the API's only authentication.
		builder.Services.AddIdentityCore<Member>()
			.AddRoles<Role>()
			.AddEntityFrameworkStores<LyriousContext>()
			.AddSignInManager()
			.AddDefaultTokenProviders();

		// Open to the internet, the key in appsettings.json (public, in the repository) won't do.
		var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration, requireOwnKey: hostPort is not null);
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

		// The web app served here (see below) is on the API's own origin and needs no CORS. Browser
		// clients served from elsewhere (e.g. Lyrious.Web's own dev server) do: the browser needs CORS
		// permission to call the API. Only the configured origins are allowed. Credentials are
		// allowed because SignalR's browser client sends them when connecting to the hub.
		var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
		builder.Services.AddCors(options => options.AddPolicy(WebClientsCorsPolicy, policy => policy
			.WithOrigins(allowedOrigins)
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials()));

		builder.Logging.AddConsole();

		var app = builder.Build();

		using (var scope = app.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<LyriousContext>();
			Console.WriteLine(context.EnsureCreatedAndUpToDate()
				? "Database has been created."
				: "Database already exists.");
		}

		app.UseHttpsRedirection();
		app.UseRouting();
		// Must run before authentication so preflight (OPTIONS) requests get CORS headers.
		app.UseCors(WebClientsCorsPolicy);
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

		// The web app (Lyrious.Web, WebAssembly) on the same address as the API: its files, and its
		// page at the root. Only the root: an unknown api/ path stays a 404 instead of becoming the page.
		app.MapStaticAssets();
		app.MapFallbackToFile("/", "index.html", new StaticFileOptions
		{
			// Always checked with the server (ETag), so a new version of the app shows up at once.
			OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
		});

		app.Run();
	}

	/// <summary>The port to serve to the outside when hosted in a container, else null.</summary>
	private static int? GetHostPort()
	{
		// Set by the .NET container images.
		var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
		var value = Environment.GetEnvironmentVariable("PORT");
		if (!inContainer || value is null)
		{
			return null;
		}

		// A PORT that can't be served is an error: falling back to the local address would start
		// an app that nothing outside the container can reach.
		return int.TryParse(value, out var port) && port is >= 1 and <= 65535
			? port
			: throw new InvalidOperationException($"PORT must be a port number (1-65535), but is \"{value}\".");
	}
}
