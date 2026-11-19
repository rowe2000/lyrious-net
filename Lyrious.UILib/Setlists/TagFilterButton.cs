using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>How a tag filters a list of songs.</summary>
	public enum TagFilterState
	{
		/// <summary>Not used.</summary>
		Off,

		/// <summary>Only the songs that have the tag are listed.</summary>
		With,

		/// <summary>Only the songs that don't have the tag are listed.</summary>
		Without,
	}

	/// <summary>
	/// A tag as a small rounded button for filtering a list of songs: a click lists only the songs
	/// with the tag (highlighted), a longclick (held, or right-clicked) only those without it
	/// (struck through, in red); a click on a tag in use turns it off again.
	/// </summary>
	public static class TagFilterButton
	{
		private static readonly IBrush WithoutBrush = new SolidColorBrush(Color.Parse("#E5484D"));

		/// <param name="changed">Called with the tag's new state when it's clicked or longclicked.</param>
		public static Button Create(string tag, TagFilterState state, double fontSize, Thickness padding, Action<TagFilterState> changed)
		{
			var text = new TextBlock { Text = tag, FontSize = fontSize };
			var button = new LongClickButton
			{
				Content = text,
				Padding = padding,
				MinHeight = 0,
				CornerRadius = new CornerRadius(12),
			};

			void Show()
			{
				button.Classes.Set("accent", state == TagFilterState.With);
				text.TextDecorations = state == TagFilterState.Without ? TextDecorations.Strikethrough : null;
				if (state == TagFilterState.Without)
				{
					text.Foreground = WithoutBrush;
				}
				else
				{
					text.ClearValue(TextBlock.ForegroundProperty);
				}

				ToolTip.SetTip(button, state switch
				{
					TagFilterState.With => L.T("Only songs tagged {0}: click to stop", tag),
					TagFilterState.Without => L.T("Only songs not tagged {0}: click to stop", tag),
					_ => L.T("Click: only songs tagged {0}. Hold: only songs without it", tag),
				});
			}

			void Set(TagFilterState next)
			{
				state = next;
				Show();
				changed(state);
			}

			button.Click += (_, _) => Set(state == TagFilterState.Off ? TagFilterState.With : TagFilterState.Off);
			button.LongClick += () => Set(state == TagFilterState.Without ? TagFilterState.Off : TagFilterState.Without);
			Show();
			return button;
		}
	}
}
