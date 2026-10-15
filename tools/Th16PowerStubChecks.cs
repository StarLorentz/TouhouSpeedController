// Execute the real x86 dispatcher against a synthetic game layout in THIS process.
// Tests mailbox, guards, lock and stack/return behavior; not proof of game compatibility.
using System;
using System.Runtime.InteropServices;
using TouhouSpeedController;

internal static class Th16PowerStubChecks
{
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr n, uint t, uint p);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr a, UIntPtr n, uint t);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Frame();
    private static void Put(long p, int n) { Marshal.WriteInt32(new IntPtr(p), n); }
    private static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    private static void Bytes(long p, params byte[] b) { Marshal.Copy(b, 0, new IntPtr(p), b.Length); }
    private static void Check(bool b, string name) { if (!b) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static int Main()
    {
        if (Environment.Is64BitProcess) throw new Exception("Compile /platform:x86");
        IntPtr image = VirtualAlloc(IntPtr.Zero, (UIntPtr)0xB0000, 0x3000, 0x40);
        IntPtr allocation = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x40000, 0x3000, 0x40);
        if (image == IntPtr.Zero || allocation == IntPtr.Zero) throw new Exception("Allocation failed");
        long b = (long)(uint)image.ToInt32(), d = (long)(uint)allocation.ToInt32(), code = d + 4096, player = d + 0x10000;
        try
        {
            byte[] stub = PowerSyncSession.BuildCode((uint)code, (uint)d, (uint)b, PowerSyncProfile.Get("th16"));
            Bytes(code, stub);
            Bytes(b + 0x156C, 0x8B, 0xE5, 0x5D, 0xC3); // original function epilogue
            // Fake rebuild: keep its this pointer contract and update both observable tiers.
            long f = b + 0x440E0;
            Bytes(f, 0xA1); Bytes(f + 1, BitConverter.GetBytes((uint)(b + 0xA57E4)));
            // EAX /= 100 using a stack divisor, preserving every nonvolatile register.
            Bytes(f + 5, 0x6A, 0x64, 0x31, 0xD2, 0xF7, 0x34, 0x24, 0x83, 0xC4, 0x04,
                0x89, 0x81, 0x88, 0x60, 0x01, 0, 0x89, 0x81, 0xE4, 0x5F, 0x01, 0, 0xC3);
            // Wrapper uses the original update function's saved-register stack, then jumps.
            long entry = d + 0x3000;
            Bytes(entry, 0x55, 0x8B, 0xEC, 0x53, 0x56, 0x57, 0xBE, 0x7B, 0, 0, 0, 0xE9);
            Bytes(entry + 12, BitConverter.GetBytes(unchecked((int)(code - entry - 16))));
            Frame frame = (Frame)Marshal.GetDelegateForFunctionPointer(new IntPtr(entry), typeof(Frame));
            Put(d, 1); Put(d + 20, -1); Put(b + 0xA6EF8, (int)player); Put(player + 0x165A8, 1);
            Put(b + 0xA57EC, 100);
            Put(b + 0xA57E4, 100);
            Check(frame() == 123 && Get(player + 0x16698) == 1 && Get(d + 28) == 1, "initial synchronization and original return");
            Put(d + 12, 400); Put(d + 40, (int)player); Put(d + 4, 1);
            Check(frame() == 123 && Get(b + 0xA57E4) == 400 && Get(player + 0x16698) == 4 && Get(d + 8) == 1, "request 1.00 -> 4.00");
            Put(d + 12, 320); Put(d + 4, 2); frame();
            Check(Get(player + 0x16698) == 3, "downward request 4.00 -> 3.20");
            int count = Get(d + 28); Put(b + 0xA57E4, 321); frame();
            Check(Get(d + 28) == count + 1 && Get(player + 0x16698) == 3, "0.01 change refreshes without changing tier rule");
            Put(d + 48, 1); Put(b + 0xA57E4, 100); frame();
            Check(Get(b + 0xA57E4) == 320 && Get(player + 0x16698) == 3, "lock restores target and tier");
            Put(player + 0x16698, 2); Put(player + 0x165F4, 2); frame();
            Check(Get(b + 0xA57E4) == 320 && Get(player + 0x16698) == 3 && Get(player + 0x165F4) == 3,
                "repair stale attack caches even when locked Power already matches target");
            Put(d + 48, 0); Put(b + 0xA57E4, 250); frame();
            Check(Get(b + 0xA57E4) == 250 && Get(player + 0x16698) == 2, "unlock permits natural changes");
            Put(player + 0x165A8, 4); Put(d + 12, 400); Put(d + 4, 3); frame();
            Check(Get(d + 8) == 2 && Get(b + 0xA57E4) == 250, "defer during hit/death state");
            Put(player + 0x165A8, 1); frame();
            Check(Get(d + 8) == 3 && Get(b + 0xA57E4) == 400, "resume pending request when active");
            Put(b + 0xA6EF8, 0); Put(d + 4, 4); frame();
            Check(Get(d + 8) == 4 && Get(d + 16) == 3, "cancel request after player teardown");
            count = Get(d + 28); Put(d, 0); Put(b + 0xA6EF8, (int)player); Put(b + 0xA57E4, 100);
            Check(frame() == 123 && Get(d + 28) == count && Get(b + 0xA57E4) == 100, "disabled dispatcher leaves game state alone");
            return 0;
        }
        finally { VirtualFree(image, UIntPtr.Zero, 0x8000); VirtualFree(allocation, UIntPtr.Zero, 0x8000); }
    }
}
