using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Importing a SongbookPro backup into a band, as a page of its own: the checklist of what the
	/// import does, and the button that picks the backup and imports it (see <see cref="SettingsView.ImportSetting"/>).
	/// </summary>
	public sealed class ImportView : UserControl
	{
		/// <param name="bandName">The band imported into, under the title.</param>
		/// <param name="import">Picks the backup and imports it so; what happened (null: nothing picked).</param>
		public ImportView(string bandName, Func<Control, SongbookProImportOptions, Task<string?>> import)
		{
			var form = SettingsView.ImportSetting(import);
			form.MaxWidth = 640;
			form.HorizontalAlignment = HorizontalAlignment.Left;
			form.Margin = new Thickness(0, 0, 0, 16);
			Content = PageLayout.Page(
				L.T("Import SongbookPro backup"),
				new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				subtitle: bandName,
				closable: true,
				icon: AppIcons.Songbook);
		}
	}
}
