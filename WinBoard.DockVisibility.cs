using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace WinBoard {
    public static class DockOcclusion {
        [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;public System.Drawing.Rectangle Bounds {get{return System.Drawing.Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        delegate bool EnumProc(IntPtr window,IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowStyle(IntPtr window,int index);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int FrameBounds(IntPtr window,int attribute,out Rect rect,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int Cloaked(IntPtr window,int attribute,out int value,int size);
        public static bool CoversMonitor(System.Drawing.Rectangle window,System.Drawing.Rectangle monitor) {
            const int tolerance=2;
            return window.Width>0&&window.Height>0&&monitor.Width>0&&monitor.Height>0&&window.Left<=monitor.Left+tolerance&&window.Top<=monitor.Top+tolerance&&window.Right>=monitor.Right-tolerance&&window.Bottom>=monitor.Bottom-tolerance;
        }
        static bool DesktopWindow(IntPtr window) {
            var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);
            return name.ToString()=="Progman"||name.ToString()=="WorkerW"||name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd";
        }
        static bool VisibleBounds(IntPtr window,out Rect bounds) {
            bounds=new Rect();if(window==IntPtr.Zero||!IsWindowVisible(window)||IsIconic(window)||DesktopWindow(window))return false;
            int cloaked;if(Cloaked(window,14,out cloaked,4)==0&&cloaked!=0)return false;
            return FrameBounds(window,9,out bounds,Marshal.SizeOf(typeof(Rect)))==0||GetWindowRect(window,out bounds);
        }
        static bool Blocks(IntPtr window,IntPtr monitor,uint ownProcess) {
            Rect bounds;if(!VisibleBounds(window,out bounds))return false;
            uint process;GetWindowThreadProcessId(window,out process);if(process==ownProcess)return false;
            if(IsZoomed(window)&&MonitorFromWindow(window,2)==monitor)return true;
            // Borderless video/browser full-screen windows often never become "maximized".
            // Compare visible bounds with the whole monitor, not the taskbar work area.
            var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(!GetMonitorInfo(monitor,ref info))return false;
            return CoversMonitor(bounds.Bounds,info.Monitor.Bounds);
        }
        static bool MonitorApplicationBlocks(IntPtr monitor,uint ownProcess) {
            var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(!GetMonitorInfo(monitor,ref info))return false;
            bool hide=false;
            // Foreground focus may be on another display while a video stays full-screen here.
            // EnumWindows visits top-level windows in Z order: a normal window above the
            // video keeps the Dock visible; a background full-screen window alone does not win.
            EnumWindows((window,data)=>{
                Rect bounds;if(!VisibleBounds(window,out bounds)||!bounds.Bounds.IntersectsWith(info.Monitor.Bounds))return true;
                uint process;GetWindowThreadProcessId(window,out process);
                if(process==ownProcess){var title=new StringBuilder(128);GetWindowText(window,title,title.Capacity);return title.ToString()!="WinBoard";}
                long style=GetWindowStyle(window,-20).ToInt64();
                if((style&0x08000080)!=0&&!CoversMonitor(bounds.Bounds,info.Monitor.Bounds))return true;
                IntPtr owner=GetAncestor(window,3);hide=Blocks(window,monitor,ownProcess)||(owner!=window&&Blocks(owner,monitor,ownProcess));return false;
            },IntPtr.Zero);
            return hide;
        }
        public static bool ShouldHide(IntPtr foreground,IntPtr dock,uint ownProcess) {
            if(foreground==IntPtr.Zero||dock==IntPtr.Zero)return false;
            uint process;GetWindowThreadProcessId(foreground,out process);if(process==ownProcess)return false;
            IntPtr old=IntPtr.Zero;
            try {
                // Keep monitor and fallback GetWindowRect coordinates in physical pixels.
                try{old=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
                IntPtr monitor=MonitorFromWindow(dock,2);if(monitor==IntPtr.Zero)return false;
                IntPtr root=GetAncestor(foreground,2);if(root==IntPtr.Zero)root=foreground;
                IntPtr owner=GetAncestor(foreground,3);
                if(Blocks(root,monitor,ownProcess)||(owner!=root&&Blocks(owner,monitor,ownProcess)))return true;
                if(MonitorFromWindow(root,2)==monitor)return false;
                return MonitorApplicationBlocks(monitor,ownProcess);
            }finally {if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}
        }
    }
    public partial class Board {
        [StructLayout(LayoutKind.Sequential)] struct DockPoint {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] struct DockRect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool GetCursorPos(out DockPoint point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out DockRect rect);
        DispatcherTimer dockHideTimer;
        bool dockAutoHidden,dockAutoAnimating;
        int dockAnimationVersion;
        double dockVisibleTop,dockHiddenTop;
        DateTime dockLeaveSince=DateTime.MinValue,dockKeepUntil=DateTime.MinValue;
        System.Drawing.Rectangle dockScreen;
        void EnsureDockAutoHide() {
            if(dockHideTimer!=null)return;
            dockHideTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            dockHideTimer.Tick+=(s,e)=>CheckDockAutoHide();dockHideTimer.Start();
        }
        bool CoveringApplicationActive() {return DockOcclusion.ShouldHide(RunningApps.GetForegroundWindow(),new WindowInteropHelper(this).Handle,(uint)Process.GetCurrentProcess().Id);}
        void RememberDockRevealArea() {
            DockRect rect;if(!GetWindowRect(new WindowInteropHelper(this).Handle,out rect))return;
            dockScreen=Forms.Screen.FromRectangle(new System.Drawing.Rectangle(rect.Left,rect.Top,Math.Max(1,rect.Right-rect.Left),Math.Max(1,rect.Bottom-rect.Top))).Bounds;
            var source=PresentationSource.FromVisual(this);double scale=source==null?1:source.CompositionTarget.TransformFromDevice.M22;
            dockVisibleTop=Top;dockHiddenTop=Top+(dockScreen.Bottom-rect.Top)*scale+2;
        }
        void CheckDockAutoHide() {
            if(quitting||!compact||switchingMode)return;
            if(!prefs.AutoHideDock){if(dockAutoHidden)RevealAutoHiddenDock();return;}
            if(!dockAutoHidden&&!dockAutoAnimating&&IsVisible)RememberDockRevealArea();
            DockPoint cursor;bool edge=GetCursorPos(out cursor)&&cursor.X>=dockScreen.Left&&cursor.X<dockScreen.Right&&cursor.Y>=dockScreen.Bottom-3&&cursor.Y<dockScreen.Bottom;
            bool dialogOpen=Application.Current.Windows.Cast<Window>().Any(w=>w!=this&&w.Owner==this&&w.IsVisible);
            bool keep=edge||IsMouseOver||dockDragging||dockMenuOpen||dialogOpen||!CoveringApplicationActive();
            if(keep){dockLeaveSince=DateTime.MinValue;if(dockAutoHidden)RevealAutoHiddenDock();return;}
            // Let an external file drag reach the screen edge without hiding its target.
            if(Mouse.LeftButton==MouseButtonState.Pressed||DateTime.UtcNow<dockKeepUntil)return;
            if(dockAutoHidden||dockAutoAnimating)return;
            if(dockLeaveSince==DateTime.MinValue)dockLeaveSince=DateTime.UtcNow;
            if((DateTime.UtcNow-dockLeaveSince).TotalMilliseconds<650)return;
            dockAutoHidden=true;
            AnimateDockTop(dockHiddenTop,()=>{if(!dockAutoHidden)return;Hide();Top=dockVisibleTop;});
        }
        void AnimateDockTop(double target,Action completed) {
            int version=++dockAnimationVersion;double from=Top;BeginAnimation(TopProperty,null);Top=from;dockAutoAnimating=true;
            if(!SystemParameters.ClientAreaAnimation){Top=target;dockAutoAnimating=false;completed();return;}
            var animation=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            animation.Completed+=(s,e)=>{if(version!=dockAnimationVersion)return;BeginAnimation(TopProperty,null);Top=target;dockAutoAnimating=false;completed();};
            BeginAnimation(TopProperty,animation);
        }
        void RevealAutoHiddenDock() {
            if(!dockAutoHidden)return;
            dockAutoHidden=false;dockLeaveSince=DateTime.MinValue;dockKeepUntil=DateTime.UtcNow.AddMilliseconds(900);
            if(!IsVisible){Top=dockHiddenTop;bool activated=ShowActivated;ShowActivated=false;Show();ShowActivated=activated;}
            Topmost=prefs.Topmost||prefs.HideTaskbarWhileRunning||prefs.AutoHideDock;
            AnimateDockTop(dockVisibleTop,()=>{});
        }
        void ResetDockAutoHide() {
            bool restore=dockAutoHidden||dockAutoAnimating;++dockAnimationVersion;BeginAnimation(TopProperty,null);
            if(restore)Top=dockVisibleTop;
            dockAutoHidden=false;dockAutoAnimating=false;dockLeaveSince=DateTime.MinValue;dockKeepUntil=DateTime.UtcNow.AddMilliseconds(1000);
        }
    }
}
