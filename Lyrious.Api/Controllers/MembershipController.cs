using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class MembershipController(LyriousRepository repo) : Controller<Membership>(repo);