using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// The size of the whole app's interface (Settings → Display), e.g. 1.25 for 125 %. Kept on this
	/// device (<see cref="App.Settings"/>); a change shows at once. The app's content is scaled as a
	/// whole (<see cref="Wrap"/>), so text, icons and spacing all grow or shrink together and the
	/// layout reflows to the window. Popups (confirmations, menus, dropdown lists, tooltips) are shown
	/// outside the app's content, so each popup's content is put in a scaled box of its own
	/// (<see cref="RegisterPopups"/>).
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

		/// <summary>Called once at startup: every popup's content is scaled too, from then on.</summary>
		public static void RegisterPopups()
		{
			Popup.ChildProperty.Changed.AddClassHandler<Popup>((popup, e) =>
			{
				// A popup that takes on its target's scaling already (e.g. a dropdown list) would be
				// scaled twice.
				if (e.NewValue is not Control child || child is ScaledPopupContent || popup.InheritsTransform)
				{
					return;
				}

				// At once, before the popup opens (popups get their content first). The empty box takes
				// the content's place in the popup, then the content goes into the box.
				var box = Track(new ScaledPopupContent());
				popup.Child = box;
				box.Child = child;
			});
		}

		/// <summary>The scaled box a popup's content is put in.</summary>
		private sealed class ScaledPopupContent : LayoutTransformControl;

		/// <summary>"125%".</summary>
		public static string Describe(double scale) => $"{Math.Round(scale * 100)}%";

		/// <summary>
		/// Puts the app's content in a scaled box that follows <see cref="Value"/> and, with the
		/// dynamic zoom on, the window's size. The box fills the window and isn't scaled itself, so
		/// its size is the window's.
		/// </summary>
		public static Control Wrap(Control content)
		{
			var root = new LayoutTransformControl { Child = content };
			root.SizeChanged += (_, e) =>
			{
				windowSize = e.NewSize;
				Apply();
			};
			return Track(root);
		}

		// The window's size, for the dynamic zoom; nothing until the app's content has been laid out.
		private static Size windowSize;

		static UiScale() => Setlists.DisplaySettings.DynamicZoomChanged += Apply;

		/// <summary>
		/// The scale shown: <see cref="Value"/>, times the dynamic zoom's factor when that is on
		/// (<see cref="Setlists.DisplaySettings.DynamicZoom"/>): the smaller of the window's width and
		/// height, where <see cref="Setlists.DisplaySettings.DynamicZoomSize"/> is 1.
		/// </summary>
		private static double Effective
		{
			get
			{
				var smaller = Math.Min(windowSize.Width, windowSize.Height);
				var dynamic = Setlists.DisplaySettings.DynamicZoom && smaller > 0
					? Math.Clamp(smaller / Setlists.DisplaySettings.DynamicZoomSize, 0.5, 4)
					: 1;
				return Value * dynamic;
			}
		}

		/// <summary>Scales the box now and whenever the scale changes (while it's alive).</summary>
		private static T Track<T>(T root) where T : LayoutTransformControl
		{
			roots.RemoveAll(reference => !reference.TryGetTarget(out _));
			roots.Add(new WeakReference<LayoutTransformControl>(root));
			ApplyTo(root, Effective);
			return root;
		}

		private static void Apply()
		{
			var scale = Effective;
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
		{
			// Left alone when unchanged: resizing the window asks on every step.
			var shown = root.LayoutTransform is ScaleTransform current ? current.ScaleX : 1;
			if (Math.Abs(scale - shown) >= 0.001)
			{
				root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);
			}
		}
	}
}
