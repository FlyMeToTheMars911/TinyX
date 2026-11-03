using System.Runtime.InteropServices;

namespace TinyX;

internal static class NativeMethods
{
    public const int HotkeyMessage = 0x0312;
    public const int NonClientHitTestMessage = 0x0084;
    public const int NonClientCalculateSizeMessage = 0x0083;
    public const int ThickFrameStyle = 0x00040000;
    public const int TopMostExtendedStyle = 0x00000008;
    private const int StyleIndex = -16;
    private const int ExtendedStyleIndex = -20;
    public const int HotkeyId = 0x5458;
    public const uint HotkeyModifiers = 0x0001 | 0x0002 | 0x4000;
    public const uint HideKey = 0x58;
    public const int ClientHit = 1;
    public const int CaptionHit = 2;
    public const int LeftHit = 10;
    public const int RightHit = 11;
    public const int TopHit = 12;
    public const int TopLeftHit = 13;
    public const int TopRightHit = 14;
    public const int BottomHit = 15;
    public const int BottomLeftHit = 16;
    public const int BottomRightHit = 17;
    public const int SystemCommandMessage = 0x0112;
    public const int MoveCommand = 0xF012;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr handle, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPointer(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    public static long GetWindowStyle(IntPtr handle, bool extended = false)
    {
        int index = extended ? ExtendedStyleIndex : StyleIndex;
        return IntPtr.Size == sizeof(long) ? GetWindowLongPointer(handle, index).ToInt64()
            : GetWindowLong(handle, index);
    }
}
