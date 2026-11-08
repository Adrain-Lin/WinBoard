using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WinBoard {
 public static class ConnectionStatus {
  [StructLayout(LayoutKind.Sequential)] struct Stamp {public ushort Y,M,Dow,D,H,Min,S,Ms;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Device {public uint Size;public ulong Address;public uint Class;public int Connected,Remembered,Authenticated;public Stamp Seen,Used;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)] public string Name;}
  [StructLayout(LayoutKind.Sequential)] struct Search {public uint Size;public int Auth,Remembered,Unknown,Connected,Inquiry;public byte Timeout;public IntPtr Radio;}
  [DllImport("bthprops.cpl")] static extern IntPtr BluetoothFindFirstDevice(ref Search search,ref Device device);
  [DllImport("bthprops.cpl")] static extern bool BluetoothFindNextDevice(IntPtr find,ref Device device);
  [DllImport("bthprops.cpl")] static extern bool BluetoothFindDeviceClose(IntPtr find);
  static object Call(object v,string method,params object[] args){return v.GetType().InvokeMember(method,BindingFlags.InvokeMethod,null,v,args);}
  public static string Network(){object m=null,list=null;var names=new List<string>();try{m=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")));list=Call(m,"GetNetworks",1);foreach(var n in (IEnumerable)list)try{names.Add((string)Call(n,"GetName"));}finally{Marshal.ReleaseComObject(n);}return names.Count==0?"未连接网络":string.Join("、",names.Distinct());}catch{return "网络状态暂不可用";}finally{if(list!=null&&Marshal.IsComObject(list))Marshal.ReleaseComObject(list);if(m!=null)Marshal.ReleaseComObject(m);}}
  public static string Bluetooth(){try{var s=new Search{Size=(uint)Marshal.SizeOf(typeof(Search)),Connected=1};var d=new Device{Size=(uint)Marshal.SizeOf(typeof(Device))};var h=BluetoothFindFirstDevice(ref s,ref d);if(h==IntPtr.Zero)return "未检测到已连接设备";var names=new List<string>();try{do{if(d.Connected!=0)names.Add(d.Name);d.Size=(uint)Marshal.SizeOf(typeof(Device));}while(BluetoothFindNextDevice(h,ref d));}finally{BluetoothFindDeviceClose(h);}return names.Count==0?"未检测到已连接设备":string.Join("、",names.Distinct());}catch{return "蓝牙状态暂不可用";}}
 }
 public partial class Board {
  TextBlock connectedNetwork,connectedBluetooth;bool scanningConnections;DateTime connectionRead=DateTime.MinValue;
  void BuildConnectionSummary(StackPanel panel){var row=new Grid{Margin=new Thickness(5,0,5,8)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());connectedNetwork=Text("读取网络…",11,Brushes.White);connectedBluetooth=Text("读取蓝牙…",11,Brushes.White);foreach(var t in new[]{connectedNetwork,connectedBluetooth}){t.TextWrapping=TextWrapping.NoWrap;t.TextTrimming=TextTrimming.CharacterEllipsis;t.Margin=new Thickness(5,0,5,0);}row.Children.Add(connectedNetwork);Grid.SetColumn(connectedBluetooth,1);row.Children.Add(connectedBluetooth);panel.Children.Add(row);RefreshConnectionSummary();}
  void RefreshConnectionSummary(){if(scanningConnections||connectedNetwork==null||(DateTime.UtcNow-connectionRead).TotalSeconds<5)return;scanningConnections=true;Task.Factory.StartNew(()=>new[]{ConnectionStatus.Network(),ConnectionStatus.Bluetooth()}).ContinueWith(task=>{if(Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(()=>{scanningConnections=false;if(task.IsFaulted||stoppingWidgets)return;connectionRead=DateTime.UtcNow;connectedNetwork.Text=task.Result[0];connectedNetwork.ToolTip=task.Result[0];connectedBluetooth.Text=task.Result[1];connectedBluetooth.ToolTip=task.Result[1]+"；部分低功耗设备请在系统蓝牙列表中查看。";}));});}
  void OpenConnectionControls(bool bluetooth){if(controlPanel!=null)controlPanel.Hide();Launch(bluetooth?"ms-settings:bluetooth":"ms-availablenetworks:");}
 }
}
