"""Read-only disassembly aid for tracing the game's Power update paths.

Requires pefile and capstone; does not start or modify any game process.
"""

import argparse
import struct


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("exe")
    parser.add_argument("power_va", type=lambda value: int(value, 0))
    parser.add_argument("--context", type=int, default=5)
    args = parser.parse_args()

    import capstone
    import pefile

    pe = pefile.PE(args.exe)
    text_section = next(section for section in pe.sections if section.Name.startswith(b".text"))
    base = pe.OPTIONAL_HEADER.ImageBase + text_section.VirtualAddress
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    md.detail = True
    data = text_section.get_data()
    pattern = struct.pack("<I", args.power_va)
    seen = set()
    start = 0
    while True:
        offset = data.find(pattern, start)
        if offset < 0:
            break
        start = offset + 1
        for back in range(1, 9):
            first = max(0, offset - back)
            instructions = list(md.disasm(data[first:offset + 42], base + first))
            matching = [
                index for index, instruction in enumerate(instructions)
                if instruction.address <= base + offset < instruction.address + instruction.size
                and any(
                    operand.type == capstone.x86_const.X86_OP_MEM
                    and operand.mem.base == 0
                    and operand.mem.index == 0
                    and operand.mem.disp == args.power_va
                    for operand in instruction.operands
                )
            ]
            if not matching:
                continue
            index = matching[0]
            instruction = instructions[index]
            if instruction.address in seen:
                break
            seen.add(instruction.address)
            print("\n--- Power reference at %08X ---" % instruction.address)
            for nearby in instructions[max(0, index - args.context): index + args.context + 1]:
                print("%08X %-7s %s" % (nearby.address, nearby.mnemonic, nearby.op_str))
            break


if __name__ == "__main__":
    main()
