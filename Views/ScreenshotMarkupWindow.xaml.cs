using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class ScreenshotMarkupWindow : Window
{
    private readonly BitmapSource _source;
    private readonly string _outputFolder;
    private string _tool = "Rectangle";
    private Brush _brush = Brushes.Red;
    private readonly string[] _palette = ["#EF2B2D", "#FF9F0A", "#20B15A", "#2F6FED", "#FFFFFF"];
    private int _colorIndex;
    private Point _start;
    private Shape? _activeShape;
    private readonly Stack<UIElement> _marks = new();
    public string SavedPath { get; private set; } = "";

    public ScreenshotMarkupWindow(BitmapSource source, string outputFolder, string language)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _source = source;
        _outputFolder = outputFolder;
        ScreenshotImage.Source = source;
        EmptyState.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        ApplyLanguage(language);
    }

    private void ApplyLanguage(string lang)
    {
        var es = lang.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        Title = Localizer.T("MarkupTitle", lang);
        HeaderTitle.Text = es ? "🖍 Marcar captura" : "🖍 Mark screenshot";
        HeaderSubtitle.Text = es ? "Revisa la captura, marca visualmente el problema y guarda el resultado." : "Review the screenshot, mark the issue visually and save the result.";
        ModeBadge.Text = es ? "EDICIÓN DE BUG" : "BUG MARKUP";
        RectangleButton.Content = $"▭ {Localizer.T("Rectangle", lang)}";
        CircleButton.Content = $"◯ {Localizer.T("Circle", lang)}";
        ArrowButton.Content = $"➜ {Localizer.T("Arrow", lang)}";
        LineButton.Content = $"╱ {Localizer.T("Line", lang)}";
        TextButton.Content = $"T {Localizer.T("Text", lang)}";
        CalloutButton.Content = $"💬 {Localizer.T("Callout", lang)}";
        UndoButton.Content = $"↶ {Localizer.T("Undo", lang)}";
        TextLabel.Text = es ? "Texto:" : "Text:";
        SavePlainButton.Content = es ? "💾 Guardar sin marcas" : "💾 Save without marks";
        SaveMarkedButton.Content = es ? "✅ Guardar marcado" : "✅ Save marked";
        HintText.Text = es ? "Marca visualmente el problema sobre la captura. Deshacer elimina la última marca." : "Mark the issue visually on the screenshot. Undo removes the last mark.";
        EmptyTitle.Text = es ? "No se pudo mostrar la captura" : "Screenshot preview is unavailable";
        EmptyBody.Text = es ? "Intenta nuevamente. QAFlow debe capturar automáticamente la ventana objetivo." : "Try again. QAFlow should capture the target window automatically.";
        RectangleButton.ToolTip = es ? "Dibujar rectángulo" : "Draw rectangle"; CircleButton.ToolTip = es ? "Dibujar círculo" : "Draw circle"; ArrowButton.ToolTip = es ? "Dibujar flecha" : "Draw arrow"; LineButton.ToolTip = es ? "Dibujar línea" : "Draw line"; TextButton.ToolTip = es ? "Insertar texto" : "Insert text"; CalloutButton.ToolTip = es ? "Insertar llamada" : "Insert callout"; UndoButton.ToolTip = es ? "Deshacer la última marca" : "Undo last mark";
        ColorLabel.Text = es ? "Color" : "Color";
        ColorPickerButton.ToolTip = es ? "Cambiar color de anotación" : "Change annotation color";
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string t) _tool = t;
    }

    private void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        _colorIndex = (_colorIndex + 1) % _palette.Length;
        var color = _palette[_colorIndex];
        _brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        ColorSwatch.Fill = _brush;
        ColorPickerButton.ToolTip = (Title.Contains("captura", StringComparison.OrdinalIgnoreCase) ? "Color actual: " : "Current color: ") + color;
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Overlay);
        if (_tool is "Text" or "Callout")
        {
            AddTextMark(_start, _tool == "Callout");
            return;
        }

        _activeShape = _tool switch
        {
            "Circle" => new Ellipse { Stroke = _brush, StrokeThickness = 4, Fill = Brushes.Transparent },
            "Line" or "Arrow" => new Line { Stroke = _brush, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            _ => new Rectangle { Stroke = _brush, StrokeThickness = 4, Fill = Brushes.Transparent }
        };
        Overlay.Children.Add(_activeShape);
        _marks.Push(_activeShape);
        Mouse.Capture(Overlay);
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeShape is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Overlay);
        if (_activeShape is Line line)
        {
            line.X1 = _start.X; line.Y1 = _start.Y; line.X2 = p.X; line.Y2 = p.Y;
            return;
        }
        var x = Math.Min(_start.X, p.X); var y = Math.Min(_start.Y, p.Y);
        Canvas.SetLeft(_activeShape, x); Canvas.SetTop(_activeShape, y);
        _activeShape.Width = Math.Abs(p.X - _start.X); _activeShape.Height = Math.Abs(p.Y - _start.Y);
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeShape is Line line && _tool == "Arrow") AddArrowHead(line);
        _activeShape = null;
        Mouse.Capture(null);
    }

    private void AddArrowHead(Line line)
    {
        var angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);
        var size = 18.0;
        var p1 = new Point(line.X2 - size * Math.Cos(angle - Math.PI / 6), line.Y2 - size * Math.Sin(angle - Math.PI / 6));
        var p2 = new Point(line.X2 - size * Math.Cos(angle + Math.PI / 6), line.Y2 - size * Math.Sin(angle + Math.PI / 6));
        var poly = new Polyline { Stroke = _brush, StrokeThickness = 4, Points = new PointCollection { p1, new Point(line.X2,line.Y2), p2 } };
        Overlay.Children.Add(poly); _marks.Push(poly);
    }

    private void AddTextMark(Point point, bool callout)
    {
        var text = string.IsNullOrWhiteSpace(AnnotationText.Text) ? (callout ? "BUG" : "Text") : AnnotationText.Text.Trim();
        Border? border = null;
        UIElement element;
        var tb = new TextBlock { Text = text, Foreground = callout ? Brushes.White : _brush, FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        if (callout)
        {
            border = new Border { Background = _brush, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10,7,10,7), Child = tb };
            element = border;
        }
        else element = tb;
        Canvas.SetLeft(element, point.X); Canvas.SetTop(element, point.Y);
        Overlay.Children.Add(element); _marks.Push(element);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_marks.Count == 0) return;
        Overlay.Children.Remove(_marks.Pop());
    }

    private void SavePlain_Click(object sender, RoutedEventArgs e)
    {
        SavedPath = DesktopCaptureService.SavePng(_source, _outputFolder, "bug-original");
        DialogResult = true;
    }

    private void SaveMarked_Click(object sender, RoutedEventArgs e)
    {
        CaptureSurface.Measure(new Size(1000,620)); CaptureSurface.Arrange(new Rect(0,0,1000,620)); CaptureSurface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000,620,96,96,PixelFormats.Pbgra32);
        bitmap.Render(CaptureSurface);
        bitmap.Freeze();
        SavedPath = DesktopCaptureService.SavePng(bitmap, _outputFolder, "bug-marked");
        DialogResult = true;
    }
}
