using System;
using Lyrious.UILib.Localization;

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
				1 => L.T("1 change waiting"),
				var n => L.T("{0} changes waiting", n),
			};

			if (!status.Online)
			{
				return waiting == null ? L.T("Offline") : L.T("Offline · {0}", waiting);
			}

			if (waiting != null)
			{
				return L.T("Syncing · {0}", waiting);
			}

			return status.LastSynced is { } synced ? $"Synced {When(synced)}" : L.T("Not synced yet");
		}

		/// <summary>A fuller explanation for the Sync page.</summary>
		public static string Long(SyncStatus status)
		{
			var text = status.Online
				? L.T("Connected to the server. Changes are sent as you make them, and others' changes arrive as they happen.")
				: L.T("The server can't be reached, so you're working offline from the copy on this device. Changes are kept and sent when the server is back (it's tried every few seconds).");

			if (status.PendingChanges > 0)
			{
				text += status.PendingChanges == 1 ? L.T(" 1 change is waiting to be sent.") : L.T(" {0} changes are waiting to be sent.", status.PendingChanges);
			}

			text += status.LastSynced is { } synced ? L.T(" Last full sync: {0}.", When(synced)) : L.T(" This device hasn't synced yet.");
			return text;
		}

		private static string When(DateTime time) => time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("yyyy-MM-dd HH:mm");
	}
}
