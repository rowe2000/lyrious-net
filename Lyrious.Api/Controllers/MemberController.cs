using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class MemberController(LyriousRepository repo) : Controller<Member>(repo);