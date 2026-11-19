using System.Collections.Generic;
using System.Globalization;

namespace Lyrious.UILib.Localization
{
	/// <summary>
	/// The app's texts in the chosen language (see <see cref="Language"/>). A text is looked up by its
	/// English wording, so the code reads as before: <c>L.T("Settings")</c>, or with values filled in,
	/// <c>L.T("Delete '{0}'?", name)</c>. A text that has no translation shows in English.
	/// </summary>
	public static class L
	{
		// The current language's texts by their English wording; empty for English.
		private static IReadOnlyDictionary<string, string> texts = new Dictionary<string, string>();

		/// <summary>Uses these texts from now on (see <see cref="Language.Apply"/>).</summary>
		internal static void Use(IReadOnlyDictionary<string, string> translations) => texts = translations;

		/// <summary>The text in the current language.</summary>
		public static string T(string english) => texts.TryGetValue(english, out var text) ? text : english;

		/// <summary>The text in the current language, with the values put in its {0}, {1}, …</summary>
		public static string T(string english, params object?[] values)
			=> string.Format(CultureInfo.CurrentCulture, T(english), values);

		/// <summary>
		/// "1 song" or "3 songs" in the current language: <paramref name="one"/> when the count is 1,
		/// else <paramref name="many"/>, with the count in their {0} (and further values in {1}, …).
		/// </summary>
		public static string Plural(long count, string one, string many, params object?[] values)
		{
			var all = new object?[values.Length + 1];
			all[0] = count;
			values.CopyTo(all, 1);
			return string.Format(CultureInfo.CurrentCulture, T(count == 1 ? one : many), all);
		}
	}
}
