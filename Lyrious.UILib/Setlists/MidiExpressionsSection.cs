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
	/// A band's MIDI expressions on its card in Bands (see <see cref="MidiExpression"/>): each one's
	/// use in lyrics ("$MODX.bank.program"), description and output. An admin adds, changes and
	/// deletes them; the others see them. Editing shows, as it's typed, an example and the messages
	/// it sends, or what's wrong; Save only when nothing is.
	/// </summary>
	public sealed class MidiExpressionsSection : UserControl
	{
		private readonly List<MidiExpression> expressions;
		private readonly bool canEdit;
		private readonly Action<IReadOnlyList<MidiExpression>> save;
		private readonly StackPanel rows = new() { Spacing = 8 };

		/// <param name="canEdit">Whether the member is an admin of the band (only they change its expressions).</param>
		/// <param name="save">Called with all the band's expressions after one is added, changed or deleted.</param>
		public MidiExpressionsSection(IReadOnlyList<MidiExpression> expressions, bool canEdit, Action<IReadOnlyList<MidiExpression>> save)
		{
			this.expressions = expressions.ToList();
			this.canEdit = canEdit;
			this.save = save;
			Content = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					new TextBlock { Text = L.T("MIDI expressions"), FontWeight = FontWeight.SemiBold },
					new TextBlock
					{
						Text = L.T("Shortcuts for MIDI commands in the lyrics, e.g. [midi:$MODX.0.20]: the name, then the values of its parameters. ")
						       + L.T("They're sent to every MIDI output when a song opens.")
						       + (canEdit ? "" : L.T(" Only the band's admins change them.")),
						TextWrapping = TextWrapping.Wrap,
						Opacity = 0.75,
					},
					rows,
				},
			};
			ShowRows();
		}

		private void ShowRows()
		{
			rows.Children.Clear();
			if (expressions.Count == 0)
			{
				rows.Children.Add(new TextBlock { Text = L.T("None yet."), Opacity = 0.6 });
			}

			for (var i = 0; i < expressions.Count; i++)
			{
				rows.Children.Add(Row(i));
			}

			if (canEdit)
			{
				var add = new Button { Content = L.T("Add expression"), HorizontalAlignment = HorizontalAlignment.Left };
				add.Click += (_, _) => ShowEditor(null, add);
				rows.Children.Add(add);
			}
		}

		/// <summary>An expression: its use, description and output; Edit and Delete for an admin.</summary>
		private Control Row(int index)
		{
			var expression = expressions[index];
			var info = new StackPanel
			{
				Spacing = 2,
				VerticalAlignment = VerticalAlignment.Center,
				Children = { new SelectableTextBlock { Text = expression.Usage, FontWeight = FontWeight.SemiBold, FontFamily = FontFamily.Parse("monospace") } },
			};
			if (!string.IsNullOrWhiteSpace(expression.Description))
			{
				info.Children.Add(new TextBlock { Text = expression.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
			}

			info.Children.Add(new SelectableTextBlock { Text = expression.Output, FontFamily = FontFamily.Parse("monospace"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });

			var row = new DockPanel();
			if (canEdit)
			{
				var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
				var edit = new Button { Content = L.T("Edit"), VerticalAlignment = VerticalAlignment.Center };
				edit.Click += (_, _) => ShowEditor(index, row);
				tools.Children.Add(edit);
				tools.Children.Add(PageLayout.DeleteButton(
					L.T("Delete ${0}", expression.Name),
					L.T("Delete the MIDI expression ${0}? Lyrics that use it send nothing for it any more.", expression.Name),
					() =>
					{
						expressions.RemoveAt(index);
						save(expressions.ToList());
						ShowRows();
					}));
				DockPanel.SetDock(tools, Dock.Right);
				row.Children.Add(tools);
			}

			row.Children.Add(info);
			return row;
		}

		/// <summary>The editor in place of <paramref name="replaced"/>: a new expression (index null) or the one at <paramref name="index"/>.</summary>
		private void ShowEditor(int? index, Control replaced)
		{
			var existing = index is { } at ? expressions[at] : null;
			var name = new TextBox { Text = existing?.Name, PlaceholderText = "MODX" };
			var description = new TextBox { Text = existing?.Description, PlaceholderText = L.T("Keyboard performance (optional)") };
			var parameters = new TextBox { Text = existing?.ParametersText, PlaceholderText = L.T("bank=0, program") };
			var output = new TextBox { Text = existing?.Output, PlaceholderText = L.T("MSB63 LSB{bank} PC{program}@1"), FontFamily = FontFamily.Parse("monospace") };
			var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
			var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));
			var saveButton = new Button { Content = L.T("Save"), Classes = { "accent" } };
			// Escape cancels this edit, before the page's ✕ (which would leave the band page).
			var cancel = EscapeKey.MarkAsClose(new Button { Content = L.T("Cancel") }, first: true);
			MidiExpression? valid = null;

			// The others: the one being changed isn't among them (it may keep its name).
			var others = expressions.Where((_, i) => i != index).ToList();

			void Check()
			{
				valid = null;
				string? why;
				var parsed = Midi.ParseParameters(parameters.Text, out why);
				var candidate = parsed == null ? null : new MidiExpression(name.Text?.Trim() ?? "", description.Text?.Trim() ?? "", parsed, output.Text?.Trim() ?? "");
				if (candidate != null)
				{
					why = Midi.Check(candidate, others);
				}

				if (why == null && candidate != null)
				{
					valid = candidate;
					var example = Midi.Example(candidate);
					var messages = Midi.Parse(example, others.Append(candidate).ToList());
					preview.Text = L.T("Used as {0}. For example [midi:{1}] sends: {2}.", candidate.Usage, example, string.Join("; ", messages.Select(Midi.Describe)));
				}
				else
				{
					preview.Text = "";
				}

				// Nothing to point out until something is typed.
				var typed = !string.IsNullOrWhiteSpace(name.Text) || !string.IsNullOrWhiteSpace(output.Text) || !string.IsNullOrWhiteSpace(parameters.Text);
				problem.Text = why;
				problem.IsVisible = why != null && typed;
				saveButton.IsEnabled = valid != null;
			}

			foreach (var box in new[] { name, description, parameters, output })
			{
				box.TextChanged += (_, _) => Check();
			}

			var editor = new Border
			{
				Padding = new Thickness(12),
				CornerRadius = new CornerRadius(6),
				BorderThickness = new Thickness(1),
				BorderBrush = new SolidColorBrush(Colors.Gray, 0.5),
				Child = new StackPanel
				{
					Spacing = 8,
					Children =
					{
						Field(L.T("Name"), L.T("Used in the lyrics after $, e.g. $MODX."), name),
						Field(L.T("Description"), null, description),
						Field(L.T("Parameters"), L.T("In order, separated by commas; name=value gives a default, so the value can be left out."), parameters),
						Field(L.T("Output"), L.T("MIDI commands with {parameter} for each value: PC (program), CC number:value, MSB and LSB (bank), @channel; or other expressions."), output),
						preview,
						problem,
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveButton, cancel } },
					},
				},
			};

			saveButton.Click += (_, _) =>
			{
				if (valid is not { } expression)
				{
					return;
				}

				if (index is { } at)
				{
					expressions[at] = expression;
				}
				else
				{
					expressions.Add(expression);
				}

				save(expressions.ToList());
				ShowRows();
			};
			cancel.Click += (_, _) => ShowRows();

			var position = rows.Children.IndexOf(replaced);
			rows.Children[position] = editor;
			Check();
			name.Focus();
		}

		private static Control Field(string label, string? hint, TextBox box)
		{
			var panel = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold } } };
			if (hint != null)
			{
				panel.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 13 });
			}

			panel.Children.Add(box);
			return panel;
		}
	}
}
