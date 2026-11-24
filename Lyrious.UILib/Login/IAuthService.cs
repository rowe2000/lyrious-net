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

		/// <summary>Emails a code to the address someone wants to register with (see <see cref="Registration.Code"/>).</summary>
		Task<AuthResult> SendRegistrationCodeAsync(string email, string? language = null, CancellationToken cancellationToken = default);

		/// <summary>Texts a code to the phone number someone wants to register with instead of an email address.</summary>
		Task<AuthResult> SendRegistrationSmsCodeAsync(string phoneNumber, string? language = null, CancellationToken cancellationToken = default);

		/// <summary>Creates a member, with the code emailed to their address. Doesn't log in; call <see cref="LoginAsync"/> afterwards.</summary>
		Task<AuthResult> RegisterAsync(Registration registration, CancellationToken cancellationToken = default);

		/// <summary>Sends a code needed to change the password: emailed to the logged-in member's address, or texted to their verified phone number.</summary>
		Task<AuthResult> SendPasswordChangeCodeAsync(bool bySms = false, CancellationToken cancellationToken = default);

		/// <summary>Texts a code to the phone number the logged-in member wants as theirs.</summary>
		Task<AuthResult> SendPhoneChangeCodeAsync(string phoneNumber, CancellationToken cancellationToken = default);

		/// <summary>Sets the logged-in member's phone number, with the code texted to it (verified then); no number removes theirs.</summary>
		Task<AuthResult> ChangePhoneAsync(string? phoneNumber, string? code, CancellationToken cancellationToken = default);

		/// <summary>Changes the logged-in member's password, with the code emailed to them.</summary>
		Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword, string code, CancellationToken cancellationToken = default);

		/// <summary>Emails a code to the address the logged-in member wants instead of theirs.</summary>
		Task<AuthResult> SendEmailChangeCodeAsync(string newEmail, CancellationToken cancellationToken = default);

		/// <summary>Changes the logged-in member's email address, with the code emailed to the new one.</summary>
		Task<AuthResult> ChangeEmailAsync(string newEmail, string code, CancellationToken cancellationToken = default);

		/// <summary>The logged-in member's email address; null if they have none (or it isn't known, e.g. offline).</summary>
		string? Email { get; }

		/// <summary>The logged-in member's verified phone number (+46701740610); null if they have none (or it isn't known).</summary>
		string? PhoneNumber { get; }

		/// <summary>
		/// Whether the logged-in member is logged in elevated, as a site manager: an admin of every band
		/// (but others' private ones). Only after <see cref="ElevateAsync"/>; a normal login is a member's.
		/// </summary>
		bool IsSiteManager { get; }

		/// <summary>Whether the logged-in member is listed as a site manager on the server, so may log in elevated.</summary>
		bool CanElevate { get; }

		/// <summary>Logs in elevated, as a site manager, with the member's password again (only for those listed). Needs the server.</summary>
		Task<AuthResult> ElevateAsync(string password, CancellationToken cancellationToken = default);

		/// <summary>Raised when <see cref="IsSiteManager"/> changes, on any thread.</summary>
		event System.Action? SiteManagerChanged;

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

		/// <summary>The address of the server (host) the app talks to, e.g. https://www.lyrious.se.</summary>
		string ServerAddress { get; }

		/// <summary>Checks an address for a server: whether it's an address at all, and whether a Lyrious server answers there.</summary>
		Task<ServerCheck> CheckServerAsync(string address, CancellationToken cancellationToken = default);

		/// <summary>
		/// Switches to the server at the (checked) address, answering or not; the member is logged
		/// out, since their login belongs to the old server.
		/// </summary>
		void UseServer(string address);
	}

	/// <summary>The result of checking a server address.</summary>
	/// <param name="Address">The address tidied up (e.g. "http://" added); null if it isn't an address at all.</param>
	/// <param name="Answers">Whether a Lyrious server answered there.</param>
	/// <param name="Message">Why it can't be used, or why it's doubtful; null when all is well.</param>
	public sealed record ServerCheck(string? Address, bool Answers, string? Message);

	/// <summary>The signed-in member, as far as the UI needs to know.</summary>
	public sealed record AuthenticatedUser(string UserName, string DisplayName);

	/// <param name="Code">The code emailed to <paramref name="Email"/> (see <see cref="IAuthService.SendRegistrationCodeAsync"/>).</param>
	/// <param name="Language">The new member's language ("sv" or "en"): kept as their language setting, for the app and the emails and texts sent to them.</param>
	/// <param name="PhoneNumber">The phone number registered with instead of an email address (then <paramref name="Email"/> is null, and the code was texted).</param>
	public sealed record Registration(string UserName, string Password, string DisplayName, string? Email, string? Code = null, string? Language = null, string? PhoneNumber = null);

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
		private static string NotConnected => Localization.L.T("This app isn't connected to a Lyrious server yet.");

		public string ServerAddress => "";

		public Task<ServerCheck> CheckServerAsync(string address, CancellationToken cancellationToken = default)
			=> Task.FromResult(new ServerCheck(null, false, NotConnected));

		public void UseServer(string address)
		{
		}

		public Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
			=> Task.FromResult(LoginResult.Failure(NotConnected));

		public Task<AuthResult> SendRegistrationCodeAsync(string email, string? language = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> SendRegistrationSmsCodeAsync(string phoneNumber, string? language = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> RegisterAsync(Registration registration, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> SendPasswordChangeCodeAsync(bool bySms = false, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> SendPhoneChangeCodeAsync(string phoneNumber, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ChangePhoneAsync(string? phoneNumber, string? code, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword, string code, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> SendEmailChangeCodeAsync(string newEmail, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ChangeEmailAsync(string newEmail, string code, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public string? Email => null;

		public string? PhoneNumber => null;

		public bool IsSiteManager => false;

		public bool CanElevate => false;

		public Task<AuthResult> ElevateAsync(string password, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public event System.Action? SiteManagerChanged { add { } remove { } }

		public Task<AuthResult> RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public Task<AuthResult> ResetPasswordAsync(string userNameOrEmail, string code, string newPassword, CancellationToken cancellationToken = default)
			=> Task.FromResult(AuthResult.Failure(NotConnected));

		public void LogOut()
		{
		}
	}
}
