using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class SetlistItemController(LyriousRepository repo) : Controller<SetlistItem>(repo);