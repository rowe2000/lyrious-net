using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using MimeKit;

namespace Lyrious.Api.Services;

/// <summary>
/// Sends account emails (codes, invitations) through the Gmail API, as a Google Workspace user:
/// a service account with domain-wide delegation for the gmail.send scope acts as that user. Set
/// up with environment variables:
/// <list type="bullet">
/// <item>GOOGLE_SA_JSON: the service account's JSON key.</item>
/// <item>MAIL_IMPERSONATE: the Workspace user it sends as, e.g. no-reply@lyrious.se.</item>
/// <item>MAIL_FROM: the From address, e.g. "Lyrious &lt;no-reply@lyrious.se&gt;" (that user, or one of its aliases); else MAIL_IMPERSONATE.</item>
/// </list>
/// </summary>
public sealed class GmailEmailSender(ILogger<GmailEmailSender> logger) : IEmailSender
{
	private static string? ServiceAccountJson => Environment.GetEnvironmentVariable("GOOGLE_SA_JSON");
	private static string? Impersonate => Environment.GetEnvironmentVariable("MAIL_IMPERSONATE");
	private static string? From => Environment.GetEnvironmentVariable("MAIL_FROM") is { Length: > 0 } from ? from : Impersonate;

	/// <summary>Whether the environment variables for sending through Gmail are set.</summary>
	public static bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceAccountJson) && !string.IsNullOrWhiteSpace(Impersonate);

	private readonly Lazy<GmailService> gmail = new(() =>
	{
		// As GoogleCredential.FromJson (deprecated), but taking only a service account's key.
		var cred = CredentialFactory.FromJson<ServiceAccountCredential>(ServiceAccountJson!)
			.ToGoogleCredential()
			.CreateScoped(GmailService.Scope.GmailSend)
			.CreateWithUser(Impersonate);
		return new GmailService(new BaseClientService.Initializer { HttpClientInitializer = cred, ApplicationName = "Lyrious" });
	});

	public bool CanSend => true;

	public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
	{
		var msg = new MimeMessage();
		// Set when configured (see IsConfigured): MAIL_FROM, else MAIL_IMPERSONATE.
		msg.From.Add(MailboxAddress.Parse(From!));
		msg.To.Add(MailboxAddress.Parse(to));
		msg.Subject = subject;
		msg.Body = new TextPart("html") { Text = Html(body) };

		using var ms = new MemoryStream();
		await msg.WriteToAsync(ms, cancellationToken);
		var raw = Convert.ToBase64String(ms.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
		// "me": the user the service account acts as (MAIL_IMPERSONATE).
		await gmail.Value.Users.Messages.Send(new Message { Raw = raw }, "me").ExecuteAsync(cancellationToken);
		logger.LogInformation("Email sent through Gmail to {To}: {Subject}", to, subject);
	}

	/// <summary>The plain-text body as HTML: its text escaped, its lines kept.</summary>
	internal static string Html(string text)
		=> $"<div style=\"font-family: sans-serif; font-size: 15px;\">{WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>")}</div>";
}
