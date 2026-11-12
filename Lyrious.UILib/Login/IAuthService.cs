using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// What the login page needs from the outside world: checking credentials. Each host supplies an
	/// implementation (e.g. one that calls the Lyrious API) through <see cref="App.AuthService"/>.
	/// </summary>
	public interface IAuthService
	{
		/// <summary>
		/// Checks the credentials. Returns a failed result for wrong credentials; throws when the
		/// server can't be reached.
		/// </summary>
		Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);
	}

	/// <summary>The signed-in member, as far as the UI needs to know.</summary>
	public sealed record AuthenticatedUser(string UserName, string DisplayName);

	public sealed record LoginResult(AuthenticatedUser? User, string? Error)
	{
		public bool Succeeded => User != null;

		public static LoginResult Success(AuthenticatedUser user) => new(user, null);

		public static LoginResult Failure(string error) => new(null, error);
	}

	/// <summary>Used until a host provides a real service, so the page says why login can't work.</summary>
	public sealed class UnconfiguredAuthService : IAuthService
	{
		public Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(LoginResult.Failure("This app isn't connected to a Lyrious server yet."));
		}
	}
}
