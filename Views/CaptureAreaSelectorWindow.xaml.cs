using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Views;

public partial class CaptureAreaSelectorWindow : Window
{
    private readonly DisplayMonitorInfo _monitor;
    private Point _startLocal;
    private Point _startScreen;
    private bool _dragging;
    public CaptureAreaSelection? Selection { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;

    public CaptureAreaSelectorWindow(DisplayMonitorInfo monitor, string language)
    {
        _monitor = monitor;
        InitializeComponent();
        var es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        InstructionTitle.Text = es ? "Arrastra para seleccionar el área de grabación" : "Drag to select the recording area";
        InstructionText.Text = es ? "Suelta para confirmar · Esc para cancelar" : "Release to confirm · Esc to cancel";
        SourceInitialized += (_, _) => PositionOnMonitor();
        Loaded += (_, _) => { Root.Focus(); Keyboard.Focus(Root); };
    }

    private void PositionOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _monitor.Bounds;
        SetWindowPos(hwnd, HwndTopmost, b.X, b.Y, b.Width, b.Height, SwpShowWindow);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _startLocal = e.GetPosition(Root);
        _startScreen = PointToScreen(_startLocal);
        SelectionBorder.Visibility = Visibility.Visible;
        System.Windows.Controls.Canvas.SetLeft(SelectionBorder, _startLocal.X);
        System.Windows.Controls.Canvas.SetTop(SelectionBorder, _startLocal.Y);
        SelectionBorder.Width = 1;
        SelectionBorder.Height = 1;
        Mouse.Capture(Root);
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Root);
        var left = Math.Min(_startLocal.X, p.X);
        var top = Math.Min(_startLocal.Y, p.Y);
        SelectionBorder.Width = Math.Max(1, Math.Abs(p.X - _startLocal.X));
        SelectionBorder.Height = Math.Max(1, Math.Abs(p.Y - _startLocal.Y));
        System.Windows.Controls.Canvas.SetLeft(SelectionBorder, left);
        System.Windows.Controls.Canvas.SetTop(SelectionBorder, top);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Mouse.Capture(null);
        var endScreen = PointToScreen(e.GetPosition(Root));
        var left = (int)Math.Round(Math.Min(_startScreen.X, endScreen.X));
        var top = (int)Math.Round(Math.Min(_startScreen.Y, endScreen.Y));
        var width = (int)Math.Round(Math.Abs(endScreen.X - _startScreen.X));
        var height = (int)Math.Round(Math.Abs(endScreen.Y - _startScreen.Y));
        if (width < 40 || height < 40)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }
        Selection = new CaptureAreaSelection
        {
            Bounds = new Int32Rect(left, top, width, height),
            MonitorDeviceName = _monitor.DeviceName
        };
        DialogResult = true;
        Close();
    }

    private void Root_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        Close();
    }
}
