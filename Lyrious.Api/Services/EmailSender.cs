using System.Net;
using System.Net.Mail;

namespace Lyrious.Api.Services;

/// <summary>Sends account emails (e.g. password reset codes).</summary>
public interface IEmailSender
{
	Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>SMTP settings from the "Smtp" configuration section. Without a Host, emails are only logged.</summary>
public sealed class SmtpSettings
{
	public string? Host { get; set; }
	public int Port { get; set; } = 587;
	public bool EnableSsl { get; set; } = true;
	public string? UserName { get; set; }
	public string? Password { get; set; }
	public string From { get; set; } = "no-reply@lyrious.local";
}

/// <summary>
/// Sends email over SMTP when <see cref="SmtpSettings.Host"/> is configured. Otherwise it writes the
/// message to the log instead, so password resets can be tried in development without a mail server.
/// </summary>
public sealed class EmailSender(SmtpSettings settings, ILogger<EmailSender> logger) : IEmailSender
{
	public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(settings.Host))
		{
			logger.LogWarning("No SMTP server configured (Smtp:Host); email not sent. To: {To} | Subject: {Subject} | {Body}", to, subject, body);
			return;
		}

		using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
		if (!string.IsNullOrEmpty(settings.UserName))
		{
			client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
		}

		using var message = new MailMessage(settings.From, to, subject, body);
		await client.SendMailAsync(message, cancellationToken);
	}
}
