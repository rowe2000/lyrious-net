using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class SongbookController(LyriousRepository repo) : Controller<Songbook>(repo);