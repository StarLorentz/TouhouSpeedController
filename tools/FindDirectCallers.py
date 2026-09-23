"""List direct x86 calls to a VA in a local PE. Read-only research aid."""

import argparse


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("exe")
    parser.add_argument("target", type=lambda value: int(value, 0))
    args = parser.parse_args()

    import capstone
    import pefile

    pe = pefile.PE(args.exe)
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    md.skipdata = True
    for section in pe.sections:
        if not section.Name.startswith(b".text"):
            continue
        base = pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress
        for ins in md.disasm(section.get_data(), base):
            if ins.mnemonic == "call" and ins.op_str == hex(args.target):
                print("%08X call %08X" % (ins.address, args.target))


if __name__ == "__main__":
    main()
