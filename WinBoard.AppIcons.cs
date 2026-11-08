using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace WinBoard {
 public static class RegisteredAppIcon {
  static BitmapSource Asset(string root,string logo){if(string.IsNullOrEmpty(logo))return null;string file=Path.Combine(root,logo.Replace('/',Path.DirectorySeparatorChar));string directory=Path.GetDirectoryName(file),stem=Path.GetFileNameWithoutExtension(file);foreach(int size in new[]{64,96,48,256,32}){string candidate=Path.Combine(directory,stem+".targetsize-"+size+"_altform-unplated.png");if(File.Exists(candidate)){using(var stream=File.OpenRead(candidate)){var image=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);image.Freeze();return image;}}}return null;}
  public static BitmapSource Settings(){try{return Asset(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel"),@"images\logo.png");}catch{return null;}}
  static BitmapSource Unplated(string id){try{string[] parts=id.Split('!');if(parts.Length!=2)return null;int split=parts[0].LastIndexOf('_');if(split<0)return null;string prefix=parts[0].Substring(0,split)+"_",suffix=parts[0].Substring(split);using(var packages=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")){if(packages==null)return null;foreach(var name in packages.GetSubKeyNames().Where(n=>n.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&n.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)).OrderByDescending(n=>n)){using(var key=packages.OpenSubKey(name)){string root=key.GetValue("PackageRootFolder") as string;if(root==null)continue;string manifest=Path.Combine(root,"AppxManifest.xml");if(!File.Exists(manifest))continue;var xml=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(manifest,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))xml.Load(reader);foreach(XmlElement app in xml.SelectNodes("//*[local-name()='Application']")){if(!string.Equals(app.GetAttribute("Id"),parts[1],StringComparison.OrdinalIgnoreCase))continue;var visual=app.SelectSingleNode("*[local-name()='VisualElements']") as XmlElement;if(visual!=null){var image=Asset(root,visual.GetAttribute("Square44x44Logo"));if(image!=null)return image;}}}}}}catch{}return null;}
  [StructLayout(LayoutKind.Sequential)] struct Size {public int Width,Height;}
  [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Factory{[PreserveSig]int GetImage(Size size,uint flags,out IntPtr bitmap);}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHCreateItemFromParsingName(string name,IntPtr bind,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out Factory factory);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
  public static BitmapSource Load(string appId){var native=Unplated(appId);if(native!=null)return native;Factory factory=null;IntPtr bitmap=IntPtr.Zero;try{var iid=typeof(Factory).GUID;if(SHCreateItemFromParsingName("shell:AppsFolder\\"+appId,IntPtr.Zero,ref iid,out factory)!=0)return null;if(factory.GetImage(new Size{Width=64,Height=64},0x104,out bitmap)!=0||bitmap==IntPtr.Zero)return null;var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;}catch{return null;}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}}
 }
 public partial class Board {
  readonly HashSet<string> requestedAppIcons=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  void QueuePackagedIcon(string path){if(requestedAppIcons.Contains(path))return;requestedAppIcons.Add(path);var id=RunningApps.ShortcutIdentities(path).FirstOrDefault(x=>x.StartsWith("app:",StringComparison.OrdinalIgnoreCase));if(id==null)return;Task.Factory.StartNew(()=>RegisteredAppIcon.Load(id.Substring(4))).ContinueWith(task=>{if(Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(()=>{if(quitting||task.IsFaulted||task.Result==null)return;icons[path]=task.Result;if(compact)Build();else RenderMain();}));});}
 }
}
