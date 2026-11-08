using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinBoard {
    public partial class Board {
        Board dashboardHost,dockHost;
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        // The Dock shares the live model with the dashboard, but owns its own visual tree,
        // running-app monitor and visibility timer. Widgets, tray and shutdown belong to the dashboard.
        Board(Board dashboard) {
            dashboardHost=dashboard;prefs=dashboard.prefs;compact=true;
            Title="WinBoard · 紧凑栏";Icon=AppBrand.Image;FontFamily=dashboard.FontFamily;FontSize=13;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=prefs.DockStyle==2||prefs.CompactAppearance.Opacity<=0;Background=Brushes.Transparent;
            ShowInTaskbar=false;ShowActivated=false;MinWidth=210;MinHeight=78;Height=78;
            Left=prefs.DockX??SystemParameters.WorkArea.Left;Top=prefs.DockY??SystemParameters.WorkArea.Top;
            Build();var area=SystemParameters.WorkArea;
            Left=prefs.DockX.HasValue?Math.Max(area.Left,Math.Min(prefs.DockX.Value,area.Right-Width)):area.Left+(area.Width-Width)/2;
            Top=prefs.DockY.HasValue?Math.Max(area.Top,Math.Min(prefs.DockY.Value-dockTopPadding,area.Bottom-Height)):area.Bottom-Height-12;
            Loaded+=(s,e)=>{RememberDockPosition();Save();};
            PreviewMouseLeftButtonDown+=(s,e)=>{if((Keyboard.Modifiers&ModifierKeys.Alt)!=0){e.Handled=true;if(prefs.DockPositionLocked)return;try{DragMove();RememberDockPosition();Save();}catch(InvalidOperationException){}}};
            Closing+=(s,e)=>{if(!quitting){e.Cancel=true;dashboardHost.EnterCompact();}};
            StateChanged+=(s,e)=>{if(!quitting&&WindowState!=WindowState.Normal)WindowState=WindowState.Normal;};
            KeyDown+=(s,e)=>{
                if(e.Key==Key.Escape){RestoreDashboard();e.Handled=true;}
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.N){RestoreDashboard();dashboardHost.EditProject(null);e.Handled=true;}
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.F){RestoreDashboard();dashboardHost.search.Focus();e.Handled=true;}
            };
        }
        void EnsureDockWindow() {
            if(dashboardHost!=null){dashboardHost.EnsureDockWindow();return;}
            if(quitting)return;
            if(dockHost==null)dockHost=new Board(this);
            dockHost.RevealAutoHiddenDock();if(!dockHost.IsVisible)dockHost.Show();
            if(!dockHost.Topmost)SetWindowPos(new WindowInteropHelper(dockHost).Handle,IntPtr.Zero,0,0,0,0,0x13);
        }
        void CloseDockWindow() {
            if(dockHost==null)return;var dock=dockHost;dockHost=null;
            dock.quitting=true;dock.ResetDockAutoHide();
            if(dock.dockHideTimer!=null)dock.dockHideTimer.Stop();
            if(dock.runningTimer!=null)dock.runningTimer.Stop();
            dock.RememberDockPosition();dock.Close();
        }
        void RefreshDockPreferences() {
            var host=dashboardHost??this;var dock=host.dockHost;if(dock==null)return;
            if(dock.AllowsTransparency!=(prefs.DockStyle==2||prefs.CompactAppearance.Opacity<=0)) {
                var entries=dock.dockEntries.ToArray();var apps=dock.runningApps;double scroll=dock.dockScrollOffset;var foreground=dock.dockForeground;
                host.CloseDockWindow();host.EnsureDockWindow();dock=host.dockHost;
                dock.dockEntries.Clear();dock.dockEntries.AddRange(entries);dock.runningApps=apps;dock.dockScrollOffset=scroll;dock.dockForeground=foreground;dock.Build();return;
            }
            if(!prefs.AutoHideDock){dock.ResetDockAutoHide();dock.Show();}
            dock.Build();
        }
    }
}
