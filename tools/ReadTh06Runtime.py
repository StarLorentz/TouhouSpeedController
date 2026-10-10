"""Read-only probe for Steam TH06 patch bytes and player-state transitions.

Usage: python ReadTh06Runtime.py PID [--seconds 20]
Does not inject, patch, send input, or change game state.
"""
import argparse
import ctypes as c
from ctypes import wintypes as w
from collections import Counter
import json
from pathlib import Path
import time

parser = argparse.ArgumentParser()
parser.add_argument('pid', type=int)
parser.add_argument('--seconds', type=float, default=0)
args = parser.parse_args()
if not 0 <= args.seconds <= 60:
    parser.error('Sampling must be between 0 and 60 seconds')
kernel = c.WinDLL('kernel32', use_last_error=True)
psapi = c.WinDLL('psapi', use_last_error=True)
kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
kernel.OpenProcess.restype = w.HANDLE
kernel.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
kernel.ReadProcessMemory.restype = w.BOOL
kernel.CloseHandle.argtypes = [w.HANDLE]
psapi.EnumProcessModules.argtypes = [w.HANDLE, c.POINTER(w.HMODULE), w.DWORD, c.POINTER(w.DWORD)]
psapi.EnumProcessModules.restype = w.BOOL
psapi.GetModuleFileNameExW.argtypes = [w.HANDLE, w.HMODULE, w.LPWSTR, w.DWORD]
psapi.GetModuleFileNameExW.restype = w.DWORD
handle = kernel.OpenProcess(0x410, False, args.pid)
if not handle:
    raise c.WinError(c.get_last_error())
try:
    modules = (w.HMODULE * 1024)()
    needed = w.DWORD()
    if not psapi.EnumProcessModules(handle, modules, c.sizeof(modules), c.byref(needed)):
        raise c.WinError(c.get_last_error())
    filename = c.create_unicode_buffer(32768)
    if not psapi.GetModuleFileNameExW(handle, modules[0], filename, len(filename)):
        raise c.WinError(c.get_last_error())
    name = Path(filename.value).name.lower()
    profiles = {
        'th06c.exe': ([0x4c78e, 0x4c9d0], 0xbbfda8),
        'th06nc.exe': ([0x6c1b5, 0x6c3fa], 0x551888),
    }
    if name not in profiles:
        raise ValueError('Expected a Steam TH06 executable, got ' + name)
    sites, state_rva = profiles[name]
    base = modules[0]

    def read(rva, length):
        buffer = c.create_string_buffer(length)
        count = c.c_size_t()
        if not kernel.ReadProcessMemory(handle, base + rva, buffer, length, c.byref(count)) or count.value != length:
            raise c.WinError(c.get_last_error())
        return buffer.raw

    result = {'exe': filename.value, 'pid': args.pid, 'base': hex(base),
              'sites': {hex(rva): read(rva, 13).hex(' ') for rva in sites},
              'player_state': read(state_rva, 1)[0]}
    if args.seconds:
        counts = Counter()
        transitions = []
        previous = None
        start = time.monotonic()
        while time.monotonic() - start < args.seconds:
            state = read(state_rva, 1)[0]
            counts[state] += 1
            if state != previous:
                transitions.append({'seconds': round(time.monotonic() - start, 3), 'state': state})
                previous = state
            time.sleep(.02)
        result['sample_seconds'] = args.seconds
        result['state_counts'] = dict(counts)
        result['state_transitions'] = transitions
    print(json.dumps(result, ensure_ascii=False, indent=2))
finally:
    kernel.CloseHandle(handle)
