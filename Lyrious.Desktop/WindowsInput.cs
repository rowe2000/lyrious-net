using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lyrious.Desktop;

/// <summary>
/// Where Windows says the input being handled comes from: a touchpad reaches the app as a mouse, but
/// Windows knows which it was (so a drag with one finger on a touchpad doesn't swipe a drawer, as a
/// mouse drag does; two fingers do, see App.IsTouchpadInput).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInput
{
    // INPUT_MESSAGE_DEVICE_TYPE: IMDT_TOUCHPAD.
    private const int TouchpadDevice = 0x10;

    /// <summary>Whether the input message being handled now came from a touchpad (false when Windows can't say).</summary>
    public static bool IsTouchpad()
    {
        try
        {
            return GetCurrentInputMessageSource(out var source) && source.DeviceType == TouchpadDevice;
        }
        catch (System.EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputMessageSource
    {
        public int DeviceType;
        public int OriginId;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCurrentInputMessageSource(out InputMessageSource source);
}
