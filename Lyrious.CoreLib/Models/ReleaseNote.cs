using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Lyrious.CoreLib.Models;

/// <summary>
/// The notes of a release of the app: what it brought (<see cref="Text"/>), for which
/// <see cref="Version"/> built from which <see cref="Branch"/>, and whether members see it yet
/// (<see cref="Published"/>). Not part of any band: every logged-in member reads the published
/// ones; only site managers see unpublished ones and add, change or remove them.
/// </summary>
public sealed class ReleaseNote : Entity
{
	/// <summary>The longest <see cref="Text"/>.</summary>
	public const int MaxTextLength = 8000;

	/// <summary>What the release brought, e.g. one change per line.</summary>
	[MaxLength(MaxTextLength)]
	[DataMember] public string Text { get; set; } = "";

	/// <summary>The branch the release was built from, e.g. main.</summary>
	[MaxLength(100)]
	[DataMember] public string Branch { get; set; } = "";

	/// <summary>The release's version, e.g. 0.1.2.7 (as the build takes it from git).</summary>
	[MaxLength(50)]
	[DataMember] public string Version { get; set; } = "";

	/// <summary>Whether members see it (else only site managers do, e.g. while it's written).</summary>
	[DataMember] public bool Published { get; set; }

	/// <summary>When the release note was made (the release's date).</summary>
	[DataMember] public DateTime Created { get; set; } = DateTime.Now;
}
