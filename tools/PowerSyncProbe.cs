// Same production synchronizer, CLI diagnostics and bounded live exercise.
// No input automation. Does not launch games. GPL-3.0-or-later.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using TouhouSpeedController;

internal static class PowerSyncProbe
{
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int a,bool inherit,int pid);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int n,out IntPtr read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    private sealed class Reader : IDisposable
    {
        private readonly IntPtr handle;
        public readonly long Base;
        public readonly PowerSyncProfile Profile;
        public Reader(Process p)
        {
            Profile=PowerSyncProfile.Get(p.ProcessName); Profile.Verify(p);
            Base=p.MainModule.BaseAddress.ToInt64(); handle=OpenProcess(0x410,false,p.Id);
            if(handle==IntPtr.Zero) throw new IOException("Read handle unavailable");
        }
        private byte[] Bytes(long a,int n)
        {
            byte[] b=new byte[n]; IntPtr count;
            if(!ReadProcessMemory(handle,new IntPtr(a),b,n,out count)||count.ToInt32()!=n) throw new IOException("Read failed");
            return b;
        }
        private int Int(long a) { return BitConverter.ToInt32(Bytes(a,4),0); }
        public Row Read()
        {
            PowerSyncProfile p=Profile; Row r=new Row();
            r.Player=(uint)Int(Base+p.PlayerRva); r.Power=Int(Base+p.PowerRva); r.Divisor=Int(Base+p.DivisorRva);
            r.State=r.Player==0?-1:Int(r.Player+p.StateOffset); r.Caches=new int[p.CacheOffsets.Length];
            for(int i=0;i<r.Caches.Length;i++) r.Caches[i]=r.Player==0?-1:Int(r.Player+p.CacheOffsets[i]);
            return r;
        }
        public string Hook() { return BitConverter.ToString(Bytes(Base+Profile.HookRva,Profile.Original.Length)); }
        public void Dispose() { CloseHandle(handle); }
    }
    private sealed class Row
    {
        public long Player;
        public int Power,Divisor,State;
        public int[] Caches;
        public bool Active { get { return Player!=0&&State==1&&Divisor>=100&&Divisor<=400; } }
        public bool Matches
        {
            get { if(!Active) return false; foreach(int c in Caches) if(c!=Power/Divisor) return false; return true; }
        }
        public override string ToString()
        { return String.Format("power={0} divisor={1} player={2:X} state={3} caches={4}",Power,Divisor,Player,State,String.Join(",",Array.ConvertAll(Caches,x=>x.ToString()))); }
    }
    private static bool Wait(Func<bool> predicate,int milliseconds)
    {
        Stopwatch w=Stopwatch.StartNew();
        while(w.ElapsedMilliseconds<milliseconds) { try { if(predicate()) return true; } catch(IOException) { } Thread.Sleep(20); }
        return false;
    }
    private static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--emit")
        {
            Directory.CreateDirectory(args[1]);
            foreach(PowerSyncProfile p in PowerSyncProfile.All)
                File.WriteAllBytes(Path.Combine(args[1],p.Name+".bin"),PowerSyncSession.BuildCode(0x10001000,0x10000000,0x400000,p));
            File.WriteAllBytes(Path.Combine(args[1],"th09-hit.bin"),PowerSyncSession.BuildCode(0x10001000,0x10000000,0x400000,PowerSyncProfile.Th09Hit));
            List<string> rows=new List<string>();
            List<PowerSyncProfile> layouts=new List<PowerSyncProfile>(PowerSyncProfile.All); layouts.Add(PowerSyncProfile.Th09Hit);
            foreach(PowerSyncProfile p in layouts)
                rows.Add(p.Name+"\t"+p.HookRva.ToString("X")+"\t"+BitConverter.ToString(p.Original).Replace("-","")+"\t"+p.RebuildRva.ToString("X")+"\t"+BitConverter.ToString(p.RebuildEntry).Replace("-",""));
            File.WriteAllLines(Path.Combine(args[1],"profiles.tsv"),rows.ToArray());
            return 0;
        }
        bool exercise=args.Length>=2&&args[0]=="--exercise";
        int pid=Int32.Parse(args[exercise?1:0]);
        using(Process game=Process.GetProcessById(pid))
        using(Reader reader=new Reader(game))
        {
            PowerSyncSession session=null;
            Action<string> log=message=>Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff")+" "+message);
            try
            {
                log(reader.Profile.Name+" baseline "+reader.Read()+" hook="+reader.Hook());
                if(exercise)
                {
                    if(!Wait(()=>reader.Read().Active,30000)) throw new Exception("No active initialized player within 30 seconds");
                    session=PowerSyncSession.Attach(game);
                    int[] values={100,400,250,251,320};
                    foreach(int value in values)
                    {
                        if(!Wait(()=>reader.Read().Active,10000)) throw new Exception("Player did not become active");
                        session.Request(value,value==320);
                        if(!Wait(()=>!session.Status().Pending,10000)||session.Status().Result==3) throw new Exception("Request not completed or cancelled by scene transition");
                        if(!Wait(()=>{ Row r=reader.Read(); return r.Matches&&(value!=320||r.Power==320); },10000)) throw new Exception("Attack cache did not match Power");
                        PowerSyncStatus s=session.Status();
                        log("PASS request="+value+" "+reader.Read()+" refreshes="+s.Refreshes+" ticks="+s.Ticks+" thread="+s.ThreadId);
                    }
                    Thread.Sleep(1500);
                    if(!Wait(()=>{ Row r=reader.Read(); return r.Matches&&r.Power==320; },10000)) throw new Exception("Lock did not maintain target");
                    log("PASS lock held "+reader.Read());
                    session.Unlock();
                    if(!Wait(()=>reader.Read().Active,10000)) throw new Exception("Player not active for unlock test");
                    session.Request(399,false);
                    if(!Wait(()=>!session.Status().Pending&&reader.Read().Matches,10000)) throw new Exception("Unlock request failed");
                    log("PASS unlocked "+reader.Read());
                    session.Dispose(); session=null;
                    if(reader.Hook()!=BitConverter.ToString(reader.Profile.Original)) throw new Exception("Original hook not restored");
                    log("PASS restored hook="+reader.Hook()); return 0;
                }
                string line;
                while((line=Console.ReadLine())!=null)
                {
                    try
                    {
                        string[] c=line.Trim().Split(' ');
                        if(c[0]=="quit") break;
                        if(c[0]=="restore") { if(session!=null) session.Dispose(); session=null; }
                        else if(c[0]=="unlock") { if(session!=null) session.Unlock(); }
                        else if(c[0]=="set"||c[0]=="lock")
                        { if(session==null) session=PowerSyncSession.Attach(game); session.Request(Int32.Parse(c[1]),c[0]=="lock"); }
                        PowerSyncStatus s=session==null?null:session.Status();
                        log(reader.Read()+" hook="+reader.Hook()+(s==null?"":String.Format(" pending={0} result={1} refreshes={2} ticks={3} thread={4}",s.Pending,s.Result,s.Refreshes,s.Ticks,s.ThreadId)));
                    }
                    catch(Exception ex) { log(ex.ToString()); }
                }
                return 0;
            }
            catch(Exception ex) { log("FAIL "+ex); return 1; }
            finally { if(session!=null) session.Dispose(); }
        }
    }
}
