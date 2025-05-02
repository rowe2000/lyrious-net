using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class GroupController(LyriousRepository repo) : Controller<Group>(repo);