// Execute every production x86 Power dispatcher against a synthetic game layout.
// Does not attach to any game. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TouhouSpeedController;

internal static class PowerProfileStubChecks
{
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr n, uint t, uint p);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr a, UIntPtr n, uint t);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Frame();
    private static int total;
    private static void Put(long p,int v) { Marshal.WriteInt32(new IntPtr(p),v); }
    private static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    private static void Bytes(long p,params byte[] b) { Marshal.Copy(b,0,new IntPtr(p),b.Length); }
    private static void Check(bool b,string text)
    { if(!b) throw new Exception(text); total++; Console.WriteLine("PASS "+text); }
    private static int Main()
    {
        if(Environment.Is64BitProcess) throw new Exception("Compile /platform:x86");
        foreach(PowerSyncProfile profile in PowerSyncProfile.All) Run(profile);
        RunTh09();
        Console.WriteLine("PASS total="+total); return 0;
    }
    private static void Run(PowerSyncProfile p)
    {
        IntPtr image=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x200000,0x3000,0x40);
        IntPtr block=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x80000,0x3000,0x40);
        if(image==IntPtr.Zero||block==IntPtr.Zero) throw new Exception("Allocation failed");
        long b=(long)(uint)image.ToInt32(),d=(long)(uint)block.ToInt32(),code=d+4096,player=d+0x10000;
        int divisor=p.ScaledUnits ? 20 : 100;
        int upper=p.Name=="th10" ? 100 : p.ScaledUnits ? 80 : 400;
        int lower=p.ScaledUnits ? 64 : 320, unlocked=p.ScaledUnits ? 50 : 250;
        try
        {
            Bytes(code,PowerSyncSession.BuildCode((uint)code,(uint)d,(uint)b,p));
            // Synthetic rebuild verifies this and the optional native callee-pop argument.
            List<byte> native=new List<byte>();
            native.AddRange(p.CallMode==1 ? new byte[]{0x81,0x7C,0x24,0x04} : p.CallMode==2 ? new byte[]{0x81,0xFF} : p.CallMode==3 ? new byte[]{0x81,0xFB} : new byte[]{0x81,0xF9});
            native.AddRange(BitConverter.GetBytes((uint)(player+p.ThisOffset)));
            native.AddRange(new byte[]{0x74,0x0A,0xC7,0x05}); native.AddRange(BitConverter.GetBytes((uint)(d+56))); native.AddRange(BitConverter.GetBytes(1));
            if(p.Argument.HasValue)
            {
                native.AddRange(new byte[]{0x81,0x7C,0x24,0x04}); native.AddRange(BitConverter.GetBytes(p.Argument.Value));
                native.AddRange(new byte[]{0x74,0x0A,0xC7,0x05}); native.AddRange(BitConverter.GetBytes((uint)(d+56))); native.AddRange(BitConverter.GetBytes(2));
            }
            native.Add(0xA1); native.AddRange(BitConverter.GetBytes((uint)(b+p.PowerRva)));
            native.AddRange(p.ConstantDivisor!=0 ? new byte[]{0xB9} : new byte[]{0x8B,0x0D});
            native.AddRange(BitConverter.GetBytes(p.ConstantDivisor!=0 ? (uint)p.ConstantDivisor : (uint)(b+p.DivisorRva)));
            native.AddRange(new byte[]{0x31,0xD2,0xF7,0xF1});
            foreach(uint cache in p.CacheOffsets)
            { native.Add(0xA3); native.AddRange(BitConverter.GetBytes((uint)(player+cache))); }
            if(p.CallMode==3) native.AddRange(new byte[]{0xBB,0,0,0,0}); // the dispatcher must restore its EBX request sequence
            native.AddRange(p.Argument.HasValue||p.CallMode==1?new byte[]{0xC2,0x04,0}:new byte[]{0xC3});
            Bytes(b+p.RebuildRva,native.ToArray());
            bool cookie=p.Name=="th20";
            long entry=d+0x3000;
            byte[] prologue;
            if(p.Name=="th10") {
                // RET 4 continuation: wrapper supplies the original argument and locals.
                Bytes(b+p.HookRva+(uint)p.Original.Length,0x83,0xC4,8,0xC2,4,0);
                Bytes(entry,0x6A,0,0xE8); Bytes(entry+3,BitConverter.GetBytes(11)); Bytes(entry+7,0xC3);
                entry+=18;
                prologue=new byte[]{0x83,0xEC,8,0xC7,0x04,0x24,123,0,0,0,0x53};
            } else if(p.Name=="th12"||p.Name=="th11") {
                Bytes(b+p.HookRva+(uint)p.Original.Length,0x5F,0x5E,0x5B,0xC3);
                prologue=new byte[]{0x53,0x56,0x57,0x83,0xEC,8,0xC7,0x04,0x24,123,0,0,0};
            } else if(p.Name=="th13") {
                Bytes(b+p.HookRva+(uint)p.Original.Length,0x5D,0x5F,0x5E,0x5B,0xC3);
                prologue=new byte[]{0x53,0x56,0x57,0x55,0x8B,0xEC,0x83,0xEC,4,0xC7,0x45,0xFC,123,0,0,0};
            } else {
                Bytes(b+p.HookRva+(uint)p.Original.Length,cookie?new byte[]{0x5F,0x5E,0x5B,0x8B,0xE5,0x5D,0xC3}:new byte[]{0x8B,0xE5,0x5D,0xC3});
                prologue=new byte[]{0x55,0x8B,0xEC,0x53,0x56,0x57,0xBE,0x7B,0,0,0,0xB8,0x7B,0,0,0};
            }
            Bytes(entry,prologue); Bytes(entry+prologue.Length,0xE9);
            Bytes(entry+prologue.Length+1,BitConverter.GetBytes(unchecked((int)(code-entry-prologue.Length-5))));
            Frame frame=(Frame)Marshal.GetDelegateForFunctionPointer(new IntPtr(p.Name=="th10" ? entry-18 : entry),typeof(Frame));
            Put(d,1); Put(d+20,-1); Put(b+p.PlayerRva,(int)player); Put(player+p.StateOffset,1);
            if(p.ConstantDivisor==0) Put(b+p.DivisorRva,divisor);
            if(p.MaximumRva!=0) Put(b+p.MaximumRva,upper);
            if(p.ScaledUnits) Put(d+52,divisor);
            if(p.OwnerOffset!=0) Put(player+p.OwnerOffset,(int)(b+p.OwnerRva));
            Put(b+p.PowerRva,divisor);
            Check(frame()==123&&Get(player+p.CacheOffsets[0])==1&&Get(d+56)==0,p.Name+" initial/ABI/return");
            Put(d+12,upper); Put(d+40,(int)player); Put(d+4,1); frame();
            Check(Get(b+p.PowerRva)==upper&&Get(player+p.CacheOffsets[0])==upper/divisor&&Get(d+8)==1&&Get(d+56)==0,p.Name+" increase/ABI/acknowledgement");
            Put(d+12,lower); Put(d+4,2); frame();
            Check(Get(player+p.CacheOffsets[0])==3,p.Name+" decrease to 3.20");
            int count=Get(d+28); Put(b+p.PowerRva,lower+1); frame();
            Check(Get(d+28)==count+1&&Get(player+p.CacheOffsets[0])==3,p.Name+" one native unit update");
            Put(d+48,1); Put(b+p.PowerRva,divisor); frame();
            Check(Get(b+p.PowerRva)==lower&&Get(player+p.CacheOffsets[0])==3,p.Name+" lock");
            foreach(uint cache in p.CacheOffsets) Put(player+cache,2);
            frame(); bool repaired=true;
            foreach(uint cache in p.CacheOffsets) repaired &= Get(player+cache)==3;
            Check(repaired,p.Name+" stale cache repair");
            Put(d+48,0); Put(b+p.PowerRva,unlocked); frame();
            Check(Get(b+p.PowerRva)==unlocked&&Get(player+p.CacheOffsets[0])==2,p.Name+" unlock");
            Put(player+p.StateOffset,4); Put(d+12,upper); Put(d+4,3); frame();
            Check(Get(d+8)==2&&Get(b+p.PowerRva)==unlocked,p.Name+" defer during death");
            Put(player+p.StateOffset,1); frame();
            Check(Get(d+8)==3&&Get(b+p.PowerRva)==upper,p.Name+" resume");
            if(p.ConstantDivisor==0) {
                Put(b+p.DivisorRva,1); Put(d+12,divisor); Put(d+4,4); frame();
                Check(Get(d+8)==3&&Get(b+p.PowerRva)==upper,p.Name+" uninitialized divisor guard");
                Put(b+p.DivisorRva,divisor); frame();
            }
            if(p.ScaledUnits) {
                Put(d+48,0); Put(b+p.PowerRva,0); frame();
                Check(Get(player+p.CacheOffsets[0])==0,p.Name+" zero Power rebuild");
            }
            if(p.Name=="th11") {
                Put(d+48,1); Put(b+p.DivisorRva,12); Put(b+p.MaximumRva,96); count=Get(d+28); frame();
                Check(Get(d+48)==0&&Get(d+16)==4&&Get(d+28)==count,"th11 cancel old lock after shot scale change");
                Put(d+52,12); Put(d+12,96); Put(d+4,5); frame();
                Check(Get(b+p.PowerRva)==96&&Get(player+p.CacheOffsets[0])==8&&Get(d+8)==5,"th11 Marisa A eight-tier rebuild");
                Put(d+12,12); Put(d+4,6); Put(b+p.DivisorRva,13); frame();
                Check(Get(d+8)==5,"th11 reject invalid divisor/max pair");
                Put(b+p.DivisorRva,12); frame();
                Check(Get(d+8)==6&&Get(player+p.CacheOffsets[0])==1,"th11 resume valid shot scale");
            }
            if(p.OwnerOffset!=0)
            {
                Put(player+p.OwnerOffset,0); Put(d+4,5); frame();
                Check(Get(d+8)==4,p.Name+" game-side owner guard");
                Put(player+p.OwnerOffset,(int)(b+p.OwnerRva)); frame();
            }
            Put(b+p.PlayerRva,0); Put(d+4,7); frame();
            Check(Get(d+8)==7&&Get(d+16)==3,p.Name+" teardown cancellation");
            count=Get(d+28); Put(d,0); Put(b+p.PlayerRva,(int)player); Put(b+p.PowerRva,100);
            Check(frame()==123&&Get(d+28)==count&&Get(b+p.PowerRva)==100,p.Name+" disabled");
        }
        finally { VirtualFree(image,UIntPtr.Zero,0x8000); VirtualFree(block,UIntPtr.Zero,0x8000); }
    }

    private static void RunTh09()
    {
        IntPtr image=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x200000,0x3000,0x40);
        IntPtr block=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x10000,0x3000,0x40);
        long b=(uint)image.ToInt32(),d=(uint)block.ToInt32(),player=d+0x5000,code=d+4096,entry=d+0x3000;
        if(image==IntPtr.Zero||block==IntPtr.Zero) throw new Exception("Allocation failed");
        try {
            PowerSyncProfile p=PowerSyncProfile.Th09Hit;
            Bytes(code,PowerSyncSession.BuildCode((uint)code,(uint)d,(uint)b,p));
            Bytes(b+p.RebuildRva,0xFF,0x05); Bytes(b+p.RebuildRva+2,BitConverter.GetBytes((uint)(d+56))); Bytes(b+p.RebuildRva+6,0xC3);
            Bytes(b+p.HookRva+5,0xB8,1,0,0,0,0xC3);
            List<byte> start=new List<byte>(new byte[]{0x56,0xBE}); start.AddRange(BitConverter.GetBytes((uint)player));
            start.AddRange(new byte[]{0x8B,0xCE,0xB8}); start.AddRange(BitConverter.GetBytes((uint)code)); start.AddRange(new byte[]{0xFF,0xD0,0x5E,0xC3});
            Bytes(entry,start.ToArray()); Frame frame=(Frame)Marshal.GetDelegateForFunctionPointer(new IntPtr(entry),typeof(Frame));
            Put(d,1); Put(player+8,0);
            Check(frame()==1&&Get(d+56)==0,"th09 P1 hit suppressed");
            Put(player+8,1); Check(frame()==1&&Get(d+56)==1,"th09 P2 hit remains native");
            Put(d,0); Put(player+8,0); Check(frame()==1&&Get(d+56)==2,"th09 disabled P1 hit remains native");
        } finally { VirtualFree(image,UIntPtr.Zero,0x8000); VirtualFree(block,UIntPtr.Zero,0x8000); }
    }
}
