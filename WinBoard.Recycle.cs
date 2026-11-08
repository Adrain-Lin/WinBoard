using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace WinBoard {
 public static class ShellRecycle {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Operation {public IntPtr Owner;public uint Function;public string From,To;public ushort Flags;[MarshalAs(UnmanagedType.Bool)]public bool Aborted;public IntPtr Mappings;public string Title;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHFileOperation(ref Operation operation);
  public static string[] Validate(string[] paths){if(paths==null||paths.Length==0)throw new IOException("未找到可回收的文件。");return paths.Select(path=>{if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))throw new IOException("只支持本地文件和文件夹。");string full=Path.GetFullPath(path),root=Path.GetPathRoot(full);if(string.Equals(full.TrimEnd('\\'),root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||new DriveInfo(root).DriveType!=DriveType.Fixed)throw new IOException("此位置不支持拖入回收站。");if(!File.Exists(full)&&!Directory.Exists(full))throw new IOException("文件已不存在："+full);return full;}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
  public static bool Recycle(string[] paths,IntPtr owner){paths=Validate(paths);var operation=new Operation{Owner=owner,Function=3,From=string.Join("\0",paths)+"\0\0",Flags=0x40|0x4000|0x10};int result=SHFileOperation(ref operation);if(operation.Aborted)return false;if(result!=0)throw new IOException("Windows 无法将文件移入回收站（错误 "+result+"）。");return true;}
 }
 public partial class Board {
  DockEntry RecycleDropTarget(DragEventArgs e){return dockEntries.FirstOrDefault(entry=>entry.Pin!=null&&entry.Pin.Path==ShellEntries.RecycleBin&&entry.Button!=null&&entry.Button.IsVisible&&new Rect(entry.Button.RenderSize).Contains(e.GetPosition(entry.Button)));}
  string[] RecycleDropPaths(DragEventArgs e){try{return ShellRecycle.Validate(ShellEntries.DropPaths(e.Data).ToArray());}catch{return null;}}
  void RecycleDroppedFiles(DragEventArgs e){e.Effects=DragDropEffects.None;var paths=RecycleDropPaths(e);if(paths==null){SetRecycleHover(null);return;}var entry=RecycleDropTarget(e);var icon=GetIcon(paths[0]);dockDragging=true;try{if(ShellRecycle.Recycle(paths,new WindowInteropHelper(this).Handle)){e.Effects=DragDropEffects.Move;AnimateRecycling(entry,icon);}else SetRecycleHover(null);}catch(Exception ex){SetRecycleHover(null);MessageBox.Show(this,ex.Message,"移入回收站");}finally{dockDragging=false;}}
 }
}
