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
        Console.WriteLine("PASS total="+total); return 0;
    }
    private static void Run(PowerSyncProfile p)
    {
        IntPtr image=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x200000,0x3000,0x40);
        IntPtr block=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x80000,0x3000,0x40);
        if(image==IntPtr.Zero||block==IntPtr.Zero) throw new Exception("Allocation failed");
        long b=(long)(uint)image.ToInt32(),d=(long)(uint)block.ToInt32(),code=d+4096,player=d+0x10000;
        try
        {
            Bytes(code,PowerSyncSession.BuildCode((uint)code,(uint)d,(uint)b,p));
            // Synthetic rebuild verifies this and the optional native callee-pop argument.
            List<byte> native=new List<byte>();
            native.AddRange(new byte[]{0x81,0xF9}); native.AddRange(BitConverter.GetBytes((uint)(player+p.ThisOffset)));
            native.AddRange(new byte[]{0x74,0x0A,0xC7,0x05}); native.AddRange(BitConverter.GetBytes((uint)(d+56))); native.AddRange(BitConverter.GetBytes(1));
            if(p.Argument.HasValue)
            {
                native.AddRange(new byte[]{0x81,0x7C,0x24,0x04}); native.AddRange(BitConverter.GetBytes(p.Argument.Value));
                native.AddRange(new byte[]{0x74,0x0A,0xC7,0x05}); native.AddRange(BitConverter.GetBytes((uint)(d+56))); native.AddRange(BitConverter.GetBytes(2));
            }
            native.Add(0xA1); native.AddRange(BitConverter.GetBytes((uint)(b+p.PowerRva)));
            native.AddRange(new byte[]{0x6A,0x64,0x31,0xD2,0xF7,0x34,0x24,0x83,0xC4,0x04});
            foreach(uint cache in p.CacheOffsets)
            { native.AddRange(new byte[]{0x89,0x81}); native.AddRange(BitConverter.GetBytes(unchecked(cache-p.ThisOffset))); }
            native.AddRange(p.Argument.HasValue?new byte[]{0xC2,0x04,0}:new byte[]{0xC3});
            Bytes(b+p.RebuildRva,native.ToArray());
            bool cookie=p.Name=="th20";
            Bytes(b+p.HookRva+5,cookie?new byte[]{0x5F,0x5E,0x5B,0x8B,0xE5,0x5D,0xC3}:new byte[]{0x8B,0xE5,0x5D,0xC3});
            long entry=d+0x3000;
            Bytes(entry,0x55,0x8B,0xEC,0x53,0x56,0x57,0xBE,0x7B,0,0,0,0xB8,0x7B,0,0,0,0xE9);
            Bytes(entry+17,BitConverter.GetBytes(unchecked((int)(code-entry-21))));
            Frame frame=(Frame)Marshal.GetDelegateForFunctionPointer(new IntPtr(entry),typeof(Frame));
            Put(d,1); Put(d+20,-1); Put(b+p.PlayerRva,(int)player); Put(player+p.StateOffset,1); Put(b+p.DivisorRva,100);
            if(p.OwnerOffset!=0) Put(player+p.OwnerOffset,(int)(b+p.OwnerRva));
            Put(b+p.PowerRva,100);
            Check(frame()==123&&Get(player+p.CacheOffsets[0])==1&&Get(d+56)==0,p.Name+" initial/ABI/return");
            Put(d+12,400); Put(d+40,(int)player); Put(d+4,1); frame();
            Check(Get(b+p.PowerRva)==400&&Get(player+p.CacheOffsets[0])==4&&Get(d+8)==1&&Get(d+56)==0,p.Name+" increase to 4.00");
            Put(d+12,320); Put(d+4,2); frame();
            Check(Get(player+p.CacheOffsets[0])==3,p.Name+" decrease to 3.20");
            int count=Get(d+28); Put(b+p.PowerRva,321); frame();
            Check(Get(d+28)==count+1&&Get(player+p.CacheOffsets[0])==3,p.Name+" 0.01 update");
            Put(d+48,1); Put(b+p.PowerRva,100); frame();
            Check(Get(b+p.PowerRva)==320&&Get(player+p.CacheOffsets[0])==3,p.Name+" lock");
            foreach(uint cache in p.CacheOffsets) Put(player+cache,2);
            frame(); bool repaired=true;
            foreach(uint cache in p.CacheOffsets) repaired &= Get(player+cache)==3;
            Check(repaired,p.Name+" stale cache repair");
            Put(d+48,0); Put(b+p.PowerRva,250); frame();
            Check(Get(b+p.PowerRva)==250&&Get(player+p.CacheOffsets[0])==2,p.Name+" unlock");
            Put(player+p.StateOffset,4); Put(d+12,400); Put(d+4,3); frame();
            Check(Get(d+8)==2&&Get(b+p.PowerRva)==250,p.Name+" defer during death");
            Put(player+p.StateOffset,1); frame();
            Check(Get(d+8)==3&&Get(b+p.PowerRva)==400,p.Name+" resume");
            Put(b+p.DivisorRva,1); Put(d+12,100); Put(d+4,4); frame();
            Check(Get(d+8)==3&&Get(b+p.PowerRva)==400,p.Name+" uninitialized divisor guard");
            Put(b+p.DivisorRva,100); frame();
            if(p.OwnerOffset!=0)
            {
                Put(player+p.OwnerOffset,0); Put(d+4,5); frame();
                Check(Get(d+8)==4,p.Name+" game-side owner guard");
                Put(player+p.OwnerOffset,(int)(b+p.OwnerRva)); frame();
            }
            Put(b+p.PlayerRva,0); Put(d+4,6); frame();
            Check(Get(d+8)==6&&Get(d+16)==3,p.Name+" teardown cancellation");
            count=Get(d+28); Put(d,0); Put(b+p.PlayerRva,(int)player); Put(b+p.PowerRva,100);
            Check(frame()==123&&Get(d+28)==count&&Get(b+p.PowerRva)==100,p.Name+" disabled");
        }
        finally { VirtualFree(image,UIntPtr.Zero,0x8000); VirtualFree(block,UIntPtr.Zero,0x8000); }
    }
}
