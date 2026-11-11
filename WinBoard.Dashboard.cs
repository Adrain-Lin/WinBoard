using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WinBoard {
    [DataContract] public class Shortcut {
        [DataMember] public string Name;
        [DataMember] public string Path;
    }
    [DataContract] public class Project {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name;
        [DataMember] public string Description;
        [DataMember] public string Color = "#B8A4E8";
        [DataMember] public string Folder;
        [DataMember] public List<Shortcut> Items = new List<Shortcut>();
    }
    [DataContract] public class Preferences {
        [DataMember] public double X = -1;
        [DataMember] public double Y = -1;
        [DataMember] public double Opacity = .92;
        [DataMember] public bool Topmost;
        [DataMember] public string Theme = "深色";
        [DataMember] public List<Shortcut> Items = new List<Shortcut>();
        [DataMember] public List<Project> Projects = new List<Project>();
        [DataMember] public string Selected;
        [DataMember] public double Width = 1160;
        [DataMember] public double Height = 760;
        [DataMember] public string Wallpaper;
        [DataMember] public int Version = 2;
        [DataMember] public WidgetPreferences Widgets;
        [DataMember] public Appearance DashboardAppearance;
        [DataMember] public Appearance CompactAppearance;
        [DataMember] public bool SystemSettingsAdded;
        [DataMember] public int DockStyle=1;
        [DataMember] public int GlassVersion;
        [DataMember] public List<Shortcut> DockItems;
        [DataMember] public List<Shortcut> DockSystemItems;
        [DataMember] public bool HideTaskbarWhileRunning;
        [DataMember] public bool AutoHideDock;
        [DataMember] public bool DockPositionLocked;
        [DataMember] public double? DockX;
        [DataMember] public double? DockY;
        [DataMember] public List<Shortcut> DockLayout;
    }
    public static class Storage {
        public static Preferences Normalize(Preferences p) {
            if(p.DockStyle!=2)p.DockStyle=1;
            p.DashboardAppearance=Appearance.Normalize(p.DashboardAppearance,p.Theme);
            p.CompactAppearance=Appearance.Normalize(p.CompactAppearance,p.Theme);
            p.Items = p.Items ?? new List<Shortcut>();
            p.Widgets = p.Widgets ?? new WidgetPreferences();
            p.Widgets.Normalize();
            p.Projects = p.Projects ?? new List<Project>();
            if (p.Version < 2) {
                p.Projects.Add(new Project { Name="常用入口", Description="从原来的快捷栏迁移，原文件保持不变。", Items=p.Items });
                p.Items = new List<Shortcut>(); p.X=-1; p.Y=-1; p.Width=1160; p.Height=760; p.Opacity=.92; p.Version=2;
            }
            foreach (var item in p.Projects) {
                if (string.IsNullOrEmpty(item.Id)) item.Id=Guid.NewGuid().ToString("N");
                item.Items=item.Items??new List<Shortcut>();
                item.Items.RemoveAll(i=>i==null || string.IsNullOrWhiteSpace(i.Path));
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name="未命名项目";
                if (string.IsNullOrEmpty(item.Color)) item.Color="#B8A4E8";
            }
            p.Width=Math.Max(900,p.Width); p.Height=Math.Max(600,p.Height);
            p.Opacity=Math.Max(.55,Math.Min(.98,p.Opacity));
            if (!p.Projects.Any(i=>i.Id==p.Selected)) p.Selected=null;
            if(p.DockItems==null)p.DockItems=(p.Selected==null?p.Projects.SelectMany(project=>project.Items):p.Projects.First(project=>project.Id==p.Selected).Items).Take(5).Select(i=>new Shortcut{Name=i.Name,Path=i.Path}).ToList();
            p.DockItems.RemoveAll(i=>i==null || string.IsNullOrWhiteSpace(i.Path));
            var systemItems=new List<Shortcut>();
            foreach(var entry in p.DockSystemItems??new List<Shortcut>()) {
                if(entry==null)continue;
                string path=ShellEntries.Canonical(entry.Path);
                if((path==ShellEntries.Computer || path==DockEntries.Dashboard || path==ShellEntries.RecycleBin) && !systemItems.Any(i=>i.Path==path))
                    systemItems.Add(new Shortcut{Path=path,Name=path==DockEntries.Dashboard?"打开工作台":ShellEntries.Name(path)});
            }
            foreach(string path in new[]{ShellEntries.Computer,DockEntries.Dashboard,ShellEntries.RecycleBin})
                if(!systemItems.Any(i=>i.Path==path))systemItems.Add(new Shortcut{Path=path,Name=path==DockEntries.Dashboard?"打开工作台":ShellEntries.Name(path)});
            p.DockSystemItems=systemItems;
            if(p.DockLayout==null) {
                p.DockLayout=p.DockItems.Where(i=>ShellEntries.Canonical(i.Path)!=ShellEntries.Computer && ShellEntries.Canonical(i.Path)!=ShellEntries.RecycleBin).Select(i=>new Shortcut{Name=i.Name,Path=i.Path}).ToList();
                p.DockLayout.Add(new Shortcut{Name="分隔线",Path=DockEntries.Separator});
                p.DockLayout.AddRange(systemItems.Select(i=>new Shortcut{Name=i.Name,Path=i.Path}));
            }
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            p.DockLayout.RemoveAll(i=>i==null||string.IsNullOrWhiteSpace(i.Path)||!seen.Add(ShellEntries.Canonical(i.Path)));
            if(!p.SystemSettingsAdded){if(!p.DockLayout.Any(i=>i.Path==ShellEntries.Settings)){int at=p.DockLayout.FindIndex(i=>i.Path==ShellEntries.RecycleBin);p.DockLayout.Insert(at<0?p.DockLayout.Count:at,new Shortcut{Path=ShellEntries.Settings,Name="Windows 设置"});}p.SystemSettingsAdded=true;}
            return p;
        }
        public static int AddDockPaths(Preferences p,IEnumerable<string> paths) {
            int added=0;
            foreach(string value in paths) {
                string path=ShellEntries.Canonical((value??"").Trim().Trim('"'));
                if((ShellEntries.Name(path)==null && !File.Exists(path)&&!Directory.Exists(path)) || p.DockItems.Any(i=>string.Equals(i.Path,path,StringComparison.OrdinalIgnoreCase)))continue;
                string name=ShellEntries.Name(path)??System.IO.Path.GetFileName(path.TrimEnd('\\'));
                p.DockItems.Add(new Shortcut{Name=string.IsNullOrEmpty(name)?path:name,Path=path});added++;
            }
            return added;
        }
        public static Preferences Read(string path) {
            using (var s=File.OpenRead(path)) return Normalize((Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(s));
        }
        public static void Write(string path, Preferences prefs) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temp=path+".tmp";
            using (var s=File.Create(temp)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(s,prefs);
            if (File.Exists(path)) File.Replace(temp,path,path+".bak",true); else File.Move(temp,path);
        }
        public static bool Reorder(Project project, Shortcut item, Shortcut target) {
            int from=project.Items.IndexOf(item), to=project.Items.IndexOf(target);
            if(from<0 || to<0 || from==to)return false;
            project.Items.RemoveAt(from);project.Items.Insert(to,item);return true;
        }
        public static bool ReorderProjects(Preferences prefs,Project item,Project target,bool after) {
            if(item==null||target==null||item==target||!prefs.Projects.Contains(item)||!prefs.Projects.Contains(target))return false;
            return DockOrder.Insert(prefs.Projects,item,prefs.Projects.IndexOf(target)+(after?1:0));
        }
        public static Project Create(Preferences prefs,string name,string description,string color,string folder) {
            name=(name??"").Trim();
            if (name.Length==0) throw new ArgumentException("请填写项目名称。");
            if (prefs.Projects.Any(p=>string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("已存在同名项目，请换一个名称。");
            var project=new Project {Name=name, Description=description??"", Color=color, Folder=folder};
            prefs.Projects.Add(project); prefs.Selected=project.Id; return project;
        }
    }
    public partial class Board : Window {
        static bool preview;
        static readonly string Config=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","settings.json");
        static readonly string[] Colors={"#B8A4E8","#83C9AE","#E5BC7B","#E79C9E","#B5C976","#D5B9A8"};
        Preferences prefs;
        Grid root, body;
        Border surface, side;
        StackPanel navigation, main;
        TextBox search;
        TextBlock date;
        Forms.NotifyIcon tray;
        bool quitting, compact, switchingMode;
        WindowState dashboardState=WindowState.Normal;
        System.Windows.Threading.DispatcherTimer timer;
        Dictionary<string,BitmapSource> icons=new Dictionary<string,BitmapSource>(StringComparer.OrdinalIgnoreCase);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ShellInfo {
            public IntPtr Icon; public int Index; public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Display;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type;
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,ref ShellInfo info,uint size,uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        [STAThread] public static void Main(string[] args) {
            Directory.CreateDirectory(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"));
            if(args.Contains("--power-after-exit")){PowerExitHelper.Run(args);return;}
            if(args.Contains("--enable-startup")){StartupRegistration.SetEnabled(true);return;}
            if(args.Contains("--disable-startup")){StartupRegistration.SetEnabled(false);return;}
            if(args.Contains("--taskbar-guard")){TaskbarGuard.Run(args);return;}
            preview=args.Contains("--preview");
            if (args.Contains("--check")) { Check(); return; }
            bool first;
            using (var mutex=new Mutex(true,@"Local\WinBoardSet.v1",out first)) {
                if (!first) {if(args.Contains("--startup"))return;var hwnd=FindWindow(null,"WinBoard"); if(hwnd!=IntPtr.Zero){if(args.Contains("--exit"))PostMessage(hwnd,0x8001,IntPtr.Zero,IntPtr.Zero);else{ShowWindow(hwnd,9);SetForegroundWindow(hwnd);}} return;}
                if(args.Contains("--exit"))return;
                var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                bool showingError=false;
                app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;try{File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","last-error.txt"),e.Exception.ToString());}catch{}if(systemSessionEnding||showingError)return;showingError=true;try{MessageBox.Show("操作未完成："+e.Exception.Message,"WinBoard");}finally{showingError=false;}};
                var board=new Board();if(args.Contains("--startup")){board.ShowActivated=false;EventHandler ready=null;ready=(s,e)=>{board.ContentRendered-=ready;board.EnterCompact();};board.ContentRendered+=ready;}app.Run(board);
            }
        }
        static void Check() {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"WinBoard-check-"+Guid.NewGuid().ToString("N"));
            string file=System.IO.Path.Combine(dir,"settings.json");Directory.CreateDirectory(dir);
            try {
                string parsedColor;
                if(!TryProjectColor(" #aBc ",out parsedColor)||parsedColor!="#AABBCC"||!TryProjectColor("12ab34",out parsedColor)||parsedColor!="#12AB34")throw new Exception("自定义项目颜色规范化失败");
                foreach(var invalidColor in new[]{"","##ABC","#12345","#12345678","red","GGGGGG"})if(TryProjectColor(invalidColor,out parsedColor))throw new Exception("无效项目颜色被接受");
                File.WriteAllText(file,"{\"Items\":[{\"Name\":\"旧入口\",\"Path\":\"C:\\\\Windows\"}],\"Opacity\":0.5,\"X\":900,\"Y\":900}");
                var p=Storage.Read(file);
                if(p.Projects.Count!=1 || p.Projects[0].Items.Count!=1 || p.X!=-1) throw new Exception("迁移失败");
                var item=Storage.Create(p,"研究项目","说明",Colors[1],null);
                item.Items.Add(new Shortcut{Name="资料",Path=@"C:\Windows"});item.Items.Add(new Shortcut{Name="网站",Path="https://example.org"});
                bool duplicate=false;try{Storage.Create(p,"研究项目","",Colors[0],null);}catch(ArgumentException){duplicate=true;}
                if(!duplicate) throw new Exception("重复名称校验失败");
                Storage.Write(file,p);p=Storage.Read(file);
                if(p.Projects[1].Items.Count!=2 || p.Selected!=item.Id) throw new Exception("保存失败");
                var firstShortcut=p.Projects[1].Items[0];var lastShortcut=p.Projects[1].Items[1];
                if(!Storage.Reorder(p.Projects[1],firstShortcut,lastShortcut) || p.Projects[1].Items[1]!=firstShortcut)throw new Exception("向后拖动排序失败");
                if(!Storage.Reorder(p.Projects[1],firstShortcut,lastShortcut) || p.Projects[1].Items[0]!=firstShortcut)throw new Exception("向前拖动排序失败");
                if(Storage.Reorder(p.Projects[1],firstShortcut,firstShortcut) || Storage.Reorder(p.Projects[1],firstShortcut,new Shortcut()))throw new Exception("无效拖动校验失败");
                Storage.Reorder(p.Projects[1],firstShortcut,lastShortcut);Storage.Write(file,p);p=Storage.Read(file);
                if(p.Projects[1].Items[1].Name!=firstShortcut.Name)throw new Exception("拖动顺序保存失败");
                p.Projects[1].Name="新名称";Storage.Write(file,p);p=Storage.Read(file);
                if(p.Projects[1].Name!="新名称") throw new Exception("重命名失败");
                if(p.DashboardAppearance==null || p.CompactAppearance==null || p.Widgets.Windows.Any(w=>w.Appearance==null))throw new Exception("旧配置外观迁移失败");
                p.DashboardAppearance.Opacity=.4;p.CompactAppearance.Color="#233E46";
                p.Widgets.Windows.First(w=>w.Kind=="clock").Appearance.Material="纯色";
                Storage.Write(file,p);p=Storage.Read(file);
                if(p.DashboardAppearance.Opacity!=.4 || p.CompactAppearance.Opacity!=.92 || p.CompactAppearance.Color!="#233E46" || p.Widgets.Windows.First(w=>w.Kind=="clock").Appearance.Material!="纯色" || p.Widgets.Windows.First(w=>w.Kind=="weather").Appearance.Material!="渐变")throw new Exception("独立外观保存失败");
                p.Widgets.Windows.First(w=>w.Kind=="control").Appearance.Material="液态玻璃";
                Storage.Write(file,p);p=Storage.Read(file);
                if(p.Widgets.Windows.First(w=>w.Kind=="control").Appearance.Material!="液态玻璃")throw new Exception("玻璃材质保存失败");
                p.Projects.RemoveAt(1);Storage.Write(file,p);p=Storage.Read(file);
                if(p.Projects.Count!=1 || p.Selected!=null || !Directory.Exists(@"C:\Windows")) throw new Exception("移除失败");
                int projectCount=p.Projects[0].Items.Count;p.DockItems.Clear();
                string dockTest=System.IO.Path.Combine(dir,"dock-test.lnk");File.WriteAllText(dockTest,"test");
                if(Storage.AddDockPaths(p,new[]{dockTest,dockTest.ToUpperInvariant(),System.IO.Path.Combine(dir,"missing.exe")})!=1 || p.DockItems.Count!=1 || p.Projects[0].Items.Count!=projectCount)throw new Exception("紧凑栏添加或独立性校验失败");
                Storage.Write(file,p);p=Storage.Read(file);if(p.DockItems.Count!=1 || p.DockItems[0].Path!=dockTest)throw new Exception("紧凑栏保存失败");
                p.DockItems.Clear();Storage.Write(file,p);p=Storage.Read(file);if(p.DockItems.Count!=0)throw new Exception("空紧凑栏错误地重新导入项目");
                if(!BatteryLabel(Forms.PowerLineStatus.Online,Forms.BatteryChargeStatus.Charging|Forms.BatteryChargeStatus.High,.99f).Contains("正在充电") || !BatteryLabel(Forms.PowerLineStatus.Online,Forms.BatteryChargeStatus.High,.99f).Contains("已接通电源") || !BatteryLabel(Forms.PowerLineStatus.Online,Forms.BatteryChargeStatus.High,1).Contains("已充满") || !BatteryLabel(Forms.PowerLineStatus.Offline,Forms.BatteryChargeStatus.Low,.2f).Contains("使用电池") || BatteryLabel(Forms.PowerLineStatus.Unknown,Forms.BatteryChargeStatus.Unknown,-1).Contains("正在充电"))throw new Exception("电池状态显示校验失败");
                p.DockItems.Clear();if(Storage.AddDockPaths(p,new[]{ShellEntries.Computer,ShellEntries.ControlPanel,"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"})!=2)throw new Exception("系统入口添加或去重失败");
                var dockGroup=new Project{Items=p.DockItems};var firstDock=p.DockItems[0];var lastDock=p.DockItems[1];
                if(!Storage.Reorder(dockGroup,firstDock,lastDock) || p.DockItems[1]!=firstDock)throw new Exception("紧凑栏向右排序失败");
                if(!Storage.Reorder(dockGroup,firstDock,lastDock) || p.DockItems[0]!=firstDock)throw new Exception("紧凑栏向左排序失败");
                Storage.Write(file,p);p=Storage.Read(file);if(p.DockItems.Count!=2 || p.DockItems[0].Name!="此电脑" || p.DockItems[1].Path!=ShellEntries.ControlPanel)throw new Exception("系统入口保存失败");
                foreach(string parsing in new[]{"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}","::{26EE0668-A00A-44D7-9371-BEB064C98683}","::{645FF040-5081-101B-9F08-00AA002F954E}"}) {
                    IntPtr testPidl;uint attributes;if(SHParseDisplayName(parsing,IntPtr.Zero,out testPidl,0,out attributes)!=0)throw new Exception("系统入口解析失败");
                    try {int length=0;while(Marshal.ReadInt16(testPidl,length)!=0)length+=(ushort)Marshal.ReadInt16(testPidl,length);length+=2;
                        byte[] cida=new byte[14+length];Buffer.BlockCopy(BitConverter.GetBytes(1),0,cida,0,4);Buffer.BlockCopy(BitConverter.GetBytes(12),0,cida,4,4);Buffer.BlockCopy(BitConverter.GetBytes(14),0,cida,8,4);Marshal.Copy(testPidl,cida,14,length);
                        var shellDrop=new DataObject("Shell IDList Array",new MemoryStream(cida));if(!ShellEntries.DropPaths(shellDrop).Contains(ShellEntries.Canonical(parsing)))throw new Exception("系统图标拖入解析失败");
                    }finally{Marshal.FreeCoTaskMem(testPidl);}
                }
                if(!IsDockFixed(ShellEntries.Computer) || !IsDockFixed(ShellEntries.RecycleBin) || IsDockFixed(ShellEntries.ControlPanel))throw new Exception("Dock固定分区校验失败");
                var systemGroup=new Project{Items=p.DockSystemItems};var computer=p.DockSystemItems[0];var recycle=p.DockSystemItems[2];
                if(!Storage.Reorder(systemGroup,computer,recycle) || p.DockSystemItems[2]!=computer)throw new Exception("右侧图标向右排序失败");
                Storage.Write(file,p);p=Storage.Read(file);
                if(p.DockSystemItems[2].Path!=ShellEntries.Computer || p.DockSystemItems[0].Path!=DockEntries.Dashboard)throw new Exception("右侧图标顺序保存失败");
                if(!Storage.Reorder(new Project{Items=p.DockSystemItems},p.DockSystemItems[2],p.DockSystemItems[0]) || p.DockSystemItems[0].Path!=ShellEntries.Computer)throw new Exception("右侧图标向左排序失败");
                p.DockSystemItems=new List<Shortcut>{new Shortcut{Path=ShellEntries.RecycleBin},null,new Shortcut{Path=ShellEntries.RecycleBin},new Shortcut{Path="invalid"}};
                Storage.Normalize(p);
                if(p.DockSystemItems.Count!=3 || p.DockSystemItems[0].Path!=ShellEntries.RecycleBin || p.DockSystemItems[2].Path!=DockEntries.Dashboard)throw new Exception("右侧图标配置修复失败");
                var malformed=new DataObject("Shell IDList Array",new MemoryStream(new byte[]{255,255,255,255,0,0,0,0,0,0,0,0}));if(ShellEntries.DropPaths(malformed).Any())throw new Exception("无效Shell拖入处理失败");
                p.DockStyle=2;var clearWidget=p.Widgets.Windows.First(w=>w.Kind=="clock");clearWidget.Appearance.Opacity=0;clearWidget.Appearance.HideBorder=true;clearWidget.HideHeader=true;
                Storage.Write(file,p);p=Storage.Read(file);clearWidget=p.Widgets.Windows.First(w=>w.Kind=="clock");
                if(p.DockStyle!=2||clearWidget.Appearance.Opacity!=0||!clearWidget.Appearance.HideBorder||!clearWidget.HideHeader)throw new Exception("倒影样式或组件透明外观保存失败");
                p.DockStyle=99;Storage.Normalize(p);if(p.DockStyle!=1)throw new Exception("紧凑栏样式迁移失败");
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","self-test.txt"),"PASS: legacy migration, create, duplicate validation, shortcut persistence, rename, removal, selection recovery, atomic backup, drag reorder forward/backward/invalid/persistence, appearance migration/independence/persistence, liquid glass persistence, dock add/dedup/independence/persistence/empty, charging/plugged/full/battery/unknown, dock reorder, shell entries/dedup/persistence/native-pidl-drop/malformed-drop, fixed dock grouping/recycle-bin, right dock reorder/persistence/repair\r\n"+DateTime.Now.ToString("s"));
            } catch(Exception ex) { File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","self-test.txt"),ex.ToString());Environment.ExitCode=1; }
            finally {foreach(var path in Directory.GetFiles(dir))File.Delete(path); Directory.Delete(dir);}
        }
        public Board() {
            if(File.Exists(Config)) {
                try { prefs=Storage.Read(Config); }
                catch(Exception ex) {MessageBox.Show("配置读取失败，请先保留 data 文件夹。\n"+ex.Message);throw;}
            } else {
                prefs=new Preferences();var p=Storage.Create(prefs,"常用入口","日常使用的应用、文件与文件夹",Colors[0],null);
                p.Items.Add(new Shortcut{Name="桌面",Path=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)});
                p.Items.Add(new Shortcut{Name="文档",Path=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)});prefs.Selected=null;
            }
            prefs=Storage.Normalize(prefs);
            if(prefs.GlassVersion<1){
                foreach(var style in new[]{prefs.DashboardAppearance,prefs.CompactAppearance}.Concat(prefs.Widgets.Windows.Select(w=>w.Appearance))){style.Material="液态玻璃";style.Opacity=.38;}
                prefs.GlassVersion=1;
            }
            Title="WinBoard";Icon=AppBrand.Image;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=false;Background=Brushes.Transparent;
            MinWidth=900;MinHeight=600;Width=prefs.Width;Height=prefs.Height;Topmost=prefs.Topmost;ShowInTaskbar=true;
            WindowChrome.SetWindowChrome(this,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(7),CornerRadius=new CornerRadius(22),GlassFrameThickness=new Thickness(-1)});
            prefs.Widgets=prefs.Widgets??new WidgetPreferences();prefs.Widgets.Normalize();
            Place();Build();BuildTray();Loaded+=(s,e)=>{Save();Capture(root,"dashboard-preview.png");StartWidgets();if(prefs.HideTaskbarWhileRunning)SetFullTaskbarHide(true);EnsureDockWindow();};
            Application.Current.Exit+=(s,e)=>taskbarGuard.Dispose();
            Application.Current.SessionEnding+=(s,e)=>{e.Cancel=false;ExitForWindowsSession();};
            Activated+=(s,e)=>{if(dockHost!=null)Dispatcher.BeginInvoke(new Action(EnsureDockWindow));};
            SourceInitialized+=(s,e)=>InitializeCaptureHotkeys();
            SourceInitialized+=(s,e)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr h,int m,IntPtr wp,IntPtr lp,ref bool handled)=>{if(m==0x11){systemSessionEnding=true;Dispatcher.BeginInvoke(new Action(ExitForWindowsSession));handled=true;return new IntPtr(1);}if(m==0x8001){Dispatcher.BeginInvoke(new Action(Quit));handled=true;}else if(!quitting && (m==0x10 || (m==0x112 && (wp.ToInt64() & 0xFFF0)==0xF020))){handled=true;Dispatcher.BeginInvoke(new Action(EnterCompact));}return IntPtr.Zero;});
            Closing+=(s,e)=>{if(!quitting){e.Cancel=true;Dispatcher.BeginInvoke(new Action(EnterCompact));} };
            StateChanged+=(s,e)=>{
                if(quitting || switchingMode)return;
                if(WindowState==WindowState.Minimized)Dispatcher.BeginInvoke(new Action(()=>{if(!quitting && WindowState==WindowState.Minimized)EnterCompact();}));
                else if(!compact)dashboardState=WindowState;
            };
            KeyDown+=(s,e)=>{
                if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.N){EditProject(null);e.Handled=true;}
                if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.F && search!=null){search.Focus();e.Handled=true;}
                if(e.Key==Key.Escape && compact){SwitchMode();e.Handled=true;}
            };
            timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(30)};
            timer.Tick+=(s,e)=>{if(date!=null)date.Text=DateTime.Now.ToString(compact?"HH:mm":"M月d日  dddd   HH:mm");};timer.Start();
        }
        bool Light {get{return ActiveAppearance.Theme=="浅色";}}
        Brush TextColor {get{return Hex(Light?"#252936":"#F3F0F7");}}
        Brush Muted {get{return Hex(Light?"#666777":"#B1ABB9");}}
        Brush CardColor {get{if(ActiveAppearance.Material=="液态玻璃")return Hex(Light?"#35FFFFFF":"#262B2835");return new SolidColorBrush(Color.FromArgb(Light?(byte)215:(byte)155,Light?(byte)255:(byte)49,Light?(byte)255:(byte)45,Light?(byte)255:(byte)60));}}
        Brush Line {get{return Hex(Light?"#DDD8E6":"#514958");}}
        static SolidColorBrush Hex(string color) {try{return (SolidColorBrush)new BrushConverter().ConvertFromString(color);}catch{return new SolidColorBrush(Color.FromRgb(184,164,232));}}
        TextBlock Text(string text,double size,Brush color) {return new TextBlock {Text=text,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};}
        Button Btn(string label,Action action,bool primary=false) {
            var b=new Button {Content=label,Foreground=primary?Hex("#201C29"):TextColor,Background=primary?Hex("#CEBDEB"):CardColor,
                BorderBrush=Line,BorderThickness=new Thickness(primary?0:1),Padding=new Thickness(15,10,15,10),Margin=new Thickness(0,0,8,0),Cursor=Cursors.Hand,FontSize=13};
            var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;b.Template=template;
            b.MouseEnter+=(s,e)=>b.Opacity=.8;b.MouseLeave+=(s,e)=>b.Opacity=1;b.Click+=(s,e)=>action();return b;
        }
        void Place() {
            var a=SystemParameters.WorkArea;Width=Math.Min(prefs.Width,a.Width-32);Height=Math.Min(prefs.Height,a.Height-32);
            Left=prefs.X<0?a.Left+(a.Width-Width)/2:Math.Max(a.Left,Math.Min(prefs.X,a.Right-Width));Top=prefs.Y<0?a.Top+(a.Height-Height)/2:Math.Max(a.Top,Math.Min(prefs.Y,a.Bottom-Height));
        }
        void Save() {
            if(!compact && WindowState==WindowState.Normal){prefs.X=Left;prefs.Y=Top;prefs.Width=Width;prefs.Height=Height;}
            try{Storage.Write(Config,prefs);}catch(Exception ex){if(!systemSessionEnding)MessageBox.Show(this,"保存失败："+ex.Message,"WinBoard");}
        }
        void DragHeader(object s,MouseButtonEventArgs e) {
            if(compact&&prefs.DockPositionLocked){e.Handled=true;return;}
            if(e.ClickCount==2 && !compact){WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;return;}
            try{DragMove();if(compact)RememberDockPosition();Save();}catch(InvalidOperationException){}
        }
        void Build() {
            if(!compact)Topmost=prefs.Topmost;
            root=new Grid();root.Background=AppearanceBrush(ActiveAppearance);
            if(!compact && !string.IsNullOrEmpty(prefs.Wallpaper) && File.Exists(prefs.Wallpaper)) {
                try{var bmp=new BitmapImage();bmp.BeginInit();bmp.UriSource=new Uri(prefs.Wallpaper);bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.EndInit();root.Background=new ImageBrush(bmp){Stretch=Stretch.UniformToFill,Opacity=ActiveAppearance.Opacity};}catch{}
            }
            surface=new Border {CornerRadius=new CornerRadius(22),BorderBrush=Line,BorderThickness=new Thickness(1),Child=root,Background=root.Background};root.Background=null;Content=surface;
            DecorateGlass(surface,ActiveAppearance);ConfigureGlass(this,()=>ActiveAppearance);
            Capture(root,"dashboard-preview.png");
            if(compact){BuildCompact();return;}
            var windowMenu=new ContextMenu();AddTaskbarMenu(windowMenu);surface.ContextMenu=windowMenu;
            Dispatcher.BeginInvoke(new Action(()=>{if(!compact)AddResizeEdges();}));
            body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(63)});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(32)});root.Children.Add(body);
            var header=new Grid {Margin=new Thickness(24,0,14,0),Background=Brushes.Transparent};header.MouseLeftButtonDown+=DragHeader;
            var branding=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};branding.Children.Add(new Image{Source=AppBrand.Image,Width=30,Height=30,Margin=new Thickness(0,0,10,0)});var brand=Text("WINBOARD",16,TextColor);brand.FontWeight=FontWeights.SemiBold;brand.VerticalAlignment=VerticalAlignment.Center;branding.Children.Add(brand);header.Children.Add(branding);
            var controls=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
            controls.Children.Add(DockButton("settings","设置",AppSettings));controls.Children.Add(DockButton("widgets","桌面组件",WidgetManager));controls.Children.Add(DockButton("appearance","外观",Settings));controls.Children.Add(Btn("—",EnterCompact));controls.Children.Add(Btn("□",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));controls.Children.Add(Btn("×",EnterCompact));header.Children.Add(controls);body.Children.Add(header);
            var area=new Grid();area.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(225)});area.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(area,1);body.Children.Add(area);
            side=new Border {Margin=new Thickness(14,0,0,8),Padding=new Thickness(16),CornerRadius=new CornerRadius(16),Background=CardColor};area.Children.Add(side);
            var navLayout=new DockPanel();side.Child=navLayout;
            var newProject=Btn("＋  新建项目",()=>EditProject(null),true);newProject.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(newProject,Dock.Top);navLayout.Children.Add(newProject);
            var hint=Text("Ctrl + N  新建项目\nCtrl + F  搜索入口\n拖入文件即可添加",11,Muted);hint.LineHeight=23;hint.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(hint,Dock.Bottom);navLayout.Children.Add(hint);
            navigation=new StackPanel();navLayout.Children.Add(new ScrollViewer {Content=navigation,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var right=new Grid {Margin=new Thickness(30,10,28,8)};Grid.SetColumn(right,1);area.Children.Add(right);right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(48)});right.RowDefinitions.Add(new RowDefinition());
            var top=new Grid();date=Text(DateTime.Now.ToString("M月d日  dddd   HH:mm"),12,Muted);date.VerticalAlignment=VerticalAlignment.Top;date.Margin=new Thickness(0,11,0,0);top.Children.Add(date);
            search=new TextBox {Width=245,Height=35,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(12,7,12,5),Background=CardColor,Foreground=TextColor,CaretBrush=TextColor,BorderBrush=Line,ToolTip="搜索入口名称、路径或项目名称"};
            System.Windows.Automation.AutomationProperties.SetName(search,"搜索入口");search.TextChanged+=(s,e)=>RenderMain();top.Children.Add(search);
            var placeholder=Text("搜索名称或路径…",12,Muted);placeholder.HorizontalAlignment=HorizontalAlignment.Right;placeholder.VerticalAlignment=VerticalAlignment.Top;placeholder.Width=220;placeholder.Margin=new Thickness(0,8,12,0);placeholder.IsHitTestVisible=false;top.Children.Add(placeholder);search.TextChanged+=(s,e)=>placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;right.Children.Add(top);
            main=new StackPanel();var scroll=new ScrollViewer {Content=main,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);right.Children.Add(scroll);
            var status=Text("已保存在本机 · 右键入口可重命名、移动或移除",11,Muted);status.Margin=new Thickness(28,4,0,0);Grid.SetRow(status,2);body.Children.Add(status);RenderNav();RenderMain();
        }
        Project Current {get{return prefs.Projects.FirstOrDefault(p=>p.Id==prefs.Selected);}}
        void Select(Project project) {prefs.Selected=project==null?null:project.Id;search.Text="";Save();RenderNav();RenderMain();}
        void RenderNav() {
            navigation.Children.Clear();var all=Btn("▦   我的工作台",()=>Select(null));all.Margin=new Thickness(0,0,0,20);if(Current==null)all.Background=Hex(Light?"#E2D6F2":"#524362");navigation.Children.Add(all);
            var label=Text("项目  /  "+prefs.Projects.Count,11,Muted);label.Margin=new Thickness(8,0,0,12);navigation.Children.Add(label);
            foreach(var p in prefs.Projects) {
                var item=p;var b=Btn("",()=>Select(item));var caption=Text("●   "+p.Name+"   "+p.Items.Count,13,Hex(p.Color));caption.Width=145;caption.TextWrapping=TextWrapping.NoWrap;caption.TextTrimming=TextTrimming.CharacterEllipsis;b.Content=caption;System.Windows.Automation.AutomationProperties.SetName(b,"项目："+p.Name);b.Foreground=Hex(p.Color);b.Margin=new Thickness(0,0,0,8);if(p==Current)b.Background=Hex(Light?"#E2D6F2":"#524362");b.ToolTip=p.Name;
                b.ContextMenu=ProjectMenu(item);EnableProjectDrag(b,item,true);navigation.Children.Add(b);
            }
        }
        void RenderMain() {
            if(main==null || compact)return;main.Children.Clear();var current=Current;string query=search==null?"":search.Text.Trim();
            var eyebrow=Text(current==null?"YOUR PERSONAL SPACE":"PROJECT SPACE",11,Hex(current==null?Colors[1]:current.Color));eyebrow.Margin=new Thickness(0,8,0,10);main.Children.Add(eyebrow);
            var title=Text(current==null?"把事情，放在顺手的地方。":current.Name,30,TextColor);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,10);main.Children.Add(title);
            string desc=current==null?"按项目收纳应用、资料与链接，让桌面成为你的工作台。":(string.IsNullOrWhiteSpace(current.Description)?"在这里收纳这个项目需要的文件、应用与链接。":current.Description);
            var subtitle=Text(desc,13,Muted);subtitle.Margin=new Thickness(0,0,0,22);main.Children.Add(subtitle);
            var actions=new WrapPanel {Margin=new Thickness(0,0,0,22)};
            if(current==null) actions.Children.Add(Btn("＋  新建项目",()=>EditProject(null),true));
            else {actions.Children.Add(Btn("＋  添加文件",()=>PickFiles(current),true));actions.Children.Add(Btn("添加文件夹",()=>PickFolder(current)));actions.Children.Add(Btn("添加网址",()=>EditLink(current,null)));actions.Children.Add(Btn("编辑项目",()=>EditProject(current)));if(!string.IsNullOrEmpty(current.Folder))actions.Children.Add(Btn("打开项目目录",()=>Launch(current.Folder)));}main.Children.Add(actions);
            if(current==null && query.Length==0) {
                var projects=new WrapPanel {Margin=new Thickness(0,0,0,22)};
                foreach(var p in prefs.Projects) {
                    var item=p;var card=new StackPanel();card.Children.Add(Text("●   项目",11,Hex(p.Color)));var name=Text(p.Name,18,TextColor);name.FontWeight=FontWeights.SemiBold;name.Margin=new Thickness(0,14,0,8);card.Children.Add(name);card.Children.Add(Text(p.Items.Count+" 个入口   ·   点击进入 →",11,Muted));
                    var button=Btn("",()=>Select(item));button.Content=card;button.Width=218;button.MinHeight=132;button.Padding=new Thickness(18);button.Margin=new Thickness(0,0,12,12);System.Windows.Automation.AutomationProperties.SetName(button,"项目卡片："+item.Name);button.ToolTip="拖动排序，或右键向前 / 向后移动";button.ContextMenu=ProjectMenu(item);EnableProjectDrag(button,item,false);projects.Children.Add(button);
                }
                var create=Btn("＋\n创建新的项目空间",()=>EditProject(null));create.Width=218;create.Height=132;create.Margin=new Thickness(0,0,12,12);projects.Children.Add(create);main.Children.Add(projects);
            }
            var all=prefs.Projects.SelectMany(p=>p.Items.Select(i=>new KeyValuePair<Project,Shortcut>(p,i))).Where(x=>current==null || x.Key==current).Where(x=>query.Length==0 || (x.Value.Name+" "+x.Value.Path+" "+x.Key.Name).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            var count=Text((query.Length>0?"搜索结果":current==null?"全部入口":"项目入口")+"  /  "+all.Count,13,Muted);count.Margin=new Thickness(0,0,0,14);main.Children.Add(count);var tiles=new WrapPanel();main.Children.Add(tiles);
            foreach(var pair in all) {
                var p=pair.Key;var item=pair.Value;var content=new StackPanel();var icon=GetIcon(item.Path);
                if(icon!=null)content.Children.Add(new Image{Source=icon,Width=38,Height=38,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,13)});
                else {var glyph=Text(IsWeb(item.Path)?"↗":"▰",30,Hex(p.Color));glyph.Margin=new Thickness(0,0,0,10);content.Children.Add(glyph);}
                var name=Text(item.Name,14,TextColor);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;content.Children.Add(name);
                var detail=Text(current==null?p.Name:IsWeb(item.Path)?"网页链接":Directory.Exists(item.Path)?"文件夹":"应用 / 文件",11,Muted);detail.Margin=new Thickness(0,6,0,0);detail.TextWrapping=TextWrapping.NoWrap;detail.TextTrimming=TextTrimming.CharacterEllipsis;content.Children.Add(detail);
                var b=Btn("",()=>Launch(item.Path));b.Width=158;b.Height=146;b.Margin=new Thickness(0,0,12,12);b.Padding=new Thickness(17);b.Content=content;b.ToolTip=item.Path;System.Windows.Automation.AutomationProperties.SetName(b,item.Name);
                var menu=new ContextMenu();Menu(menu,"打开",()=>Launch(item.Path));Menu(menu,"重命名",()=>RenameShortcut(p,item));if(IsWeb(item.Path))Menu(menu,"修改网址",()=>EditLink(p,item));if(!IsWeb(item.Path))Menu(menu,"在资源管理器中显示",()=>Reveal(item.Path));
                var move=new MenuItem{Header="移动到项目"};foreach(var destination in prefs.Projects.Where(d=>d!=p)) {var target=destination;Menu(move,"● "+target.Name,()=>{if(!target.Items.Any(i=>string.Equals(i.Path,item.Path,StringComparison.OrdinalIgnoreCase)))target.Items.Add(item);p.Items.Remove(item);Refresh();});}move.IsEnabled=move.Items.Count>0;menu.Items.Add(move);
                Menu(menu,"向前移动",()=>Move(p,item,-1));Menu(menu,"向后移动",()=>Move(p,item,1));Menu(menu,"移除此入口",()=>{p.Items.Remove(item);Refresh();});b.ContextMenu=menu;EnableShortcutDrag(b,p,item);tiles.Children.Add(b);
            }
            if(all.Count==0) {
                var empty=new Border {CornerRadius=new CornerRadius(14),BorderBrush=Line,BorderThickness=new Thickness(1),Padding=new Thickness(24),MinHeight=150,Background=CardColor};var inner=new StackPanel();inner.Children.Add(Text(query.Length>0?"没有找到匹配的入口":"从一个文件开始",20,TextColor));
                var help=Text(query.Length>0?"试试更短的名称，或清空右上角的搜索框。":current==null?"先新建项目，再添加文件；你也可以把文件直接拖到工作台。":"拖入文件或文件夹，或使用上方按钮添加。\n这里保存的是快捷入口，不会移动你的原文件。",13,Muted);help.Margin=new Thickness(0,12,0,0);inner.Children.Add(help);empty.Child=inner;main.Children.Add(empty);
            }
            main.AllowDrop=true;main.Drop-=OnDrop;main.Drop+=OnDrop;main.DragOver-=OnDragOver;main.DragOver+=OnDragOver;
        }
        void Refresh(){Save();if(compact)Build();else {RenderNav();RenderMain();}Capture(root,"dashboard-preview.png"); }
        void Capture(FrameworkElement element,string name) {
            if(!preview)return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>{
                try {element.UpdateLayout();int width=(int)element.ActualWidth,height=(int)element.ActualHeight;if(width<1||height<1)return;
                    var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using(var stream=File.Create(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data",name)))encoder.Save(stream);
                }catch(Exception ex){Debug.WriteLine(ex);}
            }));
        }
        void Move(Project p,Shortcut item,int delta){int a=p.Items.IndexOf(item),b=a+delta;if(b<0 || b>=p.Items.Count)return;p.Items.RemoveAt(a);p.Items.Insert(b,item);Refresh();}
        ContextMenu ProjectMenu(Project project) {
            var menu=new ContextMenu();Menu(menu,"向前移动",()=>MoveProject(project,-1));Menu(menu,"向后移动",()=>MoveProject(project,1));
            menu.Items.Add(new Separator());Menu(menu,"编辑项目",()=>EditProject(project));Menu(menu,"移除项目分组",()=>DeleteProject(project));return menu;
        }
        void MoveProject(Project project,int direction) {
            int index=prefs.Projects.IndexOf(project),target=index+direction;
            if(index<0||target<0||target>=prefs.Projects.Count)return;
            DockOrder.Insert(prefs.Projects,project,target+(direction>0?1:0));Refresh();
        }
        void EnableProjectDrag(Button button,Project project,bool vertical) {
            const string format="WinBoard.Project";Point? origin=null;bool dragged=false;
            var border=button.BorderBrush;var thickness=button.BorderThickness;
            button.PreviewMouseLeftButtonDown+=(s,e)=>{origin=e.GetPosition(button);dragged=false;};
            button.PreviewMouseLeftButtonUp+=(s,e)=>{origin=null;if(dragged)e.Handled=true;};
            button.PreviewMouseMove+=(s,e)=>{
                if(e.LeftButton!=MouseButtonState.Pressed){origin=null;return;}if(!origin.HasValue||dragged)return;
                Point point=e.GetPosition(button);if(Math.Abs(point.X-origin.Value.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-origin.Value.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                dragged=true;origin=null;e.Handled=true;button.ReleaseMouseCapture();
                try{DragDrop.DoDragDrop(button,new DataObject(format,project),DragDropEffects.Move);}finally{button.BorderBrush=border;button.BorderThickness=thickness;}
            };
            button.AllowDrop=true;
            DragEventHandler over=(s,e)=>{
                if(!e.Data.GetDataPresent(format))return;e.Handled=true;var source=e.Data.GetData(format) as Project;
                bool valid=source!=null&&source!=project&&prefs.Projects.Contains(source);e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;
                if(!valid){button.BorderBrush=border;button.BorderThickness=thickness;return;}
                bool after=vertical?e.GetPosition(button).Y>button.ActualHeight/2:e.GetPosition(button).X>button.ActualWidth/2;
                button.BorderBrush=Hex(project.Color);button.BorderThickness=vertical?new Thickness(0,after?0:3,0,after?3:0):new Thickness(after?0:3,0,after?3:0,0);
            };
            button.DragEnter+=over;button.DragOver+=over;button.DragLeave+=(s,e)=>{button.BorderBrush=border;button.BorderThickness=thickness;};
            button.Drop+=(s,e)=>{
                if(!e.Data.GetDataPresent(format))return;e.Handled=true;e.Effects=DragDropEffects.None;
                var source=e.Data.GetData(format) as Project;bool after=vertical?e.GetPosition(button).Y>button.ActualHeight/2:e.GetPosition(button).X>button.ActualWidth/2;
                button.BorderBrush=border;button.BorderThickness=thickness;
                if(Storage.ReorderProjects(prefs,source,project,after)){e.Effects=DragDropEffects.Move;Refresh();}
            };
        }
        static bool IsWeb(string path){Uri u;return Uri.TryCreate(path,UriKind.Absolute,out u) && (u.Scheme=="https" || u.Scheme=="http");}
        BitmapSource GetIcon(string path) {
            if(ShellEntries.Name(path)!=null)return GetSystemIcon(path);
            if(IsWeb(path))return null;BitmapSource image;if(icons.TryGetValue(path,out image))return image;var info=new ShellInfo();
            QueuePackagedIcon(path);
            if(SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf(info),0x100)!=IntPtr.Zero && info.Icon!=IntPtr.Zero) {try {image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();icons[path]=image;return image;}finally{DestroyIcon(info.Icon);}}return null;
        }
        // ShellExecute inherits this process's environment. An installer may add
        // runtime directories after WinBoard (or its own launcher) was started.
        static void StartShellTarget(ProcessStartInfo target) {
            string inherited=Environment.GetEnvironmentVariable("Path")??"";
            var known=new HashSet<string>(inherited.Split(';'),StringComparer.OrdinalIgnoreCase);
            var additions=new List<string>();
            foreach(var scope in new[]{EnvironmentVariableTarget.Machine,EnvironmentVariableTarget.User}){
                string registered=Environment.GetEnvironmentVariable("Path",scope)??"";
                foreach(var value in registered.Split(';')){
                    string directory=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    if(System.IO.Path.IsPathRooted(directory)&&known.Add(directory))additions.Add(directory);
                }
            }
            if(additions.Count>0)Environment.SetEnvironmentVariable("Path",inherited.TrimEnd(';')+";"+string.Join(";",additions));
            Process.Start(target);
        }
        void Launch(string path){try{if(ShellEntries.Name(path)!=null)StartShellTarget(new ProcessStartInfo("explorer.exe",path){UseShellExecute=true});else StartShellTarget(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"无法打开："+path+"\n"+ex.Message,"WinBoard");}}
        void Reveal(string path){if(Directory.Exists(path)){Launch(path);return;}try{Process.Start("explorer.exe","/select,\""+path+"\"");}catch(Exception ex){MessageBox.Show(ex.Message);}}
        void Menu(ItemsControl parent,string text,Action action){var m=new MenuItem{Header=text};m.Click+=(s,e)=>action();parent.Items.Add(m);}
        void PickFiles(Project p){PathDialog(p,false);}
        void PickFolder(Project p){PathDialog(p,true);}
        void PathDialog(Project p,bool folder) {
            StackPanel panel;var w=Dialog(folder?"添加文件夹":"添加文件",365,out panel);
            var title=Text(folder?"把文件夹加入项目":"把文件加入项目",23,TextColor);title.Margin=new Thickness(0,0,0,16);panel.Children.Add(title);
            var paths=Field(panel,"本地路径（多条路径请换行）","",true);
            var actions=new WrapPanel();actions.Children.Add(Btn("浏览选择",()=>{
                if(folder){using(var d=new Forms.FolderBrowserDialog{Description="选择文件夹",ShowNewFolderButton=true}){if(d.ShowDialog()==Forms.DialogResult.OK)paths.Text=d.SelectedPath;}}
                else{var d=new Microsoft.Win32.OpenFileDialog{Title="选择文件、程序或快捷方式",Multiselect=true};if(d.ShowDialog(w)==true)paths.Text=string.Join("\n",d.FileNames);}
            }));
            actions.Children.Add(Btn("添加到项目",()=>{
                var values=paths.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(v=>v.Trim().Trim('"')).Where(v=>v.Length>0).ToArray();
                if(values.Length==0){paths.Focus();return;}
                var missing=values.FirstOrDefault(v=>folder?!Directory.Exists(v):!File.Exists(v));
                if(missing!=null){MessageBox.Show(w,"找不到"+(folder?"文件夹":"文件")+"：\n"+missing);return;}
                AddPaths(p,values);w.Close();
            },true));panel.Children.Add(actions);var tip=Text("可粘贴完整路径，也可以浏览选择。添加后原文件仍在原位置。",12,Muted);tip.Margin=new Thickness(0,18,0,0);panel.Children.Add(tip);w.ShowDialog();
        }
        void AddPaths(Project p,IEnumerable<string> paths) {
            if(p==null){if(prefs.Projects.Count==0){p=Storage.Create(prefs,"常用入口","",Colors[0],null);}else p=prefs.Projects[0];prefs.Selected=p.Id;}
            foreach(var path in paths){if(!File.Exists(path)&&!Directory.Exists(path))continue;if(p.Items.Any(i=>string.Equals(i.Path,path,StringComparison.OrdinalIgnoreCase)))continue;string name=System.IO.Path.GetFileName(path.TrimEnd('\\'));p.Items.Add(new Shortcut{Name=string.IsNullOrEmpty(name)?path:name,Path=path});}Refresh();
        }
        const string ShortcutDragFormat="WinBoard.Shortcut";
        sealed class ShortcutDrag {
            public Project Project;
            public Shortcut Item;
        }
        void EnableShortcutDrag(Button button,Project project,Shortcut item) {
            var normalBorder=button.BorderBrush;var normalThickness=button.BorderThickness;
            Point? origin=null;bool dragged=false;
            button.PreviewMouseLeftButtonDown+=(s,e)=>{origin=e.GetPosition(button);dragged=false;};
            button.PreviewMouseLeftButtonUp+=(s,e)=>{origin=null;if(dragged)e.Handled=true;};
            button.PreviewMouseMove+=(s,e)=>{
                if(e.LeftButton!=MouseButtonState.Pressed){origin=null;return;}
                if(!origin.HasValue || dragged)return;
                Point point=e.GetPosition(button);
                if(Math.Abs(point.X-origin.Value.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-origin.Value.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                dragged=true;origin=null;e.Handled=true;
                button.ReleaseMouseCapture();
                try {DragDrop.DoDragDrop(button,new DataObject(ShortcutDragFormat,new ShortcutDrag{Project=project,Item=item}),DragDropEffects.Move);}
                finally {button.BorderBrush=normalBorder;button.BorderThickness=normalThickness;}
            };
            button.AllowDrop=true;
            DragEventHandler over=(s,e)=>{
                if(!e.Data.GetDataPresent(ShortcutDragFormat))return;
                var source=e.Data.GetData(ShortcutDragFormat) as ShortcutDrag;
                bool valid=source!=null && source.Project==project && source.Item!=item;
                e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
                button.BorderBrush=valid?Hex(project.Color):Line;
                button.BorderThickness=new Thickness(valid?3:1);
            };
            button.DragEnter+=over;button.DragOver+=over;
            button.DragLeave+=(s,e)=>{button.BorderBrush=normalBorder;button.BorderThickness=normalThickness;};
            button.Drop+=(s,e)=>{
                if(!e.Data.GetDataPresent(ShortcutDragFormat))return;
                e.Handled=true;e.Effects=DragDropEffects.None;
                button.BorderBrush=normalBorder;button.BorderThickness=normalThickness;
                var source=e.Data.GetData(ShortcutDragFormat) as ShortcutDrag;
                if(source!=null && source.Project==project && Storage.Reorder(project,source.Item,item)){
                    e.Effects=DragDropEffects.Move;Refresh();
                }
            };
        }
        void OnDrop(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){AddPaths(Current,(string[])e.Data.GetData(DataFormats.FileDrop));e.Handled=true;}}
        void OnDragOver(object s,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
        Window Dialog(string title,double height,out StackPanel panel) {
            var w=new Window{Title=title,Icon=AppBrand.Image,Width=490,Height=height,Owner=this,WindowStartupLocation=compact||!IsVisible?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Hex(Light?"#F4F1F6":"#29252F"),Foreground=TextColor,FontFamily=FontFamily};
            panel=new StackPanel{Margin=new Thickness(26)};w.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};w.Loaded+=(s,e)=>Capture((FrameworkElement)w.Content,"dialog-preview.png");return w;
        }
        TextBox Field(StackPanel panel,string label,string value,bool multiline=false) {
            var t=Text(label,12,Muted);t.Margin=new Thickness(0,0,0,7);panel.Children.Add(t);
            var input=new TextBox{Text=value??"",Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,17),Foreground=TextColor,CaretBrush=TextColor,Background=CardColor,BorderBrush=Line,FontSize=14,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,Height=multiline?64:38};
            System.Windows.Automation.AutomationProperties.SetName(input,label);panel.Children.Add(input);return input;
        }
        void EditProject(Project original) {
            StackPanel panel;var w=Dialog(original==null?"新建项目":"编辑项目",740,out panel);w.MaxHeight=Math.Max(500,SystemParameters.WorkArea.Height-40);var title=Text(original==null?"建立一个项目空间":"整理这个项目",25,TextColor);title.Margin=new Thickness(0,0,0,8);panel.Children.Add(title);
            var tip=Text("项目是入口分组，可关联你已有的本地文件夹。",12,Muted);tip.Margin=new Thickness(0,0,0,22);panel.Children.Add(tip);
            var name=Field(panel,"项目名称",original==null?"":original.Name);name.MaxLength=48;var desc=Field(panel,"项目说明（可选）",original==null?"":original.Description,true);desc.MaxLength=400;
            var folder=Field(panel,"关联本地目录（可选）",original==null?"":original.Folder);
            var dirs=new WrapPanel{Margin=new Thickness(0,-7,0,14)};dirs.Children.Add(Btn("选择 / 新建文件夹",()=>{using(var d=new Forms.FolderBrowserDialog{Description="选择现有目录，或点击“新建文件夹”创建实际文件夹",ShowNewFolderButton=true}){if(d.ShowDialog()==Forms.DialogResult.OK)folder.Text=d.SelectedPath;}}));dirs.Children.Add(Btn("清除关联",()=>folder.Text=""));panel.Children.Add(dirs);
            var readColor=ProjectColorEditor(panel,original==null?Colors[prefs.Projects.Count%Colors.Length]:original.Color,w);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};if(original!=null)buttons.Children.Add(Btn("移除项目分组",()=>{w.Close();DeleteProject(original);}));buttons.Children.Add(Btn("取消",()=>w.Close()));
            var save=Btn(original==null?"创建项目":"保存修改",()=>{
                string color=readColor();if(color==null)return;
                string label=name.Text.Trim();if(label.Length==0){MessageBox.Show(w,"请填写项目名称。");name.Focus();return;}
                if(prefs.Projects.Any(p=>p!=original && string.Equals(p.Name,label,StringComparison.OrdinalIgnoreCase))){MessageBox.Show(w,"已存在同名项目，请换一个名称。");return;}
                folder.Text=folder.Text.Trim().Trim('"');if(folder.Text.Length>0 && !Directory.Exists(folder.Text)){MessageBox.Show(w,"关联目录不存在，请选择现有目录或先新建文件夹。");return;}
                if(original==null){Storage.Create(prefs,label,desc.Text.Trim(),color,folder.Text);}else{original.Name=label;original.Description=desc.Text.Trim();original.Color=color;original.Folder=folder.Text;}Refresh();w.Close();},true);
            buttons.Children.Add(save);panel.Children.Add(buttons);w.Loaded+=(s,e)=>name.Focus();w.ShowDialog();
        }
        void DeleteProject(Project p) {
            if(MessageBox.Show(this,"移除“"+p.Name+"”及其中的 "+p.Items.Count+" 个快捷入口？\n磁盘上的文件和文件夹不会被删除。","移除项目分组",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            prefs.Projects.Remove(p);if(prefs.Selected==p.Id)prefs.Selected=null;Refresh();
        }
        void RenameShortcut(Project p,Shortcut item) {
            StackPanel panel;var w=Dialog("重命名入口",235,out panel);var name=Field(panel,"入口名称",item.Name);panel.Children.Add(Btn("保存名称",()=>{if(string.IsNullOrWhiteSpace(name.Text))return;item.Name=name.Text.Trim();Refresh();w.Close();},true));w.ShowDialog();
        }
        void EditLink(Project p,Shortcut item) {
            StackPanel panel;var w=Dialog(item==null?"添加网址":"修改网址",335,out panel);var name=Field(panel,"显示名称",item==null?"":item.Name);var url=Field(panel,"网址（http / https）",item==null?"https://":item.Path);
            panel.Children.Add(Btn("保存网址",()=>{string value=url.Text.Trim();if(!IsWeb(value)){MessageBox.Show(w,"请输入完整的 http 或 https 网址。");return;}if(string.IsNullOrWhiteSpace(name.Text)){name.Focus();return;}if(item==null){if(p.Items.Any(i=>i.Path==value)){MessageBox.Show(w,"这个网址已经在项目中。");return;}p.Items.Add(new Shortcut{Name=name.Text.Trim(),Path=value});}else{item.Name=name.Text.Trim();item.Path=value;}Refresh();w.Close();},true));w.ShowDialog();
        }
        void Settings() { AppearanceSettings(compact?"compact":"dashboard"); }
        void SwitchMode() {if(compact)RestoreDashboard();else EnterCompact();}
        void EnterCompact() {
            if(dashboardHost!=null){dashboardHost.EnterCompact();return;}
            if(quitting||switchingMode)return;
            Save();if(WindowState!=WindowState.Minimized)dashboardState=WindowState;
            Hide();EnsureDockWindow();
        }
        void RestoreDashboard() {
            if(dashboardHost!=null){dashboardHost.RestoreDashboard();return;}
            if(quitting)return;switchingMode=true;
            try{if(WindowState==WindowState.Minimized)WindowState=dashboardState;Show();Activate();EnsureDockWindow();Save();}
            finally{switchingMode=false;}
        }
        FrameworkElement DockSymbol(string kind) {
            string geometry=kind=="settings"?"M9,2 L15,2 16,5 19,6 22,6 24,11 22,13 22,16 19,20 16,20 14,23 9,23 8,20 5,19 2,19 0,14 2,12 2,9 5,5 8,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12":kind=="add"?"M12,4 L12,20 M4,12 L20,12":kind=="widgets"?"M3,3 L10,3 10,10 3,10 Z M14,3 L21,3 21,10 14,10 Z M3,14 L10,14 10,21 3,21 Z M14,14 L21,14 21,21 14,21 Z":
                kind=="appearance"?"M4,6 L9,6 M15,6 L20,6 M4,12 L3,12 M9,12 L20,12 M4,18 L13,18 M19,18 L20,18 M12,3 L12,9 M6,9 L6,15 M16,15 L16,21":
                kind=="compact"?"M3,5 L21,5 21,19 3,19 Z M7,15 L17,15":
                kind=="restore"?"M4,10 L4,4 10,4 M14,4 L20,4 20,10 M20,14 L20,20 14,20 M10,20 L4,20 4,14":
                "M8,4 L20,4 20,16 M20,4 L7,17 M4,10 L4,20 14,20";
            var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=TextColor,StrokeThickness=1.65,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
            return new Viewbox{Width=24,Height=24,Child=new Canvas{Width=24,Height=24,Children={path}}};
        }
        Button DockButton(string symbol,string name,Action action) {
            var button=Btn("",action);button.Content=DockSymbol(symbol);button.Width=48;button.Height=48;button.Padding=new Thickness(10);button.Margin=new Thickness(0,0,8,0);button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);button.ToolTip=name;
            System.Windows.Automation.AutomationProperties.SetName(button,name);
            button.MouseEnter+=(s,e)=>button.Background=Hex(Light?"#22000000":"#22FFFFFF");button.MouseLeave+=(s,e)=>button.Background=Brushes.Transparent;
            return button;
        }
        void PickDockApps() {
            StackPanel panel;var window=Dialog("添加到紧凑栏",270,out panel);
            panel.Children.Add(Text("添加应用或系统入口",22,TextColor));
            var actions=new WrapPanel{Margin=new Thickness(0,20,0,20)};
            foreach(string path in new[]{ShellEntries.Computer,ShellEntries.ControlPanel}){string target=path;actions.Children.Add(Btn(ShellEntries.Name(target),()=>{InsertDockPaths(new[]{target},dockEntries.Count);window.Close();}));}
            panel.Children.Add(actions);panel.Children.Add(Btn("浏览应用或快捷方式…",()=>{window.Close();BrowseDockApps();},true));window.ShowDialog();
        }
        void BrowseDockApps() {
            var dialog=new Microsoft.Win32.OpenFileDialog{Title="添加应用到紧凑栏",Filter="应用和快捷方式|*.exe;*.lnk;*.appref-ms|所有文件|*.*",Multiselect=true,DereferenceLinks=false,InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)};
            if(dialog.ShowDialog(this)==true)InsertDockPaths(dialog.FileNames,dockEntries.Count);
        }
        void AddResizeEdges() {
            foreach(int mask in new[]{1,2,4,8,5,6,9,10}) {
                int edgeMask=mask;bool left=(mask&1)!=0,right=(mask&2)!=0,top=(mask&4)!=0,bottom=(mask&8)!=0;
                var edge=new Border{Background=Brushes.Transparent,HorizontalAlignment=left?HorizontalAlignment.Left:right?HorizontalAlignment.Right:HorizontalAlignment.Stretch,VerticalAlignment=top?VerticalAlignment.Top:bottom?VerticalAlignment.Bottom:VerticalAlignment.Stretch,Cursor=left||right?(top||bottom?(left==top?Cursors.SizeNWSE:Cursors.SizeNESW):Cursors.SizeWE):Cursors.SizeNS};
                if(left||right)edge.Width=top||bottom?12:6;if(top||bottom)edge.Height=left||right?12:6;
                Point start=new Point();Rect bounds=new Rect();
                edge.MouseLeftButtonDown+=(s,e)=>{if(WindowState!=WindowState.Normal)return;start=PointToScreen(e.GetPosition(this));bounds=new Rect(Left,Top,Width,Height);edge.CaptureMouse();e.Handled=true;};
                edge.MouseMove+=(s,e)=>{if(!edge.IsMouseCaptured || e.LeftButton!=MouseButtonState.Pressed)return;
                    Point position=PointToScreen(e.GetPosition(this));var source=PresentationSource.FromVisual(this);var matrix=source.CompositionTarget.TransformFromDevice;Vector delta=matrix.Transform(position-start);
                    double width=Math.Max(MinWidth,bounds.Width+((edgeMask&1)!=0?-delta.X:(edgeMask&2)!=0?delta.X:0));
                    double height=Math.Max(MinHeight,bounds.Height+((edgeMask&4)!=0?-delta.Y:(edgeMask&8)!=0?delta.Y:0));
                    Width=width;Height=height;if((edgeMask&1)!=0)Left=bounds.Right-width;if((edgeMask&4)!=0)Top=bounds.Bottom-height;e.Handled=true;
                };
                edge.MouseLeftButtonUp+=(s,e)=>{if(edge.IsMouseCaptured){edge.ReleaseMouseCapture();Save();e.Handled=true;}};
                root.Children.Add(edge);
            }
        }
        void BringBack(){RestoreDashboard();}
        void BuildTray() {
            tray=new Forms.NotifyIcon{Icon=AppBrand.TrayIcon(),Text="WinBoard · 项目工作台",Visible=true};var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("打开工作台",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{if(compact)SwitchMode();BringBack();})));
            menu.Items.Add("新建项目",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{if(compact)SwitchMode();BringBack();EditProject(null);})));
            menu.Items.Add("桌面组件",null,(s,e)=>Dispatcher.Invoke(new Action(WidgetManager)));
            menu.Items.Add("框选截图",null,(s,e)=>Dispatcher.Invoke(new Action(()=>TakeScreenshot(null))));
            menu.Items.Add("录屏 / 录制控制",null,(s,e)=>Dispatcher.Invoke(new Action(()=>OpenCaptureSettings(true))));
            menu.Items.Add("结束录屏",null,(s,e)=>Dispatcher.Invoke(new Action(()=>StopCaptureRecording(null))));
            menu.Items.Add("设置",null,(s,e)=>Dispatcher.Invoke(new Action(AppSettings)));
            menu.Items.Add("退出 WinBoard",null,(s,e)=>Dispatcher.Invoke(new Action(Quit)));tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(BringBack));
        }
        void Quit(){if(dashboardHost!=null){dashboardHost.Quit();return;}if(quitting)return;if(FinishRecordingBeforeExit())return;quitting=true;CloseDockWindow();ResetDockAutoHide();if(dockHideTimer!=null)dockHideTimer.Stop();taskbarGuard.Dispose(systemSessionEnding?300:4000);StopWidgets();Save();if(runningTimer!=null)runningTimer.Stop();timer.Stop();tray.Dispose();Close();Application.Current.Shutdown();}
    }
}
