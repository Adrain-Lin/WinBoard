using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WinBoard {
    public static class DockEntries {
        public const string Dashboard="winboard:dashboard";
        public const string Separator="winboard:separator";
    }
    public static class WindowsTaskbar {
        [StructLayout(LayoutKind.Sequential)] struct AppBarData {
            public uint Size;public IntPtr Window;public uint Callback,Edge;
            public int Left,Top,Right,Bottom;public IntPtr Parameter;
        }
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message,ref AppBarData data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
        static AppBarData Data() {return new AppBarData{Size=(uint)Marshal.SizeOf(typeof(AppBarData)),Window=FindWindow("Shell_TrayWnd",null)};}
        public static bool AutoHide {get {var data=Data();return (SHAppBarMessage(4,ref data).ToUInt64()&1)!=0;}}
        public static bool SetAutoHide(bool enabled) {
            var data=Data();if(data.Window==IntPtr.Zero)return false;
            long state=(long)SHAppBarMessage(4,ref data).ToUInt64();
            data.Parameter=new IntPtr(enabled?state|1:state&~1L);
            SHAppBarMessage(10,ref data);return AutoHide==enabled;
        }
    }
    public partial class Board {
        sealed class DockMotion {
            public Button Button;
            public ScaleTransform Scale;
            public TranslateTransform Lift;
            public double Target=1;
        }
        sealed class DockEntry {
            public Shortcut Pin;public RunningApp App;public Button Button;public Border Dot;public string[] Target;
            public bool Separator {get{return Pin!=null&&Pin.Path==DockEntries.Separator;}}
            public string Name {get{return Pin!=null?Pin.Name:App.Name;}}
        }
        readonly List<DockEntry> dockEntries=new List<DockEntry>();
        readonly Dictionary<string,string[]> dockTargets=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
        List<RunningApp> runningApps=new List<RunningApp>();
        DispatcherTimer runningTimer;
        bool scanningApps,dockDragging,dockMenuOpen;
        IntPtr dockForeground;
        const string DockDragFormat="WinBoard.DockEntry";
        readonly TaskbarGuard taskbarGuard=new TaskbarGuard();
        double dockScrollOffset;
        double dockTopPadding,dockBottomPadding;
        static bool IsDockFixed(string path) {path=ShellEntries.Canonical(path);return path==ShellEntries.Computer||path==ShellEntries.RecycleBin;}
        static DoubleAnimation DockAnimation(double target,int milliseconds) {
            return new DoubleAnimation(target,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        }
        void AnimateDockIcon(DockMotion motion,double scale) {
            if(Math.Abs(motion.Target-scale)<.025)return;motion.Target=scale;
            if(!SystemParameters.ClientAreaAnimation){motion.Scale.ScaleX=motion.Scale.ScaleY=1;motion.Lift.Y=0;return;}
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,DockAnimation(scale,130));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty,DockAnimation(scale,130));
            motion.Lift.BeginAnimation(TranslateTransform.YProperty,DockAnimation(-(scale-1)*19,130));
        }
        void BounceDockIcon(DockMotion motion) {
            if(!SystemParameters.ClientAreaAnimation)return;
            var bounce=new DoubleAnimationUsingKeyFrames();
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(2,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(45)),new CubicEase{EasingMode=EasingMode.EaseOut}));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-8,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(135)),new CubicEase{EasingMode=EasingMode.EaseOut}));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-(motion.Target-1)*19,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),new CubicEase{EasingMode=EasingMode.EaseOut}));
            motion.Lift.BeginAnimation(TranslateTransform.YProperty,bounce);
        }
        FrameworkElement DockTileImage(string path,bool restore) {
            if(restore)return new Image{Source=AppBrand.Image,Width=36,Height=36,Stretch=Stretch.Uniform};
            var bitmap=GetIcon(path);
            if(bitmap!=null)return new Image{Source=bitmap,Width=34,Height=34,Stretch=Stretch.Uniform};
            return DockSymbol("launch");
        }
        DockMotion MakeDockTile(string name,string path,bool restore,Action action,System.Windows.Media.Imaging.BitmapSource windowIcon=null) {
            var motion=new DockMotion{Scale=new ScaleTransform(1,1),Lift=new TranslateTransform()};
            var transforms=new TransformGroup();transforms.Children.Add(motion.Scale);transforms.Children.Add(motion.Lift);
            var visual=new Grid{Width=36,Height=prefs.DockStyle==2?64:36,RenderTransformOrigin=new Point(.5,.65),RenderTransform=transforms};
            FrameworkElement artwork=windowIcon==null?DockTileImage(path,restore):new Image{Source=windowIcon,Width=34,Height=34,Stretch=Stretch.Uniform};visual.Children.Add(prefs.DockStyle==2?DockReflectionVisual(artwork):artwork);
            var button=Btn("",()=>{BounceDockIcon(motion);action();});button.Content=visual;button.Width=52;button.Height=68;button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);button.ToolTip=name;
            button.MouseEnter+=(s,e)=>button.Opacity=1;button.MouseLeave+=(s,e)=>button.Opacity=1;
            ToolTipService.SetInitialShowDelay(button,450);System.Windows.Automation.AutomationProperties.SetName(button,name);
            motion.Button=button;return motion;
        }
        void OpenDashboardFromDock() {
            RestoreDashboard();
        }
        void EnsureRunningMonitor() {
            if(runningTimer!=null)return;
            runningTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};
            runningTimer.Tick+=(s,e)=>PollRunningApps();runningTimer.Start();PollRunningApps();
        }
        void PollRunningApps() {
            if(quitting||!compact||scanningApps||dockDragging||dockMenuOpen||Mouse.LeftButton==MouseButtonState.Pressed)return;
            var foreground=RunningApps.GetForegroundWindow();
            if(foreground!=new System.Windows.Interop.WindowInteropHelper(this).Handle)dockForeground=foreground;
            scanningApps=true;
            Task.Factory.StartNew(()=>RunningApps.Scan()).ContinueWith(task=>{
                if(Dispatcher.HasShutdownStarted)return;
                Dispatcher.BeginInvoke(new Action(()=>{
                    scanningApps=false;if(quitting||task.IsFaulted||!compact||dockDragging||dockMenuOpen||Mouse.LeftButton==MouseButtonState.Pressed)return;
                    stableRunningKeys=new HashSet<string>(runningApps.Select(app=>app.Key),StringComparer.OrdinalIgnoreCase);
                    runningApps=task.Result;
                    stableRunningKeys.IntersectWith(runningApps.Select(app=>app.Key));
                    bool changed=SyncDockEntries();
                    if(changed)Build();else UpdateDockStatus();
                }));
            });
        }
        HashSet<string> stableRunningKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool SyncDockEntries() {
            bool changed=false;
            foreach(var entry in dockEntries.ToList())if(entry.Pin!=null&&!prefs.DockLayout.Contains(entry.Pin)){dockEntries.Remove(entry);changed=true;}
            foreach(var pin in prefs.DockLayout)if(!dockEntries.Any(e=>e.Pin==pin)){
                string[] target;if(!dockTargets.TryGetValue(pin.Path,out target)){target=RunningApps.ShortcutIdentities(pin.Path);dockTargets[pin.Path]=target;}
                var entry=new DockEntry{Pin=pin,Target=target};
                // Preserve the persisted order when a pin is added outside the Dock.
                var next=prefs.DockLayout.Skip(prefs.DockLayout.IndexOf(pin)+1).Select(p=>dockEntries.FirstOrDefault(e=>e.Pin==p)).FirstOrDefault(e=>e!=null);
                dockEntries.Insert(next==null?dockEntries.Count:dockEntries.IndexOf(next),entry);changed=true;
            }
            var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in dockEntries.Where(e=>e.Pin!=null)) {
                var matches=runningApps.Where(a=>!used.Contains(a.Key)&&RunningApps.Matches(entry.Target,a)).ToList();
                entry.App=matches.FirstOrDefault();
                if(entry.App==null)continue;
                foreach(var match in matches)used.Add(match.Key);
                if(matches.Count>1){
                    var first=entry.App;
                    var merged=new RunningApp{Key=first.Key,Name=first.Name,Path=first.Path,Identities=first.Identities};
                    merged.Windows.AddRange(matches.SelectMany(match=>match.Windows));
                    entry.App=merged;
                }
            }
            foreach(var entry in dockEntries.Where(e=>e.Pin==null).ToList()) {
                var app=runningApps.FirstOrDefault(a=>a.Key==entry.App.Key&&!used.Contains(a.Key));
                if(app==null){dockEntries.Remove(entry);changed=true;}else{entry.App=app;used.Add(app.Key);}
            }
            // Only publish an unmatched window after its identity survives two scans.
            foreach(var app in runningApps)if(!used.Contains(app.Key)&&stableRunningKeys.Contains(app.Key)) {
                int index=dockEntries.FindIndex(e=>e.Separator);dockEntries.Insert(index<0?dockEntries.Count:index,new DockEntry{App=app});changed=true;
            }
            return changed;
        }
        void SaveDockOrder() {prefs.DockLayout=dockEntries.Where(e=>e.Pin!=null).Select(e=>e.Pin).ToList();Save();}
        void InsertDockPaths(IEnumerable<string> paths,int slot) {
            SyncDockEntries();slot=Math.Max(0,Math.Min(slot,dockEntries.Count));
            foreach(var pin in DockOrder.ReadPaths(paths)) {
                var entry=dockEntries.FirstOrDefault(e=>e.Pin!=null&&string.Equals(e.Pin.Path,pin.Path,StringComparison.OrdinalIgnoreCase));
                if(entry==null)entry=new DockEntry{Pin=pin,Target=RunningApps.ShortcutIdentities(pin.Path)};
                DockOrder.Insert(dockEntries,entry,slot);slot=dockEntries.IndexOf(entry)+1;
            }
            SaveDockOrder();if(compact)Build();
        }
        void MoveDockEntry(DockEntry entry,int direction) {
            int index=dockEntries.IndexOf(entry);if(index+direction<0||index+direction>=dockEntries.Count)return;
            DockOrder.Insert(dockEntries,entry,index+direction+(direction>0?1:0));SaveDockOrder();Build();
        }
        void UpdateDockStatus() {
            foreach(var entry in dockEntries.Where(e=>!e.Separator&&e.Button!=null)) {
                bool active=entry.App!=null&&entry.App.Windows.Any(w=>w.Handle==dockForeground);
                entry.Dot.Visibility=entry.App==null?Visibility.Collapsed:Visibility.Visible;
                entry.Dot.Width=active?16:entry.App!=null&&entry.App.Windows.Count>1?10:5;entry.Dot.Background=active?Hex("#82E4C4"):TextColor;
                string label=entry.Name+(entry.App==null?"":"\n"+string.Join("\n",entry.App.Windows.Select(w=>w.Title)));
                entry.Button.ToolTip=label;System.Windows.Automation.AutomationProperties.SetHelpText(entry.Button,entry.App==null?"固定入口":label);
            }
        }
        readonly HashSet<DockEntry> dockOpening=new HashSet<DockEntry>();
        void RelaunchDockEntry(DockEntry entry) {
            // Keep the original shortcut, including its arguments and working directory.
            string path=entry.Pin!=null?entry.Pin.Path:entry.App!=null?entry.App.Path:null;
            if(!string.IsNullOrEmpty(path))Launch(path);
        }
        async void OpenDockWindow(DockEntry entry,RunningWindow window,bool minimize) {
            if(!dockOpening.Add(entry))return;
            try {
                bool toggle=minimize&&RunningApps.CanActivate(window.Handle)&&!RunningApps.IsIconic(window.Handle);
                if(!RunningApps.Activate(window.Handle,minimize)){RelaunchDockEntry(entry);return;}
                if(toggle)return;
                // Restore is asynchronous. Check after it has had time to run; a tray
                // close may also race the monitor's last window snapshot.
                await Task.Delay(300);
                if(quitting)return;
                if(!RunningApps.CanActivate(window.Handle)||RunningApps.IsIconic(window.Handle))RelaunchDockEntry(entry);
                else RunningApps.Activate(window.Handle,false);
            }finally{dockOpening.Remove(entry);}
        }
        DockPreviewWindow previewWindow;
        DockEntry previewEntry;
        void ShowDockPreview(DockEntry entry,List<RunningWindow> windows,bool clicked){
            if(previewWindow!=null&&previewEntry==entry){if(clicked)previewWindow.FocusPreview();return;}
            if(previewWindow!=null)previewWindow.Close();
            var panel=new DockPreviewWindow(this,entry.Button,windows,w=>OpenDockWindow(entry,w,false),clicked);
            previewWindow=panel;previewEntry=entry;dockMenuOpen=true;
            panel.Closed+=(s,e)=>{previewWindow=null;previewEntry=null;dockMenuOpen=false;};
            panel.Show();
        }
        void OpenDockEntry(DockEntry entry) {
            if(previewWindow!=null&&(entry.App==null||entry.App.Windows.Count<=1||previewEntry!=entry))previewWindow.Close();
            if(entry.Pin!=null&&entry.Pin.Path==DockEntries.Dashboard){OpenDashboardFromDock();return;}
            var windows=entry.App==null?new List<RunningWindow>():entry.App.Windows.Where(w=>RunningApps.CanActivate(w.Handle)).ToList();
            if(windows.Count==0){RelaunchDockEntry(entry);return;}
            if(windows.Count==1){var w=windows[0];OpenDockWindow(entry,w,w.Handle==dockForeground);return;}
            ShowDockPreview(entry,windows,true);
        }
        void TrackDockMenu(ContextMenu menu) {menu.Opened+=(s,e)=>dockMenuOpen=true;menu.Closed+=(s,e)=>dockMenuOpen=false;}
        ContextMenu EntryMenu(DockEntry entry) {
            var menu=new ContextMenu();TrackDockMenu(menu);
            menu.Opened+=(s,e)=>{
                menu.Items.Clear();
                if(entry.App!=null)foreach(var w in entry.App.Windows){var window=w;Menu(menu,window.Title,()=>OpenDockWindow(entry,window,false));}
                Menu(menu,"向左移动",()=>MoveDockEntry(entry,-1));Menu(menu,"向右移动",()=>MoveDockEntry(entry,1));
                if(entry.Pin==null && !string.IsNullOrEmpty(entry.App.Path))Menu(menu,"固定到紧凑栏",()=>{entry.Pin=new Shortcut{Name=entry.Name,Path=entry.App.Path};entry.Target=entry.App.Identities.ToArray();SaveDockOrder();Build();});
                if(entry.Pin!=null && entry.Pin.Path!=DockEntries.Dashboard)Menu(menu,entry.App==null?"从紧凑栏移除":"取消固定",()=>{
                    if(entry.App!=null)entry.Pin=null;else dockEntries.Remove(entry);SaveDockOrder();Build();
                });
                if(entry.Pin!=null&&!entry.Separator&&entry.Pin.Path!=DockEntries.Dashboard)Menu(menu,"打开新窗口 / 启动",()=>Launch(entry.Pin.Path));
                Menu(menu,"添加应用…",BrowseDockApps);AddTaskbarMenu(menu,false);
            };
            return menu;
        }
        void EnableDockDrag(Button button,DockEntry entry) {
            Point? start=null;bool moved=false;
            button.PreviewMouseLeftButtonDown+=(s,e)=>{start=e.GetPosition(button);moved=false;};
            button.PreviewMouseLeftButtonUp+=(s,e)=>{start=null;if(moved)e.Handled=true;};
            button.PreviewMouseMove+=(s,e)=>{
                if(e.LeftButton!=MouseButtonState.Pressed){start=null;return;}if(!start.HasValue||moved)return;
                var point=e.GetPosition(button);if(Math.Abs(point.X-start.Value.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Value.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                moved=true;start=null;e.Handled=true;dockDragging=true;button.ReleaseMouseCapture();
                try{DragDrop.DoDragDrop(button,new DataObject(DockDragFormat,entry),DragDropEffects.Move);}finally{dockDragging=false;}
            };
        }
        DockEntry ApplicationDropTarget(DragEventArgs e) {
            if(e.Data.GetDataPresent(DockDragFormat)||!e.Data.GetDataPresent(DataFormats.FileDrop))return null;
            foreach(var entry in dockEntries) {
                var button=entry.Button;
                if(button==null||!button.IsVisible)continue;
                var point=e.GetPosition(button);
                if(point.X<9||point.X>button.ActualWidth-9||point.Y<0||point.Y>button.ActualHeight)continue;
                string path=entry.Pin!=null?entry.Pin.Path:entry.App.Path;
                if(string.IsNullOrEmpty(path))continue;
                if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return entry;
                if(path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)&&entry.Target!=null&&
                    entry.Target.Any(id=>id.StartsWith("path:",StringComparison.OrdinalIgnoreCase)&&id.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))return entry;
            }
            return null;
        }
        static string QuoteDropPath(string path) {
            // A trailing backslash before a closing quote must itself be escaped.
            string trimmed=path.TrimEnd((char)92);
            return ((char)34)+path+new string((char)92,path.Length-trimmed.Length)+(char)34;
        }
        void OpenDroppedPaths(DockEntry entry,DragEventArgs e) {
            e.Effects=DragDropEffects.None;
            try {
                var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
                if(paths==null||paths.Length==0||paths.Any(path=>!System.IO.Path.IsPathRooted(path)||
                    (!System.IO.File.Exists(path)&&!System.IO.Directory.Exists(path))))return;
                string app=entry.Pin!=null?entry.Pin.Path:entry.App.Path;
                var start=new System.Diagnostics.ProcessStartInfo(app,string.Join(" ",paths.Select(QuoteDropPath))){UseShellExecute=true};
                StartShellTarget(start);
                e.Effects=DragDropEffects.Copy;
            } catch(Exception ex){MessageBox.Show(this,ex.Message,"使用应用打开");}
        }
        void BuildCompact() {
            double previousPadding=dockTopPadding;
            if(IsLoaded&&previousPadding!=(prefs.DockStyle==2?24:0)&&(dockAutoHidden||dockAutoAnimating)){ResetDockAutoHide();if(!IsVisible)Show();}
            dockTopPadding=prefs.DockStyle==2?24:0;dockBottomPadding=prefs.DockStyle==2?12:0;
            Height=78+dockTopPadding+dockBottomPadding;
            if(IsLoaded&&previousPadding!=dockTopPadding)Top+=previousPadding-dockTopPadding;
            ApplyDockStyle();
            root.Background=Brushes.Transparent;
            main=null;search=null;date=null;
            Topmost=prefs.Topmost||prefs.HideTaskbarWhileRunning||prefs.AutoHideDock;
            SyncDockEntries();EnsureRunningMonitor();EnsureDockAutoHide();
            var motions=new List<DockMotion>();
            var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(12,0,12,0)};root.Children.Add(row);
            var appRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,dockTopPadding,0,dockBottomPadding)};
            // Reserve the tray button, row margins and surface border in both sizing paths.
            const double trayWidth=36,trayGap=2,rowMargin=24;
            var startButton=Btn("",()=>System.Windows.Forms.SendKeys.SendWait("^{ESC}"));
            var logo=new System.Windows.Controls.Primitives.UniformGrid{Rows=2,Columns=2,Width=23,Height=23};
            for(int i=0;i<4;i++)logo.Children.Add(new Border{Background=Hex("#48BAFF"),Margin=new Thickness(.6)});
            startButton.Content=logo;startButton.ToolTip="开始";startButton.Width=38;startButton.Height=48;
            startButton.Background=Brushes.Transparent;startButton.Padding=new Thickness(0);startButton.BorderThickness=new Thickness(0);
            startButton.Margin=new Thickness(0,dockTopPadding,4,dockBottomPadding);
            System.Windows.Automation.AutomationProperties.SetName(startButton,"开始");row.Children.Add(startButton);
            double chromeWidth=startButton.Width+startButton.Margin.Right+trayWidth+trayGap+rowMargin+surface.BorderThickness.Left+surface.BorderThickness.Right;
            double contentWidth=dockEntries.Sum(e=>e.Separator?21:52),available=Math.Max(52,SystemParameters.WorkArea.Width-chromeWidth-24);
            double anchor=prefs.DockX??Left;
            if(prefs.DockPositionLocked&&anchor>=SystemParameters.WorkArea.Left&&anchor<SystemParameters.WorkArea.Right)available=Math.Min(available,Math.Max(52,SystemParameters.WorkArea.Right-anchor-chromeWidth));
            var scroll=new ScrollViewer{Content=appRow,Width=Math.Min(contentWidth,available),Height=72+dockTopPadding+dockBottomPadding,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
            var viewport=new Grid{Width=scroll.Width,Height=scroll.Height,ClipToBounds=true};viewport.Children.Add(scroll);row.Children.Add(viewport);
            var insertion=new Border{Width=3,Height=44,CornerRadius=new CornerRadius(1.5),Background=Hex("#82E4C4"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,Visibility=Visibility.Collapsed};viewport.Children.Add(insertion);
            scroll.Loaded+=(s,e)=>scroll.ScrollToHorizontalOffset(dockScrollOffset);
            scroll.ScrollChanged+=(s,e)=>dockScrollOffset=scroll.HorizontalOffset;
            scroll.PreviewMouseWheel+=(s,e)=>{scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;};
            foreach(var entryValue in dockEntries) {
                var entry=entryValue;
                if(entry.Separator) {
                    var line=new Border{Width=1,Height=34,Background=Hex(Light?"#35000000":"#55FFFFFF"),Margin=new Thickness(10,0,10,0)};
                    var handle=new Border{Width=21,Height=68,Background=Brushes.Transparent,Child=line,Cursor=prefs.DockPositionLocked?Cursors.Arrow:Cursors.SizeAll,ToolTip=prefs.DockPositionLocked?"位置已锁定 · 右键可解锁":"拖动紧凑栏；图标可跨过分隔线排序"};handle.MouseLeftButtonDown+=DragHeader;appRow.Children.Add(handle);continue;
                }
                string path=entry.Pin==null?entry.App.Path:entry.Pin.Path;
                var windowIcon=entry.Pin==null?RunningApps.Icon(entry.App.Windows[0].Handle):null;
                var motion=MakeDockTile(entry.Name,path??"",path==DockEntries.Dashboard,()=>OpenDockEntry(entry),windowIcon);motions.Add(motion);entry.Button=motion.Button;
                var icon=motion.Button.Content as UIElement;var content=new Grid{Width=52,Height=68};motion.Button.Content=null;content.Children.Add(icon);
                entry.Dot=new Border{Height=3,Width=5,CornerRadius=new CornerRadius(2),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,prefs.DockStyle==2?27:5)};content.Children.Add(entry.Dot);motion.Button.Content=content;
                motion.Button.ToolTipOpening+=(s,e)=>{
                    if(entry.App==null||dockDragging||entry.Separator)return;
                    var visible=entry.App.Windows.Where(w=>RunningApps.CanActivate(w.Handle)).ToList();
                    if(visible.Count==0)return;
                    e.Handled=true;ShowDockPreview(entry,visible,false);
                };
                EnableDockDrag(motion.Button,entry);motion.Button.ContextMenu=EntryMenu(entry);appRow.Children.Add(motion.Button);
            }
            Button trayButton=null;trayButton=Btn("",()=>ToggleBackgroundApps(trayButton));trayButton.Content=new TextBlock{Text="\uE70E",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18,Foreground=TextColor,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            trayButton.Width=trayWidth;trayButton.Height=48;trayButton.Padding=new Thickness(0);trayButton.Margin=new Thickness(trayGap,dockTopPadding,0,dockBottomPadding);trayButton.Background=Brushes.Transparent;trayButton.BorderThickness=new Thickness(0);trayButton.ToolTip="后台程序";
            System.Windows.Automation.AutomationProperties.SetName(trayButton,"后台程序");row.Children.Add(trayButton);
            row.MouseMove+=(s,e)=>{foreach(var motion in motions){double x=e.GetPosition(motion.Button).X-motion.Button.ActualWidth/2;double target=e.LeftButton==MouseButtonState.Pressed?1:1+.30*Math.Max(0,1-Math.Abs(x)/85);AnimateDockIcon(motion,target);}};
            row.MouseLeave+=(s,e)=>{foreach(var motion in motions)AnimateDockIcon(motion,1);};
            root.PreviewDragEnter+=(s,e)=>{foreach(var motion in motions)AnimateDockIcon(motion,1);};
            var dockMenu=new ContextMenu();TrackDockMenu(dockMenu);Menu(dockMenu,"添加应用…",BrowseDockApps);Menu(dockMenu,"打开工作台",OpenDashboardFromDock);AddTaskbarMenu(dockMenu);surface.ContextMenu=dockMenu;
            surface.MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource==surface||e.OriginalSource==root)DragHeader(s,e);};
            Width=Math.Max(210,scroll.Width+chromeWidth);if(IsLoaded&&!prefs.DockPositionLocked)Left=Math.Max(SystemParameters.WorkArea.Left,Math.Min(Left,SystemParameters.WorkArea.Right-Width));
            Func<double,int> slotAt=x=>{double edge=0;for(int i=0;i<dockEntries.Count;i++){double width=dockEntries[i].Separator?21:52;if(x<edge+width/2)return i;edge+=width;}return dockEntries.Count;};
            DockEntry highlighted=null;
            Action<DockEntry> highlight=entry=>{
                if(highlighted==entry)return;
                if(highlighted!=null)highlighted.Button.Background=Brushes.Transparent;
                highlighted=entry;
                if(entry!=null)entry.Button.Background=Hex("#4482E4C4");
            };
            int lastScroll=0;
            root.AllowDrop=true;root.PreviewDragOver+=(s,e)=>{
                bool internalDrag=e.Data.GetDataPresent(DockDragFormat),external=e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent("Shell IDList Array");
                e.Handled=true;e.Effects=internalDrag?DragDropEffects.Move:external?DragDropEffects.Copy:DragDropEffects.None;
                var appTarget=new Rect(viewport.RenderSize).Contains(e.GetPosition(viewport))?ApplicationDropTarget(e):null;
                highlight(appTarget);
                if(appTarget!=null){SetRecycleHover(null);insertion.Visibility=Visibility.Collapsed;e.Effects=DragDropEffects.Copy;return;}
                if(!internalDrag&&!external){insertion.Visibility=Visibility.Collapsed;return;}
                if(!internalDrag&&RecycleDropTarget(e)!=null){insertion.Visibility=Visibility.Collapsed;bool valid=RecycleDropPaths(e)!=null;SetRecycleHover(valid?RecycleDropTarget(e):null);e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;return;}
                SetRecycleHover(null);
                double x=e.GetPosition(scroll).X;
                if(unchecked(Environment.TickCount-lastScroll)>80){if(x<24)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-26);else if(x>scroll.ActualWidth-24)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+26);lastScroll=Environment.TickCount;}
                int slot=slotAt(e.GetPosition(appRow).X);double left=dockEntries.Take(slot).Sum(item=>item.Separator?21:52)-scroll.HorizontalOffset;
                insertion.Margin=new Thickness(Math.Max(0,Math.Min(scroll.Width-3,left-1)),0,0,0);insertion.Visibility=Visibility.Visible;
            };
            root.DragLeave+=(s,e)=>{highlight(null);insertion.Visibility=Visibility.Collapsed;SetRecycleHover(null);};
            root.PreviewDrop+=(s,e)=>{
                highlight(null);
                int slot=slotAt(e.GetPosition(appRow).X);insertion.Visibility=Visibility.Collapsed;e.Handled=true;
                if(!e.Data.GetDataPresent(DockDragFormat)&&RecycleDropTarget(e)!=null){RecycleDroppedFiles(e);return;}
                var appTarget=new Rect(viewport.RenderSize).Contains(e.GetPosition(viewport))?ApplicationDropTarget(e):null;
                if(appTarget!=null){OpenDroppedPaths(appTarget,e);return;}
                if(e.Data.GetDataPresent(DockDragFormat)) {var entry=e.Data.GetData(DockDragFormat) as DockEntry;e.Effects=DragDropEffects.None;if(entry!=null&&dockEntries.Contains(entry)){DockOrder.Insert(dockEntries,entry,slot);SaveDockOrder();Build();e.Effects=DragDropEffects.Move;}}
                else if(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent("Shell IDList Array")){InsertDockPaths(ShellEntries.DropPaths(e.Data),slot);e.Effects=DragDropEffects.Copy;}
            };
            UpdateDockStatus();
            if(SystemParameters.ClientAreaAnimation && switchingMode){
                var lift=new TranslateTransform();root.RenderTransform=lift;lift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});root.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
            }
            Capture(root,"compact-preview.png");
        }
        void AddTaskbarMenu(ContextMenu menu,bool refreshOnOpen=true) {
            menu.Items.Add(new Separator());
            Menu(menu,"设置",AppSettings);
            AddDockStyleMenu(menu);
            var positionLock=new MenuItem{Header="锁定紧凑栏位置",IsCheckable=true,IsChecked=prefs.DockPositionLocked};menu.Items.Add(positionLock);
            positionLock.Click+=(s,e)=>{prefs.DockPositionLocked=positionLock.IsChecked;var dock=dashboardHost!=null?this:dockHost;if(dock!=null)dock.RememberDockPosition();Save();RefreshDockPreferences();};
            var dockHide=new MenuItem{Header="窗口最大化或全屏时自动隐藏紧凑栏",IsCheckable=true,IsChecked=prefs.AutoHideDock};menu.Items.Add(dockHide);
            dockHide.Click+=(s,e)=>{prefs.AutoHideDock=dockHide.IsChecked;RefreshDockPreferences();Save();};
            var full=new MenuItem{Header="运行时完全隐藏 Windows 任务栏",IsCheckable=true};menu.Items.Add(full);
            full.Click+=(s,e)=>{SetFullTaskbarHide(full.IsChecked);full.IsChecked=(dashboardHost??this).taskbarGuard.Active;};
            var toggle=new MenuItem{Header="自动隐藏 Windows 任务栏",IsCheckable=true};
            menu.Items.Add(toggle);
            Action sync=()=>{positionLock.IsChecked=prefs.DockPositionLocked;dockHide.IsChecked=prefs.AutoHideDock;full.IsChecked=(dashboardHost??this).taskbarGuard.Active;toggle.IsChecked=WindowsTaskbar.AutoHide;toggle.IsEnabled=!full.IsChecked;};sync();
            if(refreshOnOpen)menu.Opened+=(s,e)=>sync();
            toggle.Click+=(s,e)=>{
                if(!WindowsTaskbar.SetAutoHide(toggle.IsChecked))MessageBox.Show(this,"任务栏设置未生效，请在 Windows 设置 → 个性化 → 任务栏 → 任务栏行为中修改。","WinBoard");
                toggle.IsChecked=WindowsTaskbar.AutoHide;
            };
            Menu(menu,"退出 WinBoard 并恢复任务栏",Quit);
        }
        void SetFullTaskbarHide(bool enabled) {
            if(dashboardHost!=null){dashboardHost.SetFullTaskbarHide(enabled);return;}
            try {if(enabled)taskbarGuard.Start();else taskbarGuard.Dispose();prefs.HideTaskbarWhileRunning=enabled;RefreshDockPreferences();Save();}
            catch(Exception ex){prefs.HideTaskbarWhileRunning=false;Save();MessageBox.Show(this,"未能隐藏任务栏："+ex.Message,"WinBoard");}
        }
        void RememberDockPosition(){if(compact){prefs.DockX=Left;prefs.DockY=(dockAutoHidden||dockAutoAnimating?dockVisibleTop:Top)+dockTopPadding;}}
    }
}
