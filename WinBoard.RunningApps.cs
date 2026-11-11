using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WinBoard {
    public sealed class RunningWindow {
        public IntPtr Handle;public string Title;
    }
    public sealed class RunningApp {
        public string Key,Path,Name;public List<RunningWindow> Windows=new List<RunningWindow>();
        public HashSet<string> Identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    public static class RunningApps {
        delegate bool EnumProc(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr window,EnumProc callback,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetStyle(IntPtr window,int index);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder id);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
            public uint Size,Usage,Id;public IntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
        }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint process);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")] static extern IntPtr GetClassIcon(IntPtr window,int index);
        static string ProcessPath(uint id) {
            IntPtr process=OpenProcess(0x1000,false,id);if(process==IntPtr.Zero)return null;
            try {var text=new StringBuilder(32768);int length=text.Capacity;return QueryFullProcessImageName(process,0,text,ref length)?text.ToString():null;}finally{CloseHandle(process);}
        }
        static string ProcessAppId(uint id) {
            IntPtr process=OpenProcess(0x1000,false,id);if(process==IntPtr.Zero)return null;
            try{uint size=1024;var text=new StringBuilder((int)size);return GetApplicationUserModelId(process,ref size,text)==0?text.ToString():null;}finally{CloseHandle(process);}
        }
        static Dictionary<uint,ProcessEntry> ProcessTree() {
            var result=new Dictionary<uint,ProcessEntry>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==new IntPtr(-1))return result;
            try{var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};if(Process32FirstW(snapshot,ref entry))do{result[entry.Id]=entry;}while(Process32NextW(snapshot,ref entry));}finally{CloseHandle(snapshot);}return result;
        }
        static uint ApplicationProcess(uint pid,string path,Dictionary<uint,ProcessEntry> tree) {
            string name=System.IO.Path.GetFileName(path);
            // WeChat's web/mini-app windows are hosted outside its installation folder.
            // Only merge these known helpers when their live ancestry reaches WeChat.
            if(!new[]{"WeChatAppEx.exe","WeChatPlayer.exe","WeChatBrowser.exe"}.Contains(name,StringComparer.OrdinalIgnoreCase))return pid;
            uint cursor=pid;var seen=new HashSet<uint>();
            for(int i=0;i<12&&seen.Add(cursor);i++) {
                ProcessEntry current,parent;if(!tree.TryGetValue(cursor,out current)||!tree.TryGetValue(current.Parent,out parent))break;
                if(string.Equals(parent.Name,"Weixin.exe",StringComparison.OrdinalIgnoreCase)||string.Equals(parent.Name,"WeChat.exe",StringComparison.OrdinalIgnoreCase))return parent.Id;
                cursor=parent.Id;
            }
            return pid;
        }
        public static string AppIdentity(string value) {
            if(string.IsNullOrWhiteSpace(value))return null;value=value.Trim();
            const string shell="shell:AppsFolder\\",guid="::{4234d49b-0245-4df3-b780-3893943456e1}\\";
            if(value.StartsWith(shell,StringComparison.OrdinalIgnoreCase))value=value.Substring(shell.Length);
            if(value.StartsWith(guid,StringComparison.OrdinalIgnoreCase))value=value.Substring(guid.Length);
            // Package app IDs contain a family/publisher and an application name.
            return value.IndexOf('!')>0 && value.IndexOfAny(new[]{'\\','/',':'})<0?"app:"+value.ToLowerInvariant():null;
        }
        static string PathIdentity(string value) {return string.IsNullOrWhiteSpace(value)?null:"path:"+Environment.ExpandEnvironmentVariables(value).ToLowerInvariant();}
        public static bool Matches(string[] identities,RunningApp app) {return identities!=null&&app!=null&&identities.Any(id=>app.Identities.Contains(id));}
        public static string ShellIdentity(string path){if(string.IsNullOrEmpty(path))return null;string canonical=ShellEntries.Canonical(path);if(ShellEntries.Name(canonical)!=null)return canonical;string value=path.ToUpperInvariant();if(value.Contains("::{26EE0668-A00A-44D7-9371-BEB064C98683}")||value.Contains("::{21EC2020-3AEA-1069-A2DD-08002B30309D}"))return ShellEntries.ControlPanel;return null;}
        static object ShellProperty(object value,string property){return value.GetType().InvokeMember(property,BindingFlags.GetProperty,null,value,null);}
        static void ReleaseShell(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
        static Dictionary<long,string> ShellWindows(){var result=new Dictionary<long,string>();object shell=null,windows=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=shell.GetType().InvokeMember("Windows",BindingFlags.InvokeMethod,null,shell,null);int count=Convert.ToInt32(ShellProperty(windows,"Count"));for(int i=0;i<count;i++){object window=null,document=null,folder=null,item=null;try{window=windows.GetType().InvokeMember("Item",BindingFlags.InvokeMethod,null,windows,new object[]{i});long handle=Convert.ToInt64(ShellProperty(window,"HWND"));document=ShellProperty(window,"Document");folder=ShellProperty(document,"Folder");item=ShellProperty(folder,"Self");string path=Convert.ToString(ShellProperty(item,"Path"));result[handle]=ShellIdentity(path)??ShellEntries.Computer;}catch{}finally{ReleaseShell(item);ReleaseShell(folder);ReleaseShell(document);ReleaseShell(window);}}}catch{}finally{ReleaseShell(windows);ReleaseShell(shell);}return result;}
        public static List<RunningApp> Scan() {
            int self=Process.GetCurrentProcess().Id;var groups=new Dictionary<string,RunningApp>(StringComparer.OrdinalIgnoreCase);
            var paths=new Dictionary<uint,string>();var appIds=new Dictionary<uint,string>();var tree=ProcessTree();var shellWindows=ShellWindows();
            EnumWindows((window,unused)=>{
                if(!IsWindowVisible(window))return true;
                long style=GetStyle(window,-20).ToInt64();
                if((style&0x80)!=0 || (style&0x08000000)!=0 || (GetWindow(window,4)!=IntPtr.Zero && (style&0x40000)==0))return true;
                int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0 && cloaked!=0)return true;
                var title=new StringBuilder(1024);GetWindowText(window,title,title.Capacity);if(title.Length==0)return true;
                var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);
                if(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"}.Contains(cls.ToString()))return true;
                uint pid;GetWindowThreadProcessId(window,out pid);if(pid==self)return true;
                string path;if(!paths.TryGetValue(pid,out path)){path=ProcessPath(pid);paths[pid]=path;}
                // Hosted Windows apps can expose the real app through a child window.
                if(string.Equals(System.IO.Path.GetFileName(path),"ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)) {
                    EnumChildWindows(window,(child,p)=>{uint childId;GetWindowThreadProcessId(child,out childId);if(childId!=pid){string candidate=ProcessPath(childId);if(!string.IsNullOrEmpty(candidate)){path=candidate;pid=childId;return false;}}return true;},IntPtr.Zero);
                }
                uint application=ApplicationProcess(pid,path,tree);
                if(application!=pid){string parentPath;if(!paths.TryGetValue(application,out parentPath)){parentPath=ProcessPath(application);paths[application]=parentPath;}if(!string.IsNullOrEmpty(parentPath)){pid=application;path=parentPath;}}
                string appId;if(!appIds.TryGetValue(pid,out appId)){appId=AppIdentity(ProcessAppId(pid));appIds[pid]=appId;}
                string shellIdentity=null;if(string.Equals(System.IO.Path.GetFileName(path),"explorer.exe",StringComparison.OrdinalIgnoreCase)&&(cls.ToString()=="CabinetWClass"||cls.ToString()=="ExploreWClass"))shellWindows.TryGetValue(window.ToInt64(),out shellIdentity);
                string pathId=PathIdentity(path),key=shellIdentity??appId??pathId??"window:"+window.ToInt64();
                RunningApp app;if(!groups.TryGetValue(key,out app)) {
                    app=new RunningApp{Key=key,Path=shellIdentity??path,Name=shellIdentity==null?(string.IsNullOrEmpty(path)?title.ToString():System.IO.Path.GetFileNameWithoutExtension(path)):ShellEntries.Name(shellIdentity)};
                    groups.Add(key,app);
                }
                if(shellIdentity!=null)app.Identities.Add(shellIdentity);else {if(appId!=null)app.Identities.Add(appId);if(pathId!=null)app.Identities.Add(pathId);}
                app.Windows.Add(new RunningWindow{Handle=window,Title=title.ToString()});return true;
            },IntPtr.Zero);
            return groups.Values.OrderBy(a=>a.Key,StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static bool CanActivate(IntPtr window) {
            if(!IsWindow(window)||!IsWindowVisible(window))return false;
            int cloaked;return DwmGetWindowAttribute(window,14,out cloaked,4)!=0||cloaked==0;
        }
        public static bool Activate(IntPtr window,bool minimize) {
            if(!CanActivate(window))return false;
            if(minimize && !IsIconic(window)){ShowWindowAsync(window,6);return true;}
            if(IsIconic(window))ShowWindowAsync(window,9);
            SetForegroundWindow(window);return true;
        }
        public static BitmapSource Icon(IntPtr window) {
            IntPtr icon;SendMessageTimeout(window,0x7F,new IntPtr(1),IntPtr.Zero,2,40,out icon);
            if(icon==IntPtr.Zero)icon=GetClassIcon(window,-14);
            if(icon==IntPtr.Zero)return null;
            try{var bitmap=Imaging.CreateBitmapSourceFromHIcon(icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());bitmap.Freeze();return bitmap;}catch{return null;}
        }
        public static string ShortcutTarget(string path) {
            if(string.IsNullOrEmpty(path))return null;
            if(string.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase)) {
                object shell=null,link=null;
                try {
                    shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
                    path=(string)link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null);
                }catch{return null;}finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
            }
            return string.Equals(System.IO.Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)?Environment.ExpandEnvironmentVariables(path).ToLowerInvariant():null;
        }
        public static string[] ShortcutIdentities(string path) {
            if(path==ShellEntries.Settings)return new[]{ShellEntries.Settings,PathIdentity(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","SystemSettings.exe"))};
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string shellIdentity=ShellIdentity(path);if(shellIdentity!=null)return new[]{shellIdentity};
            string target=ShortcutTarget(path);if(target!=null)result.Add(PathIdentity(target));
            string direct=AppIdentity(path);if(direct!=null)result.Add(direct);
            if(!string.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase))return result.ToArray();
            object shell=null,folder=null,item=null;
            try {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                folder=shell.GetType().InvokeMember("NameSpace",BindingFlags.InvokeMethod,null,shell,new object[]{System.IO.Path.GetDirectoryName(path)});
                item=folder.GetType().InvokeMember("ParseName",BindingFlags.InvokeMethod,null,folder,new object[]{System.IO.Path.GetFileName(path)});
                foreach(string property in new[]{"System.AppUserModel.ID","System.Link.TargetParsingPath"}) {
                    string value=Convert.ToString(item.GetType().InvokeMember("ExtendedProperty",BindingFlags.InvokeMethod,null,item,new object[]{property}));
                    string shellId=ShellIdentity(value);if(shellId!=null)result.Add(shellId);
                    string id=AppIdentity(value);if(id!=null)result.Add(id);
                    else if(string.Equals(System.IO.Path.GetExtension(value),".exe",StringComparison.OrdinalIgnoreCase))result.Add(PathIdentity(value));
                }
            }catch{}finally{if(item!=null)Marshal.FinalReleaseComObject(item);if(folder!=null)Marshal.FinalReleaseComObject(folder);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
            return result.ToArray();
        }
    }
    public static class DockOrder {
        // Slots are measured before removal, so dropping to either side is symmetric.
        public static bool Insert<T>(List<T> items,T item,int slot) {
            slot=Math.Max(0,Math.Min(items.Count,slot));int from=items.IndexOf(item);
            if(from>=0){if(from<slot)slot--;if(from==slot)return false;items.RemoveAt(from);}
            items.Insert(slot,item);return true;
        }
        public static List<Shortcut> ReadPaths(IEnumerable<string> paths) {
            var result=new List<Shortcut>();
            foreach(string value in paths) {
                string path=ShellEntries.Canonical((value??"").Trim().Trim('"'));
                if((ShellEntries.Name(path)==null&&!File.Exists(path)&&!Directory.Exists(path))||result.Any(i=>string.Equals(i.Path,path,StringComparison.OrdinalIgnoreCase)))continue;
                result.Add(new Shortcut{Path=path,Name=ShellEntries.Name(path)??System.IO.Path.GetFileName(path.TrimEnd('\\'))});
            }
            return result;
        }
    }
}
