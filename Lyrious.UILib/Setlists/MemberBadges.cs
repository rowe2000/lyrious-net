using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's members as small circles with their initials (the name and status as the tip), coloured
	/// by status: green while connected with this band joined, yellow while connected with another band
	/// joined, in the text colour when this band is joined but they're not connected, else gray. In that
	/// order; more than fit end with "+N", which names the rest.
	/// </summary>
	public sealed class MemberBadges : StackPanel
	{
		private const int MostShown = 6;

		public MemberBadges()
		{
			Orientation = Orientation.Horizontal;
			Spacing = 4;
			VerticalAlignment = VerticalAlignment.Center;
			IsVisible = false;
		}

		/// <summary>Shows these members (none hides the badges).</summary>
		public void Show(IReadOnlyList<GroupMember> members)
		{
			Children.Clear();
			var ordered = members
				.OrderBy(Rank)
				.ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
			var shown = ordered.Count > MostShown ? MostShown - 1 : ordered.Count;
			foreach (var member in ordered.Take(shown))
			{
				Children.Add(Badge(Initials(member.Name), Tip(member), Rank(member)));
			}

			if (shown < ordered.Count)
			{
				var rest = ordered.Skip(shown).ToList();
				Children.Add(Badge($"+{rest.Count}", string.Join("\n", rest.Select(Tip)), rest.Min(Rank)));
			}

			IsVisible = ordered.Count > 0;
			Avalonia.Automation.AutomationProperties.SetName(this, string.Join(", ", ordered.Select(Tip)));
		}

		// 0: connected, this band joined (green); 1: connected, another band joined (yellow); 2: this band
		// joined, not connected (text colour); 3: the rest (gray).
		private static int Rank(GroupMember member) => member.Connected ? member.Joined ? 0 : 1 : member.Joined ? 2 : 3;

		private static string Tip(GroupMember member) => member.Connected
			? member.Joined ? L.T("{0}: playing with the band now", member.Name) : L.T("{0}: online, not with the band right now", member.Name)
			: member.Joined ? L.T("{0}: with the band, offline", member.Name) : L.T("{0}: offline", member.Name);

		private static Control Badge(string text, string tip, int rank)
		{
			var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			var badge = new Border
			{
				Width = 28,
				Height = 28,
				CornerRadius = new CornerRadius(14),
				BorderThickness = new Thickness(rank == 3 ? 1 : 2),
				VerticalAlignment = VerticalAlignment.Center,
				Opacity = rank == 3 ? 0.45 : 1,
				Child = label,
			};
			// Yellow is the theme's warning colour (darker in the light theme, so it reads on white); gray
			// is the text colour dimmed (above), so it works in both themes.
			var brush = rank switch { 0 => LyriousTheme.SuccessBrush, 1 => LyriousTheme.WarningBrush, _ => LyriousTheme.ForegroundBrush };
			badge.Bind(Border.BorderBrushProperty, badge.GetResourceObservable(brush));
			label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(brush));
			ToolTip.SetTip(badge, tip);
			return badge;
		}

		/// <summary>
		/// A name's initials: the first letters of its first and last words ("Anna Maria Berg" → "AB");
		/// of a single word, its first two letters ("Anna" → "An").
		/// </summary>
		public static string Initials(string name)
		{
			var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 0)
			{
				return "?";
			}

			if (words.Length == 1)
			{
				var word = words[0];
				return word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word.Substring(1, 1).ToLowerInvariant();
			}

			return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}";
		}
	}
}
