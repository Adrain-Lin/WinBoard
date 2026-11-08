using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
namespace WinBoard {
 public static class SleepCommands {
  // SYSTEM_POWER_CAPABILITIES has a fixed native layout (BOOLEAN is one byte).
  [StructLayout(LayoutKind.Explicit,Size=76)] struct Capabilities {
   [FieldOffset(3)] public byte S1;[FieldOffset(4)] public byte S2;[FieldOffset(5)] public byte S3;
   [FieldOffset(20)] public byte AoAc;
  }
  [StructLayout(LayoutKind.Explicit,Size=16)] struct Privileges {[FieldOffset(0)] public uint Count;[FieldOffset(4)] public long Luid;[FieldOffset(12)] public uint Attributes;}
  [DllImport("powrprof.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool GetPwrCapabilities(out Capabilities capabilities);
  [DllImport("powrprof.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWake);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool LookupPrivilegeValue(string system,string name,out long luid);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr token,bool disableAll,ref Privileges privileges,uint length,out Privileges previous,out uint needed);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
  public static string DetectMode(){Capabilities c;if(!GetPwrCapabilities(out c))throw new Win32Exception(Marshal.GetLastWin32Error());return SelectMode(c.AoAc!=0,c.S1!=0||c.S2!=0||c.S3!=0);}
  public static string SelectMode(bool modern,bool legacy){if(modern)return "S0";if(legacy)return "S1-S3";throw new InvalidOperationException("此电脑当前未提供可用的睡眠状态，请在终端运行 powercfg /a 查看原因。");}
  public static void Dispatch(string mode,Action modern,Action legacy){if(mode=="S0")modern();else if(mode=="S1-S3")legacy();else throw new ArgumentException("未知睡眠类型");}
  public static void Request(IntPtr window){Dispatch(DetectMode(),()=>{if(window==IntPtr.Zero)throw new InvalidOperationException("无法取得睡眠请求窗口。");IntPtr result;if(SendMessageTimeout(window,0x112,new IntPtr(0xF170),new IntPtr(2),2,3000,out result)==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows 未接受新式待机屏幕关闭请求。");},LegacySleep);}
  static void LegacySleep(){IntPtr token;if(!OpenProcessToken(Process.GetCurrentProcess().Handle,0x28,out token))throw new Win32Exception(Marshal.GetLastWin32Error());try{long luid;if(!LookupPrivilegeValue(null,"SeShutdownPrivilege",out luid))throw new Win32Exception(Marshal.GetLastWin32Error());var requested=new Privileges{Count=1,Luid=luid,Attributes=2};Privileges previous;uint size;if(!AdjustTokenPrivileges(token,false,ref requested,(uint)Marshal.SizeOf(typeof(Privileges)),out previous,out size))throw new Win32Exception(Marshal.GetLastWin32Error());int error=Marshal.GetLastWin32Error();if(error!=0)throw new Win32Exception(error);try{if(!SetSuspendState(false,false,false))throw new Win32Exception(Marshal.GetLastWin32Error());}finally{Privileges ignored;uint unused;AdjustTokenPrivileges(token,false,ref previous,0,out ignored,out unused);}}finally{CloseHandle(token);}}
 }
 public enum ControlPowerAction {Shutdown,Restart,Sleep}
 public static class PowerCommands {
  public static string Arguments(ControlPowerAction action){if(action==ControlPowerAction.Shutdown)return "/s /t 0";if(action==ControlPowerAction.Restart)return "/r /t 0";if(action==ControlPowerAction.Sleep)return null;throw new ArgumentOutOfRangeException("action");}
 }
 public static class PowerExitHelper {
  public static bool AfterExit(Process parent,Action action,int timeout){if(!parent.WaitForExit(timeout))return false;action();return true;}
  public static void Run(string[] args){if(args.Length!=5)return;int pid;long ticks;if(!int.TryParse(args[1],out pid)||!long.TryParse(args[2],out ticks)||(args[3]!="shutdown"&&args[3]!="restart")||!args[4].StartsWith("Local\\WinBoard.Power.",StringComparison.Ordinal))return;try{using(var parent=Process.GetProcessById(pid))using(var ready=EventWaitHandle.OpenExisting(args[4])){if(parent.StartTime.ToUniversalTime().Ticks!=ticks)return;ready.Set();AfterExit(parent,()=>{var action=args[3]=="shutdown"?ControlPowerAction.Shutdown:ControlPowerAction.Restart;using(var command=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shutdown.exe"),PowerCommands.Arguments(action)){UseShellExecute=false,CreateNoWindow=true})){if(command.WaitForExit(3000)&&command.ExitCode!=0)throw new IOException("shutdown.exe exit code "+command.ExitCode);}},20000);}}catch(Exception ex){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","power-error.txt"),ex.ToString());}catch{}}}
 }
 public partial class Board {
  static bool systemSessionEnding;
  void ExitForWindowsSession(){systemSessionEnding=true;try{Quit();}catch(Exception ex){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","exit-error.txt"),ex.ToString());}catch{}Application.Current.Shutdown();}}
  void ExitThenPower(ControlPowerAction action){using(var parent=Process.GetCurrentProcess()){string token="Local\\WinBoard.Power."+Guid.NewGuid().ToString("N");using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,token))using(var helper=Process.Start(new ProcessStartInfo(parent.MainModule.FileName,"--power-after-exit "+parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks+" "+(action==ControlPowerAction.Shutdown?"shutdown":"restart")+" "+token){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(!ready.WaitOne(3000)){if(!helper.HasExited)helper.Kill();throw new IOException("电源操作准备失败，未执行关机或重启。");}Quit();}}}
  void BuildPowerButtons(StackPanel panel){
   var row=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(5,12,5,4)};
   var actions=new[]{ControlPowerAction.Shutdown,ControlPowerAction.Restart,ControlPowerAction.Sleep};var labels=new[]{"关机","重启","待机"};var glyphs=new[]{"\uE7E8","\uE777","\uE708"};
   for(int i=0;i<actions.Length;i++){var action=actions[i];string label=labels[i];var button=Btn("",()=>RequestPower(action,label));button.Height=42;button.Padding=new Thickness(4);button.Margin=new Thickness(3,0,3,0);button.Background=Hex("#18FFFFFF");button.BorderBrush=Hex("#40FFFFFF");button.ToolTip=action==ControlPowerAction.Sleep?"待机（睡眠）":"立即"+label;System.Windows.Automation.AutomationProperties.SetName(button,"电源："+label);var content=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};content.Children.Add(new TextBlock{Text=glyphs[i],FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),FontSize=17,Foreground=System.Windows.Media.Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});content.Children.Add(WText(label,12));button.Content=content;row.Children.Add(button);}panel.Children.Add(row);
  }
  void RequestPower(ControlPowerAction action,string label){
   if(controlPanel!=null)controlPanel.Hide();
   string prompt=action==ControlPowerAction.Sleep?"是否让电脑进入待机（睡眠）状态？":"是否立即"+label+"电脑？\n请先保存正在编辑的内容。";
   if(MessageBox.Show(this,prompt,"确认"+label,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   try{if(action==ControlPowerAction.Sleep){if(recording!=null||captureBusy)throw new InvalidOperationException("请先结束录屏，再让电脑进入睡眠。");SleepCommands.DetectMode();if(noteEditor!=null)FlushNote();Save();var delay=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};delay.Tick+=(s,e)=>{delay.Stop();try{SleepCommands.Request(new System.Windows.Interop.WindowInteropHelper(this).Handle);}catch(Exception ex){MessageBox.Show(this,"无法进入睡眠："+ex.Message,"WinBoard");}};delay.Start();}else ExitThenPower(action);}
   catch(Exception ex){MessageBox.Show(this,"无法执行"+label+"："+ex.Message,"WinBoard");}
  }
 }
}
