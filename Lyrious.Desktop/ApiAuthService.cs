using System;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.CoreLib;
using Lyrious.CoreLib.ApiModels;
using Lyrious.UILib.Login;

namespace Lyrious.Desktop;

/// <summary>Logs in against the Lyrious API (POST /api/auth/login) using CoreLib's ApiClient.</summary>
internal sealed class ApiAuthService(string apiBaseUrl) : IAuthService, IDisposable
{
    private readonly ApiClient apiClient = new() { BaseUrl = apiBaseUrl };

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        // Throws when the server can't be reached; the login page reports that separately.
        var member = await apiClient.LoginAsync(new LoginModel { Username = userName, Password = password });
        return member == null
            ? LoginResult.Failure("Wrong username or password.")
            : LoginResult.Success(new AuthenticatedUser(member.UserName ?? userName, string.IsNullOrWhiteSpace(member.Name) ? userName : member.Name));
    }

    public void Dispose() => apiClient.Dispose();
}
