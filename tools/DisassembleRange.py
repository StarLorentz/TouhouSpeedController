"""Disassemble a VA range from an x86 PE without starting the game."""

import argparse


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("exe")
    parser.add_argument("start", type=lambda value: int(value, 0))
    parser.add_argument("end", type=lambda value: int(value, 0))
    args = parser.parse_args()

    import capstone
    import pefile

    pe = pefile.PE(args.exe)
    base = pe.OPTIONAL_HEADER.ImageBase
    offset = pe.get_offset_from_rva(args.start - base)
    data = pe.__data__[offset:offset + args.end - args.start]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    for instruction in md.disasm(data, args.start):
        print("%08X %-9s %s" % (instruction.address, instruction.mnemonic, instruction.op_str))


if __name__ == "__main__":
    main()
