// 东方变速器 - TH16 read-only practice data preview
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// TH16 resource addresses were adapted from thprac/thprac/src/thprac/thprac_th16.cpp
// at commit 585fae1aad4b720f350655a44e3f2ec7fc0dfa25.
// thprac copyright (c) 2022 Ack, licensed under the MIT License.
// See THIRD_PARTY_NOTICES.md for the complete notice.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TouhouSpeedController
{
    internal sealed class PracticeSnapshot
    {
        public string ProfileId;
        public string Sha256;
        public int Lives;
        public int Bombs;
        public int BombFragments;
        public int Power;
    }

    internal static class PracticeMemoryAccess
    {
        private const int ProcessVmRead = 0x0010;
        private const int ProcessVmWrite = 0x0020;
        private const int ProcessVmOperation = 0x0008;
        private const int ProcessQueryLimitedInformation = 0x1000;

        // Addresses in thprac are based on the preferred image base 0x00400000.
        // Store RVAs so reading remains correct if Windows relocates the module.
        private const long Th16LivesRva = 0x000A57F4;
        private const long Th16BombsRva = 0x000A5800;
        private const long Th16BombFragmentsRva = 0x000A5804;
        private const long Th16PowerRva = 0x000A57E4;

        private const string Th16JapaneseSha256 = "C11776019F083978E66027E7394DAFB1FB9543AFCA986F28049A49417E341929";
        private const string Th16ChineseSha256 = "099BF0EDEEF663C1001C6782D7C6DE2AA22727526F314A98527C08DD99F06BC0";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static PracticeSnapshot ReadTh16(Process process)
        {
            string profileId;
            string sha256 = IdentifySupportedTh16(process, out profileId);

            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法以只读方式打开游戏进程");

            try
            {
                PracticeSnapshot snapshot = new PracticeSnapshot();
                snapshot.ProfileId = profileId;
                snapshot.Sha256 = sha256;
                snapshot.Lives = ReadInt32(handle, moduleBase + Th16LivesRva);
                snapshot.Bombs = ReadInt32(handle, moduleBase + Th16BombsRva);
                snapshot.BombFragments = ReadInt32(handle, moduleBase + Th16BombFragmentsRva);
                snapshot.Power = ReadInt32(handle, moduleBase + Th16PowerRva);
                Validate(snapshot);
                return snapshot;
            }
            finally { CloseHandle(handle); }
        }

        public static PracticeSnapshot WriteTh16(Process process, int? lives, int? bombs, int? bombFragments, int? power)
        {
            ValidateRequestedValues(lives, bombs, bombFragments, power);

            string profileId;
            string sha256 = IdentifySupportedTh16(process, out profileId);
            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(
                ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryLimitedInformation,
                false,
                process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开游戏进程进行资源修改");

            try
            {
                PracticeSnapshot before = ReadSnapshot(handle, moduleBase, profileId, sha256);
                Validate(before);
                if (before.Power < 100 || before.Power > 400)
                    throw new InvalidOperationException("请先进入实际关卡，再重新读取资源后应用数值。");

                try
                {
                    if (lives.HasValue) WriteInt32(handle, moduleBase + Th16LivesRva, lives.Value);
                    if (bombs.HasValue) WriteInt32(handle, moduleBase + Th16BombsRva, bombs.Value);
                    if (bombFragments.HasValue) WriteInt32(handle, moduleBase + Th16BombFragmentsRva, bombFragments.Value);
                    if (power.HasValue) WriteInt32(handle, moduleBase + Th16PowerRva, power.Value);
                }
                catch
                {
                    // Best-effort rollback if any individual write fails.
                    if (lives.HasValue) TryWriteInt32(handle, moduleBase + Th16LivesRva, before.Lives);
                    if (bombs.HasValue) TryWriteInt32(handle, moduleBase + Th16BombsRva, before.Bombs);
                    if (bombFragments.HasValue) TryWriteInt32(handle, moduleBase + Th16BombFragmentsRva, before.BombFragments);
                    if (power.HasValue) TryWriteInt32(handle, moduleBase + Th16PowerRva, before.Power);
                    throw;
                }

                PracticeSnapshot after = ReadSnapshot(handle, moduleBase, profileId, sha256);
                if ((lives.HasValue && after.Lives != lives.Value) ||
                    (bombs.HasValue && after.Bombs != bombs.Value) ||
                    (bombFragments.HasValue && after.BombFragments != bombFragments.Value) ||
                    (power.HasValue && after.Power != power.Value))
                    throw new IOException("写入后的回读校验不一致，游戏可能正在切换场景；请重新读取后再试。");
                return after;
            }
            finally { CloseHandle(handle); }
        }

        private static string IdentifySupportedTh16(Process process, out string profileId)
        {
            if (process == null) throw new ArgumentNullException("process");

            string processName = process.ProcessName.ToLowerInvariant();
            if (processName != "th16" && processName != "th16c")
                throw new NotSupportedException("当前练习功能只支持 TH16 日文版。");

            string sha256 = ComputeSha256(process.MainModule.FileName);
            if (String.Equals(sha256, Th16JapaneseSha256, StringComparison.OrdinalIgnoreCase))
                profileId = "TH16 日文版（本机已验证版本）";
            else if (String.Equals(sha256, Th16ChineseSha256, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("已识别本机 TH16 汉化版，但它的内存布局与日文版不同；当前阶段保持禁用，等待单独适配。");
            else
                throw new NotSupportedException("TH16 EXE 哈希不在已验证列表中，已拒绝访问。SHA-256：" + sha256);
            return sha256;
        }

        private static PracticeSnapshot ReadSnapshot(IntPtr handle, long moduleBase, string profileId, string sha256)
        {
            PracticeSnapshot snapshot = new PracticeSnapshot();
            snapshot.ProfileId = profileId;
            snapshot.Sha256 = sha256;
            snapshot.Lives = ReadInt32(handle, moduleBase + Th16LivesRva);
            snapshot.Bombs = ReadInt32(handle, moduleBase + Th16BombsRva);
            snapshot.BombFragments = ReadInt32(handle, moduleBase + Th16BombFragmentsRva);
            snapshot.Power = ReadInt32(handle, moduleBase + Th16PowerRva);
            return snapshot;
        }

        private static int ReadInt32(IntPtr handle, long address)
        {
            byte[] buffer = new byte[4];
            IntPtr bytesRead;
            if (!ReadProcessMemory(handle, new IntPtr(address), buffer, buffer.Length, out bytesRead) || bytesRead.ToInt64() != buffer.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取游戏内存失败");
            return BitConverter.ToInt32(buffer, 0);
        }

        private static void WriteInt32(IntPtr handle, long address, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            IntPtr bytesWritten;
            if (!WriteProcessMemory(handle, new IntPtr(address), buffer, buffer.Length, out bytesWritten) || bytesWritten.ToInt64() != buffer.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "写入游戏内存失败");
        }

        private static void TryWriteInt32(IntPtr handle, long address, int value)
        {
            try { WriteInt32(handle, address, value); } catch { }
        }

        private static void ValidateRequestedValues(int? lives, int? bombs, int? bombFragments, int? power)
        {
            if (!lives.HasValue && !bombs.HasValue && !bombFragments.HasValue && !power.HasValue)
                throw new ArgumentException("至少要选择一项资源进行修改。");
            if (lives.HasValue && (lives.Value < 0 || lives.Value > 8)) throw new ArgumentOutOfRangeException("lives", "残机必须在 0–8 之间。");
            if (bombs.HasValue && (bombs.Value < 0 || bombs.Value > 8)) throw new ArgumentOutOfRangeException("bombs", "Bomb 必须在 0–8 之间。");
            if (bombFragments.HasValue && (bombFragments.Value < 0 || bombFragments.Value > 4)) throw new ArgumentOutOfRangeException("bombFragments", "Bomb 碎片必须在 0–4 之间。");
            if (power.HasValue && (power.Value < 100 || power.Value > 400)) throw new ArgumentOutOfRangeException("power", "Power 必须在 1.00–4.00 之间。");
        }

        private static void Validate(PracticeSnapshot snapshot)
        {
            // Broad bounds also allow title-screen/default values while rejecting
            // obviously wrong addresses. The UI remains read-only in this stage.
            if (snapshot.Lives < 0 || snapshot.Lives > 99 ||
                snapshot.Bombs < 0 || snapshot.Bombs > 99 ||
                snapshot.BombFragments < 0 || snapshot.BombFragments > 99 ||
                snapshot.Power < 0 || snapshot.Power > 1000)
                throw new InvalidDataException("读取到的资源数值超出合理范围，已停止显示；该版本可能需要单独适配。");
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty);
            }
        }
    }
}
