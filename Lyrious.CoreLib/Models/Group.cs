using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib.Models;

public sealed class Group : Entity, IName
{
	[MaxLength(100)]
	[DataMember]
	public string Name { get; set; } = "";
	//[Member]
	//public Guid? GroupStateId { get; set; }

	//[JsonIgnore]
	//public GroupState? GroupState { get; set; }

	/// <summary>
	/// The member's own band, with only them in it (made by the server for every member, named
	/// <see cref="PrivateName"/>): it can't be renamed, deleted, left or shared.
	/// </summary>
	[DataMember] public bool IsPrivate { get; set; }

	/// <summary>The name of every member's private band.</summary>
	public const string PrivateName = "Private";

	/// <summary>
	/// The band's MIDI expressions: shortcuts for MIDI commands used in lyrics as "$NAME.PARAM1.PARAM2"
	/// (see Lyrious.UILib's Midi), as a JSON array of { name, description, parameters: [{ name,
	/// default }], output }; null if none. Every member gets them; only admins change them.
	/// </summary>
	[MaxLength(MaxMidiExpressionsLength)]
	[DataMember] public string? MidiExpressions { get; set; }

	/// <summary>The longest <see cref="MidiExpressions"/> stored.</summary>
	public const int MaxMidiExpressionsLength = 8000;

	/// <summary>Whether <paramref name="json"/> can be stored as <see cref="MidiExpressions"/>: none, or a JSON array of objects that fits.</summary>
	public static bool IsValidMidiExpressions(string? json)
	{
		if (json == null)
		{
			return true;
		}

		if (json.Length > MaxMidiExpressionsLength)
		{
			return false;
		}

		try
		{
			return System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonArray array
			       && array.All(item => item is System.Text.Json.Nodes.JsonObject);
		}
		catch (System.Text.Json.JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// The band's MIDI commands for every song: a JSON object of "select", "start" and "stop", each
	/// the commands of a MIDI tag (see Lyrious.UILib's Midi), sent when a song is selected, starts
	/// playing and stops; null if none. Every member gets them; only admins change them.
	/// </summary>
	[MaxLength(MaxMidiCommandsLength)]
	[DataMember] public string? MidiCommands { get; set; }

	/// <summary>The longest <see cref="MidiCommands"/> stored.</summary>
	public const int MaxMidiCommandsLength = 2000;

	/// <summary>Whether <paramref name="json"/> can be stored as <see cref="MidiCommands"/>: none, or a JSON object of texts that fits.</summary>
	public static bool IsValidMidiCommands(string? json)
	{
		if (json == null)
		{
			return true;
		}

		if (json.Length > MaxMidiCommandsLength)
		{
			return false;
		}

		try
		{
			return System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject item
			       && item.All(pair => pair.Value is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out _));
		}
		catch (System.Text.Json.JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// The header shown above every song's lyrics, in ChordPro, with the song's details for
	/// placeholders: $songname, $artist, $key, $tempo, $beat and $tags (see Lyrious.UILib's LyricsHeader).
	/// Null for the apps' default ("# [$key]$songname", then "#### $tags"); empty for no header. Every member gets it;
	/// only admins change it.
	/// </summary>
	[MaxLength(MaxLyricsHeaderLength)]
	[DataMember] public string? LyricsHeader { get; set; }

	/// <summary>The longest <see cref="LyricsHeader"/> stored.</summary>
	public const int MaxLyricsHeaderLength = 1000;

	[DataMember] public Guid? CurrentSetlistId { get; set; }
	[DataMember] public Guid? CurrentPlayId { get; set; }
	[DataMember] public Guid? CurrentSetlistItemId { get; set; }

	[JsonIgnore] public Setlist? CurrentSetlist { get; set; }
	[JsonIgnore] public SetlistItem? CurrentSetlistItem { get; set; }
	[JsonIgnore] public Play? CurrentPlay { get; set; }


	[JsonIgnore] public ObservableList<Member> JoinedMembers { get; set; } = [];
	[JsonIgnore] public ObservableList<Playlog> Playlogs { get; set; } = [];
	[JsonIgnore] public ObservableList<Setlist> Setlists { get; set; } = [];
	[JsonIgnore] public ObservableList<Songbook> Songbooks { get; set; } = [];
	[JsonIgnore] public ObservableList<Membership> Memberships { get; set; } = [];

	[NotMapped]
	[JsonIgnore] public ObservableList<Member> ConnectedMembers { get; set; } = [];

	//[NotMapped]
	//[JsonIgnore]
	//public GroupState? GroupState { get; set; }

	//public override int CalculateChecksum()
	//{
	//    return HashCode.Combine(base.CalculateChecksum(), CurrentPlay?.Id.GetHashCode() ?? 0, CurrentSetlist?.Id.GetHashCode() ?? 0) % int.MaxValue;
	//}

	public override string ToString()
	{
		return $"{Name}, {Memberships.Count} members, {JoinedMembers.Count} joined, {ConnectedMembers.Count} connected";
	}

	public bool IsJustMe => Memberships.Count == 1;

	public Membership CreateMembership(Member member, RoleEnum role = RoleEnum.Member)
	{
		var membership = Memberships.FirstOrDefault(o => member.UserName?.Equals(o.Member?.UserName) == true) 
		                 ?? Membership.Create(member, this, role);

		if (!member.Memberships.Contains(membership))
		{
			member.Memberships.Add(membership);
		}

		return membership;
	}

	public Setlist CreateSetlist(string setlistName)
	{
		var setlist = Setlist.Create(this, setlistName);
		if (Setlists.Count == 0)
		{
			CurrentSetlist = setlist;
			CurrentSetlistId = setlist.Id;
		}

		Setlists.Add(setlist);
		return setlist;
	}

	public Songbook CreateSongbook(string songbookName)
	{
		var songbook = Songbook.Create(this, songbookName);
		Songbooks.Add(songbook);
		return songbook;
	}

	public Playlog CreatePlaylog()
	{
		var playlog = Playlog.Create(this);
		Playlogs.Add(playlog);
		return playlog;
	}
	public static Group Create(string name)
	{
		return new Group { Name = name, };
	}

	public Membership? ApproveMembership(Member member)
	{
		var membership = Memberships.FirstOrDefault(o => o.MemberId == member.Id);
		if (membership == null)
		{
			return null;
		}

		membership.MembershipStatusEnum = MembershipStatusEnum.Approved;
		return membership;
	}

	public bool IsMember(Member member)
	{
		return Memberships.Any(o => o.MemberId == member.Id);
	}
}