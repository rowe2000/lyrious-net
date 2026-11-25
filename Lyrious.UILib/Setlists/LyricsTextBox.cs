using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The lyrics editor's text box: as any text box, but its tags in brackets ("[G]", "[12]",
	/// "[1:23]") show in a dimmed text colour, so the words stand out, and a chord in one ("[Am7]") in
	/// the chord colour (as the lyrics show chords; the brackets dimmed). A text box draws its text in one
	/// colour only, so the text is drawn again over it, coloured, in the same place (in the text box's
	/// own scrolling panel, wrapped the same way); the text box's own text is see-through, its caret and
	/// selection as before. A click inside a tag selects what's in it (between the brackets), to type over.
	/// </summary>
	public sealed partial class LyricsTextBox : TextBox
	{
		// The text drawn over the text box's own, coloured.
		private readonly TextBlock colored = new() { IsHitTestVisible = false };

		// How much of the text colour a tag gets.
		private const double TagOpacity = 0.45;

		protected override Type StyleKeyOverride => typeof(TextBox);

		public LyricsTextBox()
		{
			TextChanged += (_, _) => ShowColored();
			ActualThemeVariantChanged += (_, _) => ShowColored();
			// The chord colour as the member chooses it (Settings › Colors) too.
			this.GetResourceObservable(LyriousTheme.ChordBrush).Subscribe(new ResourceWatch(ShowColored));
			// The bar and time marks follow the highlight colour (chosen in Settings).
			this.GetResourceObservable("SystemAccentColor").Subscribe(new ResourceWatch(ShowColored));
		}

		protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
		{
			base.OnApplyTemplate(e);
			(colored.Parent as Panel)?.Children.Remove(colored);
			if (e.NameScope.Find<TextPresenter>("PART_TextPresenter") is not { Parent: Panel panel } presenter)
			{
				return; // Another template: its text as any text box's.
			}

			// In the presenter's panel, after it (over it): the same room, scrolled and clipped with it.
			panel.Children.Insert(panel.Children.IndexOf(presenter) + 1, colored);
			// The presenter's own text see-through; its caret (which takes the text's colour otherwise) in the text's colour.
			presenter.SetValue(TextElement.ForegroundProperty, Brushes.Transparent);
			this.Bind(CaretBrushProperty, this.GetResourceObservable(LyriousTheme.ForegroundBrush));
			// Laid out as the presenter lays out the text.
			colored.Bind(TextBlock.FontFamilyProperty, this.GetObservable(FontFamilyProperty));
			colored.Bind(TextBlock.FontSizeProperty, this.GetObservable(FontSizeProperty));
			colored.Bind(TextBlock.FontWeightProperty, this.GetObservable(FontWeightProperty));
			colored.Bind(TextBlock.FontStyleProperty, this.GetObservable(FontStyleProperty));
			colored.Bind(TextBlock.LineHeightProperty, this.GetObservable(LineHeightProperty));
			colored.Bind(TextBlock.LetterSpacingProperty, this.GetObservable(LetterSpacingProperty));
			colored.Bind(TextBlock.TextWrappingProperty, this.GetObservable(TextWrappingProperty));
			colored.Bind(TextBlock.TextAlignmentProperty, this.GetObservable(TextAlignmentProperty));
			colored.HorizontalAlignment = presenter.HorizontalAlignment;
			colored.VerticalAlignment = presenter.VerticalAlignment;
			colored.Margin = presenter.Margin;
			ShowColored();
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			// After the text box has put the caret where it was clicked: a click (not a drag) inside a tag
			// selects what's between its brackets.
			Dispatcher.UIThread.Post(() =>
			{
				if (SelectionStart != SelectionEnd)
				{
					return;
				}

				var text = Text ?? "";
				var caret = CaretIndex;
				foreach (Match tag in Tag().Matches(text))
				{
					if (caret > tag.Index && caret < tag.Index + tag.Length)
					{
						SelectionStart = tag.Index + 1;
						SelectionEnd = tag.Index + tag.Length - 1;
						return;
					}

					if (tag.Index > caret)
					{
						return;
					}
				}
			}, DispatcherPriority.Input);
		}

		/// <summary>The text again, as runs of words and tags: chords in the chord colour (their brackets dimmed), bar and time marks in the highlight colour.</summary>
		private void ShowColored()
		{
			var text = Text ?? "";
			var color = this.TryFindResource(LyriousTheme.ForegroundBrush, ActualThemeVariant, out var found) && found is ISolidColorBrush brush
				? brush.Color
				: Colors.White;
			var words = new SolidColorBrush(color);
			var tags = new SolidColorBrush(color, TagOpacity);
			var chords = this.TryFindResource(LyriousTheme.ChordBrush, ActualThemeVariant, out var chord) && chord is IBrush chordBrush ? chordBrush : words;
			var marks = this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var accent) && accent is Color accentColor ? new SolidColorBrush(accentColor) : tags;
			var inlines = new InlineCollection();
			var at = 0;
			foreach (Match tag in Tag().Matches(text))
			{
				if (tag.Index > at)
				{
					inlines.Add(new Run(text[at..tag.Index]) { Foreground = words });
				}

				// A chord: its name in the chord colour, the brackets dimmed. Bar and time marks in the highlight colour.
				var inside = tag.Value[1..^1];
				if (ChordPro.IsChord(inside))
				{
					inlines.Add(new Run("[") { Foreground = tags });
					inlines.Add(new Run(inside) { Foreground = chords });
					inlines.Add(new Run("]") { Foreground = tags });
				}
				else
				{
					inlines.Add(new Run(tag.Value) { Foreground = marks });
				}
				at = tag.Index + tag.Length;
			}

			if (at < text.Length)
			{
				inlines.Add(new Run(text[at..]) { Foreground = words });
			}

			colored.Inlines = inlines;
		}

		/// <summary>Calls back on each change of a resource (an observer of its value).</summary>
		private sealed class ResourceWatch(Action changed) : IObserver<object?>
		{
			public void OnNext(object? value) => changed();

			public void OnError(Exception error)
			{
			}

			public void OnCompleted()
			{
			}
		}

		// A tag: from "[" to the next "]" on the same line.
		[GeneratedRegex(@"\[[^\[\]\n]*\]")]
		private static partial Regex Tag();
	}
}
