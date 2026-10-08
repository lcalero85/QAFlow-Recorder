using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class PrivacyZoneEditorWindow : Window
{
    private readonly string _language;
    private readonly List<PrivacyZone> _zones;
    private Point _start;
    private Rectangle? _draft;
    private BitmapSource? _capture;
    private readonly IntPtr _targetWindow;
    public IReadOnlyList<PrivacyZone> Zones => _zones;

    public PrivacyZoneEditorWindow(IEnumerable<PrivacyZone> zones, string language, IntPtr targetWindow = default)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _language = language;
        _targetWindow = targetWindow;
        _zones = zones.Select(Clone).ToList();
        ApplyLanguage();
        Loaded += (_,__) => CaptureTarget();
        RefreshList();
    }

    private static PrivacyZone Clone(PrivacyZone z) => new() { Id=z.Id, Name=z.Name, WindowTitleContains=z.WindowTitleContains, X=z.X, Y=z.Y, Width=z.Width, Height=z.Height, Enabled=z.Enabled };

    private void ApplyLanguage()
    {
        var es=_language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase);
        Title=es?"Zonas de privacidad — QAFlow":"Privacy zones — QAFlow";
        TitleText.Text=es?"Zonas de privacidad":"Privacy zones";
        SubtitleText.Text=es?"Dibuja áreas que QAFlow debe ocultar antes de guardar evidencia.":"Draw areas that QAFlow must always hide before saving evidence.";
        RefreshButton.Content=es?"Capturar ventana activa":"Capture active window";
        NameLabel.Text=es?"Nombre de zona":"Zone name";
        ScopeLabel.Text=es?"El título de ventana contiene (opcional)":"Window title contains (optional)";
        ScopeHelp.Text=es?"Déjalo vacío para aplicar esta zona a cualquier ventana capturada.":"Leave empty to apply this zone to every captured window.";
        InstructionText.Text=es?"Arrastra sobre la vista previa para crear una zona de privacidad.":"Drag on the preview to create a privacy zone.";
        AddZoneButton.Content=es?"Agregar zona dibujada":"Add drawn zone";
        SavedLabel.Text=es?"Zonas guardadas":"Saved zones";
        DeleteZoneButton.Content=es?"Eliminar seleccionada":"Delete selected";
        FooterHint.Text=es?"Las zonas se aplican localmente antes de guardar la evidencia en disco.":"Zones are applied locally before evidence is written to disk.";
        CancelButton.Content=es?"Cancelar":"Cancel"; SaveButton.Content=es?"Guardar zonas":"Save zones";
    }

    private void CaptureTarget()
    {
        var hwnd = _targetWindow;
        if (hwnd == IntPtr.Zero || DesktopCaptureService.IsHandleOwnedByCurrentProcess(hwnd)) hwnd=IntPtr.Zero;
        _capture= hwnd==IntPtr.Zero ? DesktopCaptureService.CapturePrimaryScreen() : DesktopCaptureService.CaptureWindowFromScreen(hwnd);
        CaptureImage.Source=_capture;
        if(hwnd!=IntPtr.Zero && string.IsNullOrWhiteSpace(ScopeBox.Text)) ScopeBox.Text=DesktopCaptureService.GetWindowTitle(hwnd);
        RenderZones();
    }
    private void Refresh_Click(object sender,RoutedEventArgs e)=>CaptureTarget();

    private void Overlay_MouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        _start=e.GetPosition(Overlay);
        _draft=new Rectangle{Stroke=Brushes.DeepSkyBlue,StrokeThickness=3,Fill=new SolidColorBrush(Color.FromArgb(60,0,140,255))};
        Overlay.Children.Add(_draft); Mouse.Capture(Overlay);
    }
    private void Overlay_MouseMove(object sender,MouseEventArgs e)
    {
        if(_draft is null||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(Overlay); var x=Math.Min(_start.X,p.X); var y=Math.Min(_start.Y,p.Y);
        Canvas.SetLeft(_draft,x);Canvas.SetTop(_draft,y);_draft.Width=Math.Abs(p.X-_start.X);_draft.Height=Math.Abs(p.Y-_start.Y);
    }
    private void Overlay_MouseLeftButtonUp(object sender,MouseButtonEventArgs e){Mouse.Capture(null);}

    private void AddZone_Click(object sender,RoutedEventArgs e)
    {
        if(_draft is null||_draft.Width<8||_draft.Height<8)return;
        var x=Canvas.GetLeft(_draft); var y=Canvas.GetTop(_draft);
        _zones.Add(new PrivacyZone{Name=string.IsNullOrWhiteSpace(ZoneNameBox.Text)?"Private area":ZoneNameBox.Text.Trim(),WindowTitleContains=ScopeBox.Text.Trim(),X=x/1000d,Y=y/620d,Width=_draft.Width/1000d,Height=_draft.Height/620d,Enabled=true});
        _draft=null; RefreshList(); RenderZones();
    }
    private void DeleteZone_Click(object sender,RoutedEventArgs e){if(ZonesList.SelectedItem is PrivacyZone z){_zones.Remove(z);RefreshList();RenderZones();}}
    private void ZonesList_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(ZonesList.SelectedItem is PrivacyZone z){ZoneNameBox.Text=z.Name;ScopeBox.Text=z.WindowTitleContains;}}
    private void RefreshList(){ZonesList.ItemsSource=null;ZonesList.ItemsSource=_zones;}
    private void RenderZones()
    {
        Overlay.Children.Clear();
        foreach(var z in _zones.Where(x=>x.Enabled))
        {
            var r=new Rectangle{Stroke=Brushes.OrangeRed,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(55,255,60,40)),Width=Math.Max(1,z.Width*1000),Height=Math.Max(1,z.Height*620),IsHitTestVisible=false};
            Canvas.SetLeft(r,z.X*1000);Canvas.SetTop(r,z.Y*620);Overlay.Children.Add(r);
        }
    }
    private void Save_Click(object sender,RoutedEventArgs e){DialogResult=true;}
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;}
}
