using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinBoard {
    public partial class Board {
        readonly Dictionary<string,FrameworkElement> widgetHeaders=new Dictionary<string,FrameworkElement>();
        void UpdateWidgetHeader(string kind) {
            FrameworkElement header;if(widgetHeaders.TryGetValue(kind,out header))header.Visibility=prefs.Widgets.Windows.First(w=>w.Kind==kind).HideHeader?Visibility.Collapsed:Visibility.Visible;
        }
        void AddWidgetHeaderMenu(ContextMenu menu,WidgetState state) {
            var item=new MenuItem{Header="显示顶部控件",IsCheckable=true,IsChecked=!state.HideHeader};menu.Items.Add(item);
            menu.Opened+=(s,e)=>item.IsChecked=!state.HideHeader;
            item.Click+=(s,e)=>{state.HideHeader=!item.IsChecked;UpdateWidgetHeader(state.Kind);Save();};
        }
        bool RecreateTransparentWidget(string kind) {
            Window old;if(kind=="control"||!widgetWindows.TryGetValue(kind,out old)||old.AllowsTransparency==(AppearanceFor(kind).Opacity<=0))return false;
            var state=prefs.Widgets.Windows.First(w=>w.Kind==kind);bool enabled=state.Enabled;
            RememberWidget(old,state);if(kind=="notes"){if(noteSaveTimer!=null)noteSaveTimer.Stop();FlushNote();}
            if(widgetSettingsDialog!=null&&widgetSettingsDialog.Owner==old)widgetSettingsDialog.Close();
            bool stopping=stoppingWidgets;stoppingWidgets=true;
            try{old.Close();}finally{stoppingWidgets=stopping;}
            glassWindows.Remove(old);widgetWindows.Remove(kind);widgetRoots.Remove(kind);widgetHeaders.Remove(kind);widgetBodies.Remove(kind);widgetResizeEdges.Remove(kind);widgetGrips.Remove(kind);
            ShowWidget(kind);if(!enabled)HideWidget(kind);return true;
        }
        FrameworkElement DockReflectionVisual(FrameworkElement icon) {
            var group=new Grid{Width=36,Height=64};icon.VerticalAlignment=VerticalAlignment.Top;group.Children.Add(icon);
            var mirror=new Border{Width=36,Height=24,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,2),IsHitTestVisible=false,
                Background=new VisualBrush(icon){Stretch=Stretch.Fill,RelativeTransform=new ScaleTransform(1,-1,.5,.5)},
                OpacityMask=new LinearGradientBrush(Color.FromArgb(105,255,255,255),System.Windows.Media.Colors.Transparent,90)};
            group.Children.Add(mirror);return group;
        }
        void ApplyDockStyle() {
            if(prefs.DockStyle!=2)return;
            surface.BorderThickness=new Thickness(0);surface.CornerRadius=new CornerRadius(0);
            var tint=Hex(prefs.CompactAppearance.Color).Color;double opacity=prefs.CompactAppearance.Opacity;
            var floor=new LinearGradientBrush();floor.StartPoint=new Point(0,0);floor.EndPoint=new Point(0,1);
            floor.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Transparent,0));
            floor.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(opacity*80),tint.R,tint.G,tint.B),.55));
            floor.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(opacity*150),tint.R,tint.G,tint.B),1));surface.Background=Brushes.Transparent;
            root.Children.Add(new Border{Background=floor,Margin=new Thickness(0,dockTopPadding,0,dockBottomPadding),IsHitTestVisible=false});
        }
        void AddDockStyleMenu(ContextMenu menu) {
            var styles=new MenuItem{Header="紧凑栏样式"};menu.Items.Add(styles);
            foreach(int value in new[]{1,2}) {
                int chosen=value;var item=new MenuItem{Header=value==1?"样式一 · 玻璃胶囊":"样式二 · 镜面倒影",IsCheckable=true,IsChecked=prefs.DockStyle==value};styles.Items.Add(item);
                styles.SubmenuOpened+=(s,e)=>item.IsChecked=prefs.DockStyle==chosen;
                item.Click+=(s,e)=>{prefs.DockStyle=chosen;Save();RefreshDockPreferences();};
            }
        }
    }
}
