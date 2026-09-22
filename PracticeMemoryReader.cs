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

    internal static class PracticeMemoryReader
    {
        private const int ProcessVmRead = 0x0010;
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

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static PracticeSnapshot ReadTh16(Process process)
        {
            if (process == null) throw new ArgumentNullException("process");

            string processName = process.ProcessName.ToLowerInvariant();
            if (processName != "th16" && processName != "th16c")
                throw new NotSupportedException("当前只支持读取 TH16 日文版和本机汉化版。");

            string executablePath = process.MainModule.FileName;
            string sha256 = ComputeSha256(executablePath);
            string profileId;
            if (String.Equals(sha256, Th16JapaneseSha256, StringComparison.OrdinalIgnoreCase))
                profileId = "TH16 日文版（本机已验证版本）";
            else if (String.Equals(sha256, Th16ChineseSha256, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("已识别本机 TH16 汉化版，但它的内存布局与日文版不同；当前阶段保持只读禁用，等待单独适配。");
            else
                throw new NotSupportedException("TH16 EXE 哈希不在已验证列表中，已拒绝读取。SHA-256：" + sha256);

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

        private static int ReadInt32(IntPtr handle, long address)
        {
            byte[] buffer = new byte[4];
            IntPtr bytesRead;
            if (!ReadProcessMemory(handle, new IntPtr(address), buffer, buffer.Length, out bytesRead) || bytesRead.ToInt64() != buffer.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取游戏内存失败");
            return BitConverter.ToInt32(buffer, 0);
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
