using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

/// <summary>A member's own song settings (transpose and lyrics); each member sees only theirs.</summary>
public class SongSettingController(LyriousRepository repo) : Controller<SongSetting>(repo);
