using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

[assembly: AssemblyTitle("WinBoard")]
[assembly: AssemblyProduct("WinBoard")]
[assembly: AssemblyCompany("AdrainLin")]
[assembly: AssemblyCopyright("AdrainLin")]
[assembly: AssemblyVersion("1.3.2.0")]
[assembly: AssemblyFileVersion("1.3.2.0")]

namespace WinBoard {
    public static class AppBrand {
        public static readonly BitmapSource Image=LoadImage();
        static BitmapSource LoadImage() {
            using(var stream=typeof(AppBrand).Assembly.GetManifestResourceStream("WinBoard.Brand.png")) {
                var image=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);image.Freeze();return image;
            }
        }
        public static System.Drawing.Icon TrayIcon() {
            using(var stream=typeof(AppBrand).Assembly.GetManifestResourceStream("WinBoard.Brand.ico"))
            using(var icon=new System.Drawing.Icon(stream,32,32))return (System.Drawing.Icon)icon.Clone();
        }
    }
    public static class StartupRegistration {
        const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string Executable {get{return Path.GetFullPath(typeof(StartupRegistration).Assembly.Location);}}
        public static string StartupLink {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"WinBoard.lnk");}}
        static object Link(){var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{return shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{StartupLink});}finally{System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);}}
        static object Read(object link,string property){return link.GetType().InvokeMember(property,BindingFlags.GetProperty,null,link,null);}
        static void Write(object link,string property,string value){link.GetType().InvokeMember(property,BindingFlags.SetProperty,null,link,new object[]{value});}
        public static bool SystemDisabled {get{string group=File.Exists(StartupLink)?"StartupFolder":"Run";using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\"+group)){var data=key==null?null:key.GetValue(group=="Run"?"WinBoard":"WinBoard.lnk") as byte[];return data!=null&&data.Length>0&&(data[0]==3||data[0]==7);}}}
        public static string Command {get{return "\""+Executable+"\" --startup";}}
        public static string RegisteredCommand {get{if(File.Exists(StartupLink)){object link=Link();try{return "\""+Read(link,"TargetPath")+"\" "+Read(link,"Arguments");}finally{System.Runtime.InteropServices.Marshal.ReleaseComObject(link);}}using(var key=Registry.CurrentUser.OpenSubKey(KeyPath))return key==null?null:key.GetValue("WinBoard") as string;}}
        public static bool Enabled {get{return !SystemDisabled&&string.Equals(RegisteredCommand,Command,StringComparison.OrdinalIgnoreCase);}}
        public static void SetEnabled(bool enabled) {
            if(enabled && Command.Length>260)throw new InvalidOperationException("程序路径过长，请先把 WinBoard 移到较短的固定目录。");
            using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)) {
                if(enabled){Directory.CreateDirectory(Path.GetDirectoryName(StartupLink));object link=Link();try{Write(link,"TargetPath",Executable);Write(link,"Arguments","--startup");Write(link,"WorkingDirectory",Path.GetDirectoryName(Executable));Write(link,"IconLocation",Executable+",0");link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{System.Runtime.InteropServices.Marshal.ReleaseComObject(link);}}
                else if(File.Exists(StartupLink))File.Delete(StartupLink);
                key.DeleteValue("WinBoard",false);
            }
            if(enabled&&SystemDisabled)throw new IOException("Windows 已禁用此启动项，请点击“Windows 启动应用设置”重新启用 WinBoard。");
            if(Enabled!=enabled)throw new IOException("Windows 未保存自启动设置，请重试。");
        }
    }
    public partial class Board {
        void AppSettings() {
            StackPanel panel;var window=Dialog("设置",470,out panel);
            var identity=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,22)};
            identity.Children.Add(new Image{Source=AppBrand.Image,Width=76,Height=76,Margin=new Thickness(0,0,18,0)});
            var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(info);
            var title=Text("WinBoard",25,TextColor);title.FontWeight=FontWeights.SemiBold;info.Children.Add(title);
            info.Children.Add(Text("开发者  AdrainLin",13,Muted));info.Children.Add(Text("开发时间  2024年11月4日",13,Muted));panel.Children.Add(identity);
            var card=new Border{Background=CardColor,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(18)};
            var options=new StackPanel();card.Child=options;panel.Children.Add(card);
            var startup=new CheckBox{Content="开机自启动",Foreground=TextColor,FontSize=15,Padding=new Thickness(0,3,0,3)};
            System.Windows.Automation.AutomationProperties.SetName(startup,"开机自启动");options.Children.Add(startup);
            var hint=Text("登录 Windows 后显示紧凑栏和已开启的组件。",12,Muted);hint.Margin=new Thickness(0,8,0,0);options.Children.Add(hint);
            var status=Text("",12,Muted);status.Margin=new Thickness(0,10,0,0);options.Children.Add(status);
            Action refresh=()=>{try{startup.IsChecked=StartupRegistration.Enabled;status.Text=StartupRegistration.SystemDisabled?"已被 Windows 禁用，请在系统启动应用设置中启用。":StartupRegistration.Enabled?"已开启 · 登录后从启动文件夹运行":string.IsNullOrEmpty(StartupRegistration.RegisteredCommand)?"已关闭":"另一份 WinBoard 已设置自启动；开启此项可切换到当前版本。";}catch(Exception ex){startup.IsEnabled=false;status.Text="无法读取自启动设置："+ex.Message;}};
            startup.Click+=(s,e)=>{try{StartupRegistration.SetEnabled(startup.IsChecked==true);}catch(Exception ex){MessageBox.Show(window,"未能更改自启动："+ex.Message,"WinBoard");}refresh();};refresh();
            options.Children.Add(Btn("Windows 启动应用设置",()=>Launch("ms-settings:startupapps")));
            var note=Text("请先解压到固定目录再开启。移动程序后，重新开启此项即可更新位置。",12,Muted);note.Margin=new Thickness(0,15,0,18);panel.Children.Add(note);
            panel.Children.Add(Btn("完成",()=>window.Close(),true));window.ShowDialog();
        }
    }
}
