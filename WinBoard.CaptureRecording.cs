using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ScreenRecorderLib;
namespace WinBoard {
 public sealed class CaptureRecording:IDisposable {
  readonly object gate=new object();readonly Queue<Action> commands=new Queue<Action>();
  readonly TaskCompletionSource<bool> done=new TaskCompletionSource<bool>();
  Recorder recorder;Dispatcher worker;bool scheduled,ended,started;int completionQueued;
  string finalPath,partialPath;volatile bool paused,finishing;
  public bool Paused {get{return paused;}}public bool Finishing {get{return finishing;}}
  public event Action<string,string> Completed;
  public void Start(CaptureTarget target,CapturePreferences prefs,string file){lock(gate){if(started||ended)throw new InvalidOperationException("录制会话不能重复使用。");started=true;finalPath=file;partialPath=Path.ChangeExtension(file,"recording.mp4");
   // Snapshot options so later edits do not race with native recorder initialization.
   var options=new CapturePreferences{SystemAudio=prefs.SystemAudio,Microphone=prefs.Microphone,Cursor=prefs.Cursor,Fps=prefs.Fps,Bitrate=prefs.Bitrate};
   commands.Enqueue(()=>StartNative(target,options));var thread=new Thread(()=>{lock(gate){worker=Dispatcher.CurrentDispatcher;Schedule();}Dispatcher.Run();}){IsBackground=true,Name="WinBoard recording session"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}}
  void Schedule(){if(worker==null||scheduled||ended)return;scheduled=true;worker.BeginInvoke(new Action(Drain));}
  void Post(Action command){lock(gate){if(ended)return;commands.Enqueue(command);Schedule();}}
  void Drain(){while(true){Action command;lock(gate){if(commands.Count==0||ended){scheduled=false;return;}command=commands.Dequeue();}try{command();}catch(Exception ex){Finish("录屏组件错误："+ex.Message);}}}
  void StartNative(CaptureTarget target,CapturePreferences prefs){var audio=new List<AudioSourceBase>();if(prefs.SystemAudio)audio.Add(LoopbackAudioSource.Default);if(prefs.Microphone)audio.Add(CaptureAudioSource.Default);RecordingSourceBase source=target.Handle==IntPtr.Zero?(RecordingSourceBase)new DisplayRecordingSource(target.Device){IsCursorCaptureEnabled=prefs.Cursor}:new WindowRecordingSource(target.Handle){IsCursorCaptureEnabled=prefs.Cursor};var options=new RecorderOptions{SourceOptions=new SourceOptions{RecordingSources=new List<RecordingSourceBase>{source}},AudioOptions=new AudioOptions{IsAudioEnabled=audio.Count>0,AudioSources=audio},VideoEncoderOptions=new VideoEncoderOptions{Framerate=prefs.Fps,Bitrate=prefs.Bitrate>0?prefs.Bitrate:8000000,IsHardwareEncodingEnabled=true,IsFixedFramerate=true},OutputOptions=new OutputOptions{RecorderMode=RecorderMode.Video}};recorder=Recorder.CreateRecorder(options);recorder.OnRecordingComplete+=(s,e)=>Finish(null);recorder.OnRecordingFailed+=(s,e)=>Finish(e.Error);recorder.Record(partialPath);}
  void Finish(string error){if(Interlocked.Exchange(ref completionQueued,1)!=0)return;
   // Unwind the native callback before disposal. Never join native capture/audio threads
   // on the WPF UI thread or inside the completion callback that they are executing.
   Post(()=>{string failure=error;try{if(recorder!=null){recorder.Dispose();recorder=null;}if(failure==null)File.Move(partialPath,finalPath);}catch(Exception ex){failure=(failure==null?"":failure+"\n")+"结束录制失败："+ex.Message;}if(failure!=null)failure+="\n未完成文件（若存在）："+partialPath;lock(gate){ended=true;commands.Clear();}done.TrySetResult(true);try{if(Completed!=null)Completed(failure==null?finalPath:null,failure);}finally{worker.BeginInvokeShutdown(DispatcherPriority.Background);}});
  }
  public void Pause(){if(finishing)return;paused=true;Post(()=>{if(recorder!=null)recorder.Pause();});}
  public void Resume(){if(finishing)return;paused=false;Post(()=>{if(recorder!=null)recorder.Resume();});}
  public void Stop(){lock(gate){if(ended||finishing)return;finishing=true;if(!started){ended=true;done.TrySetResult(true);return;}Post(()=>{if(recorder!=null)recorder.Stop();});}}
  public bool Wait(int timeout){return done.Task.Wait(timeout);}
  public void Dispose(){Stop();}
 }
}
