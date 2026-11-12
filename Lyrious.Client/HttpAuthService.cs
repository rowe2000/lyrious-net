using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.UILib.Login;

namespace Lyrious.Client;

/// <summary>
/// The account calls of the Lyrious API (api/auth/...) over plain HttpClient, so it works in every
/// host, including WebAssembly (CoreLib's ApiClient drags in EF Core/SQLite and doesn't).
/// </summary>
public sealed class HttpAuthService(HttpClient http) : IAuthService
{
    /// <summary>Creates a service for the API at <paramref name="apiBaseUrl"/>, e.g. http://localhost:7000.</summary>
    public HttpAuthService(string apiBaseUrl)
        : this(new HttpClient { BaseAddress = new System.Uri(apiBaseUrl.TrimEnd('/') + "/") })
    {
    }

    /// <summary>The bearer token from the last successful login, for authenticated API calls.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>The group the member last joined, from the last successful login.</summary>
    public System.Guid? JoinedGroupId { get; private set; }

    /// <summary>The HttpClient (with the API base address), shared with the other API services.</summary>
    internal HttpClient Http => http;

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        AccessToken = null;
        JoinedGroupId = null;

        // Throws when the server can't be reached (or CORS blocks the call); the pages report that.
        using var response = await http.PostAsJsonAsync(
            "api/auth/login", new LoginRequest(userName, password), ClientJsonContext.Default.LoginRequest, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return LoginResult.Failure("Wrong username or password.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return LoginResult.Failure(await ReadErrorAsync(response, cancellationToken));
        }

        var body = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.LoginResponse, cancellationToken);
        if (body is not { Token.Length: > 0, Member: { } member })
        {
            return LoginResult.Failure("The server sent an unexpected response.");
        }

        AccessToken = body.Token;
        JoinedGroupId = member.JoinedGroupId;
        var name = string.IsNullOrWhiteSpace(member.Name) ? userName : member.Name;
        return LoginResult.Success(new AuthenticatedUser(member.UserName ?? userName, name));
    }

    public async Task<AuthResult> RegisterAsync(Registration registration, CancellationToken cancellationToken = default)
    {
        // The API's model requires every property, so Email and PhoneNumber are sent even when null.
        var request = new RegisterRequest(registration.UserName, registration.Password, registration.DisplayName, registration.Email, PhoneNumber: null);
        using var response = await http.PostAsJsonAsync("api/auth/register", request, ClientJsonContext.Default.RegisterRequest, cancellationToken);

        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    public async Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AccessToken))
        {
            return AuthResult.Failure("Log in first.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(currentPassword, newPassword), ClientJsonContext.Default.ChangePasswordRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return AuthResult.Failure("Your session has expired. Log in again.");
        }

        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    /// <summary>
    /// Forgets the token and member. Tokens are stateless, so there is nothing to cancel on the
    /// server; the token simply isn't sent any more.
    /// </summary>
    public void LogOut()
    {
        AccessToken = null;
        JoinedGroupId = null;
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            "api/auth/forgot-password", new ForgotPasswordRequest(userNameOrEmail), ClientJsonContext.Default.ForgotPasswordRequest, cancellationToken);

        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    public async Task<AuthResult> ResetPasswordAsync(string userNameOrEmail, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            "api/auth/reset-password", new ResetPasswordRequest(userNameOrEmail, code, newPassword), ClientJsonContext.Default.ResetPasswordRequest, cancellationToken);

        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    /// <summary>
    /// Turns an error response into one readable message. The API answers with either a list of
    /// Identity errors ([{ "code", "description" }]), an ASP.NET validation problem
    /// ({ "errors": { field: [messages] } }) or plain text.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var messages = new List<string>();
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(root.EnumerateArray()
                    .Select(error => error.TryGetProperty("description", out var description) ? description.GetString() : null)
                    .OfType<string>());
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                messages.AddRange(errors.EnumerateObject()
                    .SelectMany(field => field.Value.ValueKind == JsonValueKind.Array ? field.Value.EnumerateArray() : Enumerable.Empty<JsonElement>())
                    .Select(message => message.GetString())
                    .OfType<string>());
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                messages.Add(root.GetString() ?? "");
            }
        }
        catch (JsonException)
        {
            messages.Add(text);
        }

        var message = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
        return message.Length > 0 ? message : $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.";
    }
}
