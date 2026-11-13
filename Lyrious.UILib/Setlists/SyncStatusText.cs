using System;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The sync status in words, for the drawer's Sync button and the Sync page.</summary>
	public static class SyncStatusText
	{
		/// <summary>One short line, e.g. "Offline · 3 changes waiting" or "Synced 10:42".</summary>
		public static string Short(SyncStatus status)
		{
			var waiting = status.PendingChanges switch
			{
				0 => null,
				1 => "1 change waiting",
				var n => $"{n} changes waiting",
			};

			if (!status.Online)
			{
				return waiting == null ? "Offline" : $"Offline · {waiting}";
			}

			if (waiting != null)
			{
				return $"Syncing · {waiting}";
			}

			return status.LastSynced is { } synced ? $"Synced {When(synced)}" : "Not synced yet";
		}

		/// <summary>A fuller explanation for the Sync page.</summary>
		public static string Long(SyncStatus status)
		{
			var text = status.Online
				? "Connected to the server. Changes are sent as you make them, and others' changes arrive as they happen."
				: "The server can't be reached, so you're working offline from the copy on this device. Changes are kept and sent when the server is back (it's tried every few seconds).";

			if (status.PendingChanges > 0)
			{
				text += status.PendingChanges == 1 ? " 1 change is waiting to be sent." : $" {status.PendingChanges} changes are waiting to be sent.";
			}

			text += status.LastSynced is { } synced ? $" Last full sync: {When(synced)}." : " This device hasn't synced yet.";
			return text;
		}

		private static string When(DateTime time) => time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("yyyy-MM-dd HH:mm");
	}
}
