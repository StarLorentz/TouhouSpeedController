// 东方变速器 - version-gated practice resource access
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.
//
// Resource addresses were adapted from thprac/src/thprac/thprac_th08.cpp,
// thprac_th09.cpp, thprac_th12.cpp, thprac_th13.cpp, thprac_th14.cpp,
// thprac_th15.cpp, thprac_th16.cpp, thprac_th17.cpp, thprac_th18.cpp,
// and thprac_th20.cpp
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
        public string GameCode;
        public bool InGame;
    }

    internal sealed class PracticeOptions
    {
        public int LivesMax = 8, BombsMax = 8, FragmentsMax = 4;
        public int PowerMin = 100, PowerMax = 400;
        public bool HasBombs = true, HasFragments = true, IntegerPower;
        public bool MemoryPowerLock, InfiniteResources;
        public string LivesLabel = "残机", PowerLabel = "Power";
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

        private const long Th15LivesRva = 0x000E7450;
        private const long Th15BombsRva = 0x000E745C;
        private const long Th15BombFragmentsRva = 0x000E7460;
        private const long Th15PowerRva = 0x000E7440;

        private const string Th16JapaneseSha256 = "C11776019F083978E66027E7394DAFB1FB9543AFCA986F28049A49417E341929";
        private const string Th16ChineseSha256 = "099BF0EDEEF663C1001C6782D7C6DE2AA22727526F314A98527C08DD99F06BC0";
        private const string Th15JapaneseSha256 = "67A642357C8777089F468AAB9C7A0AE346EBDB62849D842A7B7B18D1E6910364";

        private sealed class DirectProfile
        {
            public string GameCode;
            public string Sha256;
            public string ProcessName;
            public long LivesRva;
            public long BombsRva;
            public long BombFragmentsRva;
            public long PowerRva;
            public long ResourcePointerRva, PlayerPointerRva;
            public bool FloatResources, FloatPower;
            public int FragmentMultiplier = 1;
        }

        private static readonly DirectProfile[] DirectProfiles = {
            new DirectProfile { GameCode="TH08", Sha256="330FBDBF58A710829D65277B4F312CFBB38D5448B3DF523E79350B879213D924",
                ResourcePointerRva=0x120F510, FloatResources=true, LivesRva=0x74, BombsRva=0x80, PowerRva=0x98 },
            // TH09 is P1-only: health is in half-heart units, charge is a float 0..400.
            new DirectProfile { GameCode="TH09", Sha256="10350095BCF95EDB59E03BEE9849A2DC8A7714B4927AD5909C569C550FCE6822",
                ResourcePointerRva=0xA7D94, LivesRva=0xA8, PowerRva=0x30388, FloatPower=true },
            new DirectProfile { GameCode="TH12", Sha256="D8D644D2E64957A3031B1A1399D0502E1DDAA5252D2C4E492770AD6717827628",
                LivesRva=0xB0C98, BombsRva=0xB0CA0, BombFragmentsRva=0xB0CA4, PowerRva=0xB0C48,
                PlayerPointerRva=0xB4514, FragmentMultiplier=2 },
            new DirectProfile { GameCode="TH12", ProcessName="th12c", Sha256="15AECA37D7D4EC8089C5227B0871CABA0B0D908B1C1D23365E6713EA7F0BD403",
                LivesRva=0xB0C98, BombsRva=0xB0CA0, BombFragmentsRva=0xB0CA4, PowerRva=0xB0C48,
                PlayerPointerRva=0xB4514, FragmentMultiplier=2 },
            new DirectProfile { GameCode="TH13", Sha256="422F40AA176C9AAB7D0178CE6033802B4143921A5E0E463EF99259EF6D100A58",
                LivesRva=0xBE7F4, BombsRva=0xBE800, BombFragmentsRva=0xBE804, PowerRva=0xBE7E8, PlayerPointerRva=0xC22C4 },
            new DirectProfile { GameCode="TH13", ProcessName="th13c", Sha256="29028D0D74EF5D518A6A74F7D68766F57C6CDCBBAF428F3190E4C2779F45F91C",
                LivesRva=0xBE7F4, BombsRva=0xBE800, BombFragmentsRva=0xBE804, PowerRva=0xBE7E8, PlayerPointerRva=0xC22C4 },
            new DirectProfile { GameCode = "TH14", Sha256 = "F08E885E28D2E0FC8C579E88034DD7DE8F2A648D8F08CE781CF08EADC9FFE353",
                LivesRva = 0xF5864, BombsRva = 0xF5870, BombFragmentsRva = 0xF5874, PowerRva = 0xF5858 },
            new DirectProfile { GameCode = "TH15", Sha256 = Th15JapaneseSha256,
                LivesRva = Th15LivesRva, BombsRva = Th15BombsRva,
                BombFragmentsRva = Th15BombFragmentsRva, PowerRva = Th15PowerRva },
            // TH17: GlobalsReplay starts at 0x4B59DC; offsets follow thprac_th17.cpp.
            new DirectProfile { GameCode = "TH17", Sha256 = "85CB011E2E43B316C8FA42A3C402565ECDF2F4379F3BAB66D86BCB7BEEF8255F",
                LivesRva = 0xB5A40, BombsRva = 0xB5A4C,
                BombFragmentsRva = 0xB5A50, PowerRva = 0xB5A30 },
            new DirectProfile { GameCode = "TH18", Sha256 = "5AEB74B19939A29A8CF06E4FE7777AAF9139E93478507E039F43318171020E9F",
                LivesRva = 0xCCD48, BombsRva = 0xCCD58, BombFragmentsRva = 0xCCD5C, PowerRva = 0xCCD38 },
            // TH20: game side 0 is at RVA 0x1BA568; GlobalsSide begins at +0x88.
            new DirectProfile { GameCode = "TH20", Sha256 = "E6C4371E214C95DD4DBCAAF6818C79C5E89881270F65B62B8C3C965B65851E2D",
                LivesRva = 0x1BA6A8, BombsRva = 0x1BA6BC,
                BombFragmentsRva = 0x1BA6C0, PowerRva = 0x1BA620 }
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private static DirectProfile IdentifyDirectProfile(Process process, out string sha256)
        {
            if (process == null) throw new ArgumentNullException("process");
            string name = process.ProcessName.ToLowerInvariant();
            if (!SupportsDirectName(name))
                throw new NotSupportedException("当前进程尚未接入基础资源修改。");
            sha256 = ComputeSha256(process.MainModule.FileName);
            foreach (DirectProfile profile in DirectProfiles)
                if (name == (profile.ProcessName ?? profile.GameCode.ToLowerInvariant()) &&
                    String.Equals(sha256, profile.Sha256, StringComparison.OrdinalIgnoreCase))
                    return profile;
            throw new NotSupportedException("这个游戏 EXE 的资源位置尚未验证，已拒绝访问。SHA-256：" + sha256);
        }

        public static bool SupportsDirectName(string name)
        {
            foreach (DirectProfile p in DirectProfiles)
                if (String.Equals(name,p.ProcessName ?? p.GameCode,StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string CanonicalName(string name)
        {
            name = name.ToLowerInvariant();
            return name=="th12c" ? "th12" : name=="th13c" ? "th13" : name;
        }

        public static PracticeOptions Options(string name)
        {
            name=CanonicalName(name); PracticeOptions o=new PracticeOptions();
            if(name=="th08") { o.HasFragments=false; o.PowerMin=0; o.PowerMax=12800; o.IntegerPower=true; o.MemoryPowerLock=true; o.InfiniteResources=true; }
            if(name=="th09") { o.LivesMax=10; o.LivesLabel="生命"; o.HasBombs=false; o.HasFragments=false; o.PowerMin=0; o.PowerLabel="蓄力"; o.MemoryPowerLock=true; o.InfiniteResources=true; }
            if(name=="th12") { o.FragmentsMax=2; o.InfiniteResources=true; }
            if(name=="th13") { o.FragmentsMax=7; o.InfiniteResources=true; }
            if(name=="th16") o.InfiniteResources=true;
            return o;
        }

        internal static void VerifySupportedDirectGame(Process process, string gameCode)
        {
            string sha256;
            DirectProfile profile = IdentifyDirectProfile(process, out sha256);
            if (profile.GameCode != gameCode)
                throw new NotSupportedException(gameCode + " 配置不能用于其他作品。");
        }

        private static PracticeSnapshot ReadDirectSnapshot(IntPtr handle, long moduleBase, DirectProfile profile, string sha256)
        {
            PracticeOptions options=Options(profile.GameCode);
            long data=ResolveData(handle,moduleBase,profile);
            PracticeSnapshot snapshot = new PracticeSnapshot();
            snapshot.ProfileId = profile.GameCode + (profile.ProcessName==null ? " 日文版" : " 汉化版") + "（本机已识别版本）";
            snapshot.GameCode = profile.GameCode;
            snapshot.Sha256 = sha256;
            snapshot.Lives = ReadResource(handle,data+profile.LivesRva,profile.FloatResources,1);
            snapshot.Bombs = options.HasBombs ? ReadResource(handle,data+profile.BombsRva,profile.FloatResources,1) : 0;
            snapshot.BombFragments = options.HasFragments ? ReadInt32(handle,data+profile.BombFragmentsRva)/profile.FragmentMultiplier : 0;
            snapshot.Power = ReadResource(handle,data+profile.PowerRva,profile.FloatResources||profile.FloatPower,profile.FloatResources ? 100 : 1);
            if(snapshot.Lives < -1 || snapshot.Lives>99 || snapshot.Bombs<0 || snapshot.Bombs>99 ||
                snapshot.BombFragments<0 || snapshot.BombFragments>99 || snapshot.Power<0 || snapshot.Power>options.PowerMax)
                throw new InvalidDataException("资源读数超出本作范围，请进入关卡后重试。");
            snapshot.InGame=profile.ResourcePointerRva!=0 || profile.PlayerPointerRva!=0
                ? (profile.GameCode=="TH08" ? ReadInt32(handle,moduleBase+0x13CE8B0)==2
                   : profile.GameCode=="TH09" ? ReadInt32(handle,data+8)==0
                   : ReadInt32(handle,moduleBase+profile.PlayerPointerRva)!=0)
                : snapshot.Power>=100 && snapshot.Power<=400;
            return snapshot;
        }

        private static long ResolveData(IntPtr handle,long moduleBase,DirectProfile profile)
        {
            if(profile.ResourcePointerRva==0) return moduleBase;
            uint p=(uint)ReadInt32(handle,moduleBase+profile.ResourcePointerRva);
            if(p<0x10000) throw new InvalidOperationException("尚未创建玩家数据，请先进入关卡。");
            return p;
        }

        private static int ReadResource(IntPtr h,long address,bool floating,int scale)
        {
            int raw=ReadInt32(h,address);
            if(!floating) return raw;
            float value=BitConverter.ToSingle(BitConverter.GetBytes(raw),0);
            if(Single.IsNaN(value)||Single.IsInfinity(value)||value < -1 || value>1000) throw new InvalidDataException("浮点资源读数无效。");
            return (int)Math.Round(value*scale,MidpointRounding.AwayFromZero);
        }
        private static void WriteResource(IntPtr h,long address,int value,bool floating,int scale)
        { WriteInt32(h,address,floating ? BitConverter.ToInt32(BitConverter.GetBytes((float)value/scale),0) : value); }

        public static PracticeSnapshot ReadDirect(Process process)
        {
            string sha256;
            DirectProfile profile = IdentifyDirectProfile(process, out sha256);
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取游戏资源");
            try { return ReadDirectSnapshot(handle, process.MainModule.BaseAddress.ToInt64(), profile, sha256); }
            finally { CloseHandle(handle); }
        }

        public static PracticeSnapshot WriteDirect(Process process, int? lives, int? bombs, int? bombFragments, int? power)
        {
            string sha256;
            DirectProfile profile = IdentifyDirectProfile(process, out sha256);
            PracticeOptions options=Options(profile.GameCode);
            if(!lives.HasValue && !bombs.HasValue && !bombFragments.HasValue && !power.HasValue) throw new ArgumentException("至少选择一项资源。");
            if(lives.HasValue && (lives<0 || lives>options.LivesMax)) throw new ArgumentOutOfRangeException("lives");
            if(bombs.HasValue && (!options.HasBombs || bombs<0 || bombs>options.BombsMax)) throw new ArgumentOutOfRangeException("bombs");
            if(bombFragments.HasValue && (!options.HasFragments || bombFragments<0 || bombFragments>options.FragmentsMax)) throw new ArgumentOutOfRangeException("bombFragments");
            if(power.HasValue && (power<options.PowerMin || power>options.PowerMax || (options.IntegerPower && power.Value%100!=0))) throw new ArgumentOutOfRangeException("power");
            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryLimitedInformation,
                false, process.Id);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开游戏资源");
            try
            {
                PracticeSnapshot before = ReadDirectSnapshot(handle, moduleBase, profile, sha256);
                if (!before.InGame)
                    throw new InvalidOperationException("请进入实际关卡并重新读取后，再应用数值。");
                long data=ResolveData(handle,moduleBase,profile);
                // Keep the exact native DWORDs, including float fractions and TH12 half-fragments, for rollback.
                int oldLives=ReadInt32(handle,data+profile.LivesRva);
                int oldBombs=options.HasBombs ? ReadInt32(handle,data+profile.BombsRva) : 0;
                int oldFragments=options.HasFragments ? ReadInt32(handle,data+profile.BombFragmentsRva) : 0;
                int oldPower=ReadInt32(handle,data+profile.PowerRva);
                try
                {
                    if (lives.HasValue) WriteResource(handle,data+profile.LivesRva,lives.Value,profile.FloatResources,1);
                    if (bombs.HasValue) WriteResource(handle,data+profile.BombsRva,bombs.Value,profile.FloatResources,1);
                    if (bombFragments.HasValue) WriteInt32(handle,data+profile.BombFragmentsRva,bombFragments.Value*profile.FragmentMultiplier);
                    if (power.HasValue) WriteResource(handle,data+profile.PowerRva,power.Value,profile.FloatResources||profile.FloatPower,profile.FloatResources ? 100 : 1);
                    PracticeSnapshot after = ReadDirectSnapshot(handle, moduleBase, profile, sha256);
                    if ((lives.HasValue && after.Lives != lives.Value) ||
                        (bombs.HasValue && after.Bombs != bombs.Value) ||
                        (bombFragments.HasValue && after.BombFragments != bombFragments.Value) ||
                        (power.HasValue && after.Power != power.Value))
                        throw new IOException("写入后回读不一致，请重新读取。");
                    return after;
                }
                catch
                {
                    // Do not roll back into a replaced player allocation after a scene switch.
                    if(ResolveData(handle,moduleBase,profile)==data) {
                        if (lives.HasValue) TryWriteInt32(handle,data+profile.LivesRva,oldLives);
                        if (bombs.HasValue) TryWriteInt32(handle,data+profile.BombsRva,oldBombs);
                        if (bombFragments.HasValue) TryWriteInt32(handle,data+profile.BombFragmentsRva,oldFragments);
                        if (power.HasValue) TryWriteInt32(handle,data+profile.PowerRva,oldPower);
                    }
                    throw;
                }
            }
            finally { CloseHandle(handle); }
        }

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

        internal static void VerifySupportedTh16(Process process)
        {
            string profileId;
            IdentifySupportedTh16(process, out profileId);
        }

        private static string IdentifySupportedTh16(Process process, out string profileId)
        {
            if (process == null) throw new ArgumentNullException("process");

            string processName = process.ProcessName.ToLowerInvariant();
            if (processName != "th16" && processName != "th16c")
                throw new NotSupportedException("当前游戏或 EXE 版本尚未接入练习功能。");

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
