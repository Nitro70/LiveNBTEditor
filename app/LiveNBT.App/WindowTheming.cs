using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LiveNBT.App;

/// <summary>Makes the OS-drawn title bar dark to match the theme (Windows 10 1809+ / 11).</summary>
public static class WindowTheming
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call from a window's constructor. Best-effort — a light title bar on old builds is harmless.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                int on = 1;
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            }
            catch
            {
                // pre-1809 Windows: attribute unsupported; keep the default title bar
            }
        };
    }
}
