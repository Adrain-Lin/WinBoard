using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
namespace WinBoard {
 // Native screenshot APIs can wait for another application's UI. Keep them and the
 // selection dialog off the WPF/recording-control thread, with one native job at a time.
 public sealed class ScreenshotOperation {
  static readonly object gate=new object();static ScreenshotOperation active;
  readonly CancellationTokenSource cancellation=new CancellationTokenSource();
  readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();
  readonly TaskCompletionSource<BitmapSource> result=new TaskCompletionSource<BitmapSource>();
  readonly CaptureTarget target;readonly bool excludeSelector;
  public Task<BitmapSource> Completion {get{return result.Task;}}
  public Task Ready {get{return ready.Task;}}
  ScreenshotOperation(CaptureTarget source,bool exclude){target=source;excludeSelector=exclude;}
  public static ScreenshotOperation Start(CaptureTarget target,bool excludeSelector){lock(gate){if(active!=null)throw new InvalidOperationException("上次截图仍在结束，请稍后重试。");var job=new ScreenshotOperation(target,excludeSelector);active=job;try{var thread=new Thread(job.Run){IsBackground=true,Name="WinBoard screenshot"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return job;}catch{active=null;throw;}}}
  void Run(){BitmapSource captured=null;Exception failure=null;bool canceled=false;try{cancellation.Token.ThrowIfCancellationRequested();using(new CaptureDesktop.DpiScope())using(var image=target==null?CaptureDesktop.SelectRegion(cancellation.Token,()=>ready.TrySetResult(true),excludeSelector):target.Handle!=IntPtr.Zero?CaptureDesktop.CaptureWindow(target):CaptureDesktop.Capture(target.Bounds)){cancellation.Token.ThrowIfCancellationRequested();if(image!=null)captured=CaptureDesktop.ToSource(image);cancellation.Token.ThrowIfCancellationRequested();}}catch(OperationCanceledException){canceled=true;}catch(Exception ex){failure=ex;}finally{cancellation.Dispose();lock(gate){if(active==this)active=null;}}if(canceled)result.TrySetCanceled();else if(failure!=null)result.TrySetException(failure);else result.TrySetResult(captured);}

  public void Cancel(){if(result.Task.IsCompleted)return;try{cancellation.Cancel();}catch(ObjectDisposedException){}CaptureDesktop.CancelSelection();}
  public async Task<BitmapSource> ImageAsync(int timeoutMilliseconds){Task timeout=Task.Delay(timeoutMilliseconds);if(target==null){Task first=await Task.WhenAny(Ready,Completion,timeout);if(first==timeout){Cancel();throw new TimeoutException("截图准备超时，请检查目标窗口后重试。控制中心仍可使用。");}return await Completion;}if(await Task.WhenAny(Completion,timeout)!=Completion){Cancel();throw new TimeoutException("目标窗口未及时响应截图，请使用框选截图或待窗口恢复后重试。");}return await Completion;}
  public static Task CopyAsync(BitmapSource image){var result=new TaskCompletionSource<bool>();var thread=new Thread(()=>{try{Clipboard.SetImage(image);result.TrySetResult(true);}catch(Exception ex){result.TrySetException(ex);}}){IsBackground=true,Name="WinBoard screenshot clipboard"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;}
 }
}
