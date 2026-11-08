using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinBoard {
    public sealed class AnalogClockFace : FrameworkElement {
        public DateTime Time=DateTime.Now;
        public Brush Foreground=Brushes.White;
        public FontFamily NumberFont=new FontFamily("Segoe UI");
        public double NumberSize=17;
        public bool Bold,Seconds=true;
        public void Update(DateTime time){Time=time;InvalidateVisual();}
        static Point Polar(double degrees,double radius){double angle=degrees*Math.PI/180;return new Point(120+Math.Sin(angle)*radius,120-Math.Cos(angle)*radius);}
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(12,255,255,255)),new Pen(new SolidColorBrush(Color.FromArgb(42,255,255,255)),1),new Point(120,120),112,112);
            for(int i=0;i<60;i++) {
                bool major=i%5==0;dc.PushOpacity(major?.85:.36);
                dc.DrawLine(new Pen(Foreground,major?2:1),Polar(i*6,major?95:101),Polar(i*6,106));dc.Pop();
            }
            foreach(int hour in new[]{12,3,6,9}) {
                var text=new FormattedText(hour.ToString(),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(NumberFont,FontStyles.Normal,Bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),NumberSize,Foreground,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var point=Polar(hour*30,78);dc.DrawText(text,new Point(point.X-text.Width/2,point.Y-text.Height/2));
            }
            double second=Time.Second+Time.Millisecond/1000.0,minute=Time.Minute+second/60,hourAngle=(Time.Hour%12+minute/60)*30;
            Hand(dc,hourAngle,53,5,Foreground,5);Hand(dc,minute*6,78,3,Foreground,7);
            var accent=new SolidColorBrush(Color.FromRgb(126,231,206));
            if(Seconds)Hand(dc,second*6,88,1.4,accent,15);
            dc.DrawEllipse(Foreground,null,new Point(120,120),4.5,4.5);dc.DrawEllipse(accent,null,new Point(120,120),2,2);
        }
        void Hand(DrawingContext dc,double angle,double length,double width,Brush brush,double tail) {
            dc.DrawLine(new Pen(brush,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Polar(angle,-tail),Polar(angle,length));
        }
    }
    public partial class Board {
        readonly Dictionary<string,ContentControl> widgetBodies=new Dictionary<string,ContentControl>();
        AnalogClockFace analogClock;
        TextBlock clockSecondsText;
        DispatcherTimer clockAnimationTimer;
        Window widgetSettingsDialog;
        readonly Dictionary<string,Button> widgetGrips=new Dictionary<string,Button>();
        readonly Dictionary<string,List<FrameworkElement>> widgetResizeEdges=new Dictionary<string,List<FrameworkElement>>();
        void AddWidgetLockMenu(ContextMenu menu,WidgetState state) {
            var toggle=new MenuItem{Header="锁定组件位置",IsCheckable=true,IsChecked=state.PositionLocked};menu.Items.Add(toggle);
            menu.Opened+=(s,e)=>toggle.IsChecked=state.PositionLocked;
            toggle.Click+=(s,e)=>{state.PositionLocked=toggle.IsChecked;Window window;if(widgetWindows.TryGetValue(state.Kind,out window))RememberWidget(window,state);UpdateWidgetLockUI(state);Save();};
        }
        void UpdateWidgetLockUI(WidgetState state) {
            Button grip;if(widgetGrips.TryGetValue(state.Kind,out grip)){grip.Cursor=state.Kind=="control"?Cursors.Hand:state.PositionLocked?Cursors.Arrow:Cursors.SizeAll;grip.ToolTip=state.PositionLocked?"位置已锁定 · 右键可解锁":state.Kind=="control"?"控制中心 · 点击展开，拖动移动":"拖动移动";}
            List<FrameworkElement> edges;if(widgetResizeEdges.TryGetValue(state.Kind,out edges))foreach(var edge in edges)edge.IsHitTestVisible=!state.PositionLocked;
        }
        Button WidgetAction(string symbol,string label,Action action) {
            var button=Btn("",action);button.Tag="widget-action";button.Width=28;button.Height=28;button.Padding=new Thickness(0);button.Margin=new Thickness(2,0,0,0);button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;button.Opacity=.72;button.ToolTip=label;
            string geometry=symbol=="close"?"M7,7 L17,17 M17,7 L7,17":symbol=="refresh"?"M20,8 A8,8 0 1 0 20,16 M20,3 L20,8 L15,8":
                "M9,3 L15,3 15.7,5.3 17.6,6.4 20,5.9 23,11.1 21.3,12.9 21.3,15.1 23,16.9 20,22.1 17.6,21.6 15.7,22.7 15,25 9,25 8.3,22.7 6.4,21.6 4,22.1 1,16.9 2.7,15.1 2.7,12.9 1,11.1 4,5.9 6.4,6.4 8.3,5.3 Z M16,14 A4,4 0 1 1 8,14 A4,4 0 1 1 16,14";
            var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=symbol=="settings"?15:14,Height=symbol=="settings"?16:14};
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding("Foreground"){Source=button});button.Content=path;
            System.Windows.Automation.AutomationProperties.SetName(button,label);
            button.MouseEnter+=(s,e)=>{button.Opacity=1;button.Background=Hex(symbol=="close"?"#35EF7080":"#20FFFFFF");};
            button.MouseLeave+=(s,e)=>{button.Opacity=.72;button.Background=Brushes.Transparent;};return button;
        }
        FrameworkElement WidgetHeader(string kind,Window window,WidgetState state) {
            var header=new Grid{Height=30,Margin=new Thickness(0,0,0,9)};
            var grip=Btn("",()=>{});grip.Tag="widget-grip";grip.Padding=new Thickness(0);grip.Margin=new Thickness(0);grip.Background=Brushes.Transparent;grip.BorderThickness=new Thickness(0);grip.Cursor=Cursors.SizeAll;grip.HorizontalContentAlignment=HorizontalAlignment.Left;grip.ToolTip="拖动移动";
            grip.Content=new Border{Width=30,Height=4,CornerRadius=new CornerRadius(2),Background=Hex("#80FFFFFF"),HorizontalAlignment=HorizontalAlignment.Left};
            System.Windows.Automation.AutomationProperties.SetName(grip,"移动"+WidgetTitle(kind));
            widgetGrips[kind]=grip;
            grip.PreviewMouseLeftButtonDown+=(s,e)=>{e.Handled=true;if(state.PositionLocked)return;try{window.DragMove();RememberWidget(window,state);Save();}catch(InvalidOperationException){}};
            grip.Width=44;grip.HorizontalAlignment=HorizontalAlignment.Center;
            header.Children.Add(grip);var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};header.Children.Add(actions);
            if(kind=="weather") {
                Button settings=null;settings=WidgetAction("settings","天气设置",()=>{settings.ContextMenu.PlacementTarget=settings;settings.ContextMenu.IsOpen=true;});
                var menu=new ContextMenu();AddWidgetLockMenu(menu,state);Menu(menu,"选择城市",ChooseCity);Menu(menu,"组件大小",()=>WidgetSettings(kind));Menu(menu,"天气外观",()=>AppearanceSettings(kind));Menu(menu,"数据来源：Open-Meteo",()=>Launch("https://open-meteo.com/"));settings.ContextMenu=menu;actions.Children.Add(settings);
                actions.Children.Add(WidgetAction("refresh","刷新天气",StartWeatherRefresh));
            }else actions.Children.Add(WidgetAction("settings",WidgetTitle(kind)+"设置",()=>WidgetSettings(kind)));
            actions.Children.Add(WidgetAction("close","关闭"+WidgetTitle(kind),()=>HideWidget(kind)));
            header.SizeChanged+=(s,e)=>{bool narrow=header.ActualWidth<220;grip.Width=narrow?32:44;foreach(Button action in actions.Children){action.Width=narrow?24:28;action.Margin=new Thickness(narrow?1:2,0,0,0);}};
            UpdateWidgetLockUI(state);return header;
        }
        void RememberWidget(Window window,WidgetState state){state.X=window.Left;state.Y=window.Top;state.Width=window.Width;state.Height=window.Height;}
        void AddWidgetResizeEdges(Window window,WidgetState state,Grid shell) {
            var edges=new List<FrameworkElement>();widgetResizeEdges[state.Kind]=edges;
            foreach(int mask in new[]{1,2,4,8,5,6,9,10}) {
                int edgeMask=mask;bool left=(mask&1)!=0,right=(mask&2)!=0,top=(mask&4)!=0,bottom=(mask&8)!=0;
                var edge=new Border{Background=Brushes.Transparent,HorizontalAlignment=left?HorizontalAlignment.Left:right?HorizontalAlignment.Right:HorizontalAlignment.Stretch,VerticalAlignment=top?VerticalAlignment.Top:bottom?VerticalAlignment.Bottom:VerticalAlignment.Stretch,Cursor=left||right?(top||bottom?(left==top?Cursors.SizeNWSE:Cursors.SizeNESW):Cursors.SizeWE):Cursors.SizeNS};
                if(left||right)edge.Width=top||bottom?12:6;if(top||bottom)edge.Height=left||right?12:6;
                Point start=new Point();Rect bounds=new Rect();
                edge.MouseLeftButtonDown+=(s,e)=>{if(state.PositionLocked)return;start=window.PointToScreen(e.GetPosition(window));bounds=new Rect(window.Left,window.Top,window.Width,window.Height);edge.CaptureMouse();e.Handled=true;};
                edge.MouseMove+=(s,e)=>{
                    if(state.PositionLocked||!edge.IsMouseCaptured||e.LeftButton!=MouseButtonState.Pressed)return;
                    var source=PresentationSource.FromVisual(window);if(source==null)return;
                    Vector delta=source.CompositionTarget.TransformFromDevice.Transform(window.PointToScreen(e.GetPosition(window))-start);
                    double width=Math.Max(window.MinWidth,Math.Min(window.MaxWidth,bounds.Width+((edgeMask&1)!=0?-delta.X:(edgeMask&2)!=0?delta.X:0)));
                    double height=Math.Max(window.MinHeight,Math.Min(window.MaxHeight,bounds.Height+((edgeMask&4)!=0?-delta.Y:(edgeMask&8)!=0?delta.Y:0)));
                    window.Width=width;window.Height=height;if(left)window.Left=bounds.Right-width;if(top)window.Top=bounds.Bottom-height;e.Handled=true;
                };
                edge.MouseLeftButtonUp+=(s,e)=>{if(edge.IsMouseCaptured){edge.ReleaseMouseCapture();e.Handled=true;}};
                edge.LostMouseCapture+=(s,e)=>{RememberWidget(window,state);Save();};edges.Add(edge);shell.Children.Add(edge);
            }
            UpdateWidgetLockUI(state);
        }
        void RebuildClock() {
            ContentControl host;if(!widgetBodies.TryGetValue("clock",out host))return;
            var state=prefs.Widgets.Windows.First(w=>w.Kind=="clock");state.Normalize();widgetTime=null;clockSecondsText=null;analogClock=null;
            var panel=new StackPanel{Width=260,VerticalAlignment=VerticalAlignment.Center};
            FontFamily font;try{font=new FontFamily(state.ClockFont);}catch{font=new FontFamily("Segoe UI Light");}
            if(state.ClockStyle=="圆盘") {
                analogClock=new AnalogClockFace{Width=240,Height=240,NumberFont=font,NumberSize=Math.Max(11,Math.Min(26,state.ClockFontSize/54*17)),Bold=state.ClockBold,Seconds=state.ClockSeconds==true,HorizontalAlignment=HorizontalAlignment.Center};
                System.Windows.Automation.AutomationProperties.SetName(analogClock,"圆盘时钟");panel.Children.Add(analogClock);
            }else {
                widgetTime=WText("",state.ClockFontSize*(state.ClockStyle=="简约"?1.2:1));widgetTime.FontFamily=font;widgetTime.FontWeight=state.ClockBold?FontWeights.SemiBold:FontWeights.Normal;widgetTime.TextWrapping=TextWrapping.NoWrap;
                System.Windows.Automation.AutomationProperties.SetName(widgetTime,"时钟时间");
                panel.Children.Add(new Viewbox{Child=widgetTime,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=260,Margin=new Thickness(0,8,0,0)});
                if(state.ClockStyle=="简约"&&state.ClockSeconds==true){clockSecondsText=WText("",15);clockSecondsText.FontFamily=font;clockSecondsText.HorizontalAlignment=HorizontalAlignment.Center;clockSecondsText.Opacity=.65;panel.Children.Add(clockSecondsText);}
            }
            widgetDate=WText("",13);widgetDate.Opacity=.78;widgetDate.HorizontalAlignment=HorizontalAlignment.Center;widgetDate.Margin=new Thickness(0,7,0,10);panel.Children.Add(widgetDate);
            host.Content=new Viewbox{Child=panel,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center};
            UpdateClockPresentation(DateTime.Now);UpdateClockAnimation();host.UpdateLayout();ApplyWidgetAppearance("clock");
        }
        void UpdateClockPresentation(DateTime now) {
            var state=prefs.Widgets.Windows.FirstOrDefault(w=>w.Kind=="clock");if(state==null)return;
            if(widgetTime!=null)widgetTime.Text=now.ToString(state.ClockStyle=="数字"&&state.ClockSeconds==true?"HH:mm:ss":"HH:mm");
            if(clockSecondsText!=null)clockSecondsText.Text=now.ToString("ss")+" 秒";
            if(widgetDate!=null)widgetDate.Text=now.ToString("yyyy年M月d日  dddd",CultureInfo.GetCultureInfo("zh-CN"));
            if(analogClock!=null)analogClock.Update(now);
        }
        void UpdateClockAnimation() {
            if(clockAnimationTimer==null){clockAnimationTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};clockAnimationTimer.Tick+=(s,e)=>{if(analogClock!=null)analogClock.Update(DateTime.Now);};}
            Window clock;bool animate=!stoppingWidgets&&analogClock!=null&&analogClock.Seconds&&SystemParameters.ClientAreaAnimation&&widgetWindows.TryGetValue("clock",out clock)&&clock.IsVisible;
            if(animate)clockAnimationTimer.Start();else clockAnimationTimer.Stop();
        }
        void WidgetSettings(string kind) {
            if(widgetSettingsDialog!=null){widgetSettingsDialog.Activate();return;}
            var state=prefs.Widgets.Windows.First(s=>s.Kind==kind);state.Normalize();Window widget;widgetWindows.TryGetValue(kind,out widget);
            StackPanel panel;var dialog=Dialog(WidgetTitle(kind)+"设置",kind=="clock"?700:400,out panel);widgetSettingsDialog=dialog;dialog.Width=470;if(widget!=null&&widget.IsVisible)dialog.Owner=widget;
            panel.Children.Add(Text(kind=="clock"?"时钟样式":"组件大小",24,TextColor));
            var locked=new CheckBox{Content="锁定组件位置",IsChecked=state.PositionLocked,Foreground=TextColor,Margin=new Thickness(0,12,0,16),ToolTip="阻止拖动和边缘缩放；仍可在此主动设置尺寸"};panel.Children.Add(locked);
            locked.Click+=(s,e)=>{state.PositionLocked=locked.IsChecked==true;if(widget!=null)RememberWidget(widget,state);UpdateWidgetLockUI(state);Save();};
            ComboBox style=null,font=null;Slider fontSize=null;CheckBox seconds=null,bold=null;
            if(kind=="clock") {
                panel.Children.Add(Text("显示样式",13,Muted));style=new ComboBox{ItemsSource=new[]{"数字","圆盘","简约"},SelectedItem=state.ClockStyle,Margin=new Thickness(0,8,0,16),Padding=new Thickness(8)};panel.Children.Add(style);System.Windows.Automation.AutomationProperties.SetName(style,"时钟样式");
                panel.Children.Add(Text("字体",13,Muted));font=new ComboBox{ItemsSource=Fonts.SystemFontFamilies.Select(f=>f.Source).Concat(new[]{state.ClockFont,"Segoe UI Light"}).Distinct().OrderBy(n=>n).ToList(),SelectedItem=state.ClockFont,Margin=new Thickness(0,8,0,16),Padding=new Thickness(8),MaxDropDownHeight=240};panel.Children.Add(font);System.Windows.Automation.AutomationProperties.SetName(font,"时钟字体");
                var sizeText=Text("字号 · "+state.ClockFontSize.ToString("0")+"（自动适配窗口）",13,Muted);panel.Children.Add(sizeText);
                fontSize=new Slider{Minimum=24,Maximum=100,Value=state.ClockFontSize,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,16)};System.Windows.Automation.AutomationProperties.SetName(fontSize,"时钟字号");fontSize.ValueChanged+=(s,e)=>sizeText.Text="字号 · "+fontSize.Value.ToString("0")+"（自动适配窗口）";panel.Children.Add(fontSize);
                var options=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,18)};seconds=new CheckBox{Content="显示秒",IsChecked=state.ClockSeconds,Foreground=TextColor,Margin=new Thickness(0,0,28,0)};bold=new CheckBox{Content="加粗字体",IsChecked=state.ClockBold,Foreground=TextColor};options.Children.Add(seconds);options.Children.Add(bold);panel.Children.Add(options);
            }
            var width=Field(panel,kind=="control"?"直径（像素）":"宽度（像素）",Math.Round(widget==null?state.Width:widget.Width).ToString(CultureInfo.InvariantCulture));
            TextBox height=kind=="control"?null:Field(panel,"高度（像素）",Math.Round(widget==null?state.Height:widget.Height).ToString(CultureInfo.InvariantCulture));
            if(style!=null)style.SelectionChanged+=(s,e)=>{double value,current;if((string)style.SelectedItem=="圆盘"&&double.TryParse(width.Text,out value)&&double.TryParse(height.Text,out current)&&current<value)height.Text=(value+42).ToString("0");};
            var feedback=Text("也可以拖动顶部横条移动、拖动边缘改变大小。",12,Muted);panel.Children.Add(feedback);
            Func<bool> apply=()=>{
                double targetWidth,targetHeight;
                if(!double.TryParse(width.Text,out targetWidth)||(height!=null&&!double.TryParse(height.Text,out targetHeight))){feedback.Text="请输入有效的宽度和高度。";return false;}
                targetHeight=height==null?targetWidth:double.Parse(height.Text);
                if(double.IsNaN(targetWidth)||double.IsInfinity(targetWidth)||double.IsNaN(targetHeight)||double.IsInfinity(targetHeight)||targetWidth<state.MinimumWidth||targetHeight<state.MinimumHeight||targetWidth>(kind=="control"?120:900)||targetHeight>(kind=="control"?120:900)){feedback.Text="尺寸范围：宽 "+state.MinimumWidth+"–"+(kind=="control"?120:900)+"，高 "+state.MinimumHeight+"–"+(kind=="control"?120:900)+"。";return false;}
                state.Width=targetWidth;state.Height=targetHeight;
                if(style!=null){state.ClockStyle=(string)style.SelectedItem;state.ClockFont=(string)font.SelectedItem??state.ClockFont;state.ClockFontSize=fontSize.Value;state.ClockSeconds=seconds.IsChecked==true;state.ClockBold=bold.IsChecked==true;}
                if(widget!=null){widget.Width=state.Width;widget.Height=state.Height;PlaceWidget(widget,state);RememberWidget(widget,state);}
                if(kind=="clock")RebuildClock();Save();feedback.Text="已应用并保存";return true;
            };
            var actions=new WrapPanel{Margin=new Thickness(0,18,0,0)};actions.Children.Add(Btn("外观",()=>AppearanceSettings(kind)));actions.Children.Add(Btn("应用",()=>apply()));actions.Children.Add(Btn("完成",()=>{if(apply())dialog.Close();},true));panel.Children.Add(actions);
            dialog.Closed+=(s,e)=>widgetSettingsDialog=null;dialog.Show();
        }
    }
}
