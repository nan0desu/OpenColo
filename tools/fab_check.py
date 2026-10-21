"""Reference FAB decoder and MADSPACK checker.

Decodes every FAB entry of every MADSPACK file (.PIK/.SS/.FF) in a directory the way
VICEROY.EXE's pFABexp2 (10a5:0460) does, and checks the unpacked size and the consumed
input against the MADSPACK entry table. See docs/madspack.md.

Usage: python3 tools/fab_check.py <game directory>
"""

import glob
import os
import struct
import sys


def fab_unpack(src):
    """Unpack one FAB stream. Returns (data, bytes of src consumed)."""
    if src[:3] != b'FAB' or not 10 <= src[3] <= 13:
        raise ValueError('bad header')
    n = src[3]
    ofs_shift = 16 - n
    ofs_or = (0xff << (n - 8)) & 0xff
    len_mask = (1 << ofs_shift) - 1
    si = 4
    out = bytearray()

    ctrl = src[si] | src[si + 1] << 8
    si += 2
    count = 16

    def bit():
        # The 16th read of a control word returns its last bit and loads the next word.
        nonlocal ctrl, count, si
        count -= 1
        if count == 0:
            word = src[si] | src[si + 1] << 8
            si += 2
            count = 16
            ctrl = (word << 1) | (ctrl & 1)
        b = ctrl & 1
        ctrl >>= 1
        return b

    while True:
        if bit():
            out.append(src[si])
            si += 1
            continue
        if not bit():
            length = (bit() << 1 | bit()) + 2
            dist = 0x100 - src[si]
            si += 1
        else:
            lo, hi = src[si], src[si + 1]
            si += 2
            dist = 0x10000 - ((((hi >> ofs_shift) | ofs_or) << 8) | lo)
            length = hi & len_mask
            if length == 0:
                extra = src[si]
                si += 1
                if extra == 0:
                    return bytes(out), si
                if extra == 1:
                    continue  # normalize: nothing to do on flat memory
                length = extra + 1
            else:
                length += 2
        for _ in range(length):
            out.append(out[-dist])


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__.strip().splitlines()[-1])

    files = entries = ok = 0
    problems = []
    for path in sorted(glob.glob(os.path.join(sys.argv[1], '*'))):
        with open(path, 'rb') as f:
            data = f.read()
        if not data.startswith(b'MADSPACK'):
            continue
        name = os.path.basename(path)
        files += 1
        count = struct.unpack_from('<H', data, 14)[0]
        pos = 0xb0
        for i in range(count):
            kind, size, csize = struct.unpack_from('<HII', data, 16 + i * 10)
            chunk = data[pos:pos + csize]
            pos += csize
            if kind & 0xff != 1:
                continue
            entries += 1
            try:
                out, used = fab_unpack(chunk)
            except (ValueError, IndexError) as e:
                problems.append(f'{name}#{i}: {e}')
                continue
            if len(out) == size and used == csize:
                ok += 1
            else:
                problems.append(f'{name}#{i}: out {len(out)}/{size}, used {used}/{csize}')
        if pos != len(data):
            problems.append(f'{name}: entries end at {pos}, file is {len(data)}')

    print(f'files {files}, FAB entries {entries}, exact {ok}')
    for p in problems:
        print(p)
    print(f'problems: {len(problems)}')


if __name__ == '__main__':
    main()
