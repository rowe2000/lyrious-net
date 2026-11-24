using System.Globalization;
using System.Text.Json.Nodes;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Services;

/// <summary>
/// The emails and texts the server sends (codes, invitations), in the language of the one they're
/// for: Swedish or English. A text is looked up by its English wording, as in the app (L.T), so a
/// text without a translation is sent in English.
/// </summary>
public static class AccountTexts
{
	/// <summary>The language a member has chosen (their "language" setting, kept with the user): "sv" or "en"; English when none.</summary>
	public static string LanguageOf(Member? member)
	{
		if (string.IsNullOrWhiteSpace(member?.Settings))
		{
			return "en";
		}

		try
		{
			return Language(JsonNode.Parse(member.Settings)?["language"]?.GetValue<string>());
		}
		catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
		{
			return "en";
		}
	}

	/// <summary>A language code as the app sends it, made "sv" or "en".</summary>
	public static string Language(string? code) => code == "sv" ? "sv" : "en";

	/// <summary>The member's settings with the language set (the rest kept): for a new account, in the language chosen for it.</summary>
	public static string WithLanguage(string? settings, string language)
	{
		JsonObject json;
		try
		{
			json = (string.IsNullOrWhiteSpace(settings) ? null : JsonNode.Parse(settings) as JsonObject) ?? [];
		}
		catch (System.Text.Json.JsonException)
		{
			json = [];
		}

		json["language"] = Language(language);
		return json.ToJsonString();
	}

	/// <summary>The text in the language ("sv" or "en"), with its values filled in ({0}, {1}, …).</summary>
	public static string T(string language, string english, params object[] values)
	{
		var text = language == "sv" && Swedish.TryGetValue(english, out var swedish) ? swedish : english;
		return values.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, values);
	}

	// Each text the server sends, by its English wording. Texts are kept short: a text message of
	// more than 160 characters is sent (and paid) as several.
	private static readonly Dictionary<string, string> Swedish = new()
	{
		["A band admin"] = "En bandadmin",
		["Your Lyrious verification code"] = "Din verifieringskod för Lyrious",
		["Your code for creating your Lyrious account is {0}. It expires in {1} minutes.\n\nIf you didn't ask for this, you can ignore this email."]
			= "Din kod för att skapa ditt Lyrious-konto är {0}. Den gäller i {1} minuter.\n\nOm du inte bad om den kan du strunta i det här mejlet.",
		["Your code for creating your Lyrious account is {0}. It expires in {1} minutes."]
			= "Din kod för att skapa ditt Lyrious-konto är {0}. Den gäller i {1} minuter.",
		["Your Lyrious code for changing your password is {0}. It expires in a few minutes."]
			= "Din Lyrious-kod för att byta lösenord är {0}. Den gäller i några minuter.",
		["Your Lyrious code for changing your password"] = "Din Lyrious-kod för att byta lösenord",
		["Hi {0},\n\nYour code for changing your Lyrious password is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, someone may know your password: change it."]
			= "Hej {0}!\n\nDin kod för att byta lösenord i Lyrious är {1}. Den gäller i några minuter.\n\nOm du inte bad om den kan någon känna till ditt lösenord: byt det.",
		["Your Lyrious code for changing your email address"] = "Din Lyrious-kod för att byta e-postadress",
		["Hi {0},\n\nYour code for making this your Lyrious email address is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email."]
			= "Hej {0}!\n\nDin kod för att göra det här till din e-postadress i Lyrious är {1}. Den gäller i några minuter.\n\nOm du inte bad om den kan du strunta i det här mejlet.",
		["{0} has made you a Lyrious account in {1}. Username: {2}. Set your password at {3} with \"Forgot my password?\" and this number."]
			= "{0} har skapat ett Lyrious-konto åt dig i {1}. Användarnamn: {2}. Välj lösenord på {3} med \"Glömt lösenordet?\" och det här numret.",
		["{0} invites you to {1} on Lyrious"] = "{0} bjuder in dig till {1} på Lyrious",
		["Hi {0},\n\n{1} has made you an account on Lyrious and invites you to the band {2}, where the band keeps its setlists and lyrics.\n\nYour username: {3}\n\nTo start, set your password: open {4}, choose \"Forgot my password?\" and enter your username or this email address. You'll be emailed a code to set it with. Then log in and join {2}.\n\nIf you don't know who this is from, you can ignore this email."]
			= "Hej {0}!\n\n{1} har skapat ett konto åt dig på Lyrious och bjuder in dig till bandet {2}, där bandet har sina låtlistor och texter.\n\nDitt användarnamn: {3}\n\nBörja med att välja lösenord: öppna {4}, välj \"Glömt lösenordet?\" och skriv ditt användarnamn eller den här e-postadressen. Du får en kod via mejl att välja det med. Logga sedan in och gå med i {2}.\n\nOm du inte vet vem det här kommer från kan du strunta i mejlet.",
		["Your Lyrious code for adding this phone number is {0}. It expires in a few minutes."]
			= "Din Lyrious-kod för att lägga till det här telefonnumret är {0}. Den gäller i några minuter.",
		["Your Lyrious code for a new password is {0}. It expires in a few minutes."]
			= "Din Lyrious-kod för ett nytt lösenord är {0}. Den gäller i några minuter.",
		["Your Lyrious password reset code"] = "Din Lyrious-kod för nytt lösenord",
		["Hi {0},\n\nYour code for resetting your Lyrious password is {1}. It expires in a few minutes.\n\nIf you didn't ask for this, you can ignore this email."]
			= "Hej {0}!\n\nDin kod för att välja nytt lösenord i Lyrious är {1}. Den gäller i några minuter.\n\nOm du inte bad om den kan du strunta i det här mejlet.",
		["Hi,\n\n{0} invites you to the band {1} on Lyrious, where the band keeps its setlists and lyrics.\n\nTo join, create an account at {2} with this email address ({3}), and tell {0} that you have: they can then add you to the band.\n\nIf you don't know who this is from, you can ignore this email."]
			= "Hej!\n\n{0} bjuder in dig till bandet {1} på Lyrious, där bandet har sina låtlistor och texter.\n\nFör att gå med, skapa ett konto på {2} med den här e-postadressen ({3}) och berätta för {0} att du har gjort det: då kan du läggas till i bandet.\n\nOm du inte vet vem det här kommer från kan du strunta i mejlet.",
	};
}
