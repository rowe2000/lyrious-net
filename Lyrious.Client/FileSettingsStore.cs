using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lyrious.UILib;

namespace Lyrious.Client;

/// <summary>
/// Settings in a JSON file in the user's application data folder (on macOS
/// ~/Library/Application Support/Lyrious/; on iOS the app's own Library folder). Used by the
/// desktop and mobile apps. A missing or unreadable file means no settings.
/// Thread-safe (the offline cache is saved in the background): one read or write of a file at a
/// time, and a write replaces the file whole, so it's never seen half written.
/// </summary>
/// <param name="fileName">E.g. settings.json, or offline.json for the offline cache and logins.</param>
public sealed class FileSettingsStore(string fileName = "settings.json") : ISettingsStore
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lyrious", fileName);

    private static readonly Dictionary<string, object> Locks = [];

    private object Gate
    {
        get
        {
            lock (Locks)
            {
                return Locks.TryGetValue(path, out var gate) ? gate : Locks[path] = new object();
            }
        }
    }

    public string? Get(string key)
    {
        lock (Gate)
        {
            return Read().TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        lock (Gate)
        {
            Write(key, value);
        }
    }

    private void Write(string key, string? value)
    {
        var values = Read();
        if (value == null)
        {
            values.Remove(key);
        }
        else
        {
            values[key] = value;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(values, SettingsJsonContext.Default.DictionaryStringString));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember a setting isn't worth interrupting the app for.
            System.Diagnostics.Debug.WriteLine($"Couldn't save settings to {path}: {ex.Message}");
        }
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.DictionaryStringString) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
