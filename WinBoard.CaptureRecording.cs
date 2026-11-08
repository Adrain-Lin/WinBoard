using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ScreenRecorderLib;
namespace WinBoard {
 public sealed class CaptureRecording:IDisposable {
  Recorder recorder;string finalPath,partialPath;readonly ManualResetEvent done=new ManualResetEvent(false);bool disposed;
  public bool Paused {get;private set;}public bool Finishing {get;private set;}
  public event Action<string,string> Completed;
  public void Start(CaptureTarget target,CapturePreferences prefs,string file){finalPath=file;partialPath=Path.ChangeExtension(file,"recording.mp4");var audio=new List<AudioSourceBase>();if(prefs.SystemAudio)audio.Add(LoopbackAudioSource.Default);if(prefs.Microphone)audio.Add(CaptureAudioSource.Default);RecordingSourceBase source=target.Handle==IntPtr.Zero?(RecordingSourceBase)new DisplayRecordingSource(target.Device){IsCursorCaptureEnabled=prefs.Cursor}:new WindowRecordingSource(target.Handle){IsCursorCaptureEnabled=prefs.Cursor};var options=new RecorderOptions{SourceOptions=new SourceOptions{RecordingSources=new List<RecordingSourceBase>{source}},AudioOptions=new AudioOptions{IsAudioEnabled=audio.Count>0,AudioSources=audio},VideoEncoderOptions=new VideoEncoderOptions{Framerate=prefs.Fps,Bitrate=prefs.Bitrate>0?prefs.Bitrate:8000000,IsHardwareEncodingEnabled=true,IsFixedFramerate=true},OutputOptions=new OutputOptions{RecorderMode=RecorderMode.Video}};recorder=Recorder.CreateRecorder(options);recorder.OnRecordingComplete+=(s,e)=>{string error=null;try{File.Move(partialPath,finalPath);}catch(Exception ex){error="录屏保存失败："+ex.Message+"\n临时文件："+partialPath;}done.Set();if(Completed!=null)Completed(error==null?finalPath:null,error);};recorder.OnRecordingFailed+=(s,e)=>{done.Set();if(Completed!=null)Completed(null,e.Error+"\n未完成的录制（若存在）："+partialPath);};recorder.Record(partialPath);}
  public void Pause(){if(recorder==null||Finishing)return;recorder.Pause();Paused=true;}
  public void Resume(){if(recorder==null||Finishing)return;recorder.Resume();Paused=false;}
  public void Stop(){if(recorder==null||Finishing)return;Finishing=true;recorder.Stop();}
  public bool Wait(int timeout){return done.WaitOne(timeout);}
  public void Dispose(){if(disposed)return;disposed=true;if(recorder!=null){recorder.Dispose();recorder=null;}done.Dispose();}
 }
}
