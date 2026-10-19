// Native float/indirect memory edge checks without launching or attaching to games.
// Build /platform:x86 with the production PracticeMemoryAccess.cs. GPL-3.0-or-later.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using TouhouSpeedController;

internal static class NewGamesResourceChecks
{
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr a,UIntPtr n,uint t,uint p);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr a,UIntPtr n,uint t);
    private static int count;
    private static void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; Console.WriteLine("PASS "+name); }
    private static long Field(object p,string n) { return Convert.ToInt64(p.GetType().GetField(n).GetValue(p)); }
    private static void Put(long a,int v) { Marshal.WriteInt32(new IntPtr(a),v); }
    private static int Bits(float v) { return BitConverter.ToInt32(BitConverter.GetBytes(v),0); }
    private static int Main()
    {
        if(Environment.Is64BitProcess) throw new Exception("Compile /platform:x86");
        Type t=typeof(PracticeMemoryAccess);
        MethodInfo read=t.GetMethod("ReadDirectSnapshot",BindingFlags.NonPublic|BindingFlags.Static);
        MethodInfo write=t.GetMethod("WriteResource",BindingFlags.NonPublic|BindingFlags.Static);
        Array profiles=(Array)t.GetField("DirectProfiles",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        foreach(object profile in profiles)
        {
            string code=(string)profile.GetType().GetField("GameCode").GetValue(profile);
            if(code!="TH08"&&code!="TH09"&&code!="TH12"&&code!="TH13") continue;
            string name=(string)profile.GetType().GetField("ProcessName").GetValue(profile) ?? code.ToLowerInvariant();
            IntPtr memory=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x1500000,0x3000,4);
            if(memory==IntPtr.Zero) throw new Exception("Allocation failed");
            long b=(uint)memory.ToInt32(), data=b;
            try {
                if(Field(profile,"ResourcePointerRva")!=0) { data=b+0x100000; Put(b+Field(profile,"ResourcePointerRva"),(int)data); }
                if(Field(profile,"PlayerPointerRva")!=0) Put(b+Field(profile,"PlayerPointerRva"),(int)(b+0x100000));
                PracticeOptions options=PracticeMemoryAccess.Options(name);
                bool floats=(bool)profile.GetType().GetField("FloatResources").GetValue(profile);
                Put(data+Field(profile,"LivesRva"),floats ? Bits(3f) : 3);
                if(options.HasBombs) Put(data+Field(profile,"BombsRva"),floats ? Bits(5f) : 5);
                if(options.HasFragments) Put(data+Field(profile,"BombFragmentsRva"),code=="TH12" ? 3 : 6);
                Put(data+Field(profile,"PowerRva"),code=="TH08" ? Bits(64f) : code=="TH09" ? Bits(123.45f) : 250);
                if(code=="TH08") Put(b+0x13CE8B0,2);
                PracticeSnapshot s=(PracticeSnapshot)read.Invoke(null,new object[]{new IntPtr(-1),b,profile,"synthetic"});
                Check(s.InGame&&s.Lives==3&&s.Bombs==(options.HasBombs ? 5 : 0),name+" active typed resources");
                Check(s.Power==(code=="TH08" ? 6400 : code=="TH09" ? 123 : 250),name+" Power scale");
                Check(s.BombFragments==(code=="TH12" ? 1 : code=="TH13" ? 6 : 0),name+" fragments/unsupported fields");
                if(code=="TH08"||code=="TH09") {
                    write.Invoke(null,new object[]{new IntPtr(-1),data+Field(profile,"PowerRva"),code=="TH08" ? 12800 : 321,true,code=="TH08" ? 100 : 1});
                    Check(Marshal.ReadInt32(new IntPtr(data+Field(profile,"PowerRva")))==Bits(code=="TH08" ? 128f : 321f),name+" native float write");
                    Put(data+Field(profile,"PowerRva"),Bits(Single.NaN));
                    bool rejected=false; try { read.Invoke(null,new object[]{new IntPtr(-1),b,profile,"synthetic"}); }
                    catch(TargetInvocationException e) { rejected=e.InnerException is System.IO.InvalidDataException; }
                    Check(rejected,name+" reject NaN");
                    Put(b+Field(profile,"ResourcePointerRva"),0);
                    rejected=false; try { read.Invoke(null,new object[]{new IntPtr(-1),b,profile,"synthetic"}); }
                    catch(TargetInvocationException e) { rejected=e.InnerException is InvalidOperationException; }
                    Check(rejected,name+" reject missing player");
                }
            } finally { VirtualFree(memory,UIntPtr.Zero,0x8000); }
        }
        Check(PracticeMemoryAccess.Options("th08").PowerMax==12800&&PracticeMemoryAccess.Options("th08").IntegerPower,"TH08 UI range");
        Check(!PracticeMemoryAccess.Options("th09").HasBombs&&!PracticeMemoryAccess.Options("th09").HasFragments,"TH09 absent resources disabled");
        Check(PracticeMemoryAccess.Options("th12c").FragmentsMax==2&&PracticeMemoryAccess.Options("th13c").FragmentsMax==7,"Chinese aliases and fragment bounds");
        Console.WriteLine("PASS total="+count); return 0;
    }
}
