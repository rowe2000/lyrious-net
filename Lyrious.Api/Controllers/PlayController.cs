using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class PlayController(LyriousRepository repo) : Controller<Play>(repo);