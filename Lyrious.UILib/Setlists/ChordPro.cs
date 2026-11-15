using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Shows lyrics written in ChordPro (https://www.chordpro.org): chords in brackets inside the text,
	/// e.g. "[G]Blackbird singing [C]in the dead of night", appear above the words they belong to.
	/// <para>
	/// Programs add their own tags in brackets too, so a bracket tag (from "[" to its "]") counts as a
	/// chord unless it starts with a digit or "midi:" (e.g. "[1:23]", "[midi:...]"); those tags are left
	/// out. A "[" without a closing "]" is ordinary text.
	/// </para>
	/// <list type="bullet">
	/// <item>Each chord starts a piece of the line with the chord on top and its text below, so chords
	/// line up with their words in any font; the pieces wrap like words on a narrow screen.</item>
	/// <item>Directives: {comment:}/{c:}/{ci:}/{cb:}/{highlight:} show as notes, {capo:} as "Capo n",
	/// {chorus} as "Chorus"; {start_of_chorus}/{soc} … {end_of_chorus}/{eoc} is indented with a bar;
	/// {start_of_tab}/{sot} … {end_of_tab}/{eot} is shown in a fixed-width font. Title, artist, key,
	/// tempo and other directives are left out (the song's header shows those).</item>
	/// <item>Headings as in Markdown: "# " is heading 1, "## " heading 2, … "###### " heading 6.
	/// A '#' not followed by a space is a ChordPro comment and isn't shown. Time tags may come before
	/// the '#' (e.g. "[0:12]# Verse"): the line is still a heading (or comment), marked at that time.</item>
	/// <item>Time tags (not shown): "[1:23]" is a time from the start of the song (minutes:seconds,
	/// tenths allowed); "[12]" is a bar count from the start, turned into a time with the song's tempo
	/// and beats per bar. Their places in the lyrics come back as <see cref="TimeMarker"/>s, so the
	/// lyrics can scroll in time with the song.</item>
	/// <item>Bold and italic much as in Markdown: **bold** or __bold__, _italic_, ***both*** or
	/// ___both___, within a line (also across chords). Unlike Markdown, a single * is bold too:
	/// *bold* (only _ makes italic).</item>
	/// </list>
	/// </summary>
	/// <summary>A time tag's place in the rendered lyrics: the control it's on, and the time from the song's start.</summary>
	public sealed record TimeMarker(TimeSpan Time, Control Anchor);

	/// <summary>The rendered lyrics, and where their time tags are.</summary>
	/// <summary>Style classes on rendered lines: headings, and the gap an empty line leaves.</summary>
	public static class LyricsClasses
	{
		public const string Heading = "lyrics-heading";
		public const string Gap = "lyrics-gap";
	}

	public sealed record RenderedLyrics(Control Content, IReadOnlyList<TimeMarker> Markers);

	public static partial class ChordPro
	{
		/// <summary>Whether the text uses ChordPro: a chord in brackets (see <see cref="IsChord"/>), or a {directive} line.</summary>
		public static bool IsChordPro(string text)
			=> DirectiveLine().IsMatch(text) || BracketTag().Matches(text).Any(tag => IsChord(tag.Groups[1].Value));

		/// <summary>Whether the text needs <see cref="Render"/>: ChordPro, Markdown-style "# " headings or bold/italic, or time tags.</summary>
		public static bool HasFormatting(string text)
			=> IsChordPro(text)
			   || HeadingLine().IsMatch(text)
			   || BracketTag().Matches(text).Any(tag => IsTimeTag(tag.Groups[1].Value))
			   || text.Split('\n').Any(line => Emphasis(line).Any(style => style.Marker));

		/// <summary>Whether a tag is a time: digits with a ':' (minutes:seconds), or just an integer (a bar count).</summary>
		public static bool IsTimeTag(string tag) => TimeTag().IsMatch(tag.Trim());

		/// <summary>
		/// The time a tag stands for: "[1:23]" (or "[1:23.5]", "[1:02:03]") is that time from the start;
		/// "[12]" is 12 bars from the start, at the song's tempo and beats per bar. Null if the tag isn't
		/// a time, or it's bars and the tempo is unknown.
		/// </summary>
		public static TimeSpan? TimeOf(string tag, double tempo, int beatsPerBar)
		{
			var text = tag.Trim();
			if (!TimeTag().IsMatch(text))
			{
				return null;
			}

			if (!text.Contains(':'))
			{
				// A bar count.
				return tempo > 0 && int.TryParse(text, out var bars)
					? TimeSpan.FromSeconds(bars * Math.Max(1, beatsPerBar) * 60.0 / tempo)
					: null;
			}

			var parts = text.Split(':');
			var seconds = 0.0;
			foreach (var part in parts)
			{
				if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
				{
					return null;
				}

				seconds = seconds * 60 + value;
			}

			return TimeSpan.FromSeconds(seconds);
		}

		/// <summary>Beats per bar from a time signature such as "4/4" or "3/4" (the top number); 4 if unknown.</summary>
		public static int BeatsPerBar(string beat)
			=> int.TryParse(beat.Split('/')[0].Trim(), out var beats) && beats > 0 ? beats : 4;

		/// <summary>The text without chords, other bracket tags, directive lines and bold/italic markers, e.g. for searching the words.</summary>
		public static string StripChords(string text)
			=> string.Join('\n', DirectiveLine().Replace(BracketTag().Replace(text, ""), "").Split('\n').Select(PlainText));

		/// <summary>A line without its Markdown bold/italic markers.</summary>
		private static string PlainText(string line)
		{
			var styles = Emphasis(line);
			return string.Concat(line.Where((_, i) => !styles[i].Marker));
		}

		/// <summary>
		/// Whether what's inside a bracket tag is a chord: anything but nothing, or a tag that starts
		/// with a digit or "midi:" (other programs' own tags, e.g. times or MIDI events).
		/// </summary>
		public static bool IsChord(string tag)
		{
			var text = tag.Trim();
			return text.Length > 0
			       && !char.IsDigit(text[0])
			       && !text.StartsWith("midi:", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// The lyrics as controls (chord lines, plain lines, headings, notes, chorus and tab blocks), and
		/// where the time tags are in them.
		/// </summary>
		/// <param name="fontSize">The lyrics' font size, also the chords'.</param>
		/// <param name="lineHeight">The height of a plain lyrics line.</param>
		/// <param name="tempo">The song's tempo (beats per minute), to turn bar counts into times.</param>
		/// <param name="beat">The song's time signature, e.g. "4/4", for the beats in a bar.</param>
		public static RenderedLyrics Render(string text, double fontSize, double lineHeight, double tempo = 0, string beat = "4/4")
		{
			var markers = new List<TimeMarker>();
			var beatsPerBar = BeatsPerBar(beat);

			// The time tags on a line, in order (empty if none).
			List<TimeSpan> TimesOn(string line) => BracketTag().Matches(line)
				.Select(tag => TimeOf(tag.Groups[1].Value, tempo, beatsPerBar))
				.OfType<TimeSpan>()
				.ToList();

			void Mark(IEnumerable<TimeSpan> times, Control anchor) => markers.AddRange(times.Select(time => new TimeMarker(time, anchor)));

			var root = new StackPanel();
			var current = root;
			var inTab = false;

			foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
			{
				var line = rawLine.TrimEnd();
				var trimmed = line.Trim();

				if (!inTab && HeadingLine().Match(trimmed) is { Success: true } heading)
				{
					var headingBlock = Heading(heading.Groups[1].Length, BracketTag().Replace(heading.Groups[2].Value, "").Trim(), fontSize);
					Mark(TimesOn(line), headingBlock);
					current.Children.Add(headingBlock);
					continue;
				}

				if (!inTab && LeadingTimeTags().Replace(trimmed, "").StartsWith('#'))
				{
					// A ChordPro comment ('#' with no space after it), for the writer only. A time tag
					// before it still marks its place.
					if (TimesOn(line) is { Count: > 0 } commentTimes)
					{
						var place = new Border { Height = 0 };
						Mark(commentTimes, place);
						current.Children.Add(place);
					}

					continue;
				}

				if (DirectiveLine().Match(trimmed) is { Success: true } directive)
				{
					var name = directive.Groups[1].Value.ToLowerInvariant();
					var value = directive.Groups[2].Value.Trim();
					switch (name)
					{
						case "start_of_chorus" or "soc":
							current = new StackPanel();
							var chorus = new Border
							{
								BorderThickness = new Thickness(3, 0, 0, 0),
								Padding = new Thickness(16, 0, 0, 0),
								Margin = new Thickness(0, 4),
								Child = current,
							};
							chorus.Bind(Border.BorderBrushProperty, chorus.GetResourceObservable(LyriousTheme.ChordBrush));
							root.Children.Add(chorus);
							break;
						case "end_of_chorus" or "eoc":
							current = root;
							break;
						case "start_of_tab" or "sot":
							inTab = true;
							break;
						case "end_of_tab" or "eot":
							inTab = false;
							break;
						case "comment" or "c" or "comment_italic" or "ci" or "comment_box" or "cb" or "highlight":
							current.Children.Add(Note(value, fontSize));
							break;
						case "chorus":
							current.Children.Add(Note(value.Length > 0 ? value : "Chorus", fontSize));
							break;
						case "capo":
							current.Children.Add(Note($"Capo {value}", fontSize));
							break;
					}

					continue;
				}

				if (inTab)
				{
					current.Children.Add(new TextBlock { Text = line.Length > 0 ? line : " ", FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"), FontSize = fontSize * 0.7 });
				}
				else if (trimmed.Length == 0)
				{
					current.Children.Add(new Border { Height = lineHeight * 0.6, Classes = { LyricsClasses.Gap } });
				}
				else if (BracketTag().Matches(line).Any(tag => IsChord(tag.Groups[1].Value)))
				{
					current.Children.Add(ChordLine(line, fontSize, lineHeight, tempo, beatsPerBar, markers));
				}
				else
				{
					// No chords; other programs' tags (if any) are left out. A line of only time tags
					// still marks its place, with an empty line.
					var words = BracketTag().Replace(line, "");
					var times = TimesOn(line);
					if (words.Trim().Length > 0)
					{
						var block = new TextBlock { FontSize = fontSize, LineHeight = lineHeight, TextWrapping = TextWrapping.Wrap };
						SetStyledText(block, words, Emphasis(words), 0, words.Length);
						Mark(times, block);
						current.Children.Add(block);
					}
					else if (times.Count > 0)
					{
						var place = new Border { Height = 0 };
						Mark(times, place);
						current.Children.Add(place);
					}
				}
			}

			return new RenderedLyrics(root, markers);
		}

		/// <summary>
		/// A line with chords: a row of pieces, each a chord (or nothing) over its text. Text after a
		/// chord is split into words, the chord over the first, so a long line wraps between words.
		/// </summary>
		/// <param name="markers">Time tags on the line are added here, each at its place in the row.</param>
		private static Control ChordLine(string line, double fontSize, double lineHeight, double tempo, int beatsPerBar, List<TimeMarker> markers)
		{
			var chordSize = fontSize; // Chords as large as the lyrics.
			var chordHeight = Math.Round(chordSize * 1.35);
			var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

			// Bold/italic can span chords, so it's found in the line's words as a whole.
			var segments = Segments(line).ToList();
			var words = string.Concat(segments.Select(segment => segment.Text));
			var styles = Emphasis(words);
			var offset = 0;

			foreach (var (chord, text, timeTag) in segments)
			{
				if (timeTag != null)
				{
					// An invisible, zero-width mark where the time tag was: it wraps with the words around it.
					if (TimeOf(timeTag, tempo, beatsPerBar) is { } time)
					{
						var place = new Border { Width = 0, Height = chordHeight + lineHeight };
						markers.Add(new TimeMarker(time, place));
						row.Children.Add(place);
					}

					continue;
				}

				var pieces = text.Length == 0 ? [""] : WordBoundary().Split(text).Where(word => word.Length > 0).ToArray();
				for (var i = 0; i < pieces.Length; i++)
				{
					var chordText = i == 0 ? chord : null;
					if (chordText == null && pieces[i].Length > 0 && styles.AsSpan(offset, pieces[i].Length).ToArray().All(style => style.Marker))
					{
						// Only bold/italic markers (e.g. the "**" before a chord): nothing to show.
						offset += pieces[i].Length;
						continue;
					}

					var piece = new StackPanel { Orientation = Orientation.Vertical };

					var chordBlock = new TextBlock
					{
						Text = chordText ?? "",
						FontSize = chordSize,
						FontWeight = FontWeight.Bold,
						Height = chordHeight,
						// Room after a chord, so chords over short words don't run into each other.
						Margin = chordText != null ? new Thickness(0, 0, chordSize * 0.4, 0) : default,
					};
					chordBlock.Bind(TextBlock.ForegroundProperty, chordBlock.GetResourceObservable(LyriousTheme.ChordBrush));
					piece.Children.Add(chordBlock);

					var wordBlock = new TextBlock { FontSize = fontSize, LineHeight = lineHeight };
					SetStyledText(wordBlock, words, styles, offset, pieces[i].Length);
					offset += pieces[i].Length;
					piece.Children.Add(wordBlock);
					row.Children.Add(piece);
				}
			}

			return row;
		}

		/// <summary>
		/// The line in pieces: (chord or null, the text up to the next chord), and time tags where they
		/// are (TimeTag set, nothing else). Other bracket tags are left out, the text around them joined up.
		/// </summary>
		private static IEnumerable<(string? Chord, string Text, string? TimeTag)> Segments(string line)
		{
			var position = 0;
			string? chord = null;
			var text = new System.Text.StringBuilder();
			foreach (Match match in BracketTag().Matches(line))
			{
				text.Append(line, position, match.Index - position);
				position = match.Index + match.Length;
				var tag = match.Groups[1].Value;
				if (IsTimeTag(tag))
				{
					// Ends the piece so far, so the time mark lands exactly here.
					if (chord != null || text.Length > 0)
					{
						yield return (chord, text.ToString(), null);
						chord = null;
						text.Clear();
					}

					yield return (null, "", tag);
					continue;
				}

				if (!IsChord(tag))
				{
					continue; // Another program's tag: not shown.
				}

				if (chord != null || text.Length > 0)
				{
					yield return (chord, text.ToString(), null);
				}

				chord = tag.Trim();
				text.Clear();
			}

			text.Append(line, position, line.Length - position);
			if (chord != null || text.Length > 0)
			{
				yield return (chord, text.ToString(), null);
			}
		}

		/// <summary>A heading: level 1 largest, down to level 6, with room above.</summary>
		private static TextBlock Heading(int level, string text, double fontSize)
		{
			var (scale, weight, opacity) = Math.Clamp(level, 1, 6) switch
			{
				1 => (1.45, FontWeight.Bold, 1.0),
				2 => (1.25, FontWeight.Bold, 1.0),
				3 => (1.1, FontWeight.SemiBold, 1.0),
				4 => (1.0, FontWeight.SemiBold, 1.0),
				5 => (0.9, FontWeight.SemiBold, 0.85),
				_ => (0.8, FontWeight.SemiBold, 0.7),
			};

			var heading = new TextBlock
			{
				FontSize = Math.Round(fontSize * scale),
				FontWeight = weight,
				Opacity = opacity,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, fontSize * 0.5, 0, fontSize * 0.2),
			};
			SetStyledText(heading, text, Emphasis(text), 0, text.Length);
			heading.Classes.Add(LyricsClasses.Heading);
			return heading;
		}

		/// <summary>A Markdown bold/italic style for one character; markers (the * and _) aren't shown.</summary>
		private readonly record struct CharStyle(bool Bold, bool Italic, bool Marker);

		/// <summary>
		/// Finds Markdown bold and italic in a line: ***both***, **bold**, and (unlike Markdown) *bold*;
		/// ___both___, __bold__ and _italic_ ('_' doesn't work inside a word, so snake_case stays as it is).
		/// </summary>
		private static CharStyle[] Emphasis(string text)
		{
			var styles = new CharStyle[text.Length];
			if (text.IndexOfAny(['*', '_']) < 0)
			{
				return styles;
			}

			var work = text.ToCharArray();
			(Regex Pattern, bool Bold, bool Italic)[] passes =
			[
				(BoldItalicStars(), true, true), (BoldItalicUnderscores(), true, true),
				(BoldStars(), true, false), (BoldUnderscores(), true, false),
				(SingleStars(), true, false), (ItalicUnderscores(), false, true),
			];
			foreach (var (pattern, bold, italic) in passes)
			{
				foreach (Match match in pattern.Matches(new string(work)))
				{
					var marker = match.Groups["m"].Length;
					var end = match.Index + match.Length;
					for (var i = match.Index; i < end; i++)
					{
						if (i < match.Index + marker || i >= end - marker)
						{
							styles[i] = styles[i] with { Marker = true };
							work[i] = '\u0001'; // Used up: not matched again by a later pass.
						}
						else
						{
							styles[i] = styles[i] with { Bold = styles[i].Bold || bold, Italic = styles[i].Italic || italic };
						}
					}
				}
			}

			return styles;
		}

		/// <summary>
		/// Sets a text block to part of a line (from <paramref name="start"/>, <paramref name="length"/>
		/// characters) with its bold and italic, leaving out the markers. Nothing left shows as a space,
		/// so a chord still has a line under it.
		/// </summary>
		private static void SetStyledText(TextBlock block, string text, CharStyle[] styles, int start, int length)
		{
			var end = Math.Min(text.Length, start + length);
			if (!styles.AsSpan(start, end - start).ToArray().Any(style => style.Bold || style.Italic || style.Marker))
			{
				var plain = text[start..end];
				block.Text = plain.Length > 0 ? plain : " ";
				return;
			}

			var inlines = new InlineCollection();
			var i = start;
			while (i < end)
			{
				if (styles[i].Marker)
				{
					i++;
					continue;
				}

				var style = styles[i];
				var runStart = i;
				while (i < end && !styles[i].Marker && styles[i] == style)
				{
					i++;
				}

				var run = new Run(text[runStart..i]);
				if (style.Bold)
				{
					run.FontWeight = FontWeight.Bold;
				}

				if (style.Italic)
				{
					run.FontStyle = FontStyle.Italic;
				}

				inlines.Add(run);
			}

			if (inlines.Count == 0)
			{
				block.Text = " ";
				return;
			}

			block.Inlines = inlines;
		}

		private static TextBlock Note(string text, double fontSize)
		{
			var note = new TextBlock
			{
				FontSize = fontSize * 0.75,
				FontStyle = FontStyle.Italic,
				Opacity = 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 4),
			};
			SetStyledText(note, text, Emphasis(text), 0, text.Length);
			return note;
		}

		/// <summary>A time tag: an integer (bars), or numbers with ':' (minutes:seconds, perhaps hours and tenths).</summary>
		[GeneratedRegex(@"^\d+$|^\d+(:\d+(\.\d+)?)+$")]
		private static partial Regex TimeTag();

		/// <summary>
		/// A Markdown heading line: one to six '#', a space, then the heading. Hidden tags (time
		/// tags, "midi:" tags) may come before the '#', e.g. "[0:12]# Verse".
		/// </summary>
		[GeneratedRegex(@"^\s*(?:\[\s*(?:\d|[Mm][Ii][Dd][Ii]:)[^\]\n]*\]\s*)*(#{1,6})\s+(.*\S)\s*$", RegexOptions.Multiline)]
		private static partial Regex HeadingLine();

		/// <summary>Hidden tags (time tags, "midi:" tags) at the start of a line, and the spaces after them.</summary>
		[GeneratedRegex(@"^(?:\[\s*(?:\d|[Mm][Ii][Dd][Ii]:)[^\]\n]*\]\s*)+")]
		private static partial Regex LeadingTimeTags();

		/// <summary>
		/// A bracket tag: from a "[" to the next "]" on the same line. A "[" inside belongs to the tag,
		/// e.g. "[midi:[midi:$MODX, PC20@1]" is one tag (starting with "midi:", so not a chord).
		/// </summary>
		[GeneratedRegex(@"\[([^\]\n]*)\]")]
		private static partial Regex BracketTag();

		/// <summary>A directive on a line of its own: {name} or {name: value}.</summary>
		[GeneratedRegex(@"^\s*\{([A-Za-z_]+)(?:[:\s]\s*(.*?))?\}\s*$", RegexOptions.Multiline)]
		private static partial Regex DirectiveLine();

		[GeneratedRegex(@"(?<!\*)(?<m>\*\*\*)(?=\S)(.+?)(?<=\S)\*\*\*(?!\*)")]
		private static partial Regex BoldItalicStars();

		[GeneratedRegex(@"(?<![\w_])(?<m>___)(?=\S)(.+?)(?<=\S)___(?![\w_])")]
		private static partial Regex BoldItalicUnderscores();

		[GeneratedRegex(@"(?<!\*)(?<m>\*\*)(?=\S)(.+?)(?<=\S)\*\*(?!\*)")]
		private static partial Regex BoldStars();

		[GeneratedRegex(@"(?<![\w_])(?<m>__)(?=\S)(.+?)(?<=\S)__(?![\w_])")]
		private static partial Regex BoldUnderscores();

		[GeneratedRegex(@"(?<!\*)(?<m>\*)(?=\S)(.+?)(?<=\S)\*(?!\*)")]
		private static partial Regex SingleStars();

		[GeneratedRegex(@"(?<![\w_])(?<m>_)(?=\S)(.+?)(?<=\S)_(?![\w_])")]
		private static partial Regex ItalicUnderscores();

		/// <summary>After a space: splits text into words, each keeping the space that follows it.</summary>
		[GeneratedRegex(@"(?<=\s)")]
		private static partial Regex WordBoundary();
	}
}
