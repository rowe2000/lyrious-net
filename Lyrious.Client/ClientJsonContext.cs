using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyrious.Client;

internal sealed record LoginRequest(string Username, string Password);

internal sealed record LoginResponse(string? Token, MemberProfile? Member);

internal sealed record MemberProfile(System.Guid Id, string? UserName, string? Name, System.Guid? JoinedGroupId);

internal sealed record RegisterRequest(string UserName, string Password, string Name, string? Email, string? PhoneNumber);

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record ForgotPasswordRequest(string UserNameOrEmail);

internal sealed record ResetPasswordRequest(string UserNameOrEmail, string Code, string NewPassword);

internal sealed record EmailInvitationRequest(System.Guid GroupId, string Email);

/// <summary>
/// A group as the API sends it. Every field the API stores is included, because updates post the
/// whole record back and the API overwrites all fields.
/// </summary>
internal sealed record GroupDto(
    System.Guid Id,
    string Name,
    System.Guid? CurrentSetlistId,
    System.Guid? CurrentPlayId,
    System.Guid? CurrentSetlistItemId,
    System.DateTime CreatedAt,
    System.DateTime ChangedAt,
    bool Removed,
    long Checksum,
    // The member's own band (only them in it), made by the server.
    bool IsPrivate = false);

internal sealed record SetlistDto(System.Guid Id, string Name, System.Guid GroupId, System.DateTime CreatedAt, System.DateTime ChangedAt, bool Removed, long Checksum);

internal sealed record SetlistItemDto(System.Guid Id, int Position, System.Guid SongId, System.Guid SetlistId, bool Removed);

internal sealed record SongDto(System.Guid Id, string Name, string? Key, float Tempo, string? Beat, string? Lyrics, System.Guid SongbookId, bool Removed, string? MediaLink = null);

internal sealed record SongbookDto(System.Guid Id, string Name, System.Guid? GroupId, bool Removed);

/// <summary>A membership; the role (0 Member, 1 Conductor, 2 Admin) and status (2 Approved) are sent as numbers, as the API does.</summary>
/// <summary>A new song, with every field the API stores (it replaces all fields with what it receives).</summary>
internal sealed record NewSongDto(
    System.Guid Id, string Name, string Key, float Tempo, string Beat, int LengthMilliSeconds, string Lyrics,
    System.Guid SongbookId, System.DateTime CreatedAt, System.DateTime ChangedAt, bool Removed, string MediaLink = "");

/// <summary>A search result from LRCLIB (https://lrclib.net/docs).</summary>
internal sealed record LrclibTrack(long Id, string? TrackName, string? ArtistName, string? AlbumName, double? Duration, bool Instrumental, string? PlainLyrics);

/// <summary>A member's public name only; the API sends more, which is ignored.</summary>
internal sealed record MemberDto(System.Guid Id, string? Name, string? UserName, System.DateTime? LastLoginAt = null, System.Guid? JoinedGroupId = null);

/// <summary>A band's playlog: the songs played on one occasion (e.g. a gig or a rehearsal).</summary>
/// Every field the API stores is included, since new ones are posted whole.
internal sealed record PlaylogDto(System.Guid Id, System.Guid GroupId, System.DateTime CreatedAt, System.DateTime ChangedAt, bool Removed, long Checksum = 0);

/// <summary>A song played, in the key and tempo it was played in, and who conducted it.</summary>
internal sealed record PlayDto(
    System.Guid Id, System.Guid PlaylogId, System.Guid SongId, System.Guid ConductorId,
    string? KeyString, float Tempo, int DurationMilliSeconds, System.DateTime CreatedAt, System.DateTime ChangedAt, bool Removed, long Checksum = 0);

internal sealed record MembershipDto(System.Guid Id, int RoleEnum, int MembershipStatusEnum, System.Guid GroupId, System.Guid MemberId, bool Removed);

/// <summary>
/// Source-generated JSON, so serialization keeps working when a Release build trims unused code
/// (reflection-based JSON is disabled in trimmed WebAssembly and mobile apps).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(ForgotPasswordRequest))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(EmailInvitationRequest))]
[JsonSerializable(typeof(GroupDto))]
[JsonSerializable(typeof(GroupDto[]))]
[JsonSerializable(typeof(SetlistDto))]
[JsonSerializable(typeof(SetlistDto[]))]
[JsonSerializable(typeof(SetlistItemDto))]
[JsonSerializable(typeof(SetlistItemDto[]))]
[JsonSerializable(typeof(SongDto))]
[JsonSerializable(typeof(SongDto[]))]
[JsonSerializable(typeof(SongbookDto))]
[JsonSerializable(typeof(SongbookDto[]))]
[JsonSerializable(typeof(MembershipDto))]
[JsonSerializable(typeof(MembershipDto[]))]
[JsonSerializable(typeof(MemberDto))]
[JsonSerializable(typeof(NewSongDto))]
[JsonSerializable(typeof(System.Guid))]
[JsonSerializable(typeof(System.Guid?))]
[JsonSerializable(typeof(System.Guid[]))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(MemberDto[]))]
[JsonSerializable(typeof(NewSongDto[]))]
[JsonSerializable(typeof(LrclibTrack[]))]
[JsonSerializable(typeof(PlaylogDto))]
[JsonSerializable(typeof(PlayDto))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
