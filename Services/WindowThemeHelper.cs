using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QAFlowRecorder.Services;

public static class WindowThemeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyDarkTitleBar(Window window) => ApplyTitleBar(window);

    public static void ApplyTitleBar(Window window)
    {
        if (window is null) return;
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var enabled = ThemeManager.CurrentTheme == ThemeManager.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }

        if (window.IsLoaded) Apply();
        else window.SourceInitialized += (_, __) => Apply();
    }
}
