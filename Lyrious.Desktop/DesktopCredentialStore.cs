using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Lyrious.UILib.Login;

namespace Lyrious.Desktop;

/// <summary>
/// Saves the login in the operating system's password store: the Keychain on macOS, Credential
/// Manager on Windows, and the Secret Service (through <c>secret-tool</c>) on Linux. One login is
/// kept, as JSON. The password is never passed on a command line, where other processes could see it.
/// </summary>
internal static partial class DesktopCredentialStore
{
    private const string Service = "Lyrious";
    private const string Account = "login";

    /// <summary>The store for this operating system, or one that offers nothing where none is available.</summary>
    public static ICredentialStore Create()
    {
        if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/security"))
        {
            return new MacKeychainStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsLinux() && FindOnPath("secret-tool") is { } secretTool)
        {
            return new SecretToolStore(secretTool);
        }

        return new NoCredentialStore();
    }

    private static string? FindOnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, program))
            .FirstOrDefault(File.Exists);

    private sealed record StoredLogin(string UserName, string Password);

    private static string Serialize(SavedCredential credential)
        => JsonSerializer.Serialize(new StoredLogin(credential.UserName, credential.Password), StoredLoginContext.Default.StoredLogin);

    private static SavedCredential? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var login = JsonSerializer.Deserialize(json, StoredLoginContext.Default.StoredLogin);
            return login is { UserName.Length: > 0 } ? new SavedCredential(login.UserName, login.Password) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Runs a program with <paramref name="input"/> on stdin; returns its exit code and stdout.</summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(string program, string[] args, string? input)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {program}.");
        if (input != null)
        {
            await process.StandardInput.WriteAsync(input);
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output);
    }

    /// <summary>
    /// The macOS Keychain, through <c>/usr/bin/security</c>. Commands go to its interactive mode on
    /// stdin, so the password isn't visible in the process list; it's stored base64-encoded so the
    /// command needs no quoting. Items the tool creates can be read back by it without a prompt.
    /// </summary>
    private sealed class MacKeychainStore : ICredentialStore
    {
        private const string Security = "/usr/bin/security";

        public bool IsSupported => true;
        public bool AsksUserItself => false;
        public bool CanForget => true;

        public async Task<SavedCredential?> LoadAsync()
        {
            var (exitCode, output) = await RunAsync(Security, ["find-generic-password", "-s", Service, "-a", Account, "-w"], null);
            if (exitCode != 0)
            {
                return null; // Nothing saved (or the Keychain is locked).
            }

            try
            {
                return Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(output.Trim())));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public async Task SaveAsync(SavedCredential credential)
        {
            var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(Serialize(credential)));
            // -U updates the item if it already exists.
            var (exitCode, _) = await RunAsync(Security, ["-i"], $"add-generic-password -U -s {Service} -a {Account} -l {Service} -w {data}\n");
            if (exitCode != 0)
            {
                throw new InvalidOperationException("The Keychain didn't save the password.");
            }
        }

        public Task ForgetAsync() => RunAsync(Security, ["delete-generic-password", "-s", Service, "-a", Account], null);
    }

    /// <summary>Linux: the Secret Service (e.g. GNOME Keyring, KWallet) through <c>secret-tool</c>, which reads the secret from stdin.</summary>
    private sealed class SecretToolStore(string secretTool) : ICredentialStore
    {
        public bool IsSupported => true;
        public bool AsksUserItself => false;
        public bool CanForget => true;

        public async Task<SavedCredential?> LoadAsync()
        {
            var (exitCode, output) = await RunAsync(secretTool, ["lookup", "service", Service, "account", Account], null);
            return exitCode == 0 ? Deserialize(output) : null;
        }

        public async Task SaveAsync(SavedCredential credential)
        {
            var (exitCode, _) = await RunAsync(secretTool, ["store", $"--label={Service}", "service", Service, "account", Account], Serialize(credential));
            if (exitCode != 0)
            {
                throw new InvalidOperationException("The keyring didn't save the password.");
            }
        }

        public Task ForgetAsync() => RunAsync(secretTool, ["clear", "service", Service, "account", Account], null);
    }

    /// <summary>Windows Credential Manager (a generic credential, private to the signed-in Windows user).</summary>
    private sealed class WindowsCredentialStore : ICredentialStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        public bool IsSupported => true;
        public bool AsksUserItself => false;
        public bool CanForget => true;

        public Task<SavedCredential?> LoadAsync()
        {
            if (!CredRead(Service, CredTypeGeneric, 0, out var handle))
            {
                return Task.FromResult<SavedCredential?>(null);
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(handle);
                var json = credential.CredentialBlobSize == 0
                    ? null
                    : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
                return Task.FromResult(Deserialize(json));
            }
            finally
            {
                CredFree(handle);
            }
        }

        public Task SaveAsync(SavedCredential saved)
        {
            var blob = Encoding.Unicode.GetBytes(Serialize(saved));
            var blobPointer = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPointer, blob.Length);
                var credential = new Credential
                {
                    Type = CredTypeGeneric,
                    TargetName = Service,
                    UserName = saved.UserName,
                    CredentialBlob = blobPointer,
                    CredentialBlobSize = blob.Length,
                    Persist = CredPersistLocalMachine,
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blobPointer);
            }

            return Task.CompletedTask;
        }

        public Task ForgetAsync()
        {
            if (!CredDelete(Service, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(StoredLogin))]
    private sealed partial class StoredLoginContext : System.Text.Json.Serialization.JsonSerializerContext;
}
