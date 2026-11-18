using System.Text.RegularExpressions;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

/// <summary>
/// Sound files of songs' media links ("sound:&lt;id&gt;/&lt;name&gt;"), so a file added on one device
/// plays on the band's others too. Kept on disk per band, in <see cref="SoundFolder"/>: a file is
/// uploaded for a song (which says the band) and fetched by any member of that band. There's no
/// delete: the files are small next to what a device can hold, and another device may still need one.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sound")]
public partial class SoundController(LyriousRepository repo, SoundFolder folder) : ControllerBase
{
	/// <summary>The largest sound file kept, as in the apps (MediaLinks.MaxSoundSize).</summary>
	public const long MaxSize = 100L * 1024 * 1024;

	// As the apps make them (MediaLinks.NewSoundId): 32 hex digits and the file's extension.
	[GeneratedRegex("^[0-9a-f]{32}\\.(mp3|m4a|aac|wav|ogg|oga|opus|flac|webm)$")]
	private static partial Regex IdPattern();

	// PUT api/sound/<id>?songId=<song>
	/// <summary>
	/// Keeps the request's body as the sound file under the id, for the band whose song it's for.
	/// The song must be one the caller can see (a member of its band): a song made offline and not
	/// sent yet isn't found, and the app tries again later. A file already kept isn't written again.
	/// </summary>
	[HttpPut("{id}")]
	[RequestSizeLimit(MaxSize + 1024 * 1024)]
	public async Task<IActionResult> Put(string id, [FromQuery] Guid songId, CancellationToken cancellationToken)
	{
		if (!IdPattern().IsMatch(id))
		{
			return BadRequest("That isn't a sound file's id.");
		}

		repo.SetIdentity(User.Identity);
		if (repo.Get<Song>([songId]).FirstOrDefault() is not { } song
			|| repo.Get<Songbook>([song.SongbookId]).FirstOrDefault()?.GroupId is not { } groupId)
		{
			return NotFound("The song isn't on the server (yet).");
		}

		var path = folder.PathOf(groupId, id);
		if (System.IO.File.Exists(path))
		{
			return Ok();
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		// Written next to it first, so a failure halfway leaves no half a file under the id.
		var partial = path + "." + Guid.NewGuid().ToString("N") + ".part";
		try
		{
			await using (var file = System.IO.File.Create(partial))
			{
				await Request.Body.CopyToAsync(file, cancellationToken);
				if (file.Length > MaxSize)
				{
					return StatusCode(StatusCodes.Status413PayloadTooLarge, "The file is too big.");
				}
			}

			System.IO.File.Move(partial, path, overwrite: true);
			return Ok();
		}
		finally
		{
			System.IO.File.Delete(partial);
		}
	}

	// GET api/sound/<id>
	/// <summary>The sound file, if it's kept for one of the caller's bands; else 404.</summary>
	[HttpGet("{id}")]
	public IActionResult Get(string id)
	{
		if (!IdPattern().IsMatch(id))
		{
			return NotFound();
		}

		repo.SetIdentity(User.Identity);
		foreach (var groupId in repo.GetMyGroupIds())
		{
			var path = folder.PathOf(groupId, id);
			if (System.IO.File.Exists(path))
			{
				return PhysicalFile(path, MimeTypeOf(id), enableRangeProcessing: true);
			}
		}

		return NotFound();
	}

	private static string MimeTypeOf(string id) => Path.GetExtension(id) switch
	{
		".mp3" => "audio/mpeg",
		".m4a" or ".aac" => "audio/mp4",
		".wav" => "audio/wav",
		".ogg" or ".oga" or ".opus" => "audio/ogg",
		".flac" => "audio/flac",
		".webm" => "audio/webm",
		_ => "application/octet-stream",
	};
}

/// <summary>Where the sound files are kept: a folder per band under <see cref="Root"/>.</summary>
public sealed class SoundFolder(string root)
{
	public string Root { get; } = root;

	public string PathOf(Guid groupId, string id) => Path.Combine(Root, groupId.ToString("N"), id);
}
