using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

/// <summary>
/// Desktop-wide activity recorder. It observes mouse/keyboard interaction with any foreground
/// Windows application while never storing typed characters. Each event carries application
/// and window context so reports remain readable without relying on screen coordinates.
/// </summary>
public sealed class GlobalActivityRecorder : IDisposable
{
    private const int WhMouseLl=14, WhKeyboardLl=13;
    private const int WmLButtonUp=0x0202, WmRButtonUp=0x0205, WmMouseWheel=0x020A, WmKeyDown=0x0100;
    private IntPtr _mouseHook, _keyboardHook;
    private HookProc? _mouseProc, _keyboardProc;
    private bool _running;
    private DateTimeOffset _lastWheel=DateTimeOffset.MinValue;
    private DateTimeOffset _lastTyping=DateTimeOffset.MinValue;
    private readonly uint _ownProcessId=(uint)Environment.ProcessId;
    private IntPtr _lastUrlWindow;
    private string _lastBrowserUrl = "";
    private DateTimeOffset _lastUrlRead = DateTimeOffset.MinValue;

    public event Action<QaEvent>? EventCaptured;
    public IntPtr LastTargetWindow { get; private set; }
    public string LastTargetTitle { get; private set; } = "";
    public string LastApplicationName { get; private set; } = "";

    public void Start()
    {
        if(_running)return;
        _running=true;
        _mouseProc=MouseHook; _keyboardProc=KeyboardHook;
        var module=GetModuleHandle(null);
        _mouseHook=SetWindowsHookEx(WhMouseLl,_mouseProc,module,0);
        _keyboardHook=SetWindowsHookEx(WhKeyboardLl,_keyboardProc,module,0);
    }

    public void Stop()
    {
        if(!_running)return;
        _running=false;
        if(_mouseHook!=IntPtr.Zero)UnhookWindowsHookEx(_mouseHook);
        if(_keyboardHook!=IntPtr.Zero)UnhookWindowsHookEx(_keyboardHook);
        _mouseHook=_keyboardHook=IntPtr.Zero;
    }

