using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// A platform's password store, used to offer saving the password after login and to fill in the
	/// login form next time. Each host supplies one through <see cref="App.CredentialStore"/>.
	/// </summary>
	public interface ICredentialStore
	{
		/// <summary>Whether saving passwords is possible here at all; if not, nothing is offered.</summary>
		bool IsSupported { get; }

		/// <summary>
		/// True when the platform asks the user itself before saving (e.g. the browser's password
		/// manager), so the app doesn't ask first.
		/// </summary>
		bool AsksUserItself { get; }

		/// <summary>Whether the app can remove the saved password (browsers only allow that in their settings).</summary>
		bool CanForget { get; }

		/// <summary>The saved username and password, or null.</summary>
		Task<SavedCredential?> LoadAsync();

		Task SaveAsync(SavedCredential credential);

		Task ForgetAsync();
	}

	public sealed record SavedCredential(string UserName, string Password);

	/// <summary>Used where no password store is available: nothing is offered or filled in.</summary>
	public sealed class NoCredentialStore : ICredentialStore
	{
		public bool IsSupported => false;
		public bool AsksUserItself => false;
		public bool CanForget => false;
		public Task<SavedCredential?> LoadAsync() => Task.FromResult<SavedCredential?>(null);
		public Task SaveAsync(SavedCredential credential) => Task.CompletedTask;
		public Task ForgetAsync() => Task.CompletedTask;
	}
}
