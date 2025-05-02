using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class SetlistController(LyriousRepository repo) : Controller<Setlist>(repo);