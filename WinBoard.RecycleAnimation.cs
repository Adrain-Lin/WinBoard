using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace WinBoard {
 public partial class Board {
  Button recycleHover;
  void SetRecycleHover(DockEntry entry){var button=entry==null?null:entry.Button;if(button==recycleHover)return;if(recycleHover!=null){recycleHover.Background=Brushes.Transparent;recycleHover.BeginAnimation(UIElement.OpacityProperty,null);recycleHover.RenderTransform=Transform.Identity;}recycleHover=button;if(button==null)return;button.Background=Hex("#4082E4C4");button.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1,1);button.RenderTransform=scale;if(SystemParameters.ClientAreaAnimation){scale.BeginAnimation(ScaleTransform.ScaleXProperty,DockAnimation(1.12,160));scale.BeginAnimation(ScaleTransform.ScaleYProperty,DockAnimation(1.12,160));}}
  void AnimateRecycling(DockEntry entry,BitmapSource fileIcon){
   SetRecycleHover(null);dockKeepUntil=DateTime.UtcNow.AddMilliseconds(1200);icons.Remove(ShellEntries.RecycleBin);
   if(entry==null||entry.Button==null||!SystemParameters.ClientAreaAnimation){Build();return;}
   var button=entry.Button;var anchor=button.TranslatePoint(new Point(button.ActualWidth/2,prefs.DockStyle==2?20:34),this);
   var canvas=new Canvas{Width=200,Height=180,IsHitTestVisible=false};
   var overlay=new Window{Title="WinBoard · 回收动画",Width=200,Height=180,Left=Left+anchor.X-100,Top=Top+anchor.Y-100,Owner=this,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Focusable=false,IsHitTestVisible=false,Topmost=Topmost,Content=canvas};
   var trash=new Image{Source=GetSystemIcon(ShellEntries.RecycleBin),Width=40,Height=40,RenderTransformOrigin=new Point(.5,1)};Canvas.SetLeft(trash,80);Canvas.SetTop(trash,80);canvas.Children.Add(trash);
   var squash=new ScaleTransform(1,1);trash.RenderTransform=squash;
   var sx=new DoubleAnimationUsingKeyFrames();sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.2,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));sx.KeyFrames.Add(new EasingDoubleKeyFrame(.88,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));sx.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)),new BackEase{Amplitude=.4,EasingMode=EasingMode.EaseOut}));
   var sy=new DoubleAnimationUsingKeyFrames();sy.KeyFrames.Add(new EasingDoubleKeyFrame(.8,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.15,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));sy.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
   var document=new Border{Width=34,Height=34,RenderTransformOrigin=new Point(.5,.5)};document.Child=fileIcon==null?(UIElement)new TextBlock{Text="▤",FontSize=30,Foreground=Brushes.White}:new Image{Source=fileIcon};Canvas.SetLeft(document,67);Canvas.SetTop(document,22);canvas.Children.Add(document);
   var shrink=new ScaleTransform(1,1);var turn=new RotateTransform(-15);var transforms=new TransformGroup();transforms.Children.Add(shrink);transforms.Children.Add(turn);document.RenderTransform=transforms;
   overlay.Show();button.Opacity=0;
   var ease=new CubicEase{EasingMode=EasingMode.EaseIn};
   document.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(67,84,TimeSpan.FromMilliseconds(360)){EasingFunction=ease});document.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(22,86,TimeSpan.FromMilliseconds(360)){EasingFunction=ease});
   shrink.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,.05,TimeSpan.FromMilliseconds(380)){EasingFunction=ease});shrink.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.05,TimeSpan.FromMilliseconds(380)){EasingFunction=ease});turn.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(-15,20,TimeSpan.FromMilliseconds(380)));document.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(380)));
   squash.BeginAnimation(ScaleTransform.ScaleXProperty,sx);squash.BeginAnimation(ScaleTransform.ScaleYProperty,sy);
   for(int i=0;i<7;i++){double angle=(i+1)*Math.PI/8;var puff=new Ellipse{Width=8,Height=8,Fill=Hex("#B0FFFFFF"),Opacity=0};Canvas.SetLeft(puff,96);Canvas.SetTop(puff,86);canvas.Children.Add(puff);var duration=TimeSpan.FromMilliseconds(350);puff.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(96,96+Math.Cos(angle)*32,duration){BeginTime=TimeSpan.FromMilliseconds(320)});puff.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(86,86-Math.Sin(angle)*23,duration){BeginTime=TimeSpan.FromMilliseconds(320)});var fade=new DoubleAnimationUsingKeyFrames();fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(.8,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));fade.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680))));puff.BeginAnimation(UIElement.OpacityProperty,fade);}
   var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(780)};timer.Tick+=(s,e)=>{timer.Stop();button.Opacity=1;overlay.Close();if(!quitting)Build();};overlay.Closed+=(s,e)=>{timer.Stop();button.Opacity=1;};timer.Start();
  }
 }
}
