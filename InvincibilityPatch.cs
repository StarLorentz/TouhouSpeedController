// 东方变速器 - experimental TH16 invincibility patch
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// The patch location was identified by examining the supported TH16 executable.
// Three collision paths call the same player-hit transition at VA 0x00443F10.
// Its first six bytes are checked before replacing the first byte with RET.
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
        private const long HitTransitionRva = 0x00043F10;
        private static readonly byte[] ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };

        private readonly Process process;
        private readonly IntPtr handle;
        private readonly IntPtr address;
        private bool active;

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

        private InvincibilityPatch(Process target, IntPtr processHandle, IntPtr patchAddress)
        {
            process = target;
            handle = processHandle;
            address = patchAddress;
        }

        public static InvincibilityPatch Enable(Process process)
        {
            // Reuse the resource adapter's exact executable fingerprint gate.
            PracticeMemoryAccess.VerifySupportedTh16(process);
            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation |
                ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 TH16 以启用无敌");

            InvincibilityPatch patch = new InvincibilityPatch(process, handle, new IntPtr(moduleBase + HitTransitionRva));
            try
            {
                byte[] current = patch.ReadBytes(ExpectedEntry.Length);
                for (int i = 0; i < current.Length; i++)
                    if (current[i] != ExpectedEntry[i])
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
                    throw new IOException("启用失败且无法恢复受击代码；请退出 TH16。" , ex);
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

            WriteFirstByte(ExpectedEntry[0]);
            if (ReadBytes(1)[0] != ExpectedEntry[0])
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
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取 TH16 受击代码失败");
            return buffer;
        }

        private void WriteFirstByte(byte value)
        {
            uint oldProtect;
            if (!VirtualProtectEx(handle, address, (UIntPtr)1, PageExecuteReadWrite, out oldProtect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法修改 TH16 受击代码页权限");
            try
            {
                IntPtr bytesWritten;
                if (!WriteProcessMemory(handle, address, new[] { value }, 1, out bytesWritten) || bytesWritten.ToInt64() != 1)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 TH16 受击代码失败");
                if (!FlushInstructionCache(handle, address, (UIntPtr)1))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "刷新 TH16 指令缓存失败");
            }
            finally
            {
                uint ignored;
                if (!VirtualProtectEx(handle, address, (UIntPtr)1, oldProtect, out ignored))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复 TH16 代码页权限");
            }
        }
    }
}
