using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// The size of the whole app's interface (Settings → Display), e.g. 1.25 for 125 %. Kept on this
	/// device (<see cref="App.Settings"/>); a change shows at once. The app's content is scaled as a
	/// whole (<see cref="Wrap"/>), so text, icons and spacing all grow or shrink together and the
	/// layout reflows to the window. Popups (menus, tooltips) keep their own size.
	/// </summary>
	public static class UiScale
	{
		/// <summary>The sizes offered.</summary>
		public static readonly IReadOnlyList<double> Choices = [0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

		private static readonly List<WeakReference<LayoutTransformControl>> roots = [];

		/// <summary>The interface size; 1 is normal.</summary>
		public static double Value
		{
			get => double.TryParse(App.Settings.Get(SettingKeys.UiScale), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
			       && scale is >= 0.5 and <= 3
				? scale
				: 1.0;
			set
			{
				var scale = Math.Clamp(value, 0.5, 3);
				App.Settings.Set(SettingKeys.UiScale, Math.Abs(scale - 1) < 0.001 ? null : scale.ToString(CultureInfo.InvariantCulture));
				Apply();
			}
		}

		/// <summary>"125%".</summary>
		public static string Describe(double scale) => $"{Math.Round(scale * 100)}%";

		/// <summary>Puts the app's content in a scaled box that follows <see cref="Value"/>.</summary>
		public static Control Wrap(Control content)
		{
			var root = new LayoutTransformControl { Child = content };
			roots.Add(new WeakReference<LayoutTransformControl>(root));
			ApplyTo(root, Value);
			return root;
		}

		private static void Apply()
		{
			var scale = Value;
			roots.RemoveAll(reference => !reference.TryGetTarget(out _));
			foreach (var reference in roots)
			{
				if (reference.TryGetTarget(out var root))
				{
					ApplyTo(root, scale);
				}
			}
		}

		private static void ApplyTo(LayoutTransformControl root, double scale)
			=> root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);
	}
}
