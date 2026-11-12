using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// Shared state for the account forms: change notification, busy state, and an error or
	/// success message.
	/// </summary>
	public abstract class FormViewModel : INotifyPropertyChanged
	{
		private bool isBusy;
		private string? errorMessage;
		private string? successMessage;

		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsBusy
		{
			get => isBusy;
			private set => Set(ref isBusy, value, nameof(CanSubmit));
		}

		public string? ErrorMessage
		{
			get => errorMessage;
			protected set => Set(ref errorMessage, value);
		}

		public string? SuccessMessage
		{
			get => successMessage;
			protected set => Set(ref successMessage, value);
		}

		/// <summary>Whether the form is filled in enough to submit.</summary>
		public bool CanSubmit => !IsBusy && IsComplete;

		protected abstract bool IsComplete { get; }

		/// <summary>Runs a submit action with busy state and turns an unreachable server into a message.</summary>
		protected async Task RunAsync(Func<Task> action)
		{
			if (!CanSubmit)
			{
				return;
			}

			IsBusy = true;
			ErrorMessage = null;
			SuccessMessage = null;
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				ErrorMessage = $"Couldn't reach the server. {ex.Message}";
			}
			finally
			{
				IsBusy = false;
			}
		}

		/// <summary>Sets a field and notifies; also notifies <see cref="CanSubmit"/> and any extra properties.</summary>
		protected void Set<T>(ref T field, T value, string? alsoNotify = null, [CallerMemberName] string? propertyName = null)
		{
			if (Equals(field, value))
			{
				return;
			}

			field = value;
			OnPropertyChanged(propertyName);
			OnPropertyChanged(nameof(CanSubmit));
			if (alsoNotify != null)
			{
				OnPropertyChanged(alsoNotify);
			}
		}

		protected void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
