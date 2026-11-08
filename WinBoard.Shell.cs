using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WinBoard {
    public static class ShellEntries {
        public const string Computer="shell:MyComputerFolder",ControlPanel="shell:ControlPanelFolder",RecycleBin="shell:RecycleBinFolder";
        public const string Settings="ms-settings:";
        public static string Canonical(string path) {
            if(string.Equals(path,Computer,StringComparison.OrdinalIgnoreCase)||string.Equals(path,"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",StringComparison.OrdinalIgnoreCase))return Computer;
            if(string.Equals(path,ControlPanel,StringComparison.OrdinalIgnoreCase)||string.Equals(path,"::{26EE0668-A00A-44D7-9371-BEB064C98683}",StringComparison.OrdinalIgnoreCase)||string.Equals(path,"::{21EC2020-3AEA-1069-A2DD-08002B30309D}",StringComparison.OrdinalIgnoreCase))return ControlPanel;
            if(string.Equals(path,RecycleBin,StringComparison.OrdinalIgnoreCase)||string.Equals(path,"::{645FF040-5081-101B-9F08-00AA002F954E}",StringComparison.OrdinalIgnoreCase))return RecycleBin;
            return path;
        }
        public static string Name(string path){path=Canonical(path);return path==Settings?"Windows 设置":path==Computer?"此电脑":path==ControlPanel?"控制面板":path==RecycleBin?"回收站":null;}
        [DllImport("shell32.dll")] static extern IntPtr ILCombine(IntPtr parent,IntPtr child);
        [DllImport("shell32.dll")] static extern int SHGetNameFromIDList(IntPtr pidl,uint type,out IntPtr name);
        static bool ValidPidl(byte[] bytes,int offset,int minimum) {
            if(offset<minimum || offset>bytes.Length-2)return false;
            while(offset<=bytes.Length-2){int size=BitConverter.ToUInt16(bytes,offset);if(size==0)return true;if(size<2 || size>bytes.Length-offset)return false;offset+=size;}return false;
        }
        public static IEnumerable<string> DropPaths(IDataObject data) {
            var paths=new List<string>();
            if(data.GetDataPresent(DataFormats.FileDrop)){var files=data.GetData(DataFormats.FileDrop) as string[];if(files!=null)paths.AddRange(files);}
            if(!data.GetDataPresent("Shell IDList Array"))return paths;
            var stream=data.GetData("Shell IDList Array") as MemoryStream;if(stream==null||stream.Length>1048576)return paths;
            byte[] bytes=stream.ToArray();if(bytes.Length<12)return paths;uint count=BitConverter.ToUInt32(bytes,0);if(count>4096)return paths;int header=4+4*((int)count+1);if(header>bytes.Length)return paths;
            int parent=BitConverter.ToInt32(bytes,4);if(!ValidPidl(bytes,parent,header))return paths;
            IntPtr memory=Marshal.AllocCoTaskMem(bytes.Length);
            try{Marshal.Copy(bytes,0,memory,bytes.Length);for(int i=0;i<count;i++){
                int child=BitConverter.ToInt32(bytes,8+4*i);if(!ValidPidl(bytes,child,header))continue;
                IntPtr combined=ILCombine(IntPtr.Add(memory,parent),IntPtr.Add(memory,child));if(combined==IntPtr.Zero)continue;
                try{IntPtr name;if(SHGetNameFromIDList(combined,0x80028000,out name)==0){try{paths.Add(Canonical(Marshal.PtrToStringUni(name)));}finally{Marshal.FreeCoTaskMem(name);}}}finally{Marshal.FreeCoTaskMem(combined);}
            }}finally{Marshal.FreeCoTaskMem(memory);}return paths;
        }
    }
    public partial class Board {
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHParseDisplayName(string name,IntPtr context,out IntPtr pidl,uint flags,out uint attributes);
        [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW",CharSet=CharSet.Unicode)] static extern IntPtr ShellIconInfo(IntPtr pidl,uint attributes,ref ShellInfo info,uint size,uint flags);
        BitmapSource GetSystemIcon(string path) {
            if(path==ShellEntries.Settings){BitmapSource settings;if(icons.TryGetValue(path,out settings))return settings;settings=RegisteredAppIcon.Settings()??GetIcon(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","SystemSettings.exe"));if(settings!=null)icons[path]=settings;return settings;}
            BitmapSource image;if(icons.TryGetValue(path,out image))return image;IntPtr pidl;uint attrs;
            string parsing=path==ShellEntries.RecycleBin?"::{645FF040-5081-101B-9F08-00AA002F954E}":path==ShellEntries.Computer?"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}":"::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            if(SHParseDisplayName(parsing,IntPtr.Zero,out pidl,0,out attrs)!=0)return null;
            try{var info=new ShellInfo();if(ShellIconInfo(pidl,0,ref info,(uint)Marshal.SizeOf(info),0x108)==IntPtr.Zero||info.Icon==IntPtr.Zero)return null;
                try{image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();icons[path]=image;return image;}finally{DestroyIcon(info.Icon);}
            }finally{Marshal.FreeCoTaskMem(pidl);}
        }
    }
}
