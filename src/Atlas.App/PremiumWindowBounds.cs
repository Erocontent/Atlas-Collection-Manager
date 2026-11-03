using Atlas.Core;
using System.Runtime.InteropServices;
using W=System.Windows;
using H=System.Windows.Interop;

namespace Atlas.App;

public partial class PremiumWindow
{
    private H.HwndSource? workAreaSource;
    private void RefreshCaptionControls()
    {
        CaptionMaximizeButton.Content=WindowState==W.WindowState.Maximized?"\uE923":"\uE922";
        void Label(System.Windows.Controls.Button button,string key){var label=L.T(key);button.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(button,label);}
        Label(CaptionMinimizeButton,"WindowMinimize");Label(CaptionMaximizeButton,WindowState==W.WindowState.Maximized?"WindowRestore":"WindowMaximize");Label(CaptionCloseButton,"WindowClose");
    }
    private void AttachWorkAreaBounds()
    {
        workAreaSource=H.HwndSource.FromHwnd(new H.WindowInteropHelper(this).Handle);
        workAreaSource?.AddHook(WorkAreaMessage);
        Closed+=(_,_)=>{workAreaSource?.RemoveHook(WorkAreaMessage);workAreaSource=null;};
    }
    // Monitor and work rectangles are physical pixels, including negative monitor origins.
    private static W.Rect MaximizedWorkArea(W.Rect monitor,W.Rect work)=>new(work.Left-monitor.Left,work.Top-monitor.Top,work.Width,work.Height);
    private nint WorkAreaMessage(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message!=0x0024||lParam==0)return 0; // WM_GETMINMAXINFO
        var monitor=MonitorFromWindow(hwnd,2); // nearest monitor, not always primary
        var info=new NativeMonitorInfo{Size=Marshal.SizeOf<NativeMonitorInfo>()};
        if(monitor==0||!GetMonitorInfo(monitor,ref info))return 0;
        var area=MaximizedWorkArea(info.Monitor.Rect,info.Work.Rect);
        if(area.Width<=0||area.Height<=0)return 0;
        var limits=Marshal.PtrToStructure<NativeMinMax>(lParam);
        limits.MaxPosition=new(){X=(int)area.X,Y=(int)area.Y};
        limits.MaxSize=new(){X=(int)area.Width,Y=(int)area.Height};
        // Keep WPF's DPI-aware minimum tracking size and Windows' tracking limits.
        Marshal.StructureToPtr(limits,lParam,false);handled=true;return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMax{public NativePoint Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect{public int Left,Top,Right,Bottom;public W.Rect Rect=>new(Left,Top,Right-Left,Bottom-Top);}
    [StructLayout(LayoutKind.Sequential)] private struct NativeMonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")][return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor,ref NativeMonitorInfo info);
}
