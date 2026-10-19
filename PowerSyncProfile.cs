// Exact-executable Power synchronization layouts. GPL-3.0-or-later.
// See docs/POWER_ATTACK_REFRESH_RESEARCH.md and docs/BATCH_POWER_SYNC.md.
using System;
using System.Diagnostics;

namespace TouhouSpeedController
{
    internal sealed class PowerSyncProfile
    {
        public string Name;
        public uint HookRva, RebuildRva, PlayerRva, PowerRva, DivisorRva, StateOffset, ThisOffset;
        public uint[] CacheOffsets;
        public int? Argument;
        // 0=thiscall, 1=player stack argument (callee pops), 2=player in EDI, 3=player in EBX.
        public int CallMode;
        public bool HitFilterOnly;
        public bool ScaledUnits;
        public int PowerMin=100, PowerMax=400, DivisorMin=100, DivisorMax=400, ConstantDivisor;
        public uint MaximumRva;
        public byte[] Original = { 0x5F, 0x8B, 0xC6, 0x5E, 0x5B };
        public byte[] RebuildEntry;
        public uint OwnerOffset, OwnerRva;
        public string GameCode { get { return Name.ToUpperInvariant(); } }

        private static byte[] Entry(byte locals)
        { return new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, locals }; }

        internal static readonly PowerSyncProfile[] All = {
            new PowerSyncProfile { Name="th10", HookRva=0x49D06, RebuildRva=0x26F70, PlayerRva=0x77834,
                PowerRva=0x74C48, StateOffset=0x458, CallMode=1, ScaledUnits=true, ConstantDivisor=20,
                PowerMin=0, PowerMax=100, DivisorMin=20, DivisorMax=20, CacheOffsets=new uint[]{0x3500},
                Original=new byte[]{0x8B,0x44,0x24,0x04,0x5B}, RebuildEntry=new byte[]{0x83,0xEC,0x38,0x66,0x8B,0x0D,0x48,0x4C,0x47,0x00} },
            new PowerSyncProfile { Name="th11", HookRva=0x56DE5, RebuildRva=0x32CC0, PlayerRva=0xA8EB4,
                PowerRva=0xA56E8, DivisorRva=0xA574C, StateOffset=0x928, CallMode=3, ScaledUnits=true, MaximumRva=0xA5748,
                PowerMin=0, PowerMax=96, DivisorMin=12, DivisorMax=20, CacheOffsets=new uint[]{0x7C90},
                Original=new byte[]{0x8B,0x04,0x24,0x83,0xC4,0x08}, RebuildEntry=Entry(0x40) },
            new PowerSyncProfile { Name="th12", HookRva=0x625F5, RebuildRva=0x385B0, PlayerRva=0xB4514,
                PowerRva=0xB0C48, DivisorRva=0xB0CD4, StateOffset=0xA28, ThisOffset=0, CallMode=1,
                CacheOffsets=new uint[]{0xC41C}, Original=new byte[]{0x8B,0x04,0x24,0x83,0xC4,0x08},
                RebuildEntry=new byte[]{0x83,0xEC,0x38,0x8B,0x0D,0x90,0x0C,0x4B,0x00} },
            new PowerSyncProfile { Name="th13", HookRva=0x70BFE, RebuildRva=0x44DF0, PlayerRva=0xC22C4,
                PowerRva=0xBE7E8, DivisorRva=0xBE7F0, StateOffset=0x65C, ThisOffset=0, CallMode=2,
                CacheOffsets=new uint[]{0x146C4}, Original=new byte[]{0x8B,0x45,0xFC,0x8B,0xE5},
                RebuildEntry=new byte[]{0x55,0x8B,0xEC,0x83,0xEC,0x50} },
            new PowerSyncProfile { Name="th14", HookRva=0x1382, RebuildRva=0x4F970, PlayerRva=0xDB67C,
                PowerRva=0xF5858, DivisorRva=0xF5860, StateOffset=0x684, ThisOffset=0,
                CacheOffsets=new uint[]{0x18334}, RebuildEntry=Entry(0x44) },
            new PowerSyncProfile { Name="th15", HookRva=0x15F2, RebuildRva=0x567B0, PlayerRva=0xE9BB8,
                PowerRva=0xE7440, DivisorRva=0xE7448, StateOffset=0x16220, ThisOffset=0,
                CacheOffsets=new uint[]{0x162E0,0x1626C}, RebuildEntry=Entry(0x54) },
            new PowerSyncProfile { Name="th16", HookRva=0x1567, RebuildRva=0x440E0, PlayerRva=0xA6EF8,
                PowerRva=0xA57E4, DivisorRva=0xA57EC, StateOffset=0x165A8, ThisOffset=0x610,
                CacheOffsets=new uint[]{0x16698,0x165F4}, RebuildEntry=Entry(0x30) },
            new PowerSyncProfile { Name="th17", HookRva=0x13AD, RebuildRva=0x49630, PlayerRva=0xB77D0,
                PowerRva=0xB5A30, DivisorRva=0xB5A38, StateOffset=0x18DB0, ThisOffset=0x610,
                CacheOffsets=new uint[]{0x18ED8,0x18DFC}, RebuildEntry=Entry(0x2C) },
            new PowerSyncProfile { Name="th18", HookRva=0x13ED, RebuildRva=0x5D5E0, PlayerRva=0xCF410,
                PowerRva=0xCCD38, DivisorRva=0xCCD40, StateOffset=0x476AC, ThisOffset=0x620,
                CacheOffsets=new uint[]{0x477FC,0x476F8}, Argument=0, RebuildEntry=Entry(0x34) },
            new PowerSyncProfile { Name="th20", HookRva=0x12A65, RebuildRva=0xFACA0, PlayerRva=0x1BA56C,
                PowerRva=0x1BA620, DivisorRva=0x1BA628, StateOffset=0x10, ThisOffset=0,
                CacheOffsets=new uint[]{0x680}, Argument=-1,
                Original=new byte[]{0x8B,0x4D,0xF0,0x33,0xCD},
                RebuildEntry=new byte[]{0x53,0x8B,0xDC,0x83,0xEC,0x08,0x83,0xE4,0xF8,0x83,0xC4,0x04,0x55,0x8B,0x6B,0x04},
                OwnerOffset=0x14858, OwnerRva=0x1BA568 }
        };

        // thprac's P1-filtered skip at the sole native hit call, preserving P2.
        internal static readonly PowerSyncProfile Th09Hit = new PowerSyncProfile {
            Name="th09", HookRva=0x1E8EC, RebuildRva=0x1E420, HitFilterOnly=true,
            Original=new byte[]{0xE8,0x2F,0xFB,0xFF,0xFF}, RebuildEntry=new byte[]{0x55,0x8B,0xEC,0x51,0x56,0x8B,0xF1}
        };

        public static bool Supports(string name)
        { name=PracticeMemoryAccess.CanonicalName(name); foreach (PowerSyncProfile p in All) if (String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)) return true; return false; }
        public static PowerSyncProfile Get(string name)
        {
            name=PracticeMemoryAccess.CanonicalName(name);
            foreach (PowerSyncProfile p in All) if (String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)) return p;
            throw new NotSupportedException("此游戏尚未接入 Power 同步和锁定。");
        }
        public void Verify(Process game)
        {
            if (!String.Equals(PracticeMemoryAccess.CanonicalName(game.ProcessName),Name,StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Power 配置不能用于其他作品。");
            if (Name=="th16") PracticeMemoryAccess.VerifySupportedTh16(game);
            else PracticeMemoryAccess.VerifySupportedDirectGame(game,GameCode);
        }
    }
}
