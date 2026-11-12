using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// What the account pages need from the outside world. Each host supplies an implementation
	/// (e.g. one that calls the Lyrious API) through <see cref="App.AuthService"/>.
	/// Methods return failed results for problems the user can fix (wrong password, weak password,
	/// taken username) and throw when the server can't be reached.
	/// </summary>
	public interface IAuthService
	{
		Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);

		/// <summary>Creates a member. Doesn't log in; call <see cref="LoginAsync"/> afterwards.</summary>
		Task<AuthResult> RegisterAsync(Registration registration, CancellationToken cancellationToken = default);

		/// <summary>Changes the logged-in member's password.</summary>
		Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asks the server to email a password reset code. Succeeds whether or not the account exists
		/// (the server doesn't say), so the page only says a code was sent if the account has an email.
		/// </summary>
		Task<AuthResult> RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default);

		/// <summary>Sets a new password with the emailed code.</summary>
		Task<AuthResult> ResetPasswordAsync(string userNameOrEmail, string code, string newPassword, CancellationToken cancellationToken = default);

		/// <summary>
		/// Forgets the logged-in member on this device (the access token), so later API calls are no
		/// longer made as them.
		/// </summary>
		void LogOut();
	}

	/// <summary>The signed-in member, as far as the UI needs to know.</summary>
	public sealed record AuthenticatedUser(string UserName, string DisplayName);

	public sealed record Registration(string UserName, string Password, string DisplayName, string? Email);

	public sealed record LoginResult(AuthenticatedUser? User, string? Error)
	{
		public bool Succeeded => User != null;

		public static LoginResult Success(AuthenticatedUser user) => new(user, null);

		public static LoginResult Failure(string error) => new(null, error);
	}

	public sealed record AuthResult(bool Succeeded, string? Error)
	{
		public static AuthResult Success() => new(true, null);

		public static AuthResult Failure(string error) => new(false, error);
	}

	/// <summary>Used until a host provides a real service, so the pages say why nothing works.</summary>
	public sealed class UnconfiguredAuthService : IAuthService
	{
		private const string NotConnected = "This app isn't connected to a Lyrious server yet.";

		public Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
			=> Task.FromResult(LoginResult.Failure(NotConnected));

		public Task<AuthResult> RegisterAsync(Registration registration, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ResetPasswordAsync(string userNameOrEmail, string code, string newPassword, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public void LogOut()
		{
		}
	}
}
