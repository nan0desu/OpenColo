# MADSPACK and FAB decompression

How VICEROY.EXE reads its `.PIK`, `.SS` and `.FF` files. Names and address
conventions are described in [memory.md](memory.md).

## File format

All 246 `.PIK` (pictures), `.SS` (sprite sets) and `.FF` files are MADSPACK 2.0
containers. This is the archive format of MicroProse's MADS engine (Rex Nebular,
Dragonsphere); ScummVM's MADS engine reads the same format.

| offset | size | contents |
| --- | --- | --- |
| `0x00` | 12 | `"MADSPACK 2.0"` |
| `0x0c` | 2 | `1a 00` |
| `0x0e` | u16 | number of entries (up to 16) |
| `0x10` | 16 x 10 | entry table |
| `0xb0` | ... | entry data, back to back, each `compressed size` bytes long |

Each entry in the table:

| offset | size | contents |
| --- | --- | --- |
| `+0` | u16 | type. The low byte is the compression: 0 stored, 1 FAB. The high byte varies (`0xc3` in `.PIK`, `0x04` in `.SS`). |
| `+2` | u32 | unpacked size |
| `+6` | u32 | size in the file |

A `.PIK`, for example, holds three entries: 8 bytes of header, 64000 bytes of
pixels, and a 768-byte palette. In the shipped files, 793 entries use FAB and 140
are stored.

Four files (`DECLARAT.PIK`, `OPENBORD.PIK`, `OPENING.PIK`, `OPENMENU.PIK`) have
39-42 bytes after the last entry. The bytes look like the end of older compressed
data, as if the file had been overwritten by a shorter version without being
truncated. The loader only reads what the entry table describes, so it never sees them.

## FAB compression

FAB is MicroProse's LZ77 compressor from the "MPS Labs Graphic Library"
(`pFABcomp.asm` by David McKibbin, 1992, plus `pFABexp0/1/2.asm`). ScummVM ports
those sources in `engines/mads/core/pfab.cpp`, and VICEROY's segment `10a5` is
the assembled original of the three unpackers:

| ScummVM | VICEROY | work buffer, ScummVM comment | VICEROY |
| --- | --- | --- | --- |
| `pFABexp0(read, write, work)`, stream to stream | `10a5:0006` | "~14364" | `0x382c` = 14380 |
| `pFABexp1(read, write_buf, work)`, stream to memory | `10a5:0254` | "~2072" | `0x820` = 2080 |
| `pFABexp2(read_buf, write_buf, work)`, memory to memory | `10a5:0460` | 4 | 4 |
| `pFABcomp(read, write, work, type, dsize)` | not linked (`26d4` is null) | - | - |

### Stream format

A stream is `"FAB"`, a window size byte `n` (10-13; every shipped stream uses
12, a 4096-byte window), then packets. Each packet is a 16-bit little-endian
control word followed by data bytes. Control bits are read LSB first:

| bits | meaning |
| --- | --- |
| `1` | literal: copy one data byte |
| `0 0 b a` | short copy: length `(b<<1 \| a) + 2` (2-5), distance `256 - byte` (1-255) |
| `0 1` | long copy: two data bytes `lo`, `hi`. Offset = `((hi >> (16-n)) \| (0xff << (n-8))) << 8 \| lo`, a negative 16-bit number. Length = `hi & ((1 << (16-n)) - 1)`, plus 2. |
| `0 1`, length field 0 | one more data byte `x`: 0 = end of stream, 1 = normalize (no-op on flat memory), otherwise length = `x + 1` |

Copies go byte by byte, so they may overlap the bytes they produce. The bit
reader keeps a count of 16. The 16th read of a word returns that word's last bit
and loads the next word at the same time (`shr bp,1; mov bp,ax; rcl bp,1; rcr bp,1`).
The control words therefore don't fall on simple 16-bit boundaries of the bit
stream. ScummVM's `get_ctrl_bit` reproduces this.

At the end, the unpacker stores the 32-bit output length in `work[0..3]` and
returns 0. A missing `"FAB"` or a window byte outside 10-13 returns `0x16`.

### Checked against VICEROY

`pFABexp2` (`10a5:0460..05ba`) was read instruction by instruction against
`fab_explode` in ScummVM. Bit reader, literal, short copy, long copy, the
extra-length byte and the end marker are all the same. There are three
differences, none of which matters for the shipped data:

- Normalize: the original moves `es:di` and `ds:si` forward by paragraphs so that
  4096 bytes of look-behind stay inside the segment (`di = (di & 0xf) + 0x1000`,
  `es += (di >> 4) - 0x100`). On flat memory there is nothing to do, and ScummVM
  just continues.
