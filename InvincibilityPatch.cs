// 东方变速器 - version-gated experimental invincibility patches
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// Each supported executable has a separately identified player-hit transition.
// Original bytes are checked before returning or taking the game's no-hit branch.
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
        private const long Th06ClassicBulletHitBranchRva = 0x0004C78E;
        private const long Th06ClassicLaserHitBranchRva = 0x0004C9D0;
        private const long Th06NewClassicBulletHitBranchRva = 0x0006C1B5;
        private const long Th06NewClassicLaserHitBranchRva = 0x0006C3FA;
        private const long Th07BulletHitBranchRva = 0x0003E334;
        private const long Th07LaserHitBranchRva = 0x0003EB19;
        private const long Th10HitTransitionRva = 0x00026CF0;
        private const long Th11HitTransitionRva = 0x00032A90;
        private const long Th14HitTransitionRva = 0x0004F7A0;
        private const long Th15HitTransitionRva = 0x00056540;
        private const long Th16HitTransitionRva = 0x00043F10;
        private const long Th17HitTransitionRva = 0x00049420;
        private const long Th18HitTransitionRva = 0x0005D3A0;
        private const long Th20HitTransitionRva = 0x000F86F0;
        private static readonly byte[] Th16ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };
        private static readonly byte[] Th06ClassicBulletExpected = { 0x40, 0x38, 0xBE, 0xB8, 0x73, 0x00, 0x00, 0x0F, 0x85, 0xC9, 0x00, 0x00, 0x00 };
        private static readonly byte[] Th06ClassicLaserExpected = { 0x80, 0x3D, 0xD1, 0x33, 0xB7, 0x00, 0x00, 0x0F, 0x85, 0x56, 0x02, 0x00, 0x00 };
        private static readonly byte[] Th06NewClassicBulletExpected = { 0x80, 0xBB, 0x98, 0x78, 0x00, 0x00, 0x00, 0x0F, 0x85, 0xBA, 0x00, 0x00, 0x00 };
        private static readonly byte[] Th06NewClassicLaserExpected = { 0x80, 0x3D, 0x87, 0x54, 0x4E, 0x00, 0x00, 0x0F, 0x85, 0x2E, 0x02, 0x00, 0x00 };
        // Skip hit effects and state changes together, returning no hit to callers.
        // Keep the existing epilogues so x64 nonvolatile registers are restored.
        private static readonly byte[] Th06ClassicBulletSkip = { 0xE9, 0xD8, 0x00, 0x00, 0x00 };
        private static readonly byte[] Th06ClassicLaserSkip = { 0xE9, 0x5E, 0x02, 0x00, 0x00 };
        private static readonly byte[] Th06NewClassicBulletSkip = { 0xE9, 0xF4, 0xFF, 0xFF, 0xFF };
        private static readonly byte[] Th06NewClassicLaserSkip = { 0xE9, 0x36, 0x02, 0x00, 0x00 };
        private static readonly byte[] Th17ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th18ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14 };
        private static readonly byte[] Th20ExpectedEntry = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C };
        private static readonly byte[] Th07BulletExpected = { 0xA1, 0x48, 0x9E, 0x4B, 0x00, 0x0F, 0xB7, 0x88, 0xD6, 0x00, 0x00, 0x00 };
        private static readonly byte[] Th07LaserExpected = { 0xA1, 0x48, 0x9E, 0x4B, 0x00, 0x0F, 0xB7, 0x88, 0xD6, 0x00, 0x00, 0x00 };
        private static readonly byte[] Th07BulletSkip = { 0xE9, 0xF7, 0xFF, 0xFF, 0xFF };
        private static readonly byte[] Th07LaserSkip = { 0xE9, 0xD5, 0xFF, 0xFF, 0xFF };
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
        private readonly IntPtr secondaryAddress;
        private readonly byte[] secondaryExpected;
        private readonly byte[] secondaryReplacement;
        private readonly string gameCode;
        private bool active;
        private bool secondaryActive;

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
            byte[] expectedBytes, byte[] replacementBytes, string code,
            IntPtr secondAddress, byte[] secondExpected, byte[] secondReplacement)
        {
            process = target;
            handle = processHandle;
            address = patchAddress;
            expectedEntry = expectedBytes;
            replacement = replacementBytes;
            secondaryAddress = secondAddress;
            secondaryExpected = secondExpected;
            secondaryReplacement = secondReplacement;
            gameCode = code;
        }

        public static InvincibilityPatch Enable(Process process)
        {
            if (process == null) throw new ArgumentNullException("process");
            string name = process.ProcessName.ToLowerInvariant();
            long hitRva;
            byte[] entry;
            byte[] replacement = ReturnVoid;
            long secondRva = 0;
            byte[] secondEntry = null;
            byte[] secondReplacement = null;
            string code;
            if (name == "th06c" || name == "th06nc")
            {
                bool classic = name == "th06c";
                VerifyExecutableHash(process, classic
                    ? "581F6C679100EEBE2BAF33CC786AD8C2D6306B86E0EE1B3CB945692320067ED6"
                    : "48630A42A2EB6762D0DB2A7E0D151203EFBE67220FEF7D2F9D0D51857928AC82");
                hitRva = classic ? Th06ClassicBulletHitBranchRva : Th06NewClassicBulletHitBranchRva;
                entry = classic ? Th06ClassicBulletExpected : Th06NewClassicBulletExpected;
                replacement = classic ? Th06ClassicBulletSkip : Th06NewClassicBulletSkip;
                secondRva = classic ? Th06ClassicLaserHitBranchRva : Th06NewClassicLaserHitBranchRva;
                secondEntry = classic ? Th06ClassicLaserExpected : Th06NewClassicLaserExpected;
                secondReplacement = classic ? Th06ClassicLaserSkip : Th06NewClassicLaserSkip;
                code = classic ? "TH06 原典" : "TH06 新典";
            }
            else if (name == "th07")
            {
                VerifyExecutableHash(process, "35467EAF8DC7FC85F024F16FB2037255F151CEFDA33CF4867BC9122AAA2E80CA");
                hitRva = Th07BulletHitBranchRva;
                entry = Th07BulletExpected;
                replacement = Th07BulletSkip;
                secondRva = Th07LaserHitBranchRva;
                secondEntry = Th07LaserExpected;
                secondReplacement = Th07LaserSkip;
                code = "TH07";
            }
            else if (name == "th10" || name == "th10chs" || name == "th10cht")
            {
                VerifyExecutableHash(process, name == "th10"
                    ? "2F14760B6FBBF57549541583283BADB9A19A4222B90F0A146D5AA17F01DC9040"
                    : name == "th10chs"
                        ? "97FDCEDE942D425BBB21A08067D95E08D916874A1CB399D73BE16737AFCA6A17"
                        : "3B2BDA90816BF4E5168A1A0AE0B33902D5C9BEE980B9DCA735848BE098646610");
                hitRva = Th10HitTransitionRva;
                entry = Th10ExpectedEntry;
                replacement = ReturnAndPopArgument;
                code = "TH10";
            }
            else if (name == "th11" || name == "th11c")
            {
                VerifyExecutableHash(process, name == "th11"
                    ? "2978B17F6184D100D249D4311348DD30C5C32EC75C014B667A525B797D3D8813"
                    : "11A9F3C6718289D9C0B0FA78C148B168A77D3554E804EB2D77F890BE48B3865D");
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
                new IntPtr(moduleBase + hitRva), entry, replacement, code,
                secondRva == 0 ? IntPtr.Zero : new IntPtr(moduleBase + secondRva),
                secondEntry, secondReplacement);
            try
            {
                byte[] current = patch.ReadBytes(entry.Length);
                for (int i = 0; i < current.Length; i++)
                    if (current[i] != entry[i])
                        throw new InvalidOperationException("受击代码与已验证版本不一致，可能已被其他工具修改；已拒绝启用无敌。");
                if (secondEntry != null && !BytesEqual(patch.ReadBytes(patch.secondaryAddress, secondEntry.Length), secondEntry))
                    throw new InvalidOperationException("第二条受击路径与已验证版本不一致，已拒绝启用无敌。");

                patch.active = true;
                patch.WriteBytes(replacement);
                if (!BytesEqual(patch.ReadBytes(replacement.Length), replacement))
                    throw new IOException("无敌补丁写入后校验失败。");
                if (secondEntry != null)
                {
                    patch.secondaryActive = true;
                    patch.WriteBytes(patch.secondaryAddress, secondReplacement);
                    if (!BytesEqual(patch.ReadBytes(patch.secondaryAddress, secondReplacement.Length), secondReplacement))
                        throw new IOException("第二条受击路径写入后校验失败。");
                }
                return patch;
            }
            catch
            {
                // If the write occurred but verification failed, restore before releasing the handle.
                try
                {
                    if (!process.HasExited)
                    {
                        Exception rollbackError = null;
                        if (patch.secondaryActive)
                        {
                            try { patch.RollbackSite(patch.secondaryAddress, secondEntry, secondReplacement); }
                            catch (Exception ex) { rollbackError = ex; }
                        }
                        if (patch.active)
                        {
                            try { patch.RollbackSite(patch.address, entry, replacement); }
                            catch (Exception ex) { if (rollbackError == null) rollbackError = ex; }
                        }
                        if (rollbackError != null) throw rollbackError;
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
            if (!active && !secondaryActive) return;
            if (process.HasExited) { active = false; secondaryActive = false; return; }
            if (secondaryActive)
            {
                RestoreSite(secondaryAddress, secondaryExpected, secondaryReplacement);
                secondaryActive = false;
            }
            if (active)
            {
                RestoreSite(address, expectedEntry, replacement);
                active = false;
            }
        }

        public void Dispose()
        {
            Restore();
            CloseHandle(handle);
        }

        private byte[] ReadBytes(int count)
        {
            return ReadBytes(address, count);
        }

        private byte[] ReadBytes(IntPtr targetAddress, int count)
        {
            byte[] buffer = new byte[count];
            IntPtr bytesRead;
            if (!ReadProcessMemory(handle, targetAddress, buffer, count, out bytesRead) || bytesRead.ToInt64() != count)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取 " + gameCode + " 受击代码失败");
            return buffer;
        }

        private void WriteBytes(byte[] values)
        {
            WriteBytes(address, values);
        }

        private void WriteBytes(IntPtr targetAddress, byte[] values)
        {
            uint oldProtect;
            if (!VirtualProtectEx(handle, targetAddress, (UIntPtr)values.Length, PageExecuteReadWrite, out oldProtect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法修改 " + gameCode + " 受击代码页权限");
            try
            {
                IntPtr bytesWritten;
                if (!WriteProcessMemory(handle, targetAddress, values, values.Length, out bytesWritten) || bytesWritten.ToInt64() != values.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 " + gameCode + " 受击代码失败");
                if (!FlushInstructionCache(handle, targetAddress, (UIntPtr)values.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "刷新 " + gameCode + " 指令缓存失败");
            }
            finally
            {
                uint ignored;
                if (!VirtualProtectEx(handle, targetAddress, (UIntPtr)values.Length, oldProtect, out ignored))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复 " + gameCode + " 代码页权限");
            }
        }

        private void RestoreSite(IntPtr targetAddress, byte[] originalEntry, byte[] patchBytes)
        {
            byte[] original = new byte[patchBytes.Length];
            Array.Copy(originalEntry, original, original.Length);
            byte[] current = ReadBytes(targetAddress, patchBytes.Length);
            if (BytesEqual(current, original)) return;
            if (!BytesEqual(current, patchBytes))
                throw new InvalidOperationException("无敌补丁位置已被其他程序修改，未覆盖其代码；请退出游戏以彻底恢复。");
            WriteBytes(targetAddress, original);
            if (!BytesEqual(ReadBytes(targetAddress, original.Length), original))
                throw new IOException("恢复受击代码后的回读校验失败；请退出游戏以彻底恢复。");
        }

        private void RollbackSite(IntPtr targetAddress, byte[] originalEntry, byte[] patchBytes)
        {
            byte[] current = ReadBytes(targetAddress, patchBytes.Length);
            if (!BytesCompatibleWithOriginalOrPatch(current, originalEntry, patchBytes))
                throw new InvalidOperationException("受击入口在写入期间被其他程序修改。");
            byte[] original = new byte[patchBytes.Length];
            Array.Copy(originalEntry, original, original.Length);
            WriteBytes(targetAddress, original);
            if (!BytesEqual(ReadBytes(targetAddress, original.Length), original))
                throw new IOException("回滚受击入口后校验失败。");
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
