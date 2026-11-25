using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's lyrics header, in the band settings on the Settings page (see <see cref="LyricsHeader"/>):
	/// ChordPro shown above every song's lyrics, with placeholders for the song's details. It's saved
	/// with the band, the same for all its members. An admin designs it, with a preview of how it looks
	/// for an example song; empty is no header. The others see it.
	/// </summary>
	public sealed class LyricsHeaderSection : UserControl
	{
		// The song the preview shows the header for.
		private static readonly SongDetails Example = new(Guid.Empty, "Blackbird", "G", 94, "3/4", "", Tags: "acoustic ballad", Artist: "The Beatles", OriginalKey: "G");

		/// <param name="stored">The band's header as stored: null for the default, empty for none.</param>
		/// <param name="canEdit">Whether the member is an admin of the band (only they change its header).</param>
		/// <param name="save">Saves the changed header with the band: null for the default, empty for none.</param>
		public LyricsHeaderSection(string? stored, bool canEdit, Func<string?, Task> save)
		{
			var preview = new ContentControl();
			var panel = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					// Its explanation as the heading's tooltip.
					SettingsView.SettingHeading(
						L.T("Lyrics header"),
						L.T("Shown above every song's lyrics, and scrolls with them. Written in ChordPro, as the lyrics: \"# \" is a heading, a chord in brackets is over the words. ")
						+ L.T("The song's details go in with $songname, $artist, $key, $tempo, $beat and $tags (its tags, separated by spaces); [$key] is the key as a chord, transposed with the song. Empty is no header.")
						+ (canEdit ? "" : L.T(" Only the band's admins change it."))),
				},
			};
			Content = panel;

			void ShowPreview(string definition)
			{
				var header = LyricsHeader.Expand(definition, Example, Example.OriginalKey);
				preview.Content = header.Length == 0
					? new TextBlock { Text = L.T("No header."), Opacity = 0.6 }
					: ChordPro.Render(header, fontSize: 16, lineHeight: 24).Content;
			}

			var previewBox = new Border
			{
				Padding = new Thickness(12, 4, 12, 8),
				CornerRadius = new CornerRadius(6),
				BorderThickness = new Thickness(1),
				Child = new StackPanel
				{
					Spacing = 2,
					Children =
					{
						new TextBlock { Text = L.T("Preview, for {0}", $"{Example.Name} ({Example.Artist})"), FontSize = 12, Opacity = 0.6 },
						preview,
					},
				},
			};
			previewBox.Bind(Border.BorderBrushProperty, previewBox.GetResourceObservable(LyriousTheme.CardBrush));

			var definition = LyricsHeader.Definition(stored);
			if (!canEdit)
			{
				panel.Children.Add(string.IsNullOrWhiteSpace(definition)
					? new TextBlock { Text = L.T("No header."), Opacity = 0.6 }
					: new SelectableTextBlock { Text = definition, FontFamily = FontFamily.Parse("monospace"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
				panel.Children.Add(previewBox);
				ShowPreview(definition);
				return;
			}

			var box = new TextBox
			{
				Text = definition,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MaxLength = LyricsHeader.MaxLength,
				MinHeight = 60,
				PlaceholderText = L.T("No header"),
				FontFamily = FontFamily.Parse("monospace"),
			};
			var useDefault = new Button { Content = L.T("Use the default") };
			var saveButton = new Button { Content = L.T("Save lyrics header"), IsEnabled = false };
			var saved = stored;
			var saving = false;
			var saveProblem = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			saveProblem.Bind(TextBlock.ForegroundProperty, saveProblem.GetResourceObservable(LyriousTheme.ErrorBrush));

			// As stored: the default is kept as null, so a band on the default gets a better default later.
			// Not trimmed: spaces and empty lines at the start or end are part of the header as written
			// (e.g. an empty line under it), and changing only them is a change to save.
			string? Current()
			{
				var text = (box.Text ?? "").Replace("\r\n", "\n");
				return text == LyricsHeader.Default ? null : text;
			}

			void Check()
			{
				ShowPreview(LyricsHeader.Definition(Current()));
				useDefault.IsEnabled = Current() != null;
				saveButton.IsEnabled = !saving && Current() != saved;
			}

			box.TextChanged += (_, _) => Check();
			useDefault.Click += (_, _) => box.Text = LyricsHeader.Default;
			saveButton.Click += async (_, _) =>
			{
				var changed = Current();
				saving = true;
				saveProblem.IsVisible = false;
				Check();
				try
				{
					await save(changed);
					saved = changed;
				}
				catch (Exception ex)
				{
					saveProblem.Text = L.T("Couldn't save the band settings. {0}", ex.Message);
					saveProblem.IsVisible = true;
				}
				finally
				{
					saving = false;
					Check();
				}
			};

			panel.Children.Add(box);
			panel.Children.Add(previewBox);
			panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveButton, useDefault } });
			panel.Children.Add(saveProblem);
			Check();
		}
	}
}
