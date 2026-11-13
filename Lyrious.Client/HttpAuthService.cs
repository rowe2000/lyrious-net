using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.Client.Offline;
using Lyrious.UILib;
using Lyrious.UILib.Login;

namespace Lyrious.Client;

/// <summary>
/// The account calls of the Lyrious API (api/auth/...) over plain HttpClient, so it works in every
/// host, including WebAssembly (CoreLib's ApiClient drags in EF Core/SQLite and doesn't).
/// <para>
/// Login also works without the server for members who have logged in online on this device before
/// (see <see cref="OfflineLogins"/>): the session is then offline (no token) until
/// <see cref="ReauthenticateAsync"/> logs in again when the server is back. Registering, changing
/// and resetting passwords need the server.
/// </para>
/// </summary>
public sealed class HttpAuthService : IAuthService
{
    /// <summary>How long to wait for the server before treating it as unreachable (offline).</summary>
    private static readonly System.TimeSpan RequestTimeout = System.TimeSpan.FromSeconds(15);

    private readonly OfflineLogins offlineLogins;
    private HttpClient http;

    // The current session's login, kept in memory only, to log in again when the server comes back.
    private string? sessionUserName;
    private string? sessionPassword;

    // For an offline session: the server its offline login came from (its joined group is kept there).
    private string? offlineServer;

    /// <param name="store">Where offline logins are kept on this device; without one, offline login isn't possible.</param>
    /// <param name="hasher">Computes the offline login's password hash; .NET's own by default (the browser supplies its Web Crypto).</param>
    public HttpAuthService(HttpClient http, ISettingsStore? store = null, IPasswordHasher? hasher = null)
    {
        this.http = http;
        offlineLogins = new OfflineLogins(store ?? new MemorySettingsStore(), hasher ?? new DotNetPasswordHasher());
        try
        {
            http.Timeout = RequestTimeout;
        }
        catch (System.InvalidOperationException)
        {
            // The client has been used already; keep its timeout.
        }
    }

    /// <summary>Creates a service for the API at <paramref name="apiBaseUrl"/>, e.g. http://localhost:5180.</summary>
    public HttpAuthService(string apiBaseUrl, ISettingsStore? store = null)
        : this(CreateClient(new System.Uri(apiBaseUrl.TrimEnd('/') + "/")), store)
    {
    }

    /// <summary>
    /// An HttpClient for the API. Outside the browser, idle connections are closed after a few
    /// seconds: while the server is away something else may answer on its port (macOS's AirPlay
    /// Receiver on 5000 and 7000), and a kept-alive connection to that would hide the server's return for a
    /// minute. The browser manages its own connections.
    /// </summary>
    private static HttpClient CreateClient(System.Uri baseAddress)
    {
        var client = OperatingSystem.IsBrowser()
            ? new HttpClient()
            : new HttpClient(new SocketsHttpHandler { PooledConnectionIdleTimeout = System.TimeSpan.FromSeconds(5) });
        client.BaseAddress = baseAddress;
        client.Timeout = RequestTimeout;
        return client;
    }

    /// <summary>Whether the member logged in without the server; API calls need <see cref="ReauthenticateAsync"/> first.</summary>
    public bool IsOfflineSession { get; private set; }

    /// <summary>Whether a member is logged in (online or offline).</summary>
    public bool IsLoggedIn => MemberId != null;

    /// <summary>The bearer token from the last successful login, for authenticated API calls.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>The logged-in member's id, from the last successful login.</summary>
    public System.Guid? MemberId { get; private set; }

    /// <summary>The group the member last joined: from the last successful login, or chosen since.</summary>
    public System.Guid? JoinedGroupId { get; internal set; }

    /// <summary>The HttpClient (with the API base address), shared with the other API services.</summary>
    internal HttpClient Http => http;

