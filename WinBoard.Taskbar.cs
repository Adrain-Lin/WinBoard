using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Forms = System.Windows.Forms;

namespace WinBoard {
    // A separate process owns the temporary change, so it can restore Explorer
    // even when the main application is terminated without running its cleanup.
    sealed class TaskbarGuard : IDisposable {
        Process helper;
        EventWaitHandle stop;
        public bool Active {get{return helper!=null && !helper.HasExited;}}
        public void Start() {
            if(Active)return;
            Dispose();
            string token="Local\\WinBoard.Taskbar."+Guid.NewGuid().ToString("N");
            stop=new EventWaitHandle(false,EventResetMode.ManualReset,token+".stop");
            using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,token+".ready"))
            using(var parent=Process.GetCurrentProcess()) {
                try {
                    helper=Process.Start(new ProcessStartInfo(parent.MainModule.FileName,"--taskbar-guard "+parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks+" "+token){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
                    if(!ready.WaitOne(5000) || !Active)throw new InvalidOperationException("任务栏隐藏保护未能启动。");
                }catch {Dispose();throw;}
            }
        }
        public void Dispose() {
            Dispose(4000);
        }
        public void Dispose(int waitMilliseconds) {
            if(stop!=null)stop.Set();
            if(helper!=null){helper.WaitForExit(waitMilliseconds);helper.Dispose();helper=null;}
            if(stop!=null){stop.Dispose();stop=null;}
        }
        delegate bool EnumProc(IntPtr window,IntPtr parameter);
        delegate void EventProc(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int count);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,EventProc callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        static bool IsTaskbar(IntPtr window) {
            var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);
            return name.ToString()=="Shell_TrayWnd" || name.ToString()=="Shell_SecondaryTrayWnd";
        }
        static List<IntPtr> Bars() {
            var result=new List<IntPtr>();EnumWindows((w,p)=>{if(IsTaskbar(w))result.Add(w);return true;},IntPtr.Zero);return result;
        }
        public static void Run(string[] args) {
            if(args.Length!=4)return;
            int id;long started;
            if(!int.TryParse(args[1],out id)||!long.TryParse(args[2],out started)||!args[3].StartsWith("Local\\WinBoard.Taskbar.",StringComparison.Ordinal))return;
            try {
                using(var parent=Process.GetProcessById(id))
                using(var stopEvent=EventWaitHandle.OpenExisting(args[3]+".stop"))
                using(var readyEvent=EventWaitHandle.OpenExisting(args[3]+".ready")) {
                    if(parent.StartTime.ToUniversalTime().Ticks!=started || parent.HasExited)return;
                    bool autoHide=WindowsTaskbar.AutoHide;
                    var visibility=new Dictionary<IntPtr,bool>();IntPtr hook=IntPtr.Zero;
                    Action<IntPtr> hide=w=>{if(!visibility.ContainsKey(w))visibility.Add(w,IsWindowVisible(w));if(IsWindowVisible(w))ShowWindow(w,0);};
                    EventProc callback=(h,k,w,o,c,t,ms)=>{if(o==0 && c==0 && IsTaskbar(w))hide(w);};
                    var callbackRoot=GCHandle.Alloc(callback);
                    using(var context=new Forms.ApplicationContext())
                    using(var pulse=new Forms.Timer{Interval=150}) {
                        try {
                            if(Bars().Count==0 || !WindowsTaskbar.SetAutoHide(true))return;
                            hook=SetWinEventHook(0x8002,0x8002,IntPtr.Zero,callback,0,0,2);
                            foreach(var w in Bars())hide(w);
                            pulse.Tick+=(s,e)=>{
                                if(stopEvent.WaitOne(0)||parent.HasExited){context.ExitThread();return;}
                                // Explorer can recreate taskbar windows after a display change.
                                foreach(var w in Bars())hide(w);
                            };
                            pulse.Start();readyEvent.Set();Forms.Application.Run(context);
                        }finally {
                            pulse.Stop();if(hook!=IntPtr.Zero)UnhookWinEvent(hook);callbackRoot.Free();
                            foreach(var w in Bars()){bool wasVisible;if(!visibility.TryGetValue(w,out wasVisible)||wasVisible)ShowWindow(w,8);}
                            WindowsTaskbar.SetAutoHide(autoHide);
                        }
                    }
                }
            }catch(Exception ex) {
                try{System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","taskbar-guard-error.txt"),ex.ToString());}catch{}
            }
        }
    }
}
