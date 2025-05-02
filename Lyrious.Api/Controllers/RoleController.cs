using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class RoleController(LyriousRepository repo) : Controller<Role>(repo);