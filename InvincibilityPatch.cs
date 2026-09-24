// 东方变速器 - experimental TH16/TH17/TH18/TH20 invincibility patches
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// Each supported executable has a separately identified player-hit transition.
// The first six bytes are checked before replacing the first byte with RET.
// The original byte is restored when the option is disabled.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TouhouSpeedController
{
    internal sealed class InvincibilityPatch : IDisposable
    {
        private const int ProcessVmRead = 0x0010;
        private const int ProcessVmWrite = 0x0020;
        private const int ProcessVmOperation = 0x0008;
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const uint PageExecuteReadWrite = 0x40;
        private const long Th16HitTransitionRva = 0x00043F10;
        private const long Th17HitTransitionRva = 0x00049420;
        private const long Th18HitTransitionRva = 0x0005D3A0;
        private const long Th20HitTransitionRva = 0x000F86F0;
        private static readonly byte[] Th16ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };
        private static readonly byte[] Th17ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th18ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th20ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C };

        private readonly Process process;
        private readonly IntPtr handle;
        private readonly IntPtr address;
        private readonly byte[] expectedEntry;
        private readonly string gameCode;
        private bool active;

        public string GameCode { get { return gameCode; } }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private InvincibilityPatch(Process target, IntPtr processHandle, IntPtr patchAddress,
            byte[] expectedBytes, string code)
        {
            process = target;
            handle = processHandle;
            address = patchAddress;
            expectedEntry = expectedBytes;
            gameCode = code;
        }

        public static InvincibilityPatch Enable(Process process)
        {
            if (process == null) throw new ArgumentNullException("process");
            string name = process.ProcessName.ToLowerInvariant();
            long hitRva;
            byte[] entry;
            string code;
            if (name == "th16")
            {
                PracticeMemoryAccess.VerifySupportedTh16(process);
                hitRva = Th16HitTransitionRva;
                entry = Th16ExpectedEntry;
                code = "TH16";
            }
            else if (name == "th17")
            {
                PracticeMemoryAccess.VerifySupportedDirectGame(process, "TH17");
                hitRva = Th17HitTransitionRva;
                entry = Th17ExpectedEntry;
                code = "TH17";
            }
            else if (name == "th18")
            {
                PracticeMemoryAccess.VerifySupportedDirectGame(process, "TH18");
                hitRva = Th18HitTransitionRva;
                entry = Th18ExpectedEntry;
                code = "TH18";
            }
            else if (name == "th20")
            {
                PracticeMemoryAccess.VerifySupportedDirectGame(process, "TH20");
                hitRva = Th20HitTransitionRva;
                entry = Th20ExpectedEntry;
                code = "TH20";
            }
            else throw new NotSupportedException("无敌功能目前只支持已识别的 TH16、TH17、TH18 和 TH20。");
            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation |
                ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 " + code + " 以启用无敌");

            InvincibilityPatch patch = new InvincibilityPatch(process, handle,
                new IntPtr(moduleBase + hitRva), entry, code);
            try
            {
                byte[] current = patch.ReadBytes(entry.Length);
                for (int i = 0; i < current.Length; i++)
                    if (current[i] != entry[i])
                        throw new InvalidOperationException("受击代码与已验证版本不一致，可能已被其他工具修改；已拒绝启用无敌。");

                patch.active = true;
                patch.WriteFirstByte(0xC3);
                if (patch.ReadBytes(1)[0] != 0xC3)
                    throw new IOException("无敌补丁写入后校验失败。");
                return patch;
            }
            catch
            {
                // If the write occurred but verification failed, restore before releasing the handle.
                try
                {
                    if (patch.active && patch.ReadBytes(1)[0] == 0xC3) patch.Restore();
                }
                catch (Exception ex)
                {
                    throw new IOException("启用失败且无法恢复受击代码；请退出 " + code + "。" , ex);
                }
                finally { CloseHandle(handle); }
                throw;
            }
        }

        public void Restore()
        {
            if (!active) return;
            if (process.HasExited) { active = false; return; }

            byte current = ReadBytes(1)[0];
            if (current != 0xC3)
                throw new InvalidOperationException("无敌补丁位置已被其他程序修改，未覆盖其代码；请退出游戏以彻底恢复。");

            WriteFirstByte(expectedEntry[0]);
            if (ReadBytes(1)[0] != expectedEntry[0])
                throw new IOException("恢复受击代码后的回读校验失败；请退出游戏以彻底恢复。");
            active = false;
        }

        public void Dispose()
        {
            Restore();
            CloseHandle(handle);
        }

        private byte[] ReadBytes(int count)
        {
            byte[] buffer = new byte[count];
            IntPtr bytesRead;
            if (!ReadProcessMemory(handle, address, buffer, count, out bytesRead) || bytesRead.ToInt64() != count)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取 " + gameCode + " 受击代码失败");
            return buffer;
        }

        private void WriteFirstByte(byte value)
        {
            uint oldProtect;
            if (!VirtualProtectEx(handle, address, (UIntPtr)1, PageExecuteReadWrite, out oldProtect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法修改 " + gameCode + " 受击代码页权限");
            try
            {
                IntPtr bytesWritten;
                if (!WriteProcessMemory(handle, address, new[] { value }, 1, out bytesWritten) || bytesWritten.ToInt64() != 1)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 " + gameCode + " 受击代码失败");
                if (!FlushInstructionCache(handle, address, (UIntPtr)1))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "刷新 " + gameCode + " 指令缓存失败");
            }
            finally
            {
                uint ignored;
                if (!VirtualProtectEx(handle, address, (UIntPtr)1, oldProtect, out ignored))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复 " + gameCode + " 代码页权限");
            }
        }
    }
}
