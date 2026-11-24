using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// A short text (e.g. "♯♭" or "+2" on a button) drawn with the drawn part of its letters in the
	/// middle of its room, both ways. A text block centres its line, whose room above and below the
	/// letters differs from text to text (digits, symbols, a fallback font), so its letters may look
	/// high or low; this measures the letters' outline instead (or, when there's none to be had, centres
	/// the line). Its size, weight and colour are the surrounding text's (e.g. the button's), as for any text.
	/// </summary>
	public sealed class InkCenteredText : Control
	{
		public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<InkCenteredText, string?>(nameof(Text));

		static InkCenteredText()
		{
			AffectsMeasure<InkCenteredText>(TextProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontFamilyProperty);
			AffectsRender<InkCenteredText>(TextProperty, TextElement.ForegroundProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontFamilyProperty);
		}

		public string? Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		// The text as laid out at the last measure, and where its letters are drawn within it.
		private FormattedText? text;
		private Rect ink;

		protected override Size MeasureOverride(Size availableSize)
		{
			text = null;
			if (string.IsNullOrEmpty(Text))
			{
				return default;
			}

			text = new FormattedText(
				Text,
				CultureInfo.CurrentCulture,
				FlowDirection.LeftToRight,
				new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, TextElement.GetFontWeight(this)),
				TextElement.GetFontSize(this),
				null);

			// The letters' outline; the whole line when there's none (e.g. no outline for a fallback font's letters).
			var letters = text.BuildGeometry(default)?.Bounds ?? default;
			ink = letters.Width > 0 && letters.Height > 0 ? letters : new Rect(0, 0, text.WidthIncludingTrailingWhitespace, text.Height);
			return ink.Size;
		}

		public override void Render(DrawingContext context)
		{
			if (text == null)
			{
				return;
			}

			// The text drawn so that its letters' box is in the middle of the room.
			// In the inherited text colour (when there's one; else the text's own).
			if (TextElement.GetForeground(this) is { } foreground)
			{
				text.SetForegroundBrush(foreground);
			}

			var origin = new Point((Bounds.Width - ink.Width) / 2 - ink.X, (Bounds.Height - ink.Height) / 2 - ink.Y);
			context.DrawText(text, origin);
		}
	}
}