- Header errors: the original returns `0x16`; ScummVM returns 0 bytes without an error.
- An extra length byte of 255 would overflow ScummVM's `byte copyLen`
  (255 + 1 = 0). The packer never produces more than 252 (`ZIV - 1`).

A Python decoder that follows the original exactly
([tools/fab_check.py](../tools/fab_check.py)) was run over all 246 MADSPACK files. All 793 FAB entries unpack to exactly the size in the entry table, and
every stream ends with the end marker on the last byte of its entry.

## Loading code

The loader is overlay 28. `F28_002b_0000` reads one entry. The game keeps the
entry table in a structure in memory, with the first entry at `+0x2a`. For the
entry it is loading:

1. It stores the type byte in `DS:26ca` and reads the size in the file from `+0x30`.
2. A stored entry (type 0) is read directly.
3. A FAB entry goes through `F28_0046_004a`. That function picks a decompressor by
   method and by where the input and output are, sets the size of the work buffer
   it needs, allocates that buffer under the name `"$pack$"`, and calls the
   decompressor through a table of far pointers.

## The codec table at DS:26c9..26e2

`F0_2085_0004`, called near the start of `main` (see
[startup.md](startup.md#main)), fills this table:

| address | value set | used for | work buffer |
| --- | --- | --- | --- |
| `26c6` | 0 | `type` argument of `pFABcomp` (`CMP_BINARY`) | - |
| `26c9` (byte) | `0xff` | written once, never read | - |
| `26ca` | 1 | type of the entry being loaded (1 = FAB) | - |
| `26cc` (far ptr) | null | packer, other method | `0x89b8` |
| `26d0` (far ptr) | null | unpacker, other method | `0x311e` |
| `26d4` (far ptr) | null | FAB packer | `0x71be` |
| `26d8` (far ptr) | `1b1f:0014` -> `10a5:0006` | FAB, read through a callback, write through a callback | `0x382c` |
| `26dc` (far ptr) | `1b1f:000a` -> `10a5:0254` | FAB, read through a callback, write to memory | `0x820` |
| `26e0` (far ptr) | `1b1f:0000` -> `10a5:0460` | FAB, memory to memory | 4 |

The `1b1f:xxxx` addresses are resident thunks (`lcall 110d:0d91`, then a far jump)
to segment `10a5`. VICEROY links only the FAB unpackers. The packer slots and the
"other method" slot stay null; if the loader ever needs one, it reports error
`0xffe3` through `F29_0000_03ce`.

The work buffer sizes of the three FAB unpackers match their buffers:

- `0x382c`: a 0x1c-byte state block, a 0x800-byte input buffer and the output window.
- `0x820`: the state block and the input buffer. The output goes straight to memory.
- 4: memory to memory needs no buffer.

Each unpacker returns its buffer size when called with a null work buffer, and
`F28_0046_004a` uses that to size the allocation.

### Calling convention

The calls through the table are in `Overlay_28_008a` (file offsets
`0x77772..0x7786b`). The entry point takes a direction in AX: 0 = pack,
1 = unpack, anything else jumps to the error thunk `1a1f:0fe8`. All codec
routines use the Pascal convention: arguments are pushed left to right and the
callee pops them (the unpackers end with `retf 0xc`).

- unpack: `f(read, write, work)`. Each argument is a far pointer. `read` and
  `write` are callback or buffer pointers, chosen by `F28_0046_004a` and stored at
  `DS:a644/a648` and `DS:a63a/a63e`. `work` is the `$pack$` buffer at `DS:a622`.
- pack: `f(read, write, work, &DS:26c6, &dsize)`, which is
  `pFABcomp(read, write, work, type, dsize)`. `dsize` is a local set to `0x1000`
  before the call; `pFABcomp` overwrites it with the compressed size.

`DS:26c6` is the `type` word (0 = `CMP_BINARY`), passed only to the packers. The
packer slots are null in VICEROY, so it is never used. `DS:26c9` is set to `0xff`
by `F0_2085_0004`, and a raw byte search finds no instruction that reads it.

## Generated code gap

Segment `10a5` is missing from the Disassembler output. Nothing calls into it
directly; it is reached only through the far pointers above, which hold the
`1b1f` thunks, and the generator doesn't pick those thunks up as roots. The calls in
`Overlay_28_008a` (`CallUInt32(ReadUInt32(DS, 0x26d8))` and the like) are indirect
calls, which the current VCPU only logs. For OpenColo there are two options: get
the generator to emit segment `10a5`, or implement FAB natively in C# and dispatch
these three addresses to it. The format above is enough for the native route; it
also has to handle the read and write callbacks of `pFABexp0` and `pFABexp1`, which
are far pointers into game code.

## Open questions

- What `DS:26c9` is for. It is written once and never read.
- The meaning of the high byte of the entry type.
