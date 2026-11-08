using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace WinBoard {
    [DataContract] public class Appearance {
        [DataMember] public double Opacity=.92;
        [DataMember] public string Color="#28232F";
        [DataMember] public string Material="渐变";
        [DataMember] public string Theme="深色";
        [DataMember] public bool HideBorder;
        [DataMember] public string FontColor;
        public static Appearance Normalize(Appearance value,string theme) {
            if(value==null)value=new Appearance{Theme=theme=="浅色"?"浅色":"深色",Color=theme=="浅色"?"#EEE9F0":"#28232F"};
            if(double.IsNaN(value.Opacity)||double.IsInfinity(value.Opacity))value.Opacity=.92;
            value.Opacity=Math.Max(0,Math.Min(1,value.Opacity));
            string fontColor;if(!Board.TryProjectColor(value.FontColor,out fontColor))value.FontColor=null;else value.FontColor=fontColor;
            try {var color=(Color)ColorConverter.ConvertFromString(value.Color);value.Color=string.Format("#{0:X2}{1:X2}{2:X2}",color.R,color.G,color.B);}catch{value.Color="#28232F";}
            if(value.Material!="纯色" && value.Material!="渐变" && value.Material!="柔光纹理" && value.Material!="液态玻璃")value.Material="渐变";
            if(value.Theme!="浅色")value.Theme="深色";return value;
        }
    }
    public partial class Board {
        Appearance ActiveAppearance {get{return compact?prefs.CompactAppearance:prefs.DashboardAppearance;}}
        Window appearanceDialog;
        Appearance AppearanceFor(string key) {return key=="dashboard"?prefs.DashboardAppearance:key=="compact"?prefs.CompactAppearance:prefs.Widgets.Windows.First(w=>w.Kind==key).Appearance;}
        Brush AppearanceBrush(Appearance appearance) {
            if(appearance.Opacity<=0)return Brushes.Transparent;
            Color color=Hex(appearance.Color).Color;
            if(appearance.Material=="液态玻璃") {
                var glass=new LinearGradientBrush();glass.StartPoint=new Point(0,0);glass.EndPoint=new Point(1,1);
                glass.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(28+appearance.Opacity*85),255,255,255),0));
                glass.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(appearance.Opacity*160),color.R,color.G,color.B),.45));
                glass.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(18+appearance.Opacity*95),color.R,color.G,color.B),1));return glass;
            }
            if(appearance.Material=="纯色")return new SolidColorBrush(color){Opacity=appearance.Opacity};
            Color end=Color.FromRgb((byte)(color.R*.72),(byte)(color.G*.72),(byte)(color.B*.72));
            var gradient=new LinearGradientBrush(color,end,45);
            if(appearance.Material=="渐变"){gradient.Opacity=appearance.Opacity;return gradient;}
            var drawing=new DrawingGroup();drawing.Children.Add(new GeometryDrawing(gradient,null,new RectangleGeometry(new Rect(0,0,120,120))));
            var glow=new RadialGradientBrush(Color.FromArgb(65,255,255,255),Color.FromArgb(0,255,255,255));
            drawing.Children.Add(new GeometryDrawing(glow,null,new RectangleGeometry(new Rect(0,0,120,120))));
            var random=new Random(42);
            for(int i=0;i<350;i++)drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(12,255,255,255)),null,new RectangleGeometry(new Rect(random.Next(120),random.Next(120),1,1))));
            return new DrawingBrush(drawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,120,120),Opacity=appearance.Opacity};
        }
        void ApplyWidgetAppearance(string kind) {
            if(RecreateTransparentWidget(kind))return;
            FrameworkElement element;if(!widgetRoots.TryGetValue(kind,out element))return;
            var style=AppearanceFor(kind);if(kind=="control"){ApplyControlGlass();return;}var border=(Border)element;border.Background=AppearanceBrush(style);
            var foreground=WidgetFontBrush(style);
            border.BorderBrush=Hex(style.Theme=="浅色"?"#B9ADCA":"#61546C");
            ApplyWidgetText(border,foreground,style.Theme=="浅色");DecorateGlass(border,style);UpdateWidgetHeader(kind);Window window;if(widgetWindows.TryGetValue(kind,out window))ConfigureGlass(window,()=>AppearanceFor(kind));
        }
        void ApplyWidgetText(DependencyObject node,Brush foreground,bool light) {
            var face=node as AnalogClockFace;if(face!=null){face.Foreground=foreground;face.InvalidateVisual();}
            var text=node as TextBlock;if(text!=null)text.Foreground=foreground;
            var box=node as TextBox;if(box!=null){box.Foreground=foreground;box.CaretBrush=foreground;}
            var button=node as Button;if(button!=null){button.Foreground=foreground;button.Background=(button.Tag as string??"").StartsWith("widget-")?Brushes.Transparent:Hex(light?"#E4DDEA":"#3B3546");}
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ApplyWidgetText(VisualTreeHelper.GetChild(node,i),foreground,light);
        }
        Brush WidgetFontBrush(Appearance style){return Hex(style.FontColor??(style.Theme=="浅色"?"#252936":"#F3F0F7"));}
        void ApplyControlFont(DependencyObject node,Brush brush){var text=node as TextBlock;if(text!=null&&text!=controlChargeIcon)text.Foreground=brush;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ApplyControlFont(VisualTreeHelper.GetChild(node,i),brush);}
        void AppearanceSettings(string initial) {
            if(dashboardHost!=null){dashboardHost.AppearanceSettings(initial);return;}
            if(appearanceDialog!=null){appearanceDialog.Activate();return;}
            StackPanel panel;var dialog=Dialog("独立外观设置",690,out panel);appearanceDialog=dialog;dialog.Width=540;
            panel.Children.Add(Text("分别设置每个窗口",23,TextColor));
            var hint=Text("液态玻璃使用系统背景模糊与高光。修改即时预览、自动保存。",12,Muted);hint.Margin=new Thickness(0,10,0,16);panel.Children.Add(hint);
            string[] keys={"dashboard","compact","clock","weather","notes","control"};
            var target=new ComboBox{ItemsSource=new[]{"大窗口","紧凑栏","时钟","天气","备忘录","控制中心"},SelectedIndex=Array.IndexOf(keys,initial),Margin=new Thickness(0,0,0,16),Padding=new Thickness(8)};
            System.Windows.Automation.AutomationProperties.SetName(target,"设置对象");panel.Children.Add(target);
            var editor=new StackPanel();panel.Children.Add(editor);
            Action populate=null;
            populate=()=>{
                editor.Children.Clear();string key=keys[Math.Max(0,target.SelectedIndex)];Appearance style=AppearanceFor(key);
                Action apply=()=>{if(key=="dashboard")Build();else if(key=="compact")RefreshDockPreferences();else ApplyWidgetAppearance(key);Save();};
                if(key=="compact") {
                    editor.Children.Add(Text("紧凑栏样式",13,Muted));
                    var dockStyle=new ComboBox{ItemsSource=new[]{"样式一 · 玻璃胶囊","样式二 · 镜面倒影"},SelectedIndex=prefs.DockStyle-1,Padding=new Thickness(8),Margin=new Thickness(0,8,0,16)};
                    System.Windows.Automation.AutomationProperties.SetName(dockStyle,"紧凑栏样式");dockStyle.SelectionChanged+=(s,e)=>{prefs.DockStyle=dockStyle.SelectedIndex+1;apply();};editor.Children.Add(dockStyle);
                }
                if(key!="dashboard"&&key!="compact") {
                    editor.Children.Add(Text("字体颜色",13,Muted));
                    var fontRow=new WrapPanel{Margin=new Thickness(0,8,0,8)};
                    var fontCode=new TextBox{Text=style.FontColor??(style.Theme=="浅色"?"#252936":"#F3F0F7"),Width=110,Padding=new Thickness(8),Margin=new Thickness(0,0,8,6)};System.Windows.Automation.AutomationProperties.SetName(fontCode,"组件字体颜色代码");fontRow.Children.Add(fontCode);
                    var fontPreview=Text("文字预览 Aa 123",14,WidgetFontBrush(style));fontPreview.Margin=new Thickness(0,0,0,12);
                    Action updateFont=()=>{apply();fontPreview.Foreground=WidgetFontBrush(style);};
                    fontRow.Children.Add(Btn("应用字体颜色",()=>{string chosen;if(!TryProjectColor(fontCode.Text,out chosen)){fontPreview.Text="请输入 #RRGGBB 或 #RGB";return;}style.FontColor=chosen;fontCode.Text=chosen;fontPreview.Text="文字预览 Aa 123";updateFont();}));
                    fontRow.Children.Add(Btn("选择字体颜色…",()=>{using(var picker=new Forms.ColorDialog{FullOpen=true}){var c=((SolidColorBrush)WidgetFontBrush(style)).Color;picker.Color=Drawing.Color.FromArgb(c.R,c.G,c.B);if(picker.ShowDialog(new FormsWindowOwner(new System.Windows.Interop.WindowInteropHelper(dialog).Handle))==Forms.DialogResult.OK){style.FontColor=string.Format("#{0:X2}{1:X2}{2:X2}",picker.Color.R,picker.Color.G,picker.Color.B);fontCode.Text=style.FontColor;updateFont();}}}));
                    fontRow.Children.Add(Btn("跟随主题",()=>{style.FontColor=null;fontCode.Text=style.Theme=="浅色"?"#252936":"#F3F0F7";updateFont();}));editor.Children.Add(fontRow);editor.Children.Add(fontPreview);
                    var border=new CheckBox{Content="显示组件边框",IsChecked=!style.HideBorder,Foreground=TextColor,Margin=new Thickness(0,0,0,12)};border.Click+=(s,e)=>{style.HideBorder=border.IsChecked!=true;apply();};editor.Children.Add(border);
                    if(key!="control") {
                        var state=prefs.Widgets.Windows.First(w=>w.Kind==key);
                        var toolbar=new CheckBox{Content="显示顶部控件（横条、设置、刷新、关闭）",IsChecked=!state.HideHeader,Foreground=TextColor,Margin=new Thickness(0,0,0,10)};
                        toolbar.Click+=(s,e)=>{state.HideHeader=toolbar.IsChecked!=true;apply();};editor.Children.Add(toolbar);
                        var tip=Text("隐藏后可右键组件内容恢复；按住 Alt 拖动内容可移动，锁定时除外。",11,Muted);tip.Margin=new Thickness(0,0,0,14);editor.Children.Add(tip);
                    }
                }
                editor.Children.Add(Text("背景不透明度（越低越透明）",13,Muted));
                var value=Text(Math.Round(style.Opacity*100)+"%",13,TextColor);editor.Children.Add(value);
                var opacity=new Slider{Minimum=key=="dashboard"?15:0,Maximum=100,Value=style.Opacity*100,Margin=new Thickness(0,8,0,18),IsSnapToTickEnabled=true,TickFrequency=1};
                System.Windows.Automation.AutomationProperties.SetName(opacity,"背景不透明度");
                opacity.ValueChanged+=(s,e)=>{style.Opacity=opacity.Value/100;value.Text=Math.Round(opacity.Value)+"%";apply();};editor.Children.Add(opacity);
                editor.Children.Add(Text("材质",13,Muted));
                var material=new ComboBox{ItemsSource=new[]{"液态玻璃","纯色","渐变","柔光纹理"},SelectedItem=style.Material,Margin=new Thickness(0,8,0,16),Padding=new Thickness(8)};
                System.Windows.Automation.AutomationProperties.SetName(material,"材质");material.SelectionChanged+=(s,e)=>{style.Material=(string)material.SelectedItem;apply();};editor.Children.Add(material);
                var palette=new WrapPanel{Margin=new Thickness(0,6,0,12)};
                foreach(string color in new[]{"#28232F","#233E46","#244536","#49344D","#554331","#EEE9F0"}) {string chosen=color;var button=Btn("●",()=>{style.Color=chosen;apply();populate();});button.Background=Hex(color);button.Foreground=Brushes.White;button.ToolTip=color;button.Padding=new Thickness(13,6,13,6);palette.Children.Add(button);}editor.Children.Add(Text("背景颜色",13,Muted));editor.Children.Add(palette);
                var colorRow=new StackPanel{Orientation=Orientation.Horizontal};var code=new TextBox{Text=style.Color,Width=130,Padding=new Thickness(8),Margin=new Thickness(0,0,10,0)};System.Windows.Automation.AutomationProperties.SetName(code,"背景颜色代码");colorRow.Children.Add(code);
                colorRow.Children.Add(Btn("应用颜色",()=>{try{Color c=(Color)ColorConverter.ConvertFromString(code.Text.Trim());style.Color=string.Format("#{0:X2}{1:X2}{2:X2}",c.R,c.G,c.B);apply();}catch{MessageBox.Show(dialog,"请输入有效颜色，例如 #28232F。");}}));
                colorRow.Children.Add(Btn("选色",()=>{using(var picker=new Forms.ColorDialog()){var current=Hex(style.Color).Color;picker.Color=Drawing.Color.FromArgb(current.R,current.G,current.B);picker.FullOpen=true;if(picker.ShowDialog()==Forms.DialogResult.OK){style.Color=string.Format("#{0:X2}{1:X2}{2:X2}",picker.Color.R,picker.Color.G,picker.Color.B);apply();populate();}}}));editor.Children.Add(colorRow);
                var theme=new ComboBox{ItemsSource=new[]{"深色","浅色"},SelectedItem=style.Theme,Margin=new Thickness(0,16,0,16),Padding=new Thickness(8)};System.Windows.Automation.AutomationProperties.SetName(theme,"文字与控件主题");theme.SelectionChanged+=(s,e)=>{style.Theme=(string)theme.SelectedItem;apply();};editor.Children.Add(Text("文字与控件主题",13,Muted));editor.Children.Add(theme);
                if(key=="dashboard") {
                    var wallpaper=new WrapPanel();wallpaper.Children.Add(Btn("选择背景图片",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="图片|*.jpg;*.jpeg;*.png;*.bmp"};if(picker.ShowDialog(dialog)==true){prefs.Wallpaper=picker.FileName;apply();}}));wallpaper.Children.Add(Btn("清除背景图片",()=>{prefs.Wallpaper=null;apply();}));editor.Children.Add(wallpaper);
                    var top=new CheckBox{Content="大窗口与紧凑栏置顶",IsChecked=prefs.Topmost,Foreground=TextColor,Margin=new Thickness(0,14,0,10)};top.Click+=(s,e)=>{prefs.Topmost=top.IsChecked==true;Topmost=prefs.Topmost;RefreshDockPreferences();Save();};editor.Children.Add(top);
                }
                var reset=Btn("恢复此项默认外观",()=>{style.Opacity=.38;style.Color="#28232F";style.Material="液态玻璃";style.Theme="深色";style.FontColor=null;style.HideBorder=false;if(key=="dashboard")prefs.Wallpaper=null;else if(key=="compact")prefs.DockStyle=1;else prefs.Widgets.Windows.First(w=>w.Kind==key).HideHeader=false;apply();populate();});reset.Margin=new Thickness(0,14,0,8);editor.Children.Add(reset);
            };
            target.SelectionChanged+=(s,e)=>populate();populate();
            var done=Btn("完成",()=>dialog.Close(),true);done.Margin=new Thickness(0,16,0,0);panel.Children.Add(done);
            dialog.Closed+=(s,e)=>{appearanceDialog=null;Save();};dialog.Show();
        }
    }
}
