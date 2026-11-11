using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinBoard {
    internal static class BackgroundApps {
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
        static string ExpandPath(string value){
            if(string.IsNullOrWhiteSpace(value))return null;
            int slash=value.IndexOf('\\');
            Guid id;
            if(slash<=0||!Guid.TryParse(value.Substring(0,slash),out id))return Environment.ExpandEnvironmentVariables(value);
            IntPtr path;
            if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out path)!=0)return null;
            try{return Path.Combine(Marshal.PtrToStringUni(path),value.Substring(slash+1));}
            finally{Marshal.FreeCoTaskMem(path);}
        }
        public static List<string> Read(string[] pinned){
            var live=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var process in Process.GetProcesses())using(process){
                string path=RunningApps.ProcessPath((uint)process.Id);
                if(path!=null)live.Add(path);
            }
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var root=Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings")){
                if(root!=null){
                    foreach(string name in root.GetSubKeyNames())using(var item=root.OpenSubKey(name)){
                        if(item==null)continue;
                        var path=ExpandPath(item.GetValue("ExecutablePath") as string);
                        if(live.Contains(path)&&!string.Equals(Path.GetFileName(path),"explorer.exe",StringComparison.OrdinalIgnoreCase))result.Add(path);
                    }
                }else result.UnionWith(pinned.Where(live.Contains));
            }
            return result.OrderBy(path=>Path.GetFileNameWithoutExtension(path),StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
    public partial class Board {
        Popup backgroundPopup;
        async void ToggleBackgroundApps(Button anchor){
            if(backgroundPopup!=null){backgroundPopup.IsOpen=false;return;}
            if(previewWindow!=null)previewWindow.Close();
            var grid=new WrapPanel{MaxWidth=240};
            var loading=new TextBlock{Text="正在读取…",Foreground=TextColor,Margin=new Thickness(6)};
            grid.Children.Add(loading);
            var panel=new Popup{PlacementTarget=anchor,Placement=PlacementMode.Top,StaysOpen=false,AllowsTransparency=true,
                Child=new Border{Child=grid,Padding=new Thickness(8),CornerRadius=new CornerRadius(12),Background=CardColor,BorderBrush=Line,BorderThickness=new Thickness(1)}};
            backgroundPopup=panel;dockMenuOpen=true;
            panel.Closed+=(s,e)=>{if(backgroundPopup==panel){backgroundPopup=null;dockMenuOpen=false;}};
            grid.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape)panel.IsOpen=false;};
            panel.IsOpen=true;
            try{
                var pins=dockEntries.Where(entry=>entry.Pin!=null&&entry.Target!=null).SelectMany(entry=>entry.Target.Where(id=>id.StartsWith("path:",StringComparison.OrdinalIgnoreCase)).Select(id=>new {Path=id.Substring(5),Pin=entry.Pin})).ToList();
                var paths=await Task.Run(()=>BackgroundApps.Read(pins.Select(p=>p.Path).ToArray()));
                if(!panel.IsOpen)return;
                if(paths.Count==0){loading.Text="暂无后台应用";return;}
                grid.Children.Clear();
                foreach(var path in paths){
                    var pin=pins.FirstOrDefault(p=>string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase));
                    var name=PathLabel(path,pin==null?null:pin.Pin.Name);
                    var button=Btn("",()=>{
                        panel.IsOpen=false;
                        var app=runningApps.FirstOrDefault(group=>string.Equals(group.Path,path,StringComparison.OrdinalIgnoreCase));
                        var window=app==null?null:app.Windows.FirstOrDefault(w=>RunningApps.CanActivate(w.Handle));
                        if(window!=null)RunningApps.Activate(window.Handle,false);
                        else Launch(pin==null?path:pin.Pin.Path);
                    });
                    button.Width=44;button.Height=44;button.Margin=new Thickness(2);button.Padding=new Thickness(4);button.ToolTip=name;
                    var icon=GetIcon(pin==null?path:pin.Pin.Path);
                    if(icon!=null)button.Content=new Image{Width=27,Height=27,Source=icon};
                    else button.Content=name.Substring(0,1);
                    System.Windows.Automation.AutomationProperties.SetName(button,name);grid.Children.Add(button);
                }
            }catch(Exception){if(panel.IsOpen){grid.Children.Clear();loading.Text="无法读取后台应用";grid.Children.Add(loading);}}
        }
        static string PathLabel(string path,string pinName){
            return string.IsNullOrEmpty(pinName)?System.IO.Path.GetFileNameWithoutExtension(path):System.IO.Path.GetFileNameWithoutExtension(pinName);
        }
    }
}
