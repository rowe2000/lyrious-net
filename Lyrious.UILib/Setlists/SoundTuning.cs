using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Speed and pitch for sound files (see <see cref="ISoundStore.Speed"/> and
	/// <see cref="ISoundStore.Pitch"/>): a slider for each, changing one without the other, and
	/// Reset. Changes are heard at once.
	/// </summary>
	public static class SoundTuning
	{
		public const double MinSpeed = 0.5;
		public const double MaxSpeed = 1.5;
		public const double MinPitch = -12;
		public const double MaxPitch = 12;

		/// <summary>"80 %".</summary>
		public static string DescribeSpeed(double speed) => $"{Math.Round(speed * 100)} %";

		/// <summary>"+2 semitones", "-1 semitone", "As recorded".</summary>
		public static string DescribePitch(double pitch)
		{
			var steps = (int)Math.Round(pitch);
			return steps == 0 ? L.T("As recorded") : $"{steps:+0;-0} semitone{(Math.Abs(steps) == 1 ? "" : "s")}";
		}

		/// <summary>The sliders for the sounds' speed and pitch (those the store can change).</summary>
		public static Control Create(ISoundStore sounds)
		{
			var panel = new StackPanel { Spacing = 10, Width = 280 };
			var speedText = new TextBlock();
			var pitchText = new TextBlock();
			var speed = new Slider
			{
				Minimum = MinSpeed,
				Maximum = MaxSpeed,
				SmallChange = 0.05,
				LargeChange = 0.1,
				TickFrequency = 0.05,
				IsSnapToTickEnabled = true,
				Value = Math.Clamp(sounds.Speed, MinSpeed, MaxSpeed),
			};
			var pitch = new Slider
			{
				Minimum = MinPitch,
				Maximum = MaxPitch,
				SmallChange = 1,
				LargeChange = 2,
				TickFrequency = 1,
				IsSnapToTickEnabled = true,
				Value = Math.Clamp(Math.Round(sounds.Pitch), MinPitch, MaxPitch),
			};

			void Show()
			{
				speedText.Text = L.T("Speed: {0}", DescribeSpeed(speed.Value));
				pitchText.Text = L.T("Pitch: {0}", DescribePitch(pitch.Value));
			}

			speed.ValueChanged += (_, _) =>
			{
				sounds.Speed = speed.Value;
				Show();
			};
			pitch.ValueChanged += (_, _) =>
			{
				sounds.Pitch = pitch.Value;
				Show();
			};
			Show();

			if (sounds.CanChangeSpeed)
			{
				panel.Children.Add(speedText);
				panel.Children.Add(speed);
			}

			if (sounds.CanChangePitch)
			{
				panel.Children.Add(pitchText);
				panel.Children.Add(pitch);
			}

			var reset = new Button { Content = L.T("Reset"), HorizontalAlignment = HorizontalAlignment.Right };
			reset.Click += (_, _) =>
			{
				speed.Value = 1;
				pitch.Value = 0;
			};
			panel.Children.Add(reset);
			panel.Children.Add(new TextBlock
			{
				Text = L.T("Speed changes the tempo, not the key; pitch changes the key, not the tempo."),
				Opacity = 0.7,
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
			});
			return panel;
		}
	}
}
