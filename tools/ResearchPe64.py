"""Read-only x64 PE research: function ranges, RIP references and direct callers.

Requires pefile and capstone in the analysis Python environment, not in the app.
Addresses on the command line are preferred-base virtual addresses.
"""
import argparse
import bisect
import hashlib
import struct

import capstone
import pefile


parser = argparse.ArgumentParser()
parser.add_argument("exe")
parser.add_argument("mode", choices=["sound", "range", "refs", "callers", "field"])
parser.add_argument("addresses", nargs="*", type=lambda s: int(s, 0))
parser.add_argument("--radius", type=lambda s: int(s, 0), default=0)
args = parser.parse_args()
pe = pefile.PE(args.exe)
base = pe.OPTIONAL_HEADER.ImageBase
assert pe.FILE_HEADER.Machine == 0x8664, "This tool requires an x64 PE"
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
functions = sorted((base + e.struct.BeginAddress, base + e.struct.EndAddress)
                   for e in pe.DIRECTORY_ENTRY_EXCEPTION)
starts = [a for a, b in functions]


def owner(address):
    index = bisect.bisect_right(starts, address) - 1
    return functions[index][0] if index >= 0 and address < functions[index][1] else 0


def instructions(start, end):
    return md.disasm(pe.get_data(start - base, end - start), start)


def dump(start, end):
    for ins in instructions(start, end):
        print(f"{ins.address:X} {ins.bytes.hex(' '):24} {ins.mnemonic:8} {ins.op_str}")


print(args.exe, "sha256", hashlib.sha256(pe.__data__).hexdigest())
if args.mode == "range":
    start = args.addresses[0]
    if len(args.addresses) == 2:
        end = args.addresses[1]
    else:
        start = owner(start)
        end = functions[bisect.bisect_left(starts, start)][1]
    dump(start, end)
elif args.mode in ("refs", "callers", "sound", "field"):
    targets = list(args.addresses)
    if args.mode == "sound":
        needle = b"pldead00.wav"
        offset = pe.__data__.find(needle)
        start_offset = pe.__data__.rfind(b"\0", 0, offset) + 1
        string_va = base + pe.get_rva_from_offset(start_offset)
        targets.append(string_va)
        print("death sound string", hex(string_va))
        for section in pe.sections:
            data = section.get_data()
            ptr = struct.pack("<Q", string_va)
            pos = 0
            while (pos := data.find(ptr, pos)) >= 0:
                va = base + section.VirtualAddress + pos
                targets.append(va)
                print("string pointer", hex(va), "section", section.Name)
                for delta in range(-64, 65, 8):
                    p = pos + delta
                    if p >= 0:
                        value = struct.unpack_from("<Q", data, p)[0]
                        text = pe.get_data(value - base, 100).split(b"\0")[0] if base <= value < base + pe.OPTIONAL_HEADER.SizeOfImage else b""
                        print(f"  {va + delta:X}: {value:X} {text!r}")
                pos += 8
    for start, end in functions:
        for ins in instructions(start, end):
            matches = []
            for op in ins.operands:
                if args.mode == "field" and op.type == capstone.x86.X86_OP_MEM and op.mem.disp in targets:
                    matches.append(op.mem.disp)
                if op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
                    target = ins.address + ins.size + op.mem.disp
                    if any(abs(target - t) <= (128 if args.mode == "sound" else args.radius) for t in targets):
                        matches.append(target)
                if op.type == capstone.x86.X86_OP_IMM and op.imm in targets:
                    matches.append(op.imm)
            if matches and (args.mode != "callers" or ins.mnemonic == "call"):
                print(f"function {start:X}, {ins.address:X} {ins.mnemonic} {ins.op_str} -> {','.join(hex(t) for t in matches)}")
