using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace WinBoard {
    internal sealed class DockPreviewWindow : Window {
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct ThumbnailProperties {
            public uint Flags;
            public NativeRect Destination,Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NativeSize { public int Width,Height; }
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out NativeSize size);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref ThumbnailProperties properties);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

        readonly List<IntPtr> handles=new List<IntPtr>();
        readonly List<RunningWindow> windows;
        readonly Button anchor;
        readonly Action<RunningWindow> select;
        readonly StackPanel row=new StackPanel{Orientation=Orientation.Horizontal};
        readonly DispatcherTimer leaveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
        int page,columns,away;
        bool interactive;
        public DockPreviewWindow(Window owner,Button button,List<RunningWindow> items,Action<RunningWindow> action,bool clicked){
            Owner=owner;anchor=button;windows=items;select=action;interactive=clicked;
            Title="WinBoard · 窗口预览";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            ShowInTaskbar=false;ShowActivated=clicked;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(39,40,48));
            var source=PresentationSource.FromVisual(button);
            var toDip=source.CompositionTarget.TransformFromDevice;
            var point=button.PointToScreen(new Point(button.ActualWidth/2,0));
            var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y)).WorkingArea;
            var leftTop=toDip.Transform(new Point(screen.Left,screen.Top));
            var rightBottom=toDip.Transform(new Point(screen.Right,screen.Bottom));
            var center=toDip.Transform(point);
            columns=Math.Max(1,Math.Min(5,(int)((rightBottom.X-leftTop.X-16)/194)));
            Width=Math.Min(items.Count,columns)*194+16;
            Height=items.Count>columns?184:152;
            Left=Math.Max(leftTop.X,Math.Min(center.X-Width/2,rightBottom.X-Width));
            Top=center.Y-Height>=leftTop.Y?center.Y-Height:Math.Min(rightBottom.Y-Height,center.Y+button.ActualHeight);
            var body=new StackPanel{Margin=new Thickness(8)};Content=body;body.Children.Add(row);
            if(items.Count>columns){
                var nav=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
                foreach(int delta in new[]{-1,1}){
                    var step=new Button{Content=delta<0?"‹":"›",Width=42,Margin=new Thickness(3,0,3,0)};
                    step.Click+=(s,e)=>{int count=(windows.Count+columns-1)/columns;page=(page+delta+count)%count;Draw();};nav.Children.Add(step);
                }
                body.Children.Add(nav);
            }
            Loaded+=(s,e)=>{Draw();leaveTimer.Start();};
            leaveTimer.Tick+=(s,e)=>{if(interactive)return;away=IsMouseOver||anchor.IsMouseOver?0:away+1;if(away>=3)Close();};
            KeyDown+=(s,e)=>{if(e.Key==Key.Escape)Close();};
            Deactivated+=(s,e)=>{if(interactive)Close();};
            Closed+=(s,e)=>{leaveTimer.Stop();Release();};
        }
        public void FocusPreview(){interactive=true;Activate();}
        void Release(){foreach(var handle in handles)DwmUnregisterThumbnail(handle);handles.Clear();}
        void Draw(){
            Release();row.Children.Clear();
            var frames=new List<TextBlock>();
            for(int index=page*columns;index<Math.Min(windows.Count,(page+1)*columns);index++){
                var item=windows[index];
                var stack=new StackPanel();
                var frame=new TextBlock{Width=178,Height=100,Text="预览不可用",TextAlignment=TextAlignment.Center,Padding=new Thickness(0,37,0,0),Foreground=Brushes.LightGray};
                frames.Add(frame);stack.Children.Add(frame);
                stack.Children.Add(new TextBlock{Text=item.Title,Width=178,Margin=new Thickness(0,5,0,0),Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis});
                var card=new Button{Content=stack,Width=188,Height=132,Margin=new Thickness(3,0,3,0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=item.Title};
                System.Windows.Automation.AutomationProperties.SetName(card,item.Title);
                card.Click+=(s,e)=>{Close();if(RunningApps.CanActivate(item.Handle))select(item);};row.Children.Add(card);
            }
            UpdateLayout();
            var hwnd=new WindowInteropHelper(this).Handle;
            var scale=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            for(int i=0;i<frames.Count;i++){
                var frame=frames[i];IntPtr handle;
                if(DwmRegisterThumbnail(hwnd,windows[page*columns+i].Handle,out handle)!=0)continue;
                handles.Add(handle);NativeSize size;
                if(DwmQueryThumbnailSourceSize(handle,out size)!=0||size.Width<=0||size.Height<=0)continue;
                var pos=scale.Transform(frame.TranslatePoint(new Point(),this));
                var box=scale.Transform(new Vector(frame.ActualWidth,frame.ActualHeight));
                double ratio=Math.Min(box.X/size.Width,box.Y/size.Height);
                int w=(int)Math.Round(size.Width*ratio),h=(int)Math.Round(size.Height*ratio);
                int x=(int)Math.Round(pos.X+(box.X-w)/2),y=(int)Math.Round(pos.Y+(box.Y-h)/2);
                var props=new ThumbnailProperties{Flags=9,Visible=true,Destination=new NativeRect{Left=x,Top=y,Right=x+w,Bottom=y+h}};
                if(DwmUpdateThumbnailProperties(handle,ref props)==0)frame.Text="";
            }
        }
    }
}
