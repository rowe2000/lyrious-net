using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyrious.Client;

internal sealed record LoginRequest(string Username, string Password);

internal sealed record LoginResponse(string? Token, MemberProfile? Member);

internal sealed record MemberProfile(string? UserName, string? Name, System.Guid? JoinedGroupId);

internal sealed record RegisterRequest(string UserName, string Password, string Name, string? Email, string? PhoneNumber);

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record ForgotPasswordRequest(string UserNameOrEmail);

internal sealed record ResetPasswordRequest(string UserNameOrEmail, string Code, string NewPassword);

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
    long Checksum);

internal sealed record SetlistDto(System.Guid Id, string Name, System.Guid GroupId, System.DateTime CreatedAt, System.DateTime ChangedAt, bool Removed, long Checksum);

internal sealed record SetlistItemDto(System.Guid Id, int Position, System.Guid SongId, System.Guid SetlistId, bool Removed);

internal sealed record SongDto(System.Guid Id, string Name, string? Key, float Tempo, string? Beat, string? Lyrics, bool Removed);

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
[JsonSerializable(typeof(GroupDto))]
[JsonSerializable(typeof(GroupDto[]))]
[JsonSerializable(typeof(SetlistDto))]
[JsonSerializable(typeof(SetlistDto[]))]
[JsonSerializable(typeof(SetlistItemDto))]
[JsonSerializable(typeof(SetlistItemDto[]))]
[JsonSerializable(typeof(SongDto))]
[JsonSerializable(typeof(SongDto[]))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
