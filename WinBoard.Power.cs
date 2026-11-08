using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Forms=System.Windows.Forms;
namespace WinBoard {
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
   try{if(action==ControlPowerAction.Sleep){if(!Forms.Application.SetSuspendState(Forms.PowerState.Suspend,false,false))throw new InvalidOperationException("Windows 未能进入睡眠，请检查系统电源设置。");}else ExitThenPower(action);}
   catch(Exception ex){MessageBox.Show(this,"无法执行"+label+"："+ex.Message,"WinBoard");}
  }
 }
}
