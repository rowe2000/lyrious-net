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
	/// <item>Directives, alone on a line or several on one ("{c: Intro} {c: x2}", shown side by side):
	/// {comment:}/{c:}/{ci:}/{cb:}/{highlight:} show as notes, {capo:} as "Capo n",
	/// {chorus} as "Chorus"; {start_of_chorus}/{soc} … {end_of_chorus}/{eoc} is indented with a bar;
	/// {start_of_tab}/{sot} … {end_of_tab}/{eot} is shown in a fixed-width font. Title, artist, key,
	/// tempo and other directives are left out (the song's header shows those).</item>
	/// <item>Headings as in Markdown: "# " is heading 1, "## " heading 2, … "###### " heading 6; the
	/// space may be left out ("#Verse", "##Chorus"). A line of only '#'s isn't shown. Time tags may come
	/// before the '#' (e.g. "[0:12]# Verse"): the line is still a heading, marked at that time.</item>
	/// <item>Time tags (not shown): "[1:23]" is a time from the start of the song (minutes:seconds,
	/// tenths allowed); "[12]" is a bar count from the start, turned into a time with the song's tempo
	/// and beats per bar. Their places in the lyrics come back as <see cref="TimeMarker"/>s, so the
	/// lyrics can scroll in time with the song. A bar mark "|" anywhere in a line is one bar after the
	/// previous time tag, the same as "[+1]" (not in tab blocks, where '|' is part of the tab).</item>
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

	/// <param name="Rows">Each line's row (its first, when it has more), with the line's number in the text (from 0), top to bottom; lines that show nothing aren't there.</param>
	public sealed record RenderedLyrics(Control Content, IReadOnlyList<TimeMarker> Markers, IReadOnlyList<(Control Row, int Line)> Rows);

	public static partial class ChordPro
	{
		/// <summary>Whether the text uses ChordPro: a chord in brackets (see <see cref="IsChord"/>), or a {directive} line.</summary>
		public static bool IsChordPro(string text)
			=> DirectiveLine().IsMatch(text) || BracketTag().Matches(text).Any(tag => IsChord(tag.Groups[1].Value));

		/// <summary>Whether a line of the text starts with a bar mark '|' (bars that can be shown in the chord grid).</summary>
		public static bool HasBarLines(string text)
			=> text.Replace("\r\n", "\n").Split('\n').Any(line => line.TrimStart().StartsWith('|'));

		/// <summary>Whether the text needs <see cref="Render"/>: ChordPro, Markdown-style "# " headings or bold/italic, or time tags.</summary>
		public static bool HasFormatting(string text)
			=> HasFormattingExpanded(ExpandBarMarks(text));

		private static bool HasFormattingExpanded(string text)
			=> IsChordPro(text)
			   || HeadingLine().IsMatch(text)
			   || BracketTag().Matches(text).Any(tag => IsTimeTag(tag.Groups[1].Value))
			   || text.Split('\n').Any(line => Emphasis(line).Any(style => style.Marker));

		/// <summary>Whether a tag is a time: digits with a ':' (minutes:seconds), or just an integer (a bar count); either may start with '+' (after the previous tag).</summary>
		public static bool IsTimeTag(string tag) => TimeTag().IsMatch(tag.Trim());

		/// <summary>
		/// The time a tag stands for: "[1:23]" (or "[1:23.5]", "[1:02:03]") is that time from the start;
		/// "[12]" is the start of bar 12, counting from 1 (so "[1]", or "[0]", is the start of the song),
		/// at the song's tempo and beats per bar. With a '+' it's that long after the previous time tag:
		/// "[+0:08]" 8 seconds, "[+4]" 4 bars (after the start if there's none before it). Null if the
		/// tag isn't a time, or it's bars and the tempo is unknown.
		/// </summary>
		/// <param name="previous">The time of the time tag before this one, for a '+' tag.</param>
		public static TimeSpan? TimeOf(string tag, double tempo, int beatsPerBar, TimeSpan? previous = null)
		{
			var text = tag.Trim();
			if (!TimeTag().IsMatch(text))
			{
				return null;
			}

			var relative = text.StartsWith('+');
			if (relative)
			{
				text = text[1..];
			}

			if (!text.Contains(':'))
			{
				// Bars: a bar's number (from 1), or with '+' a number of bars.
				if (tempo <= 0 || !int.TryParse(text, out var bars))
				{
					return null;
				}

				var length = TimeSpan.FromSeconds((relative ? bars : Math.Max(0, bars - 1)) * Math.Max(1, beatsPerBar) * 60.0 / tempo);
				return relative ? (previous ?? TimeSpan.Zero) + length : length;
			}

			if (relative)
			{
				return TimeOf(text, tempo, beatsPerBar) is { } time ? (previous ?? TimeSpan.Zero) + time : null;
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
			=> string.Join('\n', DirectiveLine().Replace(BracketTag().Replace(ExpandBarMarks(text), ""), "").Split('\n').Select(PlainText));

		/// <summary>A line without its Markdown bold/italic markers.</summary>
		private static string PlainText(string line)
		{
			var styles = Emphasis(line);
			return string.Concat(line.Where((_, i) => !styles[i].Marker));
		}

		/// <summary>
		/// Whether what's inside a bracket tag is a chord: anything but nothing, a time tag (e.g. "+4"),
		/// or a tag that starts with a digit or "midi:" (other programs' own tags, e.g. times or MIDI events).
		/// </summary>
		public static bool IsChord(string tag)
		{
			var text = tag.Trim();
			return text.Length > 0
			       && !char.IsDigit(text[0])
			       && !IsTimeTag(text)
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
		/// <param name="chordGrid">
		/// Chords only, in a grid: each line with chords is a row of equal columns, as many as the line
		/// with the most (so every row fills the width the same way). On a line starting with '|' the
		/// bar marks decide the columns (a chord goes in the bar it's written in); on other lines each
		/// chord has a column of its own (a bar line without chords is a row of empty bars). Each row's
		/// words are shown below it; lines without chords, headings, notes and tabs as usual.
		/// </param>
		public static RenderedLyrics Render(string text, double fontSize, double lineHeight, double tempo = 0, string beat = "4/4", bool chordGrid = false)
		{
			// Which lines are bar lines (start with '|'), before the bar marks become time tags.
			var barLines = text.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimStart().StartsWith('|')).ToArray();

			// Bar marks ('|') are time tags too ("[+1]").
			text = ExpandBarMarks(text);
			var lines = text.Replace("\r\n", "\n").Split('\n');
			var gridColumns = chordGrid ? Math.Max(1, GridColumnCount(lines, barLines)) : 0;
			var markers = new List<TimeMarker>();
			var beatsPerBar = BeatsPerBar(beat);

			// A tag's time, in the order the tags come: a '+' tag counts from the one before it.
			TimeSpan? last = null;
			TimeSpan? Resolve(string tag)
			{
				if (TimeOf(tag, tempo, beatsPerBar, last) is not { } time)
				{
					return null;
				}

				last = time;
				return time;
			}

			// The time tags on a line, in order (empty if none). Call once per line ('+' tags move on).
			List<TimeSpan> TimesOn(string line) => BracketTag().Matches(line)
				.Select(tag => Resolve(tag.Groups[1].Value))
				.OfType<TimeSpan>()
				.ToList();

			void Mark(IEnumerable<TimeSpan> times, Control anchor) => markers.AddRange(times.Select(time => new TimeMarker(time, anchor)));

			var root = new StackPanel();
			var current = root;
			var inTab = false;
			var rows = new List<(Control Row, int Line)>();

			for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				// What this line adds to the panel it goes in: its first row is the line's (see Rows).
				var into = current;
				var rowsBefore = current.Children.Count;
				try
				{
					var line = lines[lineIndex].TrimEnd();
					var trimmed = line.Trim();

					if (!inTab && HeadingLine().Match(trimmed) is { Success: true } heading)
					{
						var level = heading.Groups[1].Length;
						var headingText = heading.Groups[2].Value;

						// Headings of other levels further on the line ("#Title ####tags"): side by side.
						var parts = HeadingParts(level, headingText);
						if (parts.Count > 1)
						{
							var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, fontSize * 0.5, 0, fontSize * 0.2) };
							foreach (var (partLevel, partText) in parts)
							{
								var part = BracketTag().Matches(partText).Any(tag => IsChord(tag.Groups[1].Value))
									? HeadingStyle(ChordLine(partText, Math.Round(fontSize * HeadingScale(partLevel)), lineHeight * HeadingScale(partLevel), Resolve, markers, compact: true), partLevel, fontSize)
									: (Control)Heading(partLevel, BracketTag().Replace(partText, "").Trim(), fontSize);
								// In the row's room (not each its own), on the row's bottom, a space apart.
								part.Margin = new Thickness(0, 0, fontSize * 0.5, 0);
								part.VerticalAlignment = VerticalAlignment.Bottom;
								row.Children.Add(part);
							}

							row.Classes.Add(LyricsClasses.Heading);
							Mark(TimesOn(trimmed[..trimmed.IndexOf('#')]), row);
							current.Children.Add(row);
							continue;
						}

						headingText = parts.Count == 1 ? parts[0].Text : headingText;
						if (BracketTag().Matches(headingText).Any(tag => IsChord(tag.Groups[1].Value)))
						{
							// Chords in the heading: over its words, as on a lyrics line, in the heading's size and colour.
							// The time tags before the '#' come first (a '+' tag counts from the one before it).
							var before = TimesOn(trimmed[..trimmed.IndexOf('#')]);
							// Compact: when the heading wraps, its lines are as close as plain text's.
							var row = HeadingStyle(ChordLine(headingText, Math.Round(fontSize * HeadingScale(level)), lineHeight * HeadingScale(level), Resolve, markers, compact: true), level, fontSize);
							Mark(before, row);
							current.Children.Add(row);
							continue;
						}

						var headingBlock = Heading(level, BracketTag().Replace(headingText, "").Trim(), fontSize);
						Mark(TimesOn(line), headingBlock);
						current.Children.Add(headingBlock);
						continue;
					}

					if (!inTab && LeadingTimeTags().Replace(trimmed, "").StartsWith('#'))
					{
						// Only '#'s (no heading after them, or more than six): nothing to show. A time tag
						// before it still marks its place.
						if (TimesOn(line) is { Count: > 0 } commentTimes)
						{
							var place = new Border { Height = 0 };
							Mark(commentTimes, place);
							current.Children.Add(place);
						}

						continue;
					}

					if (Directives(trimmed) is { } directives)
					{
						// Several on a line ("{c: Intro} {c: x2}"): their notes side by side, in the order
						// written. A chorus or tab starting or ending among them does so where it's written.
						var notes = new List<Control>();
						void ShowNotes()
						{
							if (notes.Count == 1)
							{
								current.Children.Add(notes[0]);
							}
							else if (notes.Count > 1)
							{
								var row = new WrapPanel { Orientation = Orientation.Horizontal };
								foreach (var note in notes)
								{
									note.Margin = new Thickness(0, 4, fontSize * 0.6, 4);
									note.VerticalAlignment = VerticalAlignment.Bottom;
									row.Children.Add(note);
								}

								current.Children.Add(row);
							}

							notes.Clear();
						}

						foreach (var (name, value) in directives)
						{
							if (name is "start_of_chorus" or "soc" or "end_of_chorus" or "eoc" or "start_of_tab" or "sot" or "end_of_tab" or "eot")
							{
								ShowNotes();
							}

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
									notes.Add(Note(value, fontSize));
									break;
								case "chorus":
									notes.Add(Note(value.Length > 0 ? value : "Chorus", fontSize));
									break;
								case "capo":
									notes.Add(Note($"Capo {value}", fontSize));
									break;
							}
						}

						ShowNotes();
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
					else if (chordGrid && (barLines[lineIndex] || BracketTag().Matches(line).Any(tag => IsChord(tag.Groups[1].Value))))
					{
						// The chords in their columns, the line's words below them; the time tags mark the row.
						var row = GridRow(GridCells(line, barLines[lineIndex]), gridColumns, fontSize);
						Mark(TimesOn(line), row);
						current.Children.Add(row);
						// Without its tags (and the spaces the bar marks leave doubled).
						var words = Regex.Replace(BracketTag().Replace(line, ""), @"[ \t]{2,}", " ").Trim();
						if (words.Length > 0)
						{
							var block = new TextBlock { FontSize = fontSize, LineHeight = lineHeight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
							SetStyledText(block, words, Emphasis(words), 0, words.Length);
							current.Children.Add(block);
						}
					}
					else if (BracketTag().Matches(line).Any(tag => IsChord(tag.Groups[1].Value)))
					{
						current.Children.Add(ChordLine(line, fontSize, lineHeight, Resolve, markers));
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
				finally
				{
					if (ReferenceEquals(into, current) && current.Children.Count > rowsBefore)
					{
						rows.Add((current.Children[rowsBefore], lineIndex));
					}
				}
			}

			return new RenderedLyrics(root, markers, rows);
		}

		/// <summary>
		/// The chords of a line by column, for the chord grid (each column's chords, joined by spaces;
		/// "" for an empty bar). On a bar line (one starting with '|', its marks already "[+1]") each
		/// bar mark starts a column (one at the end of the line too: "| | | |" is 4 bars) and a chord goes
		/// in the bar it's in. On other lines each chord is a column.
		/// </summary>
		private static List<string> GridCells(string line, bool barLine)
		{
			if (!barLine)
			{
				return Segments(line).Where(segment => segment.Chord != null).Select(segment => segment.Chord!).ToList();
			}

			var cells = new List<(List<string> Chords, bool HasText)>();
			foreach (var (chord, text, timeTag) in Segments(line))
			{
				// A bar mark: "[+1]", or "[1]" for the song's first one.
				if (timeTag?.Trim() is "+1" or "1")
				{
					cells.Add(([], false));
					continue;
				}

				if (timeTag != null)
				{
					continue; // Another time tag: no new bar.
				}

				if (cells.Count == 0)
				{
					cells.Add(([], false));
				}

				var cell = cells[^1];
				if (chord != null)
				{
					cell.Chords.Add(chord);
				}

				cells[^1] = (cell.Chords, cell.HasText || text.Trim().Length > 0);
			}

			return cells.Select(cell => string.Join(" ", cell.Chords)).ToList();
		}

		private static readonly IBrush GridLineBrush = new SolidColorBrush(Colors.Gray, 0.85);

		/// <summary>The chord grid's number of columns: the most any line with chords needs (tab blocks, headings and comments aside).</summary>
		private static int GridColumnCount(string[] lines, bool[] barLines)
		{
			var most = 0;
			var inTab = false;
			for (var i = 0; i < lines.Length; i++)
			{
				var trimmed = lines[i].Trim();
				if (Directives(trimmed) is { } directives)
				{
					foreach (var (name, _) in directives)
					{
						inTab = name is "start_of_tab" or "sot" || (inTab && name is not ("end_of_tab" or "eot"));
					}

					continue;
				}

				if (inTab || HeadingLine().IsMatch(trimmed) || LeadingTimeTags().Replace(trimmed, "").StartsWith('#'))
				{
					continue;
				}

				if (barLines[i] || BracketTag().Matches(lines[i]).Any(tag => IsChord(tag.Groups[1].Value)))
				{
					most = Math.Max(most, GridCells(lines[i], barLines[i]).Count);
				}
			}

			return most;
		}

		/// <summary>
		/// A row of the chord grid: <paramref name="columns"/> columns of the same width across the
		/// whole width, the line's chords in the first ones, each column with a thin bar line at its left
		/// (and one closing the last used column).
		/// </summary>
		private static Control GridRow(IReadOnlyList<string> cells, int columns, double fontSize)
		{
			var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
			for (var i = 0; i < columns; i++)
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
			}

			for (var i = 0; i < Math.Min(cells.Count, columns); i++)
			{
				var chord = new TextBlock
				{
					Text = cells[i],
					FontSize = fontSize,
					FontWeight = FontWeight.Bold,
					TextTrimming = TextTrimming.CharacterEllipsis,
					VerticalAlignment = VerticalAlignment.Center,
				};
				chord.Bind(TextBlock.ForegroundProperty, chord.GetResourceObservable(LyriousTheme.ChordBrush));
				// The last column of the grid closes itself at its right (as in every row that fills it).
				var closesGrid = i == columns - 1;
				var cell = new Border
				{
					BorderThickness = new Thickness(2, 0, closesGrid ? 2 : 0, 0),
					Padding = new Thickness(8, 4),
					Child = chord,
					// Grey bar lines, which show on light and dark backgrounds alike.
					BorderBrush = GridLineBrush,
				};
				Grid.SetColumn(cell, i);
				grid.Children.Add(cell);
			}

			// A row with fewer columns is closed by a bar line at the left of the next column: exactly
			// where the next bar starts in longer rows, so the lines line up.
			if (cells.Count > 0 && cells.Count < columns)
			{
				var closing = new Border { BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = GridLineBrush };
				Grid.SetColumn(closing, cells.Count);
				grid.Children.Add(closing);
			}

			return grid;
		}

		/// <summary>
		/// A line with chords: a row of pieces, each a chord (or nothing) over its text. Text after a
		/// chord is split into words, the chord over the first, so a long line wraps between words.
		/// </summary>
		/// <param name="timeOf">A time tag's time (called once per tag, in order).</param>
		/// <param name="markers">Time tags on the line are added here, each at its place in the row.</param>
		/// <param name="compact">
		/// For a heading: words without a chord keep no room for one (they sit at the bottom of their
		/// line, on the line's baseline) and the words have the font's own line spacing, so a heading
		/// that wraps has its lines as close as in plain text; only a line with a chord is taller.
		/// </param>
		private static Control ChordLine(string line, double fontSize, double lineHeight, Func<string, TimeSpan?> timeOf, List<TimeMarker> markers, bool compact = false)
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
					if (timeOf(timeTag) is { } time)
					{
						var place = new Border { Width = 0, Height = compact ? 0 : chordHeight + lineHeight };
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

					var wordBlock = compact ? new TextBlock { FontSize = fontSize } : new TextBlock { FontSize = fontSize, LineHeight = lineHeight };
					SetStyledText(wordBlock, words, styles, offset, pieces[i].Length);
					offset += pieces[i].Length;
					if (chordText == null)
					{
						// Most words have no chord: just the word, where it would be under one (a long
						// song has hundreds of these, so each is one control, not three). In a compact
						// line, at the bottom of its line instead, without the room for a chord.
						if (compact)
						{
							wordBlock.VerticalAlignment = VerticalAlignment.Bottom;
						}
						else
						{
							wordBlock.Margin = new Thickness(0, chordHeight, 0, 0);
						}

						row.Children.Add(wordBlock);
						continue;
					}

					var chordBlock = new TextBlock
					{
						Text = chordText,
						FontSize = chordSize,
						FontWeight = FontWeight.Bold,
						Height = chordHeight,
						// Room after a chord, so chords over short words don't run into each other.
						Margin = new Thickness(0, 0, chordSize * 0.4, 0),
					};
					chordBlock.Bind(TextBlock.ForegroundProperty, chordBlock.GetResourceObservable(LyriousTheme.ChordBrush));
					row.Children.Add(new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = compact ? VerticalAlignment.Bottom : VerticalAlignment.Top, Children = { chordBlock, wordBlock } });
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
		/// <summary>A heading level's size (times the lyrics'), weight and opacity.</summary>
		private static (double Scale, FontWeight Weight, double Opacity) HeadingLook(int level) => Math.Clamp(level, 1, 6) switch
		{
			1 => (1.45, FontWeight.Bold, 1.0),
			2 => (1.25, FontWeight.Bold, 1.0),
			3 => (1.1, FontWeight.SemiBold, 1.0),
			4 => (1.0, FontWeight.SemiBold, 1.0),
			5 => (0.9, FontWeight.SemiBold, 0.85),
			_ => (0.8, FontWeight.SemiBold, 0.7),
		};

		private static double HeadingScale(int level) => HeadingLook(level).Scale;

		private static TextBlock Heading(int level, string text, double fontSize)
		{
			var heading = new TextBlock
			{
				FontSize = Math.Round(fontSize * HeadingScale(level)),
				TextWrapping = TextWrapping.Wrap,
			};
			SetStyledText(heading, text, Emphasis(text), 0, text.Length);
			return HeadingStyle(heading, level, fontSize);
		}

		/// <summary>
		/// Gives a heading (its text, or a line of chords over its words) the level's weight, opacity,
		/// colour and room around it. On a chord line the words take the weight and colour; the chords
		/// keep their own colour.
		/// </summary>
		private static T HeadingStyle<T>(T heading, int level, double fontSize) where T : Control
		{
			var (_, weight, opacity) = HeadingLook(level);
			heading.SetValue(TextElement.FontWeightProperty, weight);
			heading.Opacity = opacity;
			heading.Margin = new Thickness(0, fontSize * 0.5, 0, fontSize * 0.2);
			var brush = level switch { <= 1 => LyriousTheme.Heading1Brush, 2 => LyriousTheme.Heading2Brush, _ => LyriousTheme.HeadingBrush };
			heading.Bind(TextElement.ForegroundProperty, heading.GetResourceObservable(brush));
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

		/// <summary>
		/// The text with each bar mark '|' written as the time tag it stands for, "[+1]" (one bar after
		/// the previous time tag; "[1]", the start, when it's the song's first time mark), so it's handled
		/// (and hidden) like one. A '|' stays as it is in a tab
		/// block ({sot} … {eot}, where it's part of the tab), on a {directive} line, and inside a
		/// bracket tag.
		/// </summary>
		/// <summary>The bar marks between <paramref name="start"/> and <paramref name="end"/> (all of them when that's empty): how many are relative ("[+4]") and how many absolute ("[12]").</summary>
		public static (int Relative, int Absolute) CountBarMarks(string text, int start, int end)
		{
			var (relative, absolute) = (0, 0);
			foreach (var mark in Marks(text))
			{
				if (mark.IsBars && Within(mark, start, end))
				{
					if (mark.Relative)
					{
						relative++;
					}
					else
					{
						absolute++;
					}
				}
			}

			return (relative, absolute);
		}

		/// <summary>
		/// The text with its bar marks between <paramref name="start"/> and <paramref name="end"/> (all of
		/// them when that's empty) made relative ("[+4]": bars after the time mark before it) or absolute
		/// ("[12]": bar 12 from the song's start), each standing for the same place as before. Where they
		/// are is counted along the whole song: time marks ("[1:23]", with the tempo and beats per bar)
		/// and bar lines ("|", one bar each; the first is bar 1) count, but stay as they are, and so does a
		/// mark that wouldn't come out a whole number of bars (after a time mark between bars).
		/// </summary>
		public static string FlipBarMarks(string text, int start, int end, bool toRelative, double tempo, int beatsPerBar)
		{
			var barSeconds = tempo > 0 ? 60.0 / tempo * Math.Max(1, beatsPerBar) : 0;
			var changes = new List<(int At, int Length, string Text)>();
			// Where the last time mark (or bar line) is, in bars from the song's start; null before the first.
			double? previous = null;
			foreach (var mark in Marks(text))
			{
				double? place;
				if (mark.IsBarLine)
				{
					place = previous is { } before ? before + 1 : 0;
				}
				else if (mark.IsBars)
				{
					place = mark.Relative ? (previous ?? 0) + mark.Bars : Math.Max(0, mark.Bars - 1);
					if (Within(mark, start, end) && mark.Relative != toRelative)
					{
						var replaced = toRelative ? place.Value - (previous ?? 0) : place.Value + 1;
						if (Math.Abs(replaced - Math.Round(replaced)) < 1e-6 && replaced >= 0)
						{
							changes.Add((mark.At, mark.Length, toRelative ? $"[+{Math.Round(replaced)}]" : $"[{Math.Round(replaced)}]"));
						}
					}
				}
				else
				{
					// A time ("[1:23]", "[+0:08]"): in bars at the tempo (unknown without one).
					place = barSeconds > 0 && TimeOf(mark.Tag, tempo, beatsPerBar, previous is { } before2 ? TimeSpan.FromSeconds(before2 * barSeconds) : null) is { } time
						? time.TotalSeconds / barSeconds
						: null;
				}

				previous = place ?? previous;
			}

			var result = new System.Text.StringBuilder(text);
			foreach (var (at, length, replacement) in changes.OrderByDescending(change => change.At))
			{
				result.Remove(at, length).Insert(at, replacement);
			}

			return result.ToString();
		}

		/// <summary>A time mark ("[1:23]", "[12]", "[+4]") or a bar line ("|") in the text, where it is and what it is.</summary>
		private sealed record Mark(int At, int Length, string Tag, bool IsBarLine)
		{
			public bool Relative => Tag.StartsWith('+');

			/// <summary>Whether it counts bars ("[12]", "[+4]"), not a time.</summary>
			public bool IsBars => !IsBarLine && !Tag.Contains(':') && int.TryParse(Tag.TrimStart('+'), out _);

			public int Bars => int.TryParse(Tag.TrimStart('+'), out var bars) ? bars : 0;
		}

		/// <summary>Whether a mark lies between two places in the text (anywhere when they're the same).</summary>
		private static bool Within(Mark mark, int start, int end)
			=> start == end || (mark.At >= Math.Min(start, end) && mark.At + mark.Length <= Math.Max(start, end));

		/// <summary>The text's time marks and bar lines, in order (not inside tab blocks; a '|' inside a tag isn't a bar line).</summary>
		private static IEnumerable<Mark> Marks(string text)
		{
			var inTab = false;
			var lineStart = 0;
			while (lineStart <= text.Length)
			{
				var lineEnd = text.IndexOf('\n', lineStart);
				if (lineEnd < 0)
				{
					lineEnd = text.Length;
				}

				var line = text[lineStart..lineEnd].TrimEnd('\r');
				if (Directives(line) is { } directives)
				{
					foreach (var (name, _) in directives)
					{
						inTab = name is "start_of_tab" or "sot" || (inTab && name is not ("end_of_tab" or "eot"));
					}
				}
				else if (!inTab)
				{
					for (var at = 0; at < line.Length; at++)
					{
						if (line[at] == '[' && line.IndexOf(']', at) is var close and >= 0)
						{
							var tag = line[(at + 1)..close];
							if (IsTimeTag(tag))
							{
								yield return new Mark(lineStart + at, close - at + 1, tag.Trim(), false);
							}

							at = close;
						}
						else if (line[at] == '|')
						{
							yield return new Mark(lineStart + at, 1, "|", true);
						}
					}
				}

				lineStart = lineEnd + 1;
			}
		}

		private static string ExpandBarMarks(string text)
		{
			if (!text.Contains('|'))
			{
				return text;
			}

			var lines = text.Replace("\r\n", "\n").Split('\n');
			var inTab = false;
			// Whether a time tag (or bar mark) came before: until one does, a bar mark is the start.
			var timed = false;
			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				if (Directives(line) is { } directives)
				{
					foreach (var (name, _) in directives)
					{
						if (name is "start_of_tab" or "sot")
						{
							inTab = true;
						}
						else if (name is "end_of_tab" or "eot")
						{
							inTab = false;
						}
					}

					continue;
				}

				if (inTab)
				{
					continue;
				}

				if (!line.Contains('|'))
				{
					timed |= BracketTag().Matches(line).Any(tag => IsTimeTag(tag.Groups[1].Value));
					continue;
				}

				// Outside bracket tags only: a '|' inside one (e.g. a chord "[G|D]") is left alone.
				var expanded = new System.Text.StringBuilder(line.Length + 8);
				var inTag = false;
				for (var at = 0; at < line.Length; at++)
				{
					var c = line[at];
					// A "[" only opens a tag when a "]" closes it later on the line (else it's text).
					if (c == '[' && !inTag && line.IndexOf(']', at) is var close and >= 0)
					{
						inTag = true;
						timed |= IsTimeTag(line[(at + 1)..close]);
					}
					else if (c == ']')
					{
						inTag = false;
					}

					if (c == '|' && !inTag)
					{
						// The song's first time mark, if it's a bar mark, is where the song starts: bar 1.
						expanded.Append(timed ? "[+1]" : "[1]");
						timed = true;
					}
					else
					{
						expanded.Append(c);
					}
				}

				lines[i] = expanded.ToString();
			}

			return string.Join('\n', lines);
		}

		/// <summary>A time tag: an integer (bars), or numbers with ':' (minutes:seconds, perhaps hours and tenths).</summary>
		[GeneratedRegex(@"^\+?\d+$|^\+?\d+(:\d+(\.\d+)?)+$")]
		private static partial Regex TimeTag();

		/// <summary>
		/// A Markdown heading line: one to six '#', then the heading, with or without a space between
		/// ("# Verse" or "#Verse"; the heading doesn't start with another '#'). Hidden tags (time tags,
		/// "midi:" tags) may come before the '#', e.g. "[0:12]# Verse".
		/// </summary>
		[GeneratedRegex(@"^\s*(?:\[\s*(?:\+?\d|[Mm][Ii][Dd][Ii]:)[^\]\n]*\]\s*)*(#{1,6})\s*([^#\s](?:.*\S)?)\s*$", RegexOptions.Multiline)]
		private static partial Regex HeadingLine();

		/// <summary>
		/// A heading's text in its parts, each with its level: further headings on the same line start
		/// with one to six '#' after a space ("Title ####tags" is "Title" at the heading's level and
		/// "tags" at level 4). '#' right after another character (e.g. "C#") isn't one. A '#' run with
		/// nothing after it (e.g. "Title ####" when the tags were empty) is left out.
		/// </summary>
		private static List<(int Level, string Text)> HeadingParts(int level, string text)
		{
			var parts = new List<(int Level, string Text)>();
			var marks = InlineHeading().Matches(text);
			var start = 0;
			var partLevel = level;
			foreach (Match mark in marks)
			{
				Add(partLevel, text[start..mark.Index]);
				partLevel = mark.Groups[1].Length;
				start = mark.Index + mark.Length;
			}

			Add(partLevel, text[start..]);
			return parts;

			void Add(int partLevel, string partText)
			{
				// Without a '#' run at its end that has nothing after it.
				partText = TrailingHashes().Replace(partText, "").Trim();
				if (partText.Length > 0)
				{
					parts.Add((partLevel, partText));
				}
			}
		}

		/// <summary>A further heading on a heading line: a space, one to six '#' (group 1), the spaces after them, and then text.</summary>
		[GeneratedRegex(@"\s(#{1,6})\s*(?=[^#\s])")]
		private static partial Regex InlineHeading();

		/// <summary>'#'s at the end of a heading part with nothing after them (after a space, or the part is only them).</summary>
		[GeneratedRegex(@"(?:^|\s)#+\s*$")]
		private static partial Regex TrailingHashes();

		/// <summary>Hidden tags (time tags, "midi:" tags) at the start of a line, and the spaces after them.</summary>
		[GeneratedRegex(@"^(?:\[\s*(?:\+?\d|[Mm][Ii][Dd][Ii]:)[^\]\n]*\]\s*)+")]
		private static partial Regex LeadingTimeTags();

		/// <summary>
		/// A bracket tag: from a "[" to the next "]" on the same line. A "[" inside belongs to the tag,
		/// e.g. "[midi:[midi:$MODX, PC20@1]" is one tag (starting with "midi:", so not a chord).
		/// </summary>
		[GeneratedRegex(@"\[([^\]\n]*)\]")]
		private static partial Regex BracketTag();

		/// <summary>The value of the first of these directives in the text (e.g. "artist" for "{artist: The Beatles}"); null if none.</summary>
		public static string? DirectiveValue(string text, params string[] names)
		{
			foreach (Match line in DirectiveLine().Matches(text))
			{
				foreach (var (name, value) in Directives(line.Value.Trim()) ?? [])
				{
					if (names.Contains(name, StringComparer.OrdinalIgnoreCase) && value.Length > 0)
					{
						return value;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// The directives of a line made of directives only ("{c: Intro}", or several: "{c: Intro} {c: x2}"),
		/// in order, each name in lower case with its value; null when the line is something else.
		/// </summary>
		private static List<(string Name, string Value)>? Directives(string line)
		{
			if (!DirectiveLine().IsMatch(line))
			{
				return null;
			}

			return InlineDirective().Matches(line)
				.Select(directive => (directive.Groups[1].Value.ToLowerInvariant(), directive.Groups[2].Value.Trim()))
				.ToList();
		}

		/// <summary>A line of directives only: one or more {name} or {name: value}, with spaces between them.</summary>
		[GeneratedRegex(@"^[ \t]*(?:\{[A-Za-z_]+(?:[: \t][^{}\n]*)?\}[ \t]*)+\r?$", RegexOptions.Multiline)]
		private static partial Regex DirectiveLine();

		/// <summary>One directive in a line: {name} or {name: value}.</summary>
		[GeneratedRegex(@"\{([A-Za-z_]+)(?:[: \t][ \t]*([^{}\n]*?))?\}")]
		private static partial Regex InlineDirective();

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
