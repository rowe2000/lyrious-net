using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Services;

/// <summary>
/// Adds the release note that came with the server (release-note.json next to it, written by
/// deploy/railway.sh from the commits since the last release) when it starts: as an unpublished
/// note, for a site manager to check and publish on the About page. Only once per version and
/// branch, so a restart (or a note already written by hand) leaves it alone.
/// </summary>
public static class ReleaseNoteImport
{
	private sealed record Shipped(string? Version, string? Branch, string? Text, DateTime? Created);

	public static void Run(LyriousContext context, ILogger logger)
	{
		var file = Path.Combine(AppContext.BaseDirectory, "release-note.json");
		if (!File.Exists(file))
		{
			return;
		}

		try
		{
			var shipped = JsonSerializer.Deserialize<Shipped>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (shipped is not { Version.Length: > 0 } || string.IsNullOrWhiteSpace(shipped.Text))
			{
				return;
			}

			var branch = shipped.Branch ?? "";
			if (context.ReleaseNotes.Any(note => note.Version == shipped.Version && note.Branch == branch && !note.Removed))
			{
				return;
			}

			var text = shipped.Text.Length > ReleaseNote.MaxTextLength ? shipped.Text[..ReleaseNote.MaxTextLength] : shipped.Text;
			context.ReleaseNotes.Add(new ReleaseNote
			{
				Version = shipped.Version,
				Branch = branch,
				Text = text,
				Published = false,
				Created = shipped.Created ?? DateTime.Now,
			});
			context.SaveChanges();
			logger.LogInformation("Added the release note of {Version} on {Branch} (unpublished)", shipped.Version, branch);
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			// A broken file mustn't stop the server.
			logger.LogWarning(ex, "Couldn't read release-note.json");
		}
	}
}