    private IntPtr MouseHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        if(nCode>=0 && _running)
        {
            var msg=wParam.ToInt32();
            if(msg is WmLButtonUp or WmRButtonUp)
            {
                var data=Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                var ctx=ForegroundContext();
                if(ctx.OwnedByQaFlow) return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
                var element = ElementAt(data.pt.x, data.pt.y);
                var action=msg==WmLButtonUp?"Click":"Right-click";
                var detail = ElementSummary(element);
                Emit(EventKind.Click, string.IsNullOrWhiteSpace(detail) ? $"{action} in {ctx.AppLabel}" : $"{action} {detail} in {ctx.AppLabel}",
                    SelectorFor(element, data.pt.x, data.pt.y), ctx, data.pt.x, data.pt.y, element);
            }
            else if(msg==WmMouseWheel && DateTimeOffset.Now-_lastWheel>TimeSpan.FromMilliseconds(650))
            {
                _lastWheel=DateTimeOffset.Now;
                var ctx=ForegroundContext();
                if(!ctx.OwnedByQaFlow)
                {
                    _ = GetCursorPos(out var cursor);
                    var element = ElementAt(cursor.x, cursor.y);
                    var detail = ElementSummary(element);
                    Emit(EventKind.Scroll, string.IsNullOrWhiteSpace(detail) ? $"Scroll in {ctx.AppLabel}" : $"Scroll {detail} in {ctx.AppLabel}",
                        SelectorFor(element, cursor.x, cursor.y), ctx, cursor.x, cursor.y, element);
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
    }

    private IntPtr KeyboardHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        if(nCode>=0 && _running && wParam.ToInt32()==WmKeyDown)
        {
            var data=Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var ctx=ForegroundContext();
            if(ctx.OwnedByQaFlow) return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
            _ = GetCursorPos(out var cursor);
            var element = FocusedElement() ?? ElementAt(cursor.x, cursor.y);
            var detail = ElementSummary(element);
            if(data.vkCode is 0x0D or 0x09)
                Emit(EventKind.Input,(data.vkCode==0x0D?"Enter key":"Tab key") + (string.IsNullOrWhiteSpace(detail)?$" in {ctx.AppLabel}":$" on {detail} in {ctx.AppLabel}"),
                    SelectorFor(element,cursor.x,cursor.y),ctx,cursor.x,cursor.y,element);
            else if ((data.vkCode>=0x30&&data.vkCode<=0x5A || data.vkCode is 0x08 or 0x20) && DateTimeOffset.Now-_lastTyping>TimeSpan.FromMilliseconds(900))
            {
                _lastTyping=DateTimeOffset.Now;
                Emit(EventKind.Input,string.IsNullOrWhiteSpace(detail)?$"Text entry in {ctx.AppLabel}":$"Text entry in {detail} within {ctx.AppLabel}",
                    SelectorFor(element,cursor.x,cursor.y),ctx,cursor.x,cursor.y,element);
            }
        }
        return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
    }

    private void Emit(EventKind kind,string description,string selector,ForegroundInfo ctx,double x=0,double y=0,ElementInfo? element=null)
    {
        LastTargetWindow=ctx.Hwnd; LastTargetTitle=ctx.Title; LastApplicationName=ctx.Application;
        EventCaptured?.Invoke(new QaEvent
        {
            Timestamp=DateTimeOffset.Now,Kind=kind,Description=description,TargetTitle=ctx.Title,
            WindowTitle=ctx.Title,ApplicationName=ctx.Application,Selector=selector,Url=TryGetBrowserUrl(ctx),X=x,Y=y,
            ControlName=element?.Name ?? "", ControlType=element?.ControlType ?? "", AutomationId=element?.AutomationId ?? ""
        });
    }



    private string TryGetBrowserUrl(ForegroundInfo ctx)
    {
        if (!IsBrowserProcess(ctx.Application)) return "";
        if (_lastUrlWindow == ctx.Hwnd && DateTimeOffset.Now - _lastUrlRead < TimeSpan.FromMilliseconds(650))
            return _lastBrowserUrl;

        _lastUrlWindow = ctx.Hwnd;
        _lastUrlRead = DateTimeOffset.Now;
        try
        {
            var root = AutomationElement.FromHandle(ctx.Hwnd);
            if (root is null) return _lastBrowserUrl;
            var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement el in edits)
            {
                string name;
                try { name = (el.Current.Name ?? "").Trim(); } catch { continue; }
                var likelyAddress = name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("dirección", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("barra", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("navigation", StringComparison.OrdinalIgnoreCase);
                if (!likelyAddress) continue;
                try
                {
                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) && patternObj is ValuePattern valuePattern)
                    {
                        var value = (valuePattern.Current.Value ?? "").Trim();
                        if (TryNormalizeHttpUrl(value, out var url))
                        {
                            _lastBrowserUrl = url;
                            return url;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        return _lastBrowserUrl;
    }

    private static bool IsBrowserProcess(string? app) =>
        app?.Equals("chrome", StringComparison.OrdinalIgnoreCase) == true ||
        app?.Equals("msedge", StringComparison.OrdinalIgnoreCase) == true ||
        app?.Equals("firefox", StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryNormalizeHttpUrl(string value, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        url = uri.AbsoluteUri;
        return true;
    }

    private static ElementInfo? ElementAt(int x,int y)
    {
        try
        {
#pragma warning disable
            // .NET 10 can emit a platform analyzer warning for the WindowsBase Point bridge
            // used by UI Automation. This call is Windows-only by design because QAFlow targets WPF.
            var el=AutomationElement.FromPoint(new System.Windows.Point(x,y));
#pragma warning restore
            return ReadElement(el);
        }
        catch { return null; }
    }

    private static ElementInfo? FocusedElement()
    {
        try { return ReadElement(AutomationElement.FocusedElement); }
        catch { return null; }
    }

    private static ElementInfo? ReadElement(AutomationElement? el)
    {
        if(el is null) return null;
        try
        {
            var name=(el.Current.Name ?? "").Trim();
            var type=(el.Current.ControlType?.ProgrammaticName ?? "").Replace("ControlType.","").Trim();
            var automationId=(el.Current.AutomationId ?? "").Trim();
            var className=(el.Current.ClassName ?? "").Trim();
            return new ElementInfo(name,type,automationId,className);
        }
        catch { return null; }
    }

    private static string ElementSummary(ElementInfo? element)
    {
        if(element is null) return "";
        if(!string.IsNullOrWhiteSpace(element.Name) && !string.IsNullOrWhiteSpace(element.ControlType)) return $"'{element.Name}' ({element.ControlType.ToLowerInvariant()})";
        if(!string.IsNullOrWhiteSpace(element.Name)) return $"'{element.Name}'";
        if(!string.IsNullOrWhiteSpace(element.ControlType) && element.ControlType is not ("Pane" or "Window" or "Document")) return $"the {element.ControlType.ToLowerInvariant()}";
        return "";
    }

    private static string SelectorFor(ElementInfo? element,int x,int y)
    {
        if(element is not null)
        {
            if(!string.IsNullOrWhiteSpace(element.AutomationId)) return $"automation-id:{element.AutomationId}";
            if(!string.IsNullOrWhiteSpace(element.Name)) return $"name:{element.Name}";
            if(!string.IsNullOrWhiteSpace(element.ControlType)) return $"control:{element.ControlType}";
        }
        return $"screen({x},{y})";
    }

    private ForegroundInfo ForegroundContext()
    {
        var hwnd=GetForegroundWindow();
        if(hwnd==IntPtr.Zero) return new(hwnd,"Desktop","Desktop",false);
        _=GetWindowThreadProcessId(hwnd,out var pid);
        var owned=pid==_ownProcessId;
        var sb=new StringBuilder(512); _=GetWindowText(hwnd,sb,sb.Capacity);
        var title=sb.ToString().Trim();
        string app="Application";
        try { app=Process.GetProcessById((int)pid).ProcessName; } catch { }
        if(string.IsNullOrWhiteSpace(title)) title=app;
        return new(hwnd,title,app,owned);
    }

    public void Dispose()=>Stop();

    private sealed record ElementInfo(string Name,string ControlType,string AutomationId,string ClassName);

    private sealed record ForegroundInfo(IntPtr Hwnd,string Title,string Application,bool OwnedByQaFlow)
    {
        public string AppLabel => string.IsNullOrWhiteSpace(Title) || string.Equals(Title,Application,StringComparison.OrdinalIgnoreCase)
            ? Application : $"{Application} — {Title}";
    }

    private delegate IntPtr HookProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Point{public int x,y;}
    [StructLayout(LayoutKind.Sequential)] private struct MsLlHookStruct{public Point pt;public uint mouseData,flags,time;public IntPtr dwExtraInfo;}
    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct{public uint vkCode,scanCode,flags,time;public IntPtr dwExtraInfo;}
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int idHook,HookProc lpfn,IntPtr hMod,uint dwThreadId);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk,int nCode,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point lpPoint);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int count);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
