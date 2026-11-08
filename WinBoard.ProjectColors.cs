using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace WinBoard {
    public partial class Board {
        public static bool TryProjectColor(string input,out string color) {
            color=null;string value=(input??"").Trim();if(value.StartsWith("#"))value=value.Substring(1);
            if(!Regex.IsMatch(value,@"\A(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z"))return false;
            if(value.Length==3)value=""+value[0]+value[0]+value[1]+value[1]+value[2]+value[2];
            color="#"+value.ToUpperInvariant();return true;
        }
        Func<string> ProjectColorEditor(StackPanel panel,string initial,Window owner) {
            panel.Children.Add(Text("项目标记颜色",12,Muted));
            string selected; if(!TryProjectColor(initial,out selected))selected=Colors[0];
            var palette=new WrapPanel{Margin=new Thickness(0,8,0,10)};panel.Children.Add(palette);
            var choices=new Dictionary<string,Button>();
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,5)};
            var previewColor=new Border{Width=38,Height=36,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,10,0),BorderBrush=Line,BorderThickness=new Thickness(1)};row.Children.Add(previewColor);
            var code=new TextBox{Text=selected,Width=122,Padding=new Thickness(8),FontSize=14,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0),Foreground=TextColor,Background=CardColor,BorderBrush=Line};
            System.Windows.Automation.AutomationProperties.SetName(code,"项目颜色代码");row.Children.Add(code);
            var help=Text("支持 #RRGGBB 或 #RGB，可粘贴颜色值。",11,Muted);help.Margin=new Thickness(0,0,0,14);
            Action update=()=>{
                string value;bool valid=TryProjectColor(code.Text,out value);if(valid)selected=value;
                previewColor.Background=Hex(selected);previewColor.ToolTip=selected;
                help.Text=valid?"支持 #RRGGBB 或 #RGB，可粘贴颜色值。":"请输入有效颜色，例如 #58A6FF。";help.Foreground=valid?Muted:Hex("#F39191");
                code.BorderBrush=valid?Line:Hex("#F39191");
                foreach(var pair in choices){bool match=valid&&pair.Key==selected;pair.Value.Content=match?"✓":"";pair.Value.BorderThickness=new Thickness(match?2:1);pair.Value.BorderBrush=match?TextColor:Hex("#40FFFFFF");}
            };
            foreach(var value in new[]{"#B8A4E8","#83C9AE","#E5BC7B","#E79C9E","#B5C976","#D5B9A8","#58A6FF","#47C9E5","#F078A7","#9975E8","#4CAF80","#E8A642","#F07B61","#DE5C74","#7E9CE5","#B0BAC8","#F5F5F5","#536174"}) {
                string chosen=value;var c=Hex(value).Color;
                var swatch=Btn("",()=>code.Text=chosen);swatch.Width=38;swatch.Height=32;swatch.Margin=new Thickness(0,0,8,8);swatch.Padding=new Thickness(0);swatch.Background=Hex(value);swatch.Foreground=(c.R*.299+c.G*.587+c.B*.114)>155?Hex("#20242A"):Brushes.White;swatch.ToolTip=value;
                System.Windows.Automation.AutomationProperties.SetName(swatch,"项目颜色 "+value);choices.Add(value,swatch);palette.Children.Add(swatch);
            }
            row.Children.Add(Btn("自定义颜色…",()=>{
                var c=Hex(selected).Color;
                using(var picker=new Forms.ColorDialog{FullOpen=true,AnyColor=true,Color=Drawing.Color.FromArgb(c.R,c.G,c.B)}) {
                    if(picker.ShowDialog(new FormsWindowOwner(new System.Windows.Interop.WindowInteropHelper(owner).Handle))==Forms.DialogResult.OK)
                        code.Text=string.Format("#{0:X2}{1:X2}{2:X2}",picker.Color.R,picker.Color.G,picker.Color.B);
                }
            }));panel.Children.Add(row);panel.Children.Add(help);code.TextChanged+=(s,e)=>update();update();
            return ()=>{string value;if(TryProjectColor(code.Text,out value))return value;code.Focus();return null;};
        }
        sealed class FormsWindowOwner:Forms.IWin32Window {
            public IntPtr Handle {get;private set;}
            public FormsWindowOwner(IntPtr handle){Handle=handle;}
        }
    }
}