    /// <summary>
    /// Whether the Lyrious server is there: its api/ping answers "Pong". Something else answering on
    /// the address (a proxy's error page, macOS's AirPlay Receiver on ports 5000 and 7000) doesn't count.
    /// </summary>
    internal async Task<bool> IsServerUpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsync("api/ping", content: null, cancellationToken);
            return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(cancellationToken)).Contains("Pong");
        }
        catch (System.Exception ex) when (IsNetworkFailure(ex, cancellationToken))
        {
            return false;
        }
    }

    public string ServerAddress => http.BaseAddress?.ToString().TrimEnd('/') ?? "";

    /// <summary>
    /// Checks an address for a server: whether it's a web address at all ("http://" is assumed when
    /// no scheme is given), and whether a Lyrious API answers there (POST api/ping says "Pong").
    /// </summary>
    public async Task<ServerCheck> CheckServerAsync(string address, CancellationToken cancellationToken = default)
    {
        var text = address.Trim();
        if (!text.Contains("://"))
        {
            text = "http://" + text;
        }

        if (!System.Uri.TryCreate(text.TrimEnd('/') + "/", System.UriKind.Absolute, out var uri)
            || (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps))
        {
            return new ServerCheck(null, false, "That isn't a web address. Use for example http://localhost:5180.");
        }

        var normalized = uri.ToString().TrimEnd('/');
        using var candidate = CreateClient(uri);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(System.TimeSpan.FromSeconds(10));
            using var response = await candidate.PostAsync("api/ping", content: null, timeout.Token);
            if (!response.IsSuccessStatusCode || !(await response.Content.ReadAsStringAsync(timeout.Token)).Contains("Pong"))
            {
                return new ServerCheck(normalized, false, $"{normalized} answered {(int)response.StatusCode} {response.ReasonPhrase}; it doesn't look like a Lyrious server.");
            }
        }
        catch (System.Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ServerCheck(normalized, false, $"{normalized} doesn't answer right now.");
        }

        return new ServerCheck(normalized, true, null);
    }

    /// <summary>
    /// Uses the server at <paramref name="address"/> (as returned by <see cref="CheckServerAsync"/>)
    /// for all calls from now on, and logs out: a login belongs to the server that gave it.
    /// </summary>
    public void UseServer(string address)
    {
        LogOut();
        http = CreateClient(new System.Uri(address.TrimEnd('/') + "/"));
    }

    /// <summary>
    /// Logs in with the server; if it can't be reached, against the member's last online login on
    /// this device (an offline session). Throws when neither is possible.
    /// </summary>
    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var result = await LoginOnlineOrOfflineAsync(userName, password, cancellationToken);
        if (result.User != null)
        {
            Session++;
            LoggedIn?.Invoke();
        }

        return result;
    }

    /// <summary>Counts successful logins (online or offline), so each new session can be told apart from the last.</summary>
    public int Session { get; private set; }

    /// <summary>Raised after every successful login, online or offline (not when logging in again in the background).</summary>
    public event System.Action? LoggedIn;

    private async Task<LoginResult> LoginOnlineOrOfflineAsync(string userName, string password, CancellationToken cancellationToken)
    {
        LogOut();
        try
        {
            return await LoginOnlineAsync(userName, password, cancellationToken);
        }
        catch (System.Exception ex) when (IsNetworkFailure(ex, cancellationToken))
        {
            // Any server's offline login will do: there may be no server reachable at all, or the
            // host may have been changed to one this device hasn't logged in to yet.
            if (await offlineLogins.VerifyAsync(ServerAddress, userName, password) is { } login)
            {
                MemberId = login.MemberId;
                JoinedGroupId = login.JoinedGroupId;
                IsOfflineSession = true;
                offlineServer = login.Server;
                sessionUserName = userName;
                sessionPassword = password;
                return LoginResult.Success(new AuthenticatedUser(login.UserName, login.DisplayName));
            }

            if (offlineLogins.Has(ServerAddress, userName))
            {
                return LoginResult.Failure("Wrong username or password.");
            }

            throw new HttpRequestException(
                $"The server at {ServerAddress} can't be reached, and you haven't logged in on this device before, so you can't log in offline yet. Check the host (below the form) and that the server is running.", ex);
        }
    }

    /// <summary>Why <see cref="ReauthenticateAsync"/> last failed, for the member; null if it didn't.</summary>
    public string? ReauthenticationProblem { get; private set; }

    /// <summary>
    /// Logs in with the server again with this session's login, e.g. when it's back after an offline
    /// login, or when the token has expired. True when it worked; false when the server refused the
    /// login (the password was changed elsewhere) or knows the username as another account (another
    /// server than the offline login's): the session stays offline then, so changes made offline
    /// can't end up in someone else's account. Throws when the server can't be reached.
    /// </summary>
    public async Task<bool> ReauthenticateAsync(CancellationToken cancellationToken = default)
    {
        ReauthenticationProblem = null;
        if (sessionUserName == null || sessionPassword == null)
        {
            return false;
        }

        // The member's joined group may have been changed offline and not synced yet: keep it.
        var joined = JoinedGroupId;
        var result = await LoginOnlineAsync(sessionUserName, sessionPassword, cancellationToken, expectedMemberId: MemberId);
        if (result.User == null)
        {
            ReauthenticationProblem = result.Error;
            return false;
        }

        JoinedGroupId = joined ?? JoinedGroupId;
        return true;
    }

    /// <param name="expectedMemberId">When logging in again for a session: the server must know the username as this same account.</param>
    private async Task<LoginResult> LoginOnlineAsync(string userName, string password, CancellationToken cancellationToken, System.Guid? expectedMemberId = null)
    {
        // Throws when the server can't be reached (or CORS blocks the call).
        using var response = await http.PostAsJsonAsync(
            "api/auth/login", new LoginRequest(userName, password), ClientJsonContext.Default.LoginRequest, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The password may have been changed elsewhere: it mustn't keep working offline.
            offlineLogins.Remove(ServerAddress, userName);
            return LoginResult.Failure("Wrong username or password.");
        }

        // The Lyrious API answers a login with 200, 400 or 401. Anything else means it isn't there: a
        // proxy saying it's down (502-504), or something else on the port (e.g. macOS's AirPlay
        // Receiver on 5000 and 7000 answers 403). Treat that as unreachable, not as a refusal.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound || (int)response.StatusCode >= 500)
        {
            throw new HttpRequestException($"The Lyrious server isn't answering ({(int)response.StatusCode} {response.ReasonPhrase}).");
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

        if (expectedMemberId is { } expected && member.Id != expected)
        {
            return LoginResult.Failure(
                $"{ServerAddress} has another account called {userName} than the one you logged in with offline, so this session stays offline. Log out and in again to use {ServerAddress}; changes made offline stay on this device.");
        }

        AccessToken = body.Token;
        offlineServer = null;
        MemberId = member.Id;
        JoinedGroupId = member.JoinedGroupId;
        IsOfflineSession = false;
        sessionUserName = userName;
        sessionPassword = password;

        var name = string.IsNullOrWhiteSpace(member.Name) ? userName : member.Name;
        var user = new AuthenticatedUser(member.UserName ?? userName, name);
        await offlineLogins.SaveAsync(ServerAddress, userName, password, new OfflineLogin(member.Id, user.UserName, user.DisplayName, member.JoinedGroupId));
        return LoginResult.Success(user);
    }

    /// <summary>The member joined another group: also remembered for their next offline login.</summary>
    internal void SetJoinedGroup(System.Guid groupId)
    {
        JoinedGroupId = groupId;
        if (sessionUserName != null)
        {
            offlineLogins.SetJoinedGroup(offlineServer ?? ServerAddress, sessionUserName, groupId);
        }
    }

    /// <summary>
    /// Whether the exception means the server couldn't be reached (no answer, timeout, blocked), as
    /// opposed to an answer with an error. A cancellation the caller asked for isn't one.
    /// </summary>
    internal static bool IsNetworkFailure(System.Exception ex, CancellationToken cancellationToken = default) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

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
            return AuthResult.Failure(IsOfflineSession ? "Changing your password needs a connection to the server." : "Log in first.");
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

        if (!response.IsSuccessStatusCode)
        {
            return AuthResult.Failure(await ReadErrorAsync(response, cancellationToken));
        }

        // The offline login follows the new password.
        if (sessionUserName != null && MemberId is { } memberId
            && await offlineLogins.VerifyAsync(ServerAddress, sessionUserName, currentPassword) is { } login)
        {
            await offlineLogins.SaveAsync(ServerAddress, sessionUserName, newPassword, login with { MemberId = memberId, JoinedGroupId = JoinedGroupId });
        }

        sessionPassword = newPassword;
        return AuthResult.Success();
    }

    /// <summary>
    /// Forgets the token and member. Tokens are stateless, so there is nothing to cancel on the
    /// server; the token simply isn't sent any more.
    /// </summary>
    public void LogOut()
    {
        AccessToken = null;
        MemberId = null;
        JoinedGroupId = null;
        IsOfflineSession = false;
        offlineServer = null;
        sessionUserName = null;
        sessionPassword = null;
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
