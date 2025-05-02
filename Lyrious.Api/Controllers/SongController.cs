using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class SongController(LyriousRepository repo) : Controller<Song>(repo);