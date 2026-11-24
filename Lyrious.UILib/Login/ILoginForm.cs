using System;
using Avalonia.Controls;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// A platform's own login form over the login page's fields (the web app's: real web fields, so
	/// the browser's password manager offers to save the login and to fill it in, which it can't for
	/// fields the app draws itself). The username, password and Log in shown are then the platform's;
	/// the page's own stay underneath as their frames.
	/// </summary>
	public interface ILoginForm
	{
		/// <summary>
		/// Lays the platform's fields over these while <paramref name="page"/> is shown, kept in place as
		/// the page moves or resizes.
		/// </summary>
		/// <param name="get">The username and password as the page has them (e.g. filled in by the app), shown in the fields.</param>
		/// <param name="set">Called with the username and password as they're typed (or filled in by the browser).</param>
		/// <param name="submit">Called when Log in is pressed (or Enter in the fields).</param>
		void Attach(Control page, TextBox userName, TextBox password, Button logIn, Func<(string UserName, string Password)> get, Action<string, string> set, Action submit);
	}
}
