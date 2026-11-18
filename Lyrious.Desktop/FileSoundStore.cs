using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Lyrious.UILib;

namespace Lyrious.Desktop;

/// <summary>
/// Sound files for songs' media links, in the "sounds" folder next to the settings (in the user's
/// application data folder). They're played in the computer's own player for the file type.
/// </summary>
internal sealed class FileSoundStore : ISoundStore
{
    private readonly string folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lyrious", "sounds");

    // The computer's player plays it, not the app: nothing to show as playing or to stop.
    public string? Playing => null;

    public event Action? PlayingChanged { add { } remove { } }

    public async Task SaveAsync(string id, Stream content)
    {
        Directory.CreateDirectory(folder);
        var path = PathOf(id);
        // Written next to it first, so a failure halfway leaves no half a file under the id.
        var partial = path + ".part";
        try
        {
            await using (var file = File.Create(partial))
            {
                await content.CopyToAsync(file);
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (IOException ex)
        {
            File.Delete(partial);
            throw new IOException($"It couldn't be written to {folder}. {ex.Message}", ex);
        }
    }

    public Task<bool> PlayAsync(string id)
    {
        var path = PathOf(id);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        // The player the computer opens this kind of file with.
        using var player = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.FromResult(true);
    }

    public void Stop()
    {
    }

    /// <summary>The file of a sound: just its name in the folder, whatever the id says.</summary>
    private string PathOf(string id) => Path.Combine(folder, Path.GetFileName(id));
}
