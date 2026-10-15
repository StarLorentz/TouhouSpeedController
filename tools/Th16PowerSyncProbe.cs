// Development CLI exercising the same synchronizer as the GUI. GPL-3.0-or-later.
// Compile with PracticeMemoryAccess.cs, PowerSyncSession.cs and PowerSyncProfile.cs. No input automation.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TouhouSpeedController;

internal static class Th16PowerSyncProbe
{
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    private static int Read(IntPtr h, long a)
    {
        byte[] b = new byte[4]; IntPtr n;
        if (!ReadProcessMemory(h, new IntPtr(a), b, 4, out n) || n.ToInt32() != 4) throw new IOException("Read failed");
        return BitConverter.ToInt32(b, 0);
    }
    private static void Snapshot(Process game, PowerSyncSession sync)
    {
        IntPtr h = OpenProcess(0x410, false, game.Id);
        try
        {
            long b = game.MainModule.BaseAddress.ToInt64(); int p = Read(h, b + 0xA6EF8);
            PowerSyncStatus s = sync == null ? null : sync.Status();
            byte[] hook = new byte[5]; IntPtr hookRead;
            if (!ReadProcessMemory(h, new IntPtr(b + 0x1567), hook, 5, out hookRead) || hookRead.ToInt32() != 5) throw new IOException("Hook read failed");
            Console.WriteLine("power={0} player={1:X} state={2} shotTier={3} firingTier={4} pending={5} result={6} refreshes={7} ticks={8} thread={9}",
                Read(h, b + 0xA57E4), p, p == 0 ? -1 : Read(h, (long)(uint)p + 0x165A8),
                p == 0 ? -1 : Read(h, (long)(uint)p + 0x16698), p == 0 ? -1 : Read(h, (long)(uint)p + 0x165F4),
                s != null && s.Pending, s == null ? -1 : s.Result, s == null ? -1 : s.Refreshes,
                s == null ? -1 : s.Ticks, s == null ? -1 : s.ThreadId);
            Console.WriteLine("hook=" + BitConverter.ToString(hook));
        }
        finally { CloseHandle(h); }
    }
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--emit")
        {
            File.WriteAllBytes(args[1], PowerSyncSession.BuildCode(0x10001000, 0x10000000, 0x400000, PowerSyncProfile.Get("th16"))); return 0;
        }
        if (args.Length != 1) { Console.WriteLine("Usage: probe PID; commands: status, set N, lock N, unlock, restore, quit"); return 2; }
        using (Process game = Process.GetProcessById(Int32.Parse(args[0])))
        {
            PowerSyncSession sync = null;
            try
            {
                Snapshot(game, sync);
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    try
                    {
                        string[] cmd = line.Trim().Split(' ');
                        if (cmd[0] == "quit") break;
                        if (cmd[0] == "restore") { if (sync != null) sync.Dispose(); sync = null; }
                        else if (cmd[0] == "unlock") { if (sync != null) sync.Unlock(); }
                        else if (cmd[0] == "set" || cmd[0] == "lock")
                        {
                            if (sync == null) sync = PowerSyncSession.Attach(game);
                            sync.Request(Int32.Parse(cmd[1]), cmd[0] == "lock");
                        }
                        Snapshot(game, sync);
                    }
                    catch (Exception ex) { Console.WriteLine(ex.ToString()); }
                }
            }
            finally { if (sync != null) sync.Dispose(); }
        }
        return 0;
    }
}
