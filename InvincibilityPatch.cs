// 东方变速器 - version-gated experimental invincibility patches
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// Each supported executable has a separately identified player-hit transition.
// The entry bytes are checked before replacing them with a matching return.
// The original bytes are restored when the option is disabled.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TouhouSpeedController
{
    internal sealed class InvincibilityPatch : IDisposable
    {
        private const int ProcessVmRead = 0x0010;
        private const int ProcessVmWrite = 0x0020;
        private const int ProcessVmOperation = 0x0008;
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const uint PageExecuteReadWrite = 0x40;
        private const long Th07HitTransitionRva = 0x0003EDC0;
        private const long Th10HitTransitionRva = 0x00026CF0;
        private const long Th11HitTransitionRva = 0x00032A90;
        private const long Th14HitTransitionRva = 0x0004F7A0;
        private const long Th15HitTransitionRva = 0x00056540;
        private const long Th16HitTransitionRva = 0x00043F10;
        private const long Th17HitTransitionRva = 0x00049420;
        private const long Th18HitTransitionRva = 0x0005D3A0;
        private const long Th20HitTransitionRva = 0x000F86F0;
        private static readonly byte[] Th16ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };
        private static readonly byte[] Th17ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th18ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th20ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C };
        private static readonly byte[] Th07ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };
        private static readonly byte[] Th10ExpectedEntry = { 0x83, 0xEC, 0x14, 0x8B, 0x0D, 0xF0 };
        private static readonly byte[] Th11ExpectedEntry = { 0x83, 0xEC, 0x1C, 0x53, 0x55, 0x56 };
        private static readonly byte[] Th14ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };
        private static readonly byte[] Th15ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] ReturnVoid = { 0xC3 };
        private static readonly byte[] ReturnAndPopArgument = { 0xC2, 0x04, 0x00 };

        private readonly Process process;
        private readonly IntPtr handle;
        private readonly IntPtr address;
        private readonly byte[] expectedEntry;
        private readonly byte[] replacement;
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
            byte[] expectedBytes, byte[] replacementBytes, string code)
        {
            process = target;
            handle = processHandle;
            address = patchAddress;
            expectedEntry = expectedBytes;
            replacement = replacementBytes;
            gameCode = code;
        }

        public static InvincibilityPatch Enable(Process process)
        {
            if (process == null) throw new ArgumentNullException("process");
            string name = process.ProcessName.ToLowerInvariant();
            long hitRva;
            byte[] entry;
            byte[] replacement = ReturnVoid;
            string code;
            if (name == "th07")
            {
                VerifyExecutableHash(process, "35467EAF8DC7FC85F024F16FB2037255F151CEFDA33CF4867BC9122AAA2E80CA");
                hitRva = Th07HitTransitionRva;
                entry = Th07ExpectedEntry;
                code = "TH07";
            }
            else if (name == "th10")
            {
                VerifyExecutableHash(process, "2F14760B6FBBF57549541583283BADB9A19A4222B90F0A146D5AA17F01DC9040");
                hitRva = Th10HitTransitionRva;
                entry = Th10ExpectedEntry;
                replacement = ReturnAndPopArgument;
                code = "TH10";
            }
            else if (name == "th11")
            {
                VerifyExecutableHash(process, "2978B17F6184D100D249D4311348DD30C5C32EC75C014B667A525B797D3D8813");
                hitRva = Th11HitTransitionRva;
                entry = Th11ExpectedEntry;
                code = "TH11";
            }
            else if (name == "th14")
            {
                PracticeMemoryAccess.VerifySupportedDirectGame(process, "TH14");
                hitRva = Th14HitTransitionRva;
                entry = Th14ExpectedEntry;
                code = "TH14";
            }
            else if (name == "th15")
            {
                PracticeMemoryAccess.VerifySupportedDirectGame(process, "TH15");
                hitRva = Th15HitTransitionRva;
                entry = Th15ExpectedEntry;
                code = "TH15";
            }
            else if (name == "th16")
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
            else throw new NotSupportedException("这个游戏或 EXE 版本尚未接入无敌功能。");
            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation |
                ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 " + code + " 以启用无敌");

            InvincibilityPatch patch = new InvincibilityPatch(process, handle,
                new IntPtr(moduleBase + hitRva), entry, replacement, code);
            try
            {
                byte[] current = patch.ReadBytes(entry.Length);
                for (int i = 0; i < current.Length; i++)
                    if (current[i] != entry[i])
                        throw new InvalidOperationException("受击代码与已验证版本不一致，可能已被其他工具修改；已拒绝启用无敌。");

                patch.active = true;
                patch.WriteBytes(replacement);
                if (!BytesEqual(patch.ReadBytes(replacement.Length), replacement))
                    throw new IOException("无敌补丁写入后校验失败。");
                return patch;
            }
            catch
            {
                // If the write occurred but verification failed, restore before releasing the handle.
                try
                {
                    if (patch.active && !process.HasExited)
                    {
                        byte[] current = patch.ReadBytes(replacement.Length);
                        if (!BytesCompatibleWithOriginalOrPatch(current, entry, replacement))
                            throw new InvalidOperationException("受击入口在写入期间被其他程序修改。");
                        byte[] original = new byte[replacement.Length];
                        Array.Copy(entry, original, original.Length);
                        patch.WriteBytes(original);
                        if (!BytesEqual(patch.ReadBytes(original.Length), original))
                            throw new IOException("回滚受击入口后校验失败。");
                    }
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

            byte[] current = ReadBytes(replacement.Length);
            if (!BytesEqual(current, replacement))
                throw new InvalidOperationException("无敌补丁位置已被其他程序修改，未覆盖其代码；请退出游戏以彻底恢复。");

            byte[] original = new byte[replacement.Length];
            Array.Copy(expectedEntry, original, original.Length);
            WriteBytes(original);
            if (!BytesEqual(ReadBytes(original.Length), original))
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

        private void WriteBytes(byte[] values)
        {
            uint oldProtect;
            if (!VirtualProtectEx(handle, address, (UIntPtr)values.Length, PageExecuteReadWrite, out oldProtect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法修改 " + gameCode + " 受击代码页权限");
            try
            {
                IntPtr bytesWritten;
                if (!WriteProcessMemory(handle, address, values, values.Length, out bytesWritten) || bytesWritten.ToInt64() != values.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 " + gameCode + " 受击代码失败");
                if (!FlushInstructionCache(handle, address, (UIntPtr)values.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "刷新 " + gameCode + " 指令缓存失败");
            }
            finally
            {
                uint ignored;
                if (!VirtualProtectEx(handle, address, (UIntPtr)values.Length, oldProtect, out ignored))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复 " + gameCode + " 代码页权限");
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static bool BytesCompatibleWithOriginalOrPatch(byte[] current, byte[] original, byte[] patch)
        {
            for (int i = 0; i < patch.Length; i++)
                if (current[i] != original[i] && current[i] != patch[i]) return false;
            return true;
        }

        private static void VerifyExecutableHash(Process process, string expected)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.Open(process.MainModule.FileName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty);
                if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("此 EXE 版本尚未验证，已拒绝启用无敌。SHA-256：" + actual);
            }
        }
    }
}
