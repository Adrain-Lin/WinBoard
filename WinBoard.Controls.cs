using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace WinBoard {
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class AudioEnumerator {}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IAudioDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioDevice {
        [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float value,ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float value,ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float value);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
    }
    public static class SystemControls {
        public static double Volume(double? value) {
            var enumerator=(IAudioEnumerator)new AudioEnumerator();IAudioDevice device=null;object endpoint=null;
            try {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));Guid iid=typeof(IEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out endpoint));var volume=(IEndpointVolume)endpoint;
                if(value.HasValue){Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)(Math.Max(0,Math.Min(100,value.Value))/100),ref context));}
                float actual;Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out actual));return actual*100;
            }finally {if(endpoint!=null)Marshal.ReleaseComObject(endpoint);if(device!=null)Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}
        }
        public static double Brightness(double? value) {
            if(value.HasValue)using(var search=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True"))using(var results=search.Get()) {
                bool found=false;foreach(ManagementObject monitor in results)using(monitor){monitor.InvokeMethod("WmiSetBrightness",new object[]{(uint)0,(byte)Math.Round(Math.Max(0,Math.Min(100,value.Value)))});found=true;}
                if(!found)throw new InvalidOperationException("此显示器不支持直接调节亮度，请打开显示设置。");
            }
            using(var search=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightness WHERE Active=True"))using(var results=search.Get()) {
                foreach(ManagementObject monitor in results)using(monitor)return Convert.ToDouble(monitor["CurrentBrightness"]);
            }
            throw new InvalidOperationException("此显示器不支持直接调节亮度，请打开显示设置。");
        }
    }
    public class GlassSliderTrack : Track {
        static Brush ColorBrush(string value){return (Brush)new BrushConverter().ConvertFromString(value);}
        static ControlTemplate Bar() {
            var template=new ControlTemplate(typeof(RepeatButton));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.HeightProperty,4.0);border.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));template.VisualTree=border;return template;
        }
        public GlassSliderTrack() {
            DecreaseRepeatButton=new RepeatButton{Command=Slider.DecreaseLarge,Background=ColorBrush("#38E99C"),Template=Bar(),Focusable=false};
            IncreaseRepeatButton=new RepeatButton{Command=Slider.IncreaseLarge,Background=ColorBrush("#66FFFFFF"),Template=Bar(),Focusable=false};
            var template=new ControlTemplate(typeof(Thumb));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));border.SetValue(Border.BackgroundProperty,ColorBrush("#37E79B"));border.SetValue(Border.BorderBrushProperty,ColorBrush("#CC719B87"));border.SetValue(Border.BorderThicknessProperty,new Thickness(4));template.VisualTree=border;
            Thumb=new Thumb{Width=18,Height=18,Template=template,Cursor=System.Windows.Input.Cursors.Hand};
        }
    }
    public partial class Board {
        Button WeatherIcon(string glyph,string name,Action action) {
            var button=ControlIcon(glyph,name,action,"#14FFFFFF");button.Width=32;button.Height=32;button.Margin=new Thickness(4,0,0,0);button.Padding=new Thickness(0);button.BorderThickness=new Thickness(0);((TextBlock)button.Content).FontSize=16;return button;
        }
        void StyleControlSlider(Slider slider) {
            var template=new ControlTemplate(typeof(Slider));var grid=new FrameworkElementFactory(typeof(Grid));grid.SetValue(FrameworkElement.HeightProperty,24.0);
            var track=new FrameworkElementFactory(typeof(GlassSliderTrack));track.Name="PART_Track";
            track.SetBinding(Track.MinimumProperty,new Binding("Minimum"){RelativeSource=RelativeSource.TemplatedParent});track.SetBinding(Track.MaximumProperty,new Binding("Maximum"){RelativeSource=RelativeSource.TemplatedParent});track.SetBinding(Track.ValueProperty,new Binding("Value"){RelativeSource=RelativeSource.TemplatedParent,Mode=BindingMode.TwoWay});
            grid.AppendChild(track);template.VisualTree=grid;slider.Template=template;
        }
        Slider ControlSlider(StackPanel panel,string glyph,string name,string uri,bool volume) {
            var row=new Grid{Height=48,Margin=new Thickness(2,0,2,0)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(35)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});
            var icon=WeatherIcon(glyph,volume?"静音或恢复":"显示设置",()=>{if(volume)VolumeKey(0xAD);else Launch(uri);});icon.Margin=new Thickness(0);row.Children.Add(icon);
            var slider=new Slider{Minimum=0,Maximum=100,SmallChange=1,LargeChange=10,IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,7,0)};StyleControlSlider(slider);System.Windows.Automation.AutomationProperties.SetName(slider,name);Grid.SetColumn(slider,1);row.Children.Add(slider);
            var settings=WeatherIcon("\uE76C",name+"设置",()=>Launch(uri));settings.Margin=new Thickness(0);Grid.SetColumn(settings,2);row.Children.Add(settings);panel.Children.Add(row);
            var delay=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
            Action change=()=>{try{if(volume)SystemControls.Volume(slider.Value);else SystemControls.Brightness(slider.Value);slider.ToolTip=name+" "+Math.Round(slider.Value)+"%";}catch(Exception ex){slider.ToolTip=ex.Message;slider.IsEnabled=false;}};
            delay.Tick+=(s,e)=>{delay.Stop();change();};slider.ValueChanged+=(s,e)=>{if(syncingControl)return;if(volume)change();else{delay.Stop();delay.Start();}};
            return slider;
        }
        void SyncControlValues() {
            syncingControl=true;
            try {
                if(controlVolume!=null)try{controlVolume.Value=SystemControls.Volume(null);controlVolume.IsEnabled=true;controlVolume.ToolTip="系统音量 "+Math.Round(controlVolume.Value)+"%";}catch{controlVolume.IsEnabled=false;controlVolume.ToolTip="未找到音频设备，可点击右侧打开声音设置。";}
                if(controlBrightness!=null)try{controlBrightness.Value=SystemControls.Brightness(null);controlBrightness.IsEnabled=true;controlBrightness.ToolTip="屏幕亮度 "+Math.Round(controlBrightness.Value)+"%";}catch{controlBrightness.IsEnabled=false;controlBrightness.ToolTip="此显示器不支持直接调光，可点击右侧打开显示设置。";}
            }finally{syncingControl=false;}
        }
    }
}
