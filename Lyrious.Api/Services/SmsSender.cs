using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lyrious.Api.Services;

/// <summary>Sends account texts (codes, invitations) to a phone number.</summary>
public interface ISmsSender
{
	/// <summary>Whether texts can be sent (an SMS service is set up); else they're only logged.</summary>
	bool CanSend { get; }

	/// <summary>Texts the message to the number (international form, e.g. +46701740610).</summary>
	Task SendAsync(string to, string message, CancellationToken cancellationToken = default);
}

/// <summary>The Hello SMS account the server texts through (configuration section "HelloSms").</summary>
public sealed class HelloSmsSettings
{
	/// <summary>The API user's name (Hello SMS: Konto → API → Lägg till en API-användare).</summary>
	public string? UserName { get; set; }

	/// <summary>The API user's password (shown once, when the API user is made).</summary>
	public string? Password { get; set; }

	/// <summary>Who the texts are from, as the phone shows it: 3 to 11 letters or digits.</summary>
	public string From { get; set; } = "Lyrious";

	/// <summary>Hello SMS answers as if sent, but sends nothing and uses no credits.</summary>
	public bool TestMode { get; set; }

	/// <summary>Where the Hello SMS API is (another address only for testing).</summary>
	public string BaseUrl { get; set; } = "https://api.hellosms.se";
}

/// <summary>
/// Texts through Hello SMS (https://docs.hellosms.se): POST api/v1/sms/send with the API user's
/// login (Basic). Without an API user set up the texts are only logged, as emails are without a
/// mail server, so a test server shows its codes in its log.
/// </summary>
public sealed class HelloSmsSender(HelloSmsSettings settings, ILogger<HelloSmsSender> logger) : ISmsSender
{
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

	public bool CanSend => !string.IsNullOrWhiteSpace(settings.UserName) && !string.IsNullOrWhiteSpace(settings.Password);

	public async Task SendAsync(string to, string message, CancellationToken cancellationToken = default)
	{
		if (!CanSend)
		{
			logger.LogWarning("No SMS service configured (HelloSms:UserName and HelloSms:Password); text not sent. To: {To} | {Message}", to, message);
			return;
		}

		var body = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["to"] = new[] { to },
			["from"] = settings.From,
			["subject"] = "Lyrious",
			["message"] = message,
			["testMode"] = settings.TestMode,
		});
		using var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl.TrimEnd('/') + "/api/v1/sms/send")
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Password}")));
		using var response = await Http.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			// Hello SMS says why in statusText; the caller turns the failure into words for the user.
			var answer = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"Hello SMS answered {(int)response.StatusCode}: {answer}");
		}
	}
}
