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
    public partial class Board {
        [StructLayout(LayoutKind.Sequential)] struct DockPoint {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] struct DockRect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool GetCursorPos(out DockPoint point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out DockRect rect);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
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
        bool MaximizedApplicationActive() {
            IntPtr foreground=RunningApps.GetForegroundWindow();if(foreground==IntPtr.Zero)return false;
            uint process;GetWindowThreadProcessId(foreground,out process);if(process==Process.GetCurrentProcess().Id)return false;
            var name=new StringBuilder(128);GetClassName(foreground,name,name.Capacity);
            if(name.ToString()=="Progman"||name.ToString()=="WorkerW"||name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd")return false;
            IntPtr owner=GetAncestor(foreground,3);if(owner==IntPtr.Zero)owner=foreground;
            return !IsIconic(owner)&&IsZoomed(owner)&&MonitorFromWindow(owner,2)==MonitorFromWindow(new WindowInteropHelper(this).Handle,2);
        }
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
            bool keep=edge||IsMouseOver||dockDragging||dockMenuOpen||dialogOpen||!MaximizedApplicationActive();
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
