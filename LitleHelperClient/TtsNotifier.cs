using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
namespace PixelHelper;
internal static class TtsNotifier
{
    private static readonly object sync=new();
    private static int emergencyGeneration;
    internal static bool SuppressedForTests;
    internal static Action<string,string>? EmergencyPlaybackForTests;
    public static void CancelEmergency()=>Interlocked.Increment(ref emergencyGeneration);
    private static void Queue(Action action) {
        var thread=new Thread(()=> {lock(sync) {try {action();}catch(Exception ex){Settings.Log(ex);}}}) {IsBackground=true,Name="Helper speech"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    private static object? CreateVoice() {
        var type=Type.GetTypeFromProgID("SAPI.SpVoice");
        return type==null?null:Activator.CreateInstance(type);
    }
    private static void Release(object? value) {if(value!=null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);}
    private static object? Invoke(object voice,string method,params object[] args)=>
        voice.GetType().InvokeMember(method,BindingFlags.InvokeMethod,null,voice,args);
    private static void Speak(object? voice,string text,Func<bool> canceled) {
        if(voice==null || canceled())return;
        Invoke(voice,"Speak",text,1); // asynchronous SAPI, polled on its owning STA
        while(!(bool)Invoke(voice,"WaitUntilDone",50)!) {
            if(canceled()) {Invoke(voice,"Speak","",3);return;} // purge current speech and queued buffers
        }
    }
    public static void SpeakAsync(string text,Settings? settings=null) {
        if(string.IsNullOrWhiteSpace(text) || SuppressedForTests || settings?.EnableTtsEmergencyAlerts==false || settings?.MedicalTtsMuted==true)return;
        int generation=Volatile.Read(ref emergencyGeneration);
        Queue(()=> {
            var voice=CreateVoice();
            try {Speak(voice,text,()=>generation!=Volatile.Read(ref emergencyGeneration)||settings?.MedicalTtsMuted==true);}
            finally {Release(voice);}
        });
    }
    private static void PlayWave(string path,Func<bool> canceled) {
        if(canceled())return;
        // Duration comes from RIFF chunks, rather than a blocking PlaySync.
        double seconds=0;
        using(var reader=new BinaryReader(File.OpenRead(path))) {
            if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException("Invalid WAV");
            reader.ReadUInt32();if(new string(reader.ReadChars(4))!="WAVE")throw new InvalidDataException("Invalid WAV");
            uint bytesPerSecond=0,dataBytes=0;
            while(reader.BaseStream.Position+8<=reader.BaseStream.Length) {
                string tag=new(reader.ReadChars(4));uint length=reader.ReadUInt32();long end=reader.BaseStream.Position+length;
                if(end>reader.BaseStream.Length)throw new InvalidDataException("Truncated WAV");
                if(tag=="fmt " && length>=16) {reader.ReadUInt16();reader.ReadUInt16();reader.ReadUInt32();bytesPerSecond=reader.ReadUInt32();}
                if(tag=="data")dataBytes=length;
                reader.BaseStream.Position=end+(length%2);
            }
            if(bytesPerSecond==0)throw new InvalidDataException("Invalid WAV format");
            seconds=(double)dataBytes/bytesPerSecond;
        }
        using var player=new System.Media.SoundPlayer(path);
        player.Load();if(canceled())return;player.Play();
        try {var end=DateTime.UtcNow.AddSeconds(Math.Min(seconds,120));while(DateTime.UtcNow<end && !canceled())Thread.Sleep(50);}
        finally {player.Stop();}
    }
    public static void SpeakEmergencyAsync(EmergencyAlertNotice notice,Settings settings) {
        if((SuppressedForTests && EmergencyPlaybackForTests==null)||!settings.EnableTtsEmergencyAlerts||settings.MedicalTtsMuted)return;
        int generation=Interlocked.Increment(ref emergencyGeneration);
        Queue(()=> {
            bool Canceled()=>generation!=Volatile.Read(ref emergencyGeneration)||settings.MedicalTtsMuted;
            if(Canceled())return;
            object? voice=EmergencyPlaybackForTests==null?CreateVoice():null;
            try {
                foreach(string language in EmergencyPhrases.Languages(notice)) {
                    if(Canceled())break;
                    if(EmergencyPlaybackForTests!=null){EmergencyPlaybackForTests(language,EmergencyPhrases.Text(notice,language));continue;}
                    object? voices=voice==null?null:Invoke(voice,"GetVoices"),selected=null;
                    try {
                        int count=voices==null?0:(int)voices.GetType().InvokeMember("Count",BindingFlags.GetProperty,null,voices,null)!;
                        string lcid=language switch {"kk"=>"43f","en"=>"409","zh"=>"804",_=>"419"};
                        for(int i=0;i<count;i++) {
                            var candidate=Invoke(voices!,"Item",i)!;
                            string languages=(string)Invoke(candidate,"GetAttribute","Language")!;
                            if(languages.Split(';').Any(l=>l.TrimStart('0').Equals(lcid,StringComparison.OrdinalIgnoreCase))) {selected=candidate;break;}
                            Release(candidate);
                        }
                        if(selected!=null && voice!=null) {
                            voice.GetType().InvokeMember("Voice",BindingFlags.SetProperty,null,voice,[selected]);
                            Speak(voice,EmergencyPhrases.Text(notice,language),Canceled);continue;
                        }
                        string folder=Path.Combine(AppContext.BaseDirectory,"Assets","Sounds","Emergency");
                        string prefix=Path.Combine(folder,notice.Code.ToUpperInvariant()+"-"+language+".wav");
                        if(File.Exists(prefix)) {
                            PlayWave(prefix,Canceled);string room=notice.Cabinet??"";
                            if(room.All(c=>char.IsAsciiDigit(c)||char.IsWhiteSpace(c)))
                                foreach(char digit in room.Where(char.IsAsciiDigit)) {
                                    if(Canceled())break;string path=Path.Combine(folder,"digit-"+digit+"-"+language+".wav");
                                    if(File.Exists(path))PlayWave(path,Canceled);
                                }
                            else Speak(voice,room,Canceled);
                        } else Speak(voice,EmergencyPhrases.Text(notice,language),Canceled);
                    }finally {Release(selected);Release(voices);}
                }
            }finally {Release(voice);}
        });
    }
}
