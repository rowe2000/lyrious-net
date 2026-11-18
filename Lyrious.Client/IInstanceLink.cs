using System;

namespace Lyrious.Client;

/// <summary>
/// The other instances of the app that share this one's store: in the browser, the tabs and
/// windows open on the same address (they share its stored settings and cache). Through this they pass each
/// other the changes made in them, so they show the same data without the server, and agree on
/// the one that sends the changes to the server (the owner), so no change is sent twice. Hosts
/// where instances share a store supply one to <see cref="HttpSetlistService"/>; without one the
/// app is taken to be alone.
/// </summary>
public interface IInstanceLink
{
    /// <summary>
    /// Joins the instances that use the same name (a member's cache), leaving the ones joined
    /// before.
    /// </summary>
    /// <param name="received">A message another instance posted (never one's own).</param>
    /// <param name="becameOwner">
    /// This instance is now the owner: at once when no other is, else when the owner goes away
    /// (its tab is closed, or it leaves).
    /// </param>
    void Join(string name, Action<string> received, Action becameOwner);

    void Leave();

    /// <summary>Whether this instance is the one that sends the changes to the server.</summary>
    bool IsOwner { get; }

    /// <summary>Sends a message to the other instances; nothing when not joined.</summary>
    void Post(string message);
}
