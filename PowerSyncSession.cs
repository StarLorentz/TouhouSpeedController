// Version-gated Power synchronization on each game's update thread.
// Copyright (C) 2026 StarLorentz. GPL-3.0-or-later.
// Addresses researched from the exact local executable; see docs/POWER_ATTACK_REFRESH_RESEARCH.md.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TouhouSpeedController
{
    internal sealed class PowerSyncStatus
    {
        public int Requested, Completed, Result, Refreshes, Ticks, ThreadId;
        public bool Pending { get { return Requested != Completed; } }
    }

    internal sealed class PowerSyncSession : IDisposable
    {
        // End of the update chain, after its lock has been released, before epilogue.
        private readonly PowerSyncProfile profile;
        private readonly Process process;
        private readonly long moduleBase;
        private IntPtr handle, memory;
        private byte[] jump;
        private bool installed, published;
        // Mailbox DWORDs: enabled, request, completed, target, result, lastPower,
        // lastPlayer, refreshes, ticks, threadId, requestPlayer, busy, locked.
        private const int CodeOffset = 4096;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr a, UIntPtr n, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr h, IntPtr a, UIntPtr n, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(IntPtr h, IntPtr a, UIntPtr n, uint protect, out uint old);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(IntPtr h, IntPtr a, UIntPtr n);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(int access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool Wow64GetThreadContext(IntPtr h, IntPtr context);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(IntPtr h, IntPtr context);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private PowerSyncSession(Process target, PowerSyncProfile layout)
        { process = target; profile=layout; moduleBase = target.MainModule.BaseAddress.ToInt64(); }

        public static PowerSyncSession Attach(Process target)
        {
            PowerSyncProfile layout = PowerSyncProfile.Get(target.ProcessName);
            layout.Verify(target);
            PowerSyncSession sync = new PowerSyncSession(target,layout);
            sync.handle = OpenProcess(0x1038, false, target.Id);
            if (sync.handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!Equal(sync.Read(sync.moduleBase + layout.HookRva, 5), layout.Original))
                    throw new InvalidOperationException(layout.GameCode+" 更新位置已被其他工具修改，不能启用 Power 同步。");
                // Also verify the native rebuild entry before publishing a call to it.
                if (!Equal(sync.Read(sync.moduleBase + layout.RebuildRva, layout.RebuildEntry.Length),layout.RebuildEntry))
                    throw new InvalidOperationException(layout.GameCode+" 攻击更新代码不一致，已拒绝启用。");
                sync.memory = VirtualAllocEx(sync.handle, IntPtr.Zero, (UIntPtr)8192, 0x3000, 0x04);
                if (sync.memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                long data = sync.memory.ToInt64(), code = data + CodeOffset;
                if (code + 4096 > UInt32.MaxValue) throw new InvalidOperationException("Power 回调地址超出 32 位范围。");
                sync.WriteInt(0, 1); sync.WriteInt(20, -1);
                byte[] body = BuildCode((uint)code, (uint)data, (uint)sync.moduleBase,layout);
                sync.Write(code, body);
                if (!Equal(sync.Read(code, body.Length), body)) throw new IOException("Power 回调写入后校验失败。");
                uint old;
                if (!VirtualProtectEx(sync.handle, new IntPtr(code), (UIntPtr)4096, 0x20, out old) ||
                    !FlushInstructionCache(sync.handle, new IntPtr(code), (UIntPtr)body.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                sync.jump = new byte[5]; sync.jump[0] = 0xE9;
                Array.Copy(BitConverter.GetBytes(unchecked((int)(code - (sync.moduleBase + layout.HookRva + 5)))), 0, sync.jump, 1, 4);
                sync.ChangeHook(true);
                return sync;
            }
            catch
            {
                // A failure after publishing the hook must disable it before cleanup.
                if (sync.installed)
                {
                    try { sync.WriteInt(0, 0); sync.ChangeHook(false); }
                    catch { /* Retain callback memory if restoration was not possible. */ }
                }
                if (!sync.published && sync.memory != IntPtr.Zero)
                    VirtualFreeEx(sync.handle, sync.memory, UIntPtr.Zero, 0x8000);
                CloseHandle(sync.handle);
                throw;
            }
        }

        public int ProcessId { get { return process.Id; } }

        public void Request(int power, bool locked)
        {
            if (power < 100 || power > 400) throw new ArgumentOutOfRangeException("power");
            PowerSyncStatus status = Status();
            if (status.Pending) throw new InvalidOperationException("上一次 Power 请求尚未完成；请回到游戏后再试。");
            int player = ReadInt(moduleBase + profile.PlayerRva);
            int divisor = ReadInt(moduleBase + profile.DivisorRva);
            if (player == 0 || ReadInt((long)(uint)player + profile.StateOffset) != 1 || divisor<100 || divisor>400 ||
                (profile.OwnerOffset!=0 && (uint)ReadInt((long)(uint)player+profile.OwnerOffset)!=(uint)(moduleBase+profile.OwnerRva)))
                throw new InvalidOperationException("请在自机正常活动时应用 Power，死亡或加载期间请稍后重试。");
            // Publish target/owner/lock before sequence. Game consumes at most one request.
            WriteInt(12, power); WriteInt(40, player); WriteInt(48, locked ? 1 : 0);
            WriteInt(4, unchecked(status.Requested + 1));
        }

        public void Unlock() { if (!process.HasExited) WriteInt(48, 0); }

        public PowerSyncStatus Status()
        {
            if (process.HasExited) throw new InvalidOperationException("游戏已退出。");
            byte[] b = Read(memory.ToInt64(), 52);
            return new PowerSyncStatus { Requested = BitConverter.ToInt32(b, 4), Completed = BitConverter.ToInt32(b, 8),
                Result = BitConverter.ToInt32(b, 16), Refreshes = BitConverter.ToInt32(b, 28),
                Ticks = BitConverter.ToInt32(b, 32), ThreadId = BitConverter.ToInt32(b, 36) };
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            if (!process.HasExited)
            {
                WriteInt(0, 0); WriteInt(48, 0);
                ChangeHook(false);
                // All threads are out of our code and busy==0 when ChangeHook returns.
                // Retain the allocation until game exit: a suspended thread may have
                // a return address into the stub below an OS/kernel frame. No dangling code.
            }
            CloseHandle(handle); handle = IntPtr.Zero;
        }

        private void ChangeHook(bool enable)
        {
            long site = moduleBase + profile.HookRva;
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 2000)
            {
                List<IntPtr> suspended = new List<IntPtr>();
                bool safe = true;
                try
                {
                    process.Refresh();
                    foreach (ProcessThread thread in process.Threads)
                    {
                        IntPtr th = OpenThread(0x004A, false, thread.Id); // suspend/context/query
                        if (th == IntPtr.Zero) { safe = false; break; }
                        if (SuspendThread(th) == UInt32.MaxValue) { CloseHandle(th); safe = false; break; }
                        suspended.Add(th);
                        long ip = InstructionPointer(th);
                        if ((ip > site && ip < site + 5) ||
                            (!enable && ip >= memory.ToInt64() + CodeOffset && ip < memory.ToInt64() + 8192)) safe = false;
                    }
                    if (!enable && ReadInt(memory.ToInt64() + 44) != 0) safe = false;
                    if (safe)
                    {
                        byte[] expected = enable ? profile.Original : jump, desired = enable ? jump : profile.Original;
                        if (!Equal(Read(site, 5), expected))
                            throw new InvalidOperationException("Power 同步入口被其他程序改动；请退出游戏恢复。");
                        uint old;
                        if (!VirtualProtectEx(handle, new IntPtr(site), (UIntPtr)5, 0x40, out old))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        try
                        {
                            try
                            {
                                installed = true; // retain callback allocation if a partial write cannot be rolled back
                                published = true;
                                Write(site, desired);
                                if (!Equal(Read(site, 5), desired) || !FlushInstructionCache(handle, new IntPtr(site), (UIntPtr)5))
                                    throw new IOException("Power 同步入口写入后验证失败。");
                                installed = enable;
                            }
                            catch
                            {
                                Write(site, expected);
                                bool restored = Equal(Read(site, 5), expected) && FlushInstructionCache(handle, new IntPtr(site), (UIntPtr)5);
                                if (!restored) { installed = true; throw new IOException("无法恢复 Power 同步入口，请退出游戏。"); }
                                installed = !enable;
                                throw;
                            }
                        }
                        finally
                        {
                            uint ignored;
                            if (!VirtualProtectEx(handle, new IntPtr(site), (UIntPtr)5, old, out ignored))
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        return;
                    }
                }
                finally
                {
                    for (int i = suspended.Count - 1; i >= 0; i--) { ResumeThread(suspended[i]); CloseHandle(suspended[i]); }
                }
                Thread.Sleep(10);
            }
            throw new IOException("暂时无法安全切换 Power 同步入口，请稍后再试。");
        }

        private static long InstructionPointer(IntPtr thread)
        {
            IntPtr raw = Marshal.AllocHGlobal(736);
            try
            {
                IntPtr aligned = new IntPtr((raw.ToInt64() + 15) & ~15L);
                byte[] zero = new byte[716]; Marshal.Copy(zero, 0, aligned, zero.Length);
                Marshal.WriteInt32(aligned, 0x10001); // x86 CONTEXT_CONTROL
                bool ok = Environment.Is64BitProcess ? Wow64GetThreadContext(thread, aligned) : GetThreadContext(thread, aligned);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "读取游戏线程位置失败");
                return (long)(uint)Marshal.ReadInt32(aligned, 184);
            }
            finally { Marshal.FreeHGlobal(raw); }
        }

        private byte[] Read(long a, int n)
        {
            byte[] b = new byte[n]; IntPtr count;
            if (!ReadProcessMemory(handle, new IntPtr(a), b, n, out count) || count.ToInt64() != n)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取游戏 Power 同步状态失败");
            return b;
        }
        private int ReadInt(long a) { return BitConverter.ToInt32(Read(a, 4), 0); }
        private void Write(long a, byte[] b)
        {
            IntPtr count;
            if (!WriteProcessMemory(handle, new IntPtr(a), b, b.Length, out count) || count.ToInt64() != b.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "写入游戏 Power 同步状态失败");
        }
        private void WriteInt(int offset, int value) { Write(memory.ToInt64() + offset, BitConverter.GetBytes(value)); }
        private static bool Equal(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

        // Small, auditable x86 emitter; no native compiler/runtime dependency in the package.
        internal static byte[] BuildCode(uint code, uint data, uint image, PowerSyncProfile layout)
        {
            X86 a = new X86(code);
            a.Emit(0x9C, 0x60, 0x8B, 0xEC); // pushfd; pushad; mov ebp,esp
            a.Emit(0x81, 0xEC); a.Word(0x220); a.Emit(0x83, 0xE4, 0xF0, 0x0F, 0xAE, 0x04, 0x24); // aligned FXSAVE
            a.Emit(0xBF); a.Word(data);
            a.Emit(0xFF, 0x47, 32, 0x64, 0xA1); a.Word(0x24); a.Emit(0x89, 0x47, 36); // tick and current TEB thread id
            a.Emit(0x83, 0x3F, 1); a.If(0x85, "restore");
            a.Emit(0xC7, 0x47, 44); a.Word(1);
            a.Emit(0x8B, 0x5F, 4); // ebx = sequence consumed this iteration, never reload to acknowledge
            a.Emit(0x8B, 0x35); a.Word(image + layout.PlayerRva);
            a.Emit(0x85, 0xF6); a.If(0x84, "noPlayer");
            a.Emit(0x83, 0xBE); a.Word(layout.StateOffset); a.Emit(1); a.If(0x85, "defer");
            if (layout.OwnerOffset!=0)
            {
                a.Emit(0x81,0xBE); a.Word(layout.OwnerOffset); a.Word(image+layout.OwnerRva); a.If(0x85,"defer");
            }
            // Never divide or rebuild during uninitialized/card setup states.
            a.Emit(0x8B,0x0D); a.Word(image+layout.DivisorRva);
            a.Emit(0x81,0xF9); a.Word(100); a.If(0x8C,"defer");
            a.Emit(0x81,0xF9); a.Word(400); a.If(0x8F,"defer");
            a.Emit(0x3B, 0x5F, 8); a.If(0x84, "noRequest");
            a.Emit(0x3B, 0x77, 40); a.If(0x85, "cancel");
            a.Emit(0x8B, 0x47, 12, 0xA3); a.Word(image + layout.PowerRva); a.Jump("refresh");
            a.Label("noRequest");
            a.Emit(0x83, 0x7F, 48, 1); a.If(0x85, "observe");
            a.Emit(0x8B, 0x47, 12, 0x3B, 0x05); a.Word(image + layout.PowerRva); a.If(0x84, "observe");
            a.Emit(0xA3); a.Word(image + layout.PowerRva); a.Jump("refresh");
            a.Label("observe");
            a.Emit(0xA1); a.Word(image + layout.PowerRva);
            a.Emit(0x3D); a.Word(100); a.If(0x8C, "done");
            a.Emit(0x3D); a.Word(400); a.If(0x8F, "done");
            a.Emit(0x3B, 0x47, 20); a.If(0x85, "refresh");
            a.Emit(0x3B, 0x77, 24); a.If(0x85, "refresh");
            // Death/respawn can rebuild a lower tier while the raw Power is already
            // restored to the lock target. Compare actual caches as well as lastPower.
            a.Emit(0x8B, 0x0D); a.Word(image + layout.DivisorRva);
            a.Emit(0x31, 0xD2, 0xF7, 0xF1);
            foreach (uint offset in layout.CacheOffsets)
            { a.Emit(0x3B,0x86); a.Word(offset); a.If(0x85,"refresh"); }
            a.Jump("done");
            a.Label("refresh");
            a.Emit(0xA1); a.Word(image + layout.PowerRva);
            a.Emit(0x89, 0x47, 20, 0x89, 0x77, 24, 0x8D, 0x8E); a.Word(layout.ThisOffset);
            if (layout.Argument.HasValue) { a.Emit(0x68); a.Word(unchecked((uint)layout.Argument.Value)); }
            a.Emit(0xFC, 0xB8); a.Word(image + layout.RebuildRva); a.Emit(0xFF, 0xD0);
            a.Emit(0xFF, 0x47, 28, 0xC7, 0x47, 16); a.Word(1);
            a.Emit(0x89, 0x5F, 8); a.Jump("done");
            a.Label("noPlayer");
            a.Emit(0xC7, 0x47, 20); a.Word(UInt32.MaxValue);
            a.Emit(0xC7, 0x47, 24); a.Word(0);
            a.Emit(0x3B, 0x5F, 8); a.If(0x84, "done");
            a.Label("cancel");
            a.Emit(0xC7, 0x47, 16); a.Word(3); a.Emit(0x89, 0x5F, 8); a.Jump("done");
            a.Label("defer");
            a.Emit(0x3B, 0x5F, 8); a.If(0x84, "done");
            a.Emit(0xC7, 0x47, 16); a.Word(2);
            a.Label("done"); a.Emit(0xC7, 0x47, 44); a.Word(0);
            a.Label("restore");
            a.Emit(0x0F, 0xAE, 0x0C, 0x24, 0x8B, 0xE5, 0x61, 0x9D); // FXRSTOR; saved stack; popad; popfd
            a.Emit(layout.Original); a.To(image + layout.HookRva + 5);
            return a.Finish();
        }

        private sealed class X86
        {
            private readonly uint address;
            private readonly List<byte> bytes = new List<byte>();
            private readonly Dictionary<string, int> labels = new Dictionary<string, int>();
            private readonly Dictionary<int, string> fixups = new Dictionary<int, string>();
            public X86(uint start) { address = start; }
            public void Emit(params byte[] b) { bytes.AddRange(b); }
            public void Word(uint v) { Emit(BitConverter.GetBytes(v)); }
            public void Label(string name) { labels.Add(name, bytes.Count); }
            public void If(byte condition, string target) { Emit(0x0F, condition); Fix(target); }
            public void Jump(string target) { Emit(0xE9); Fix(target); }
            private void Fix(string label) { fixups.Add(bytes.Count, label); Word(0); }
            public void To(uint target) { Emit(0xE9); Word(unchecked(target - (address + (uint)bytes.Count + 4))); }
            public byte[] Finish()
            {
                byte[] b = bytes.ToArray();
                foreach (KeyValuePair<int, string> fix in fixups)
                    Array.Copy(BitConverter.GetBytes(labels[fix.Value] - fix.Key - 4), 0, b, fix.Key, 4);
                return b;
            }
        }
    }
}
