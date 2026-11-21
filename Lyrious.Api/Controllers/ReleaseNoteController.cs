using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

/// <summary>
/// The app's release notes (api/releasenote): every logged-in member reads the published ones;
/// site managers read all of them and add, change (e.g. publish) and remove them.
/// </summary>
public class ReleaseNoteController(LyriousRepository repo) : Controller<ReleaseNote>(repo);
