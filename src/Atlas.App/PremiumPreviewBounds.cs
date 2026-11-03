using System.Runtime.InteropServices;
using W=System.Windows;
using H=System.Windows.Interop;
using M=System.Windows.Media;

namespace Atlas.App;
public partial class PremiumWindow
{
    private static W.Size PreviewDialogSize(W.Size requested,W.Size workArea)
        =>new(Math.Min(requested.Width,Math.Max(1,workArea.Width-32)),Math.Min(requested.Height,Math.Max(1,workArea.Height-32)));
    private void ConstrainModPreview(W.Window dialog)
    {
        var handle=new H.WindowInteropHelper(this).Handle;
        var monitor=MonitorFromWindow(handle,2);
        var info=new NativeMonitorInfo{Size=Marshal.SizeOf<NativeMonitorInfo>()};
        var native=monitor!=0&&GetMonitorInfo(monitor,ref info);
        var transform=W.PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??M.Matrix.Identity;
        var work=native?new W.Size(info.Work.Rect.Width*Math.Abs(transform.M11),info.Work.Rect.Height*Math.Abs(transform.M22)):W.SystemParameters.WorkArea.Size;
        var size=PreviewDialogSize(new(dialog.Width,dialog.Height),work);
        dialog.MinWidth=Math.Min(dialog.MinWidth,size.Width);dialog.MinHeight=Math.Min(dialog.MinHeight,size.Height);
        dialog.MaxWidth=Math.Max(1,work.Width-32);dialog.MaxHeight=Math.Max(1,work.Height-32);dialog.Width=size.Width;dialog.Height=size.Height;
        dialog.Loaded+=(_,_)=>{
            if(!native||!GetPreviewWindowRect(handle,out var owner)||!owner.Rect.IntersectsWith(info.Work.Rect))return;
            var hwnd=new H.WindowInteropHelper(dialog).Handle;if(!GetPreviewWindowRect(hwnd,out var bounds))return;
            var x=Math.Clamp(bounds.Left,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-(bounds.Right-bounds.Left)));
            var y=Math.Clamp(bounds.Top,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-(bounds.Bottom-bounds.Top)));
            SetPreviewWindowPosition(hwnd,0,x,y,0,0,0x0015); // no size, z-order or activation changes
        };
    }
    [DllImport("user32.dll",EntryPoint="GetWindowRect")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetPreviewWindowRect(nint handle,out NativeRect bounds);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetPreviewWindowPosition(nint handle,nint after,int x,int y,int width,int height,uint flags);
}
