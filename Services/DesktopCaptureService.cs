using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class DesktopCaptureService
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    public static string GetWindowTitle(IntPtr hwnd)
    {
        if(hwnd==IntPtr.Zero) return "";
        var sb=new System.Text.StringBuilder(512);
        _=GetWindowText(hwnd,sb,sb.Capacity);
        return sb.ToString().Trim();
    }

    public static string GetWindowProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? "" : Process.GetProcessById((int)processId).ProcessName;
        }
        catch { return ""; }
    }

    public static Point? GetPointRelativeToWindow(IntPtr hwnd, double screenX, double screenY)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        if (screenX <= 0 && screenY <= 0) return null;
        return new Point(screenX - r.Left, screenY - r.Top);
    }

    public static bool IsHandleOwnedByCurrentProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    public static BitmapSource? CaptureForegroundWindow() => CaptureWindowFromScreen(GetForegroundWindow());

    public static BitmapSource? CaptureWindowFromScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return CapturePrimaryScreen();
        var width = Math.Max(1, r.Right - r.Left);
        var height = Math.Max(1, r.Bottom - r.Top);
        return CaptureRegion(r.Left, r.Top, width, height);
    }

    public static IReadOnlyList<DisplayMonitorInfo> GetMonitors()
    {
        var result = new List<DisplayMonitorInfo>();
        bool Callback(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var bounds = new Int32Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                Math.Max(1, info.rcMonitor.Right - info.rcMonitor.Left),
                Math.Max(1, info.rcMonitor.Bottom - info.rcMonitor.Top));
            var work = new Int32Rect(info.rcWork.Left, info.rcWork.Top,
                Math.Max(1, info.rcWork.Right - info.rcWork.Left),
                Math.Max(1, info.rcWork.Bottom - info.rcWork.Top));
            var primary = (info.dwFlags & 1) != 0;
            result.Add(new DisplayMonitorInfo
            {
                DeviceName = info.szDevice ?? string.Empty,
                DisplayName = $"{(primary ? "★ " : "")}Screen {result.Count + 1} · {bounds.Width}×{bounds.Height}",
                Bounds = bounds,
                WorkArea = work,
                IsPrimary = primary
            });
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return result.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Bounds.X).ThenBy(x => x.Bounds.Y).ToList();
    }

    public static BitmapSource? CapturePrimaryScreen()
    {
        var primary = GetMonitors().FirstOrDefault(x => x.IsPrimary) ?? GetMonitors().FirstOrDefault();
        if (primary is not null) return CaptureRegion(primary.Bounds);
        var left = (int)SystemParameters.VirtualScreenLeft;
        var top = (int)SystemParameters.VirtualScreenTop;
        var width = Math.Max(1, (int)SystemParameters.VirtualScreenWidth);
        var height = Math.Max(1, (int)SystemParameters.VirtualScreenHeight);
        return CaptureRegion(left, top, width, height);
    }

    public static BitmapSource? CaptureMonitor(DisplayMonitorInfo monitor) => CaptureRegion(monitor.Bounds);

    public static BitmapSource? CaptureRegion(Int32Rect region) =>
        region.Width <= 0 || region.Height <= 0 ? null : CaptureRegion(region.X, region.Y, region.Width, region.Height);

    private static BitmapSource? CaptureRegion(int left, int top, int width, int height)
    {
        var src = GetDC(IntPtr.Zero);
        if (src == IntPtr.Zero) return null;
        var mem = CreateCompatibleDC(src);
        var bmp = CreateCompatibleBitmap(src, width, height);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, width, height, src, left, top, SRCCOPY | CAPTUREBLT)) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, src);
        }
    }

    public static string SavePng(BitmapSource bitmap, string folder, string prefix)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{prefix}.png");
        SavePngToPath(bitmap, path);
        return path;
    }

    public static void SavePngToPath(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
