using System.Windows;

namespace QAFlowRecorder.Models;

public sealed class DisplayMonitorInfo
{
    public string DeviceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Int32Rect Bounds { get; set; }
    public Int32Rect WorkArea { get; set; }
    public bool IsPrimary { get; set; }
    public override string ToString() => DisplayName;
}

public sealed class CaptureAreaSelection
{
    public Int32Rect Bounds { get; set; }
    public string MonitorDeviceName { get; set; } = "";
    public bool IsValid => Bounds.Width > 0 && Bounds.Height > 0;
}
