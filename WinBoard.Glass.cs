using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace WinBoard {
    public partial class Board {
        [StructLayout(LayoutKind.Sequential)] struct GlassMargins {public int Left,Right,Top,Bottom;}
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref GlassMargins margins);
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy {public int State,Flags,Gradient,Animation;}
        [StructLayout(LayoutKind.Sequential)] struct CompositionData {public int Attribute;public IntPtr Data;public IntPtr Size;}
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
        static int ApplyGlassBlur(IntPtr handle,bool enabled) {
            var policy=new AccentPolicy{State=enabled?4:0,Flags=0,Gradient=0x012F2328};
            IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(policy));
            try {Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=new IntPtr(Marshal.SizeOf(policy))};return SetWindowCompositionAttribute(handle,ref data);}
            catch(EntryPointNotFoundException){return 0;}
            finally {Marshal.FreeHGlobal(memory);}
        }
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
        void RoundGlassWindow(Window window) {
            IntPtr handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
            var border=window.Content as Border;
            if(border!=null)border.CornerRadius=new CornerRadius(window.WindowState==WindowState.Maximized?0:8);
            SetWindowRgn(handle,IntPtr.Zero,true);
            int corner=window.WindowState==WindowState.Maximized?1:2;DwmSetWindowAttribute(handle,33,ref corner,4);
            int noBorder=-2;DwmSetWindowAttribute(handle,34,ref noBorder,4);

        }
        readonly HashSet<Window> glassWindows=new HashSet<Window>();
        Window controlPanel;
        Border controlPanelSurface,orbSurface;
        void ConfigureGlass(Window window,Func<Appearance> appearance) {
            // Zero-background widgets and the reflection Dock use per-pixel alpha, not native acrylic.
            if(window.AllowsTransparency){WindowChrome.SetWindowChrome(window,null);return;}
            Action update=()=>{
                var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
                var style=appearance();
                bool translucent=style.Material=="液态玻璃" || style.Opacity<1;
                var chrome=WindowChrome.GetWindowChrome(window);
                var glassFrame=new Thickness(translucent?-1:0);
                if(chrome!=null && chrome.GlassFrameThickness!=glassFrame)chrome.GlassFrameThickness=glassFrame;
                var source=HwndSource.FromHwnd(handle);
                if(source!=null)source.CompositionTarget.BackgroundColor=ColorsTransparent;
                int extent=style.Material=="液态玻璃" || style.Opacity<1?-1:0;var margins=new GlassMargins{Left=extent,Right=extent,Top=extent,Bottom=extent};
                int extended=DwmExtendFrameIntoClientArea(handle,ref margins);
                int dark=style.Theme=="浅色"?0:1;DwmSetWindowAttribute(handle,20,ref dark,4);
                int corner=2;DwmSetWindowAttribute(handle,33,ref corner,4);
                int backdrop=style.Material=="液态玻璃"?3:1;
                int result=DwmSetWindowAttribute(handle,38,ref backdrop,4);
                int accent=0;
                if(style.Material=="液态玻璃" || style.Opacity<1) {
                    int none=1;DwmSetWindowAttribute(handle,38,ref none,4);
                    accent=ApplyGlassBlur(handle,true);
                    if(accent==0)DwmSetWindowAttribute(handle,38,ref backdrop,4);
                }else ApplyGlassBlur(handle,false);
                RoundGlassWindow(window);
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","glass-status.txt"),"DWM backdrop="+backdrop+", HRESULT="+result+", frame="+extended+", blur="+accent+"\r\n"+DateTime.Now.ToString("s"));
            };
            if(glassWindows.Add(window)) {
                WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(appearance().Material=="液态玻璃" || appearance().Opacity<1?-1:0),CornerRadius=new CornerRadius(0)});
                bool queued=false;
                Action schedule=()=>{
                    if(queued)return;queued=true;
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{queued=false;update();}));
                };
                window.SizeChanged+=(s,e)=>schedule();
                window.StateChanged+=(s,e)=>schedule();
                window.SourceInitialized+=(s,e)=>schedule();window.Loaded+=(s,e)=>schedule();window.ContentRendered+=(s,e)=>schedule();window.Activated+=(s,e)=>schedule();

            }
            update();
        }
        static Color ColorsTransparent {get{return Color.FromArgb(0,0,0,0);}}
        void DecorateGlass(Border border,Appearance style) {
            border.BorderThickness=new Thickness(style.HideBorder?0:1);
            if(style.HideBorder)return;
            if(style.Material!="液态玻璃")return;
            var rim=new LinearGradientBrush();rim.StartPoint=new Point(0,0);rim.EndPoint=new Point(1,1);
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(190,255,255,255),0));
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(28,255,255,255),.4));
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(95,255,255,255),1));
            border.BorderBrush=rim;border.BorderThickness=new Thickness(1);
        }
        Button ControlIcon(string glyph,string name,Action action,string tint) {
            var button=Btn("",action);button.Content=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=25,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            button.ToolTip=name;System.Windows.Automation.AutomationProperties.SetName(button,name);
            button.Foreground=Brushes.White;button.Background=Hex(tint);button.BorderBrush=Hex("#44FFFFFF");
            button.Height=64;button.Margin=new Thickness(5);button.Padding=new Thickness(10);
            return button;
        }
        Slider controlVolume,controlBrightness;
        bool syncingControl;
        TextBlock controlPower,controlChargeIcon;
        void BuildControlIcons(StackPanel panel) {
            var shortcuts=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Rows=2,Margin=new Thickness(0,2,0,10)};
            string[] glyphs={"\uE701","\uE702","\uE709","\uEBA0","\uE708","\uE776"};
            string[] names={"网络设置","蓝牙设置","飞行模式设置","电源与电池设置","夜间模式设置","辅助功能设置"};
            string[] uris={"ms-settings:network-wifi","ms-settings:bluetooth","ms-settings:network-airplanemode","ms-settings:batterysaver","ms-settings:nightlight","ms-settings:easeofaccess"};
            for(int i=0;i<names.Length;i++){string uri=uris[i];var button=ControlIcon(glyphs[i],names[i],()=>{if(uri=="ms-settings:network-wifi")OpenConnectionControls(false);else if(uri=="ms-settings:bluetooth")OpenConnectionControls(true);else Launch(uri);},i<2?"#5035DFA0":"#20FFFFFF");button.Height=58;((TextBlock)button.Content).FontSize=21;shortcuts.Children.Add(button);}panel.Children.Add(shortcuts);BuildConnectionSummary(panel);
            controlBrightness=ControlSlider(panel,"\uE706","屏幕亮度","ms-settings:display",false);
            controlVolume=ControlSlider(panel,"\uE767","系统音量","ms-settings:sound",true);
            BuildPowerButtons(panel);
        }
        void ShowControlOrb(WidgetState state) {
            state.Normalize();var orb=new Window{Title="WinBoard · 控制中心",Width=state.Width,Height=state.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=prefs.Widgets.Topmost,ShowActivated=false};
            var orbGrid=new Grid{Width=50,Height=50,Margin=new Thickness(3)};
            orbSurface=new Border{CornerRadius=new CornerRadius(25),BorderThickness=new Thickness(1),Padding=new Thickness(0)};
            var orbButton=Btn("",ToggleControlPanel);orbButton.Margin=new Thickness(0);orbButton.Padding=new Thickness(0);orbButton.BorderThickness=new Thickness(0);orbButton.Background=Brushes.Transparent;orbButton.ToolTip="控制中心 · 点击展开，拖动移动，右键设置";
            System.Windows.Automation.AutomationProperties.SetName(orbButton,"展开控制中心");
            widgetGrips["control"]=orbButton;UpdateWidgetLockUI(state);
            var rings=new Grid{Width=27,Height=27};rings.Children.Add(new Ellipse{Width=23,Height=23,Fill=new RadialGradientBrush(Hex("#F7FFFFFF").Color,Hex("#A8FFFFFF").Color),Stroke=Hex("#55FFFFFF"),StrokeThickness=1});orbButton.Content=rings;
            orbSurface.Child=orbButton;orbGrid.Children.Add(orbSurface);orb.Content=new Viewbox{Child=orbGrid,Stretch=Stretch.Uniform};
            Point? origin=null;bool moved=false;
            orbButton.PreviewMouseLeftButtonDown+=(s,e)=>{origin=e.GetPosition(orb);moved=false;};
            orbButton.PreviewMouseMove+=(s,e)=>{
                if(state.PositionLocked){origin=null;return;}
                if(!origin.HasValue || e.LeftButton!=MouseButtonState.Pressed || moved)return;
                Point point=e.GetPosition(orb);if(Math.Abs(point.X-origin.Value.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-origin.Value.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                moved=true;origin=null;e.Handled=true;orbButton.ReleaseMouseCapture();if(controlPanel!=null)controlPanel.Hide();
                try{orb.DragMove();}catch(InvalidOperationException){}
                var area=SystemParameters.WorkArea;orb.Left=Math.Max(area.Left,Math.Min(orb.Left,area.Right-orb.Width));orb.Top=Math.Max(area.Top,Math.Min(orb.Top,area.Bottom-orb.Height));
                state.X=orb.Left;state.Y=orb.Top;Save();
            };
            orbButton.PreviewMouseLeftButtonUp+=(s,e)=>{origin=null;if(moved)e.Handled=true;};
            var menu=new ContextMenu();AddWidgetLockMenu(menu,state);Menu(menu,"组件大小",()=>WidgetSettings("control"));Menu(menu,"外观设置",()=>AppearanceSettings("control"));Menu(menu,"收起控制中心",()=>HideWidget("control"));orbSurface.ContextMenu=menu;
            widgetWindows["control"]=orb;widgetRoots["control"]=orbSurface;
            PlaceWidget(orb,state);orb.Closing+=(s,e)=>{if(!stoppingWidgets){e.Cancel=true;HideWidget("control");}};
            ApplyControlGlass();orb.Show();Save();
        }
        void ApplyControlGlass() {
            var style=AppearanceFor("control");
            if(controlPanel!=null&&controlPanel.AllowsTransparency!=(style.Opacity<=0)) {
                bool visible=controlPanel.IsVisible;var previous=controlPanel;bool stopping=stoppingWidgets;stoppingWidgets=true;
                try{previous.Close();}finally{stoppingWidgets=stopping;}glassWindows.Remove(previous);controlPanel=null;controlPanelSurface=null;
                if(visible)ToggleControlPanel();
            }
            if(orbSurface!=null){orbSurface.Background=AppearanceBrush(style);DecorateGlass(orbSurface,style);}
            if(controlPanelSurface!=null){controlPanelSurface.Background=AppearanceBrush(style);DecorateGlass(controlPanelSurface,style);if(style.FontColor!=null)ApplyControlFont(controlPanelSurface,WidgetFontBrush(style));else ApplyControlFont(controlPanelSurface,Brushes.White);ConfigureGlass(controlPanel,()=>AppearanceFor("control"));}
        }
        void ToggleControlPanel() {
            if(controlPanel!=null && controlPanel.IsVisible){controlPanel.Hide();return;}
            if(controlPanel==null) {
                controlPanel=new Window{Title="WinBoard · 控制面板",Width=360,Height=420,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=AppearanceFor("control").Opacity<=0,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=prefs.Widgets.Topmost,FontFamily=new FontFamily("Microsoft YaHei UI")};
                controlPanelSurface=new Border{CornerRadius=new CornerRadius(22),Padding=new Thickness(14),BorderThickness=new Thickness(1)};controlPanel.Content=controlPanelSurface;
                var layout=new DockPanel();controlPanelSurface.Child=layout;
                var header=new DockPanel{Margin=new Thickness(5,0,0,4),Height=42};
                var collapse=ControlIcon("\uE70D","收起控制面板",()=>controlPanel.Hide(),"#22FFFFFF");collapse.Width=32;collapse.Height=30;collapse.Padding=new Thickness(0);((TextBlock)collapse.Content).FontSize=15;DockPanel.SetDock(collapse,Dock.Right);header.Children.Add(collapse);
                var settings=ControlIcon("\uE713","控制中心外观",()=>{controlPanel.Hide();AppearanceSettings("control");},"#22FFFFFF");settings.Width=32;settings.Height=30;settings.Padding=new Thickness(0);((TextBlock)settings.Content).FontSize=16;DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);
                var status=new TextBlock{Text="\uE83F",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=25,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center};
                controlStatus=WText("",11);controlPower=new TextBlock{FontSize=14,FontWeight=FontWeights.Medium,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,0,0)};header.Children.Add(status);controlChargeIcon=new TextBlock{Text="\u26A1",FontFamily=new FontFamily("Segoe UI Symbol"),FontSize=18,Foreground=Hex("#52EBA7"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,0,0)};header.Children.Add(controlChargeIcon);header.Children.Add(controlPower);UpdateWidgetClock();status.ToolTip=controlStatus.Text;DockPanel.SetDock(header,Dock.Bottom);layout.Children.Add(header);
                var controls=new StackPanel();BuildControlIcons(controls);layout.Children.Add(controls);
                controlPanel.Deactivated+=(s,e)=>{Window orb;if(widgetWindows.TryGetValue("control",out orb) && !orb.IsMouseOver)controlPanel.Hide();};
                controlPanel.KeyDown+=(s,e)=>{if(e.Key==Key.Escape)controlPanel.Hide();};
                controlPanel.Closing+=(s,e)=>{if(!stoppingWidgets){e.Cancel=true;controlPanel.Hide();}};
                ApplyControlGlass();
            }
            var ball=widgetWindows["control"];var area=SystemParameters.WorkArea;
            double x=ball.Left-controlPanel.Width-12;if(x<area.Left)x=ball.Left+ball.Width+12;
            controlPanel.Left=Math.Max(area.Left,Math.Min(x,area.Right-controlPanel.Width));controlPanel.Top=Math.Max(area.Top,Math.Min(ball.Top-60,area.Bottom-controlPanel.Height));
            SyncControlValues();controlPanel.Show();controlPanel.Activate();
            controlPanelSurface.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(160)));
        }
    }
}
