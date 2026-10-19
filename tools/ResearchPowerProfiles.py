"""Read-only native Power profile checks against installed x86 executables."""
import hashlib
from pathlib import Path
import capstone
import pefile

root = Path.home() / 'Desktop/game/东方'
specs = {
    'th10': (0x449d06, 0x426f70), 'th11': (0x456de5, 0x432cc0),
    'th12': (0x4625f5, 0x4385b0), 'th13': (0x470bfe, 0x444df0),
    'th14': (0x401382, 0x44f970), 'th15': (0x4015f2, 0x4567b0),
    'th16': (0x401567, 0x4440e0), 'th17': (0x4013ad, 0x449630),
    'th18': (0x4013ed, 0x45d5e0), 'th20': (0x412a65, 0x4faca0),
}
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = True
for name, (hook, rebuild) in specs.items():
    path = next(root.rglob(name + '.exe'))
    pe = pefile.PE(str(path)); base = pe.OPTIONAL_HEADER.ImageBase
    print(name, hashlib.sha256(path.read_bytes()).hexdigest())
    print('hook', hex(hook), pe.get_data(hook - base, 5).hex(' '),
          'rebuild', hex(rebuild), pe.get_data(rebuild - base, 16).hex(' '))
    for ins in md.disasm(pe.get_data(rebuild - base, 0x1600), rebuild):
        if ins.mnemonic.startswith('ret'):
            print(f'{ins.address:X} {ins.mnemonic} {ins.op_str}'); break
        if ins.mnemonic in ('mov', 'cmp', 'idiv') and (
            ins.mnemonic == 'idiv' or any(o.type == capstone.x86.X86_OP_MEM and
                o.mem.base and o.mem.disp >= 0x600 for o in ins.operands)):
            print(f'{ins.address:X} {ins.mnemonic} {ins.op_str}')
