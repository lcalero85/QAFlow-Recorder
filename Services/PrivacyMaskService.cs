using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class PrivacyMaskService
{
    public static BitmapSource Apply(BitmapSource source, QaEvent? ev, IntPtr targetWindow, PrivacyMaskMode mode)
    {
        if (mode == PrivacyMaskMode.Off || ev is null) return source;
        if (mode == PrivacyMaskMode.Automatic && ev.Kind != EventKind.Input && !LooksSensitive(ev)) return source;
        if (mode == PrivacyMaskMode.Manual && !ev.MaskSensitiveEvidence) return source;

        var wb = new WriteableBitmap(source);
        var width = wb.PixelWidth;
        var height = wb.PixelHeight;
        var point = DesktopCaptureService.GetPointRelativeToWindow(targetWindow, ev.X, ev.Y);
        var cx = point?.X ?? width * 0.5;
        var cy = point?.Y ?? height * 0.5;
        var maskWidth = Math.Min(width * 0.62, 560);
        var maskHeight = Math.Min(height * 0.12, 92);
        var x = (int)Math.Clamp(cx - maskWidth / 2, 0, Math.Max(0, width - maskWidth));
        var y = (int)Math.Clamp(cy - maskHeight / 2, 0, Math.Max(0, height - maskHeight));
        var rect = new Int32Rect(x, y, Math.Max(1,(int)maskWidth), Math.Max(1,(int)maskHeight));
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        for (var i=0;i<pixels.Length;i+=4) { pixels[i]=32; pixels[i+1]=29; pixels[i+2]=27; pixels[i+3]=255; }
        wb.WritePixels(rect, pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    public static bool ApplyToFile(string filePath, QaEvent ev, IntPtr targetWindow, PrivacyMaskMode mode)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            using var stream = File.OpenRead(filePath);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = stream; bmp.EndInit(); bmp.Freeze();
            var masked = Apply(bmp, ev, targetWindow, mode);
            if (ReferenceEquals(masked, bmp)) return false;
            DesktopCaptureService.SavePngToPath(masked, filePath);
            return true;
        }
        catch { return false; }
    }

    private static bool LooksSensitive(QaEvent ev)
    {
        var s = $"{ev.Description} {ev.TargetTitle} {ev.WindowTitle}".ToLowerInvariant();
        return s.Contains("password") || s.Contains("contraseña") || s.Contains("token") || s.Contains("card") || s.Contains("tarjeta") || s.Contains("account") || s.Contains("cuenta") || s.Contains("login") || s.Contains("sign in");
    }
}
