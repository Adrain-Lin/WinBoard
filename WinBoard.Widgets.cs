using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace WinBoard {
    [DataContract] public class WidgetState {
        [DataMember] public string Kind;
        [DataMember] public Appearance Appearance;
        [DataMember] public bool Enabled=true;
        [DataMember] public double X=-1;
        [DataMember] public double Y=-1;
        [DataMember] public double Width;
        [DataMember] public double Height;
        [DataMember] public bool PositionLocked;
        [DataMember] public bool HideHeader;
        [DataMember] public string ClockStyle="数字";
        [DataMember] public string ClockFont="Segoe UI Light";
        [DataMember] public double ClockFontSize=54;
        [DataMember] public bool ClockBold;
        [DataMember] public bool? ClockSeconds;
        public double MinimumWidth {get{return Kind=="control"?40:Kind=="clock"?200:220;}}
        public double MinimumHeight {get{return Kind=="control"?40:Kind=="clock"?140:Kind=="weather"?250:200;}}
        public void Normalize() {
            double defaultWidth=Kind=="control"?56:Kind=="notes"?330:300;
            double defaultHeight=Kind=="control"?56:Kind=="clock"?210:330;
            Width=ValidSize(Width,defaultWidth,MinimumWidth,Kind=="control"?120:900);
            Height=Kind=="control"?Width:ValidSize(Height,defaultHeight,MinimumHeight,900);
            if(ClockStyle!="圆盘"&&ClockStyle!="简约")ClockStyle="数字";
            if(string.IsNullOrWhiteSpace(ClockFont))ClockFont="Segoe UI Light";
            ClockFontSize=ValidSize(ClockFontSize,54,24,100);ClockSeconds=ClockSeconds??true;
        }
        static double ValidSize(double value,double fallback,double min,double max){return double.IsNaN(value)||double.IsInfinity(value)||value<=0?fallback:Math.Max(min,Math.Min(max,value));}
    }
    [DataContract] public class WidgetPreferences {
        [DataMember] public List<WidgetState> Windows=new List<WidgetState>();
        [DataMember] public bool Topmost;
        [DataMember] public string Notes="";
        [DataMember] public string City="广州";
        [DataMember] public double Latitude=23.1291;
        [DataMember] public double Longitude=113.2644;
        public void Normalize(){Windows=Windows??new List<WidgetState>();foreach(var kind in new[]{"clock","weather","notes","control"})if(!Windows.Any(w=>w.Kind==kind))Windows.Add(new WidgetState{Kind=kind});foreach(var state in Windows){state.Appearance=Appearance.Normalize(state.Appearance,"深色");state.Normalize();}Notes=Notes??"";if(string.IsNullOrEmpty(City)){City="广州";Latitude=23.1291;Longitude=113.2644;}}
    }
    [DataContract] public class WeatherCurrent {
        [DataMember] public string time;
        [DataMember] public double? temperature_2m;
        [DataMember] public double? apparent_temperature;
        [DataMember] public double? relative_humidity_2m;
        [DataMember] public double? wind_speed_10m;
        [DataMember] public int? weather_code;
    }
    [DataContract] public class WeatherDaily {
        [DataMember] public double[] temperature_2m_max;
        [DataMember] public double[] temperature_2m_min;
    }
    [DataContract] public class WeatherData {
        [DataMember] public WeatherCurrent current;
        [DataMember] public WeatherDaily daily;
    }
    [DataContract] public class WeatherCache {
        [DataMember] public string City;
        [DataMember] public double Latitude;
        [DataMember] public double Longitude;
        [DataMember] public WeatherData Data;
    }
    [DataContract] public class CityResult {
        [DataMember] public string name;
        [DataMember] public string country;
        [DataMember] public string admin1;
        [DataMember] public double latitude;
        [DataMember] public double longitude;
        public override string ToString(){return name+" · "+admin1+" · "+country;}
    }
    [DataContract] public class CityResults {[DataMember] public List<CityResult> results;}
    public partial class Board {
        readonly Dictionary<string,Window> widgetWindows=new Dictionary<string,Window>();
        readonly Dictionary<string,FrameworkElement> widgetRoots=new Dictionary<string,FrameworkElement>();
        Window widgetManager;
        DispatcherTimer widgetTimer,weatherTimer,noteSaveTimer;
        TextBlock widgetTime,widgetDate,weatherTemp,weatherDetail,weatherStatus,weatherCity,noteStatus,controlStatus;
        TextBox noteEditor;
        WeatherData lastWeather;
        bool weatherBusy,stoppingWidgets;
        int weatherGeneration;
        static readonly HttpClient weatherClient=CreateClient();
        static readonly string WeatherFile=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","weather-cache.json");
        [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
        static HttpClient CreateClient(){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;var c=new HttpClient();c.Timeout=TimeSpan.FromSeconds(15);c.DefaultRequestHeaders.UserAgent.ParseAdd("WinBoard/3.0");return c;}
        string WidgetTitle(string kind){return kind=="clock"?"时钟":kind=="weather"?"天气":kind=="notes"?"备忘录":"控制中心";}
        TextBlock WText(string value,double size,string color="#F5F1F8"){return Text(value,size,Hex(color));}
        Button WButton(string label,Action action){var b=Btn(label,action);b.Foreground=Hex("#F5F1F8");b.Background=Hex("#3B3546");b.BorderBrush=Hex("#575061");b.Padding=new Thickness(11,7,11,7);return b;}
        void StartWidgets(){
            foreach(var state in prefs.Widgets.Windows.Where(s=>s.Enabled))ShowWidget(state.Kind);
            widgetTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};widgetTimer.Tick+=(s,e)=>UpdateWidgetClock();widgetTimer.Start();
            weatherTimer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(20)};weatherTimer.Tick+=async(s,e)=>{if(prefs.Widgets.Windows.Any(w=>w.Kind=="weather"&&w.Enabled))await RefreshWeather();};weatherTimer.Start();
        }
        void ShowWidget(string kind){
            Window existing;if(widgetWindows.TryGetValue(kind,out existing)){existing.Show();existing.Activate();return;}
            var state=prefs.Widgets.Windows.First(s=>s.Kind==kind);state.Enabled=true;
            if(kind=="control"){ShowControlOrb(state);return;}
            state.Normalize();
            var w=new Window{Title="WinBoard · "+WidgetTitle(kind),Width=state.Width,Height=state.Height,MinWidth=state.MinimumWidth,MinHeight=state.MinimumHeight,MaxWidth=900,MaxHeight=900,
                WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=state.Appearance.Opacity<=0,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=prefs.Widgets.Topmost,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,UseLayoutRounding=true};
            var surface=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=Hex("#61546C"),Padding=new Thickness(0)};
            var shell=new Grid();surface.Child=shell;w.Content=surface;
            var layout=new DockPanel{Margin=new Thickness(18,10,18,18)};shell.Children.Add(layout);
            var header=WidgetHeader(kind,w,state);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
            widgetHeaders[kind]=header;header.Visibility=state.HideHeader?Visibility.Collapsed:Visibility.Visible;
            w.PreviewMouseLeftButtonDown+=(s,e)=>{if((Keyboard.Modifiers&ModifierKeys.Alt)==0||state.PositionLocked)return;e.Handled=true;try{w.DragMove();RememberWidget(w,state);Save();}catch(InvalidOperationException){}};
            var host=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};layout.Children.Add(host);
            widgetWindows[kind]=w;widgetRoots[kind]=surface;widgetBodies[kind]=host;
            if(kind=="clock")RebuildClock();else if(kind=="weather"){
                var content=new StackPanel{Width=260};BuildWeather(content);host.Content=new Viewbox{Stretch=Stretch.Uniform,Child=content,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
            }else if(kind=="notes"){
                var content=new StackPanel();BuildNotes(content);host.Content=content;host.SizeChanged+=(s,e)=>{if(noteEditor!=null)noteEditor.Height=Math.Max(80,host.ActualHeight-40);};
            }
            AddWidgetResizeEdges(w,state,shell);
            ApplyWidgetAppearance(kind);
            var appearanceMenu=new ContextMenu();AddWidgetLockMenu(appearanceMenu,state);AddWidgetHeaderMenu(appearanceMenu,state);Menu(appearanceMenu,kind=="clock"?"时钟样式与大小":"组件大小",()=>WidgetSettings(kind));Menu(appearanceMenu,"外观设置",()=>AppearanceSettings(kind));surface.ContextMenu=appearanceMenu;
            PlaceWidget(w,state);
            w.Closing+=(s,e)=>{if(!stoppingWidgets){e.Cancel=true;HideWidget(kind);}};
            w.IsVisibleChanged+=(s,e)=>UpdateClockAnimation();
            w.Loaded+=(s,e)=>{ApplyWidgetAppearance(kind);Capture(surface,"widget-"+kind+".png");};w.Show();Save();
            if(kind=="weather")StartWeatherRefresh();
        }
        void PlaceWidget(Window w,WidgetState state){
            var area=SystemParameters.WorkArea;
            bool firstColumn=state.Kind=="clock"||state.Kind=="weather";
            double x=firstColumn?area.Right-320:area.Right-670;
            double y=area.Top+32+(state.Kind=="weather"?230:state.Kind=="control"?350:0);
            if(state.X>=0){x=state.X;y=state.Y;}
            w.Left=Math.Max(area.Left,Math.Min(x,area.Right-w.Width));w.Top=Math.Max(area.Top,Math.Min(y,area.Bottom-w.Height));
        }
        void HideWidget(string kind){
            var state=prefs.Widgets.Windows.First(s=>s.Kind==kind);state.Enabled=false;
            if(kind=="control" && controlPanel!=null)controlPanel.Hide();
            Window w;if(widgetWindows.TryGetValue(kind,out w)){RememberWidget(w,state);if(widgetSettingsDialog!=null&&widgetSettingsDialog.Owner==w)widgetSettingsDialog.Close();w.Hide();}
            if(kind=="notes")FlushNote();Save();
        }
        void ToggleWidget(string kind,bool show){
            var state=prefs.Widgets.Windows.First(s=>s.Kind==kind);state.Enabled=show;if(show)ShowWidget(kind);else HideWidget(kind);Save();
        }
        void WidgetManager(){
            if(widgetManager!=null){widgetManager.Activate();return;}
            StackPanel panel;widgetManager=Dialog("桌面组件",480,out panel);var dialog=widgetManager;
            panel.Children.Add(Text("把常用信息放到桌面",24,TextColor));var intro=Text("拖动顶部横条移动，拖动边缘调整大小；位置和尺寸会自动保存。",12,Muted);intro.Margin=new Thickness(0,10,0,20);panel.Children.Add(intro);
            foreach(var state in prefs.Widgets.Windows){var kind=state.Kind;var check=new CheckBox{Content=WidgetTitle(kind),IsChecked=state.Enabled,Foreground=TextColor,FontSize=15,Margin=new Thickness(0,0,0,16)};check.Checked+=(s,e)=>ToggleWidget(kind,true);check.Unchecked+=(s,e)=>ToggleWidget(kind,false);var row=new StackPanel{Orientation=Orientation.Horizontal};check.Width=190;row.Children.Add(check);var style=Btn("外观",()=>AppearanceSettings(kind));style.Padding=new Thickness(12,4,12,4);row.Children.Add(style);panel.Children.Add(row);}
            var top=new CheckBox{Content="组件保持置顶",IsChecked=prefs.Widgets.Topmost,Foreground=TextColor,Margin=new Thickness(0,8,0,20)};RoutedEventHandler setTop=(s,e)=>{prefs.Widgets.Topmost=top.IsChecked==true;foreach(var w in widgetWindows.Values)w.Topmost=prefs.Widgets.Topmost;if(controlPanel!=null)controlPanel.Topmost=prefs.Widgets.Topmost;Save();};top.Checked+=setTop;top.Unchecked+=setTop;panel.Children.Add(top);
            var buttons=new WrapPanel();buttons.Children.Add(Btn("恢复组件位置",()=>{foreach(var state in prefs.Widgets.Windows.Where(s=>!s.PositionLocked)){state.X=-1;state.Y=-1;Window w;if(widgetWindows.TryGetValue(state.Kind,out w))PlaceWidget(w,state);}Save();}));buttons.Children.Add(Btn("完成",()=>dialog.Close(),true));panel.Children.Add(buttons);dialog.Closed+=(s,e)=>widgetManager=null;dialog.Show();
        }
        void UpdateWidgetClock(){
            if(controlPanel!=null&&controlPanel.IsVisible)RefreshConnectionSummary();
            UpdateClockPresentation(DateTime.Now);
            if(controlStatus!=null){
                var power=Forms.SystemInformation.PowerStatus;
                string battery=BatteryLabel(power.PowerLineStatus,power.BatteryChargeStatus,power.BatteryLifePercent);
                if(controlPower!=null)controlPower.Text=battery;
                if(controlChargeIcon!=null){controlChargeIcon.Visibility=power.PowerLineStatus==Forms.PowerLineStatus.Online?Visibility.Visible:Visibility.Collapsed;controlChargeIcon.ToolTip=battery;}
                controlStatus.Text=(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()?"网络可用":"网络未连接")+"  ·  "+battery;
            }
        }
        public static string BatteryLabel(Forms.PowerLineStatus line,Forms.BatteryChargeStatus charge,float fraction) {
            if(charge!=Forms.BatteryChargeStatus.Unknown && (charge & Forms.BatteryChargeStatus.NoSystemBattery)!=0)return "交流电源";
            bool known=!float.IsNaN(fraction) && fraction>=0 && fraction<=1;
            string percent=known?Math.Round(fraction*100)+"%":"电量未知";
            bool charging=charge!=Forms.BatteryChargeStatus.Unknown && (charge & Forms.BatteryChargeStatus.Charging)!=0;
            if(charging)return percent+" · 正在充电";
            if(line==Forms.PowerLineStatus.Online)return percent+(known && fraction>=.995f?" · 已充满":" · 已接通电源");
            if(line==Forms.PowerLineStatus.Unknown)return percent+" · 电源状态未知";
            return percent+" · 使用电池";
        }
        void BuildNotes(StackPanel panel){
            noteEditor=new TextBox{Text=prefs.Widgets.Notes,Height=195,AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,Foreground=Hex("#F7EFE2"),CaretBrush=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(1),FontSize=15};
            System.Windows.Automation.AutomationProperties.SetName(noteEditor,"备忘录内容");noteEditor.ToolTip="随手记录，输入后自动保存到本机";panel.Children.Add(noteEditor);
            noteStatus=WText(string.IsNullOrEmpty(prefs.Widgets.Notes)?"随手记点什么，输入后自动保存":"已保存到本机",11,"#C6BAA7");noteStatus.Margin=new Thickness(0,12,0,0);panel.Children.Add(noteStatus);
            noteSaveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};noteSaveTimer.Tick+=(s,e)=>{noteSaveTimer.Stop();FlushNote();};
            noteEditor.TextChanged+=(s,e)=>{prefs.Widgets.Notes=noteEditor.Text;noteStatus.Text="正在保存…";noteSaveTimer.Stop();noteSaveTimer.Start();};
        }
        void FlushNote(){if(noteEditor!=null)prefs.Widgets.Notes=noteEditor.Text;try{Storage.Write(Config,prefs);if(noteStatus!=null)noteStatus.Text="已保存  "+DateTime.Now.ToString("HH:mm");}catch(Exception ex){if(noteStatus!=null)noteStatus.Text="保存失败，请勿关闭";if(!systemSessionEnding)MessageBox.Show("备忘录保存失败："+ex.Message,"WinBoard");}}
        void VolumeKey(byte key){keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);}
        void BuildWeather(StackPanel panel){
            weatherCity=WText(prefs.Widgets.City,20);panel.Children.Add(weatherCity);
            weatherTemp=WText("—°",44);weatherTemp.FontFamily=new FontFamily("Segoe UI Light");weatherTemp.Margin=new Thickness(0,8,0,0);panel.Children.Add(weatherTemp);
            weatherDetail=WText("正在获取天气…",13,"#D3CADA");weatherDetail.LineHeight=22;panel.Children.Add(weatherDetail);
            weatherStatus=WText("联网更新中",10,"#B9AFBF");weatherStatus.Margin=new Thickness(0,9,0,8);panel.Children.Add(weatherStatus);
            try{if(File.Exists(WeatherFile)){var cache=ReadJson<WeatherCache>(File.ReadAllText(WeatherFile));if(cache.City==prefs.Widgets.City && cache.Latitude==prefs.Widgets.Latitude && cache.Longitude==prefs.Widgets.Longitude){lastWeather=cache.Data;DisplayWeather(lastWeather,"缓存 · 正在更新");}}}catch{}
        }
        async void StartWeatherRefresh(){await RefreshWeather();}
        static T ReadJson<T>(string json){using(var s=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s);}
        async Task RefreshWeather(){
            if(weatherBusy||stoppingWidgets||weatherStatus==null)return;weatherBusy=true;int generation=weatherGeneration;
            string city=prefs.Widgets.City;double lat=prefs.Widgets.Latitude,lon=prefs.Widgets.Longitude;weatherStatus.Text="正在更新…";
            try {
                var url="https://api.open-meteo.com/v1/forecast?latitude="+lat.ToString(CultureInfo.InvariantCulture)+"&longitude="+lon.ToString(CultureInfo.InvariantCulture)+"&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&timezone=Asia%2FShanghai&forecast_days=1";
                var data=ReadJson<WeatherData>(await weatherClient.GetStringAsync(url));
                if(generation!=weatherGeneration||stoppingWidgets)return;
                if(data.current==null||!data.current.temperature_2m.HasValue)throw new InvalidDataException("响应缺少温度");
                lastWeather=data;DisplayWeather(data,"已更新");
                try{using(var stream=File.Create(WeatherFile))new DataContractJsonSerializer(typeof(WeatherCache)).WriteObject(stream,new WeatherCache{City=city,Latitude=lat,Longitude=lon,Data=data});}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);}
            }catch(Exception ex){if(generation==weatherGeneration&&!stoppingWidgets){if(lastWeather!=null)DisplayWeather(lastWeather,"连接失败 · 上次数据");else{weatherTemp.Text="—°";weatherDetail.Text="暂时无法获取天气\n请检查网络后点击刷新";weatherStatus.Text="未获取到天气数据";}weatherStatus.ToolTip=ex.Message;}}
            finally{weatherBusy=false;if(generation!=weatherGeneration&&!stoppingWidgets)StartWeatherRefresh();else if(widgetRoots.ContainsKey("weather"))Capture(widgetRoots["weather"],"widget-weather.png");}
        }
        string WeatherLabel(int? code){if(!code.HasValue)return "天气状态未知";int c=code.Value;if(c==0)return "晴";if(c==1)return "大部晴朗";if(c==2)return "多云";if(c==3)return "阴";if(c==45||c==48)return "雾";if(c>=51&&c<=57)return "毛毛雨";if(c>=61&&c<=67)return "雨";if(c>=71&&c<=77)return "雪";if(c>=80&&c<=82)return "阵雨";if(c==85||c==86)return "阵雪";if(c>=95)return "雷暴";return "天气代码 "+c;}
        string Number(double? value){return value.HasValue?value.Value.ToString("0",CultureInfo.InvariantCulture):"—";}
        void DisplayWeather(WeatherData data,string status){
            if(data==null||data.current==null)return;var c=data.current;weatherCity.Text=prefs.Widgets.City;weatherTemp.Text=Number(c.temperature_2m)+"°C";
            string range="";if(data.daily!=null&&data.daily.temperature_2m_min!=null&&data.daily.temperature_2m_min.Length>0&&data.daily.temperature_2m_max!=null&&data.daily.temperature_2m_max.Length>0)range="  "+Number(data.daily.temperature_2m_min[0])+"° / "+Number(data.daily.temperature_2m_max[0])+"°";
            weatherDetail.Text=WeatherLabel(c.weather_code)+range+"\n体感 "+Number(c.apparent_temperature)+"°   湿度 "+Number(c.relative_humidity_2m)+"%\n风速 "+Number(c.wind_speed_10m)+" km/h";
            weatherStatus.Text=status+" · "+(c.time??"").Replace("T"," ");
        }
        void ChooseCity(){
            StackPanel panel;var w=Dialog("选择天气城市",510,out panel);panel.Children.Add(Text("天气城市",24,TextColor));var query=Field(panel,"城市名（可用中文或拼音）",prefs.Widgets.City);
            var resultList=new ListBox{Height=180,Background=CardColor,Foreground=TextColor,BorderBrush=Line,Margin=new Thickness(0,12,0,12)};
            var feedback=Text("选择匹配城市后应用。",12,Muted);bool busy=false;
            var searchButton=Btn("搜索城市",async()=>{if(busy||string.IsNullOrWhiteSpace(query.Text))return;busy=true;feedback.Text="正在搜索…";try{string q=query.Text.Trim();if(q=="广州")q="Guangzhou";var data=ReadJson<CityResults>(await weatherClient.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(q)+"&count=8&language=zh&format=json"));resultList.ItemsSource=data.results;feedback.Text=data.results==null||data.results.Count==0?"未找到城市，可尝试拼音或英文名。":"请选择匹配的城市。";}catch{feedback.Text="搜索失败，请检查网络或稍后重试。";}finally{busy=false;}});
            panel.Children.Add(searchButton);panel.Children.Add(resultList);panel.Children.Add(feedback);var apply=Btn("应用城市",()=>{var city=resultList.SelectedItem as CityResult;if(city==null)return;prefs.Widgets.City=city.name;prefs.Widgets.Latitude=city.latitude;prefs.Widgets.Longitude=city.longitude;weatherGeneration++;lastWeather=null;weatherTemp.Text="—°";weatherDetail.Text="正在获取天气…";weatherCity.Text=city.name;Save();StartWeatherRefresh();w.Close();},true);apply.Margin=new Thickness(0,18,0,0);panel.Children.Add(apply);
            resultList.ItemsSource=new[]{new CityResult{name="广州",admin1="广东",country="中国",latitude=23.1291,longitude=113.2644},new CityResult{name="深圳",admin1="广东",country="中国",latitude=22.5431,longitude=114.0579},new CityResult{name="北京",admin1="北京",country="中国",latitude=39.9042,longitude=116.4074},new CityResult{name="上海",admin1="上海",country="中国",latitude=31.2304,longitude=121.4737}};w.ShowDialog();
        }
        void StopWidgets(){stoppingWidgets=true;if(clockAnimationTimer!=null)clockAnimationTimer.Stop();if(widgetSettingsDialog!=null)widgetSettingsDialog.Close();if(controlPanel!=null)controlPanel.Close();weatherGeneration++;if(widgetTimer!=null)widgetTimer.Stop();if(weatherTimer!=null)weatherTimer.Stop();if(noteSaveTimer!=null)noteSaveTimer.Stop();FlushNote();if(widgetManager!=null)widgetManager.Close();foreach(var w in widgetWindows.Values)w.Close();widgetWindows.Clear();}
    }
}
