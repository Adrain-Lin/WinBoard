using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WinBoard {
    public static class NativeTray {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string name,string title);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        public static IntPtr Popup {get{var h=FindWindow("TopLevelWindowForOverflowXamlIsland",null);return h!=IntPtr.Zero?h:FindWindow("NotifyIconOverflowWindow",null);}}
        public static bool IsOpen {get{var h=Popup;return h!=IntPtr.Zero&&IsWindowVisible(h);}}
        public static void Close(){var h=Popup;if(h!=IntPtr.Zero)ShowWindow(h,0);}
        public static void ShowTaskbar(){var h=FindWindow("Shell_TrayWnd",null);if(h!=IntPtr.Zero)ShowWindow(h,5);}
        public static bool Open(){
            var handle=FindWindow("Shell_TrayWnd",null);if(handle==IntPtr.Zero)return false;
            var root=AutomationElement.FromHandle(handle);
            var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            foreach(AutomationElement button in buttons){
                string name=button.Current.Name??"",id=button.Current.AutomationId??"";
                if(id!="ChevronButton"&&id!="OverflowButton"&&name!="显示隐藏的图标"&&name!="显示隐藏图标"&&name!="Show hidden icons"&&name!="顯示隱藏的圖示")continue;
                object pattern;if(button.TryGetCurrentPattern(InvokePattern.Pattern,out pattern)){((InvokePattern)pattern).Invoke();return true;}
            }
            return false;
        }
    }
    public partial class Board {
        bool nativeTrayActive,nativeTrayAutoHide,nativeTrayResumeGuard;
        int nativeTrayGeneration;
        DispatcherTimer nativeTrayTimer;
        async void ToggleNativeTray(){
            if(dashboardHost!=null){dashboardHost.ToggleNativeTray();return;}
            if(nativeTrayActive){EndNativeTray();return;}
            nativeTrayResumeGuard=taskbarGuard.Active||prefs.HideTaskbarWhileRunning;
            taskbarGuard.Dispose();nativeTrayAutoHide=WindowsTaskbar.AutoHide;nativeTrayActive=true;
            int generation=++nativeTrayGeneration;
            if(dockHost!=null)dockHost.dockMenuOpen=true;
            try{
                WindowsTaskbar.SetAutoHide(false);NativeTray.ShowTaskbar();await Task.Delay(250);
                if(!nativeTrayActive||generation!=nativeTrayGeneration)return;
                var opening=Task.Run(()=>NativeTray.Open());
                if(await Task.WhenAny(opening,Task.Delay(3000))!=opening)throw new TimeoutException("系统托盘响应超时。");
                if(!nativeTrayActive||generation!=nativeTrayGeneration)return;
                bool opened=await opening;
                if(!opened)throw new InvalidOperationException("未找到系统的隐藏图标按钮，请在 Windows 任务栏设置中启用隐藏图标菜单。");
                await Task.Delay(350);
                if(!nativeTrayActive||generation!=nativeTrayGeneration)return;
                nativeTrayTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
                nativeTrayTimer.Tick+=(s,e)=>{if(!NativeTray.IsOpen)EndNativeTray();};nativeTrayTimer.Start();
            }catch(Exception ex){EndNativeTray();if(!quitting)MessageBox.Show(this,ex.Message,"后台程序");}
        }
        void EndNativeTray(){
            if(!nativeTrayActive)return;nativeTrayActive=false;nativeTrayGeneration++;
            if(nativeTrayTimer!=null){nativeTrayTimer.Stop();nativeTrayTimer=null;}
            NativeTray.Close();WindowsTaskbar.SetAutoHide(nativeTrayAutoHide);
            if(dockHost!=null)dockHost.dockMenuOpen=false;
            if(nativeTrayResumeGuard&&!quitting&&!systemSessionEnding)taskbarGuard.Start();
        }
    }
}
