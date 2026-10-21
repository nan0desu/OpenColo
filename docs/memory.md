# VICEROY.EXE memory layout

Where things are in VICEROY.EXE (v3.0, 494,910 bytes,
md5 `0f5d5b0063721fbc6aca314e5a43ddaf`), and which data addresses are known so far.
Everything here was read from the binary.

## Naming

Names come from the Disassembler's address scheme:

- `Segment_XXXX`: a resident code segment. XXXX is the link-time segment plus
  0x1000 (the load base OpenCivOne uses). `Segment_1d1d` is link segment `0d1d`.
  Raw `seg:off` pairs in this documentation (`0a29:0004`) use link-time segments.
- `Overlay_N`: overlay N (1-based, decimal). `Overlay_N_PPPP` is a later code
  segment inside that overlay, starting at paragraph `PPPP` of the overlay's code.
- `F{overlay}_{segment}_{offset}`: a function. Resident functions have overlay 0
  (`F0_130d_000a`). Overlay functions use the sub-segment paragraph, `0000` for
  the first one (`F25_00e7_025a`).

## File layout

| file offset | contents |
| --- | --- |
| `0x0000` | MZ header and relocation table |
| `0x2400` | resident load module |
| `0x20670` | overlay container, 31 overlay images chained to EOF |

The resident image ends at `0x20665`. The `pages*512` value from the MZ header gives
`0x20800`, which is wrong; `extra_bytes` has to be subtracted.

Each overlay image starts with a header:

| offset | size | meaning |
| --- | --- | --- |
| `+0x00` | u16 | size of the whole image (header and code) in paragraphs |
| `+0x02` | u16 | header size in paragraphs; the code starts at header + this*16 |
| `+0x04` | u32 | always 700 |
| `+0x08` | u32 | number of fixups |
| `+0x0c` | u32 | 0 |
| `+0x10` | `count` pairs of (u16 offset, u16 paragraph) | fixups at paragraph*16 + offset in the code |

The next image starts at header + (`+0x00`)*16. `overlay-directory.json` in the
Disassembler output lists the header and code file offsets of each overlay.

Overlays are reached through thunks in the resident image:

- overlay thunk: `lcall 110d:0dab`, `jmp far 0000:<offset>`, a u16 overlay number
  and a u16 paragraph of the sub-segment. That is 14 bytes, or 12 when the next
  thunk starts right after the overlay number (sub-segment 0);
- resident thunk, 10 bytes: `lcall 110d:0d91`, then `jmp far <seg>:<offset>`.

There are 659 overlay thunks and 369 resident thunks.

## Address to file offset

- resident `seg:off` -> `0x2400 + seg*16 + off`
- overlay N, sub-segment paragraph P, offset `off` -> `code(N) + P*16 + off`
- data `DS:x` -> `0x2400 + 0x1b5a*16 + x`

## Entry point and overlay manager

The MZ header gives:

| field | value |
| --- | --- |
| CS:IP | `110d:071d` |
| SS:SP | `25e5:4096` |
| minalloc / maxalloc | `0x0bc9` / `0x0be9` paragraphs |
| relocations | 2260 |

The entry point isn't `Start`. Segment `110d` is the overlay manager, the same code
every overlay thunk calls (`lcall 110d:0dab`). Its entry does `lcall 110d:0727` and
then `ljmp 0d1d:0150`, which is `Start`. `110d:0727` sets the manager up:

- It looks for EMS: the INT 67h vector, the `EMMXXXX0` device name, version 3.2 or
  later (`46h`), status (`41h`), page count (`42h`), and an allocate/free test
  (`43h`/`45h`).
- It looks for XMS through the driver entry point at `cs:4e84`.
- It sizes its memory with INT 21h/4Ah.

It keeps its state in its own segment (`cs:3954..4fe4`). The stack is back at
`25e5:4096` when it jumps to `Start`.

The Disassembler doesn't emit segment `110d`. Overlay calls in the generated code
are resolved to direct C# calls, so the overlay manager isn't needed at all.
OpenColo can start at `Start` with the header's SS:SP.

All 7 INT 67h instructions in VICEROY.EXE are in this segment. The game code
itself doesn't use EMS. The INT 67h, 2Fh and 1Ah calls in the generated output all
come from the sound drivers (`Seg0{A,G,P,R}Snd`).

The game has its own XMS check, `1088:0048` (INT 2Fh 4300h/4310h, then the XMS
driver; it stores the entry point at `DS:26e8` and sets `DS:26e4` when at least
512 KB are free). It only runs when `DS:26e6` is 0, and `main` sets `DS:26e6 = 1`
on its first line. XMS is therefore never used either.

## DGROUP

DGROUP is link segment `1b5a`. `Start` points DS and SS at it, so the stack lives
in DGROUP as well. `Start` zeroes `DS:2cc6..a8b0` (BSS), so everything in that
range starts at 0 regardless of the file contents.

### Stack and near heap

The header's stack segment `25e5` is DGROUP + `0xa8b`, so the stack segment begins
at `DS:a8b0`, right where BSS ends. `Start` (`0d1d:0150`) moves the stack into
DGROUP:

1. `si = [PSP:2] - 0x1b5a`: the paragraphs available from DGROUP to the top of
   memory, capped at `0x1000` (64 KB).
2. `ss = 0x1b5a; sp += 0xa8ae`. With the header SP of `0x4096`, that gives
   `SP = 0xe944`, the same place as `25e5:4096` minus 2 bytes. If the addition
   carries, `Start` prints its error message and exits with 255.
3. It sets `DS:2776 = si*16 - 1`, the last usable DGROUP offset (`0xffff` with
   enough memory), and builds the near heap at the top of the stack. The heap
   descriptor lives at `DS:2778`:
   - `+0` = SS;
   - `+4` = `SP & ~1` = `0xe944`, the heap start;
   - after `push 0xfffe`, `+0xa` = `0xe942`, the end marker;
   - after `push 0x0001`, `+6` and `+8` = `0xe940`, the first block (size 0, free)
     and the rover.

   The two words just pushed are the heap's first block header and end marker, so
   the heap begins at `DS:e940` and the stack is below it. `DS:2772` keeps that
   SP as the top of the stack.
4. It saves the PSP segment in `DS:27b2`, writes `DS + si` into `PSP:2`, and shrinks
   the program's memory block to end there (INT 21h/4Ah). Memory above DGROUP +
   64 KB goes back to DOS for later far allocations, such as the `$pack$` buffers.

The resulting layout of DGROUP:

| range | contents |
| --- | --- |
| `0000..2cc5` | initialized data from the file |
| `2cc6..a8af` | BSS, zeroed by `Start` |
| `a8b0..a9af` | bottom 256 bytes of the stack, below the stack-check limit |
| `a9b0..e93f` | stack, growing down from `0xe940` (about 15 KB); `DS:27e6 = 0xa9b0` is the limit |
| `e940..ffff` | near heap, growing up to `DS:2776` (about 5.8 KB) |

`DS:27e6` is the stack-check limit. `main`'s first call, `F0_1ae3_0006`, zeroes the
stack from that limit up to the current SP. `F0_1ae3_0021` later counts how much of
that area is still zero, which is the stack still available.

### Near heap users

The game code never calls the near allocator. `_nmalloc` (`F0_1d1d_2916`, at most
`0xffe8` bytes) and `_nfree` (`F0_1d1d_291c`) are only called from inside the C
runtime. A call graph of `Segment_1d1d` shows which runtime functions reach them:

| runtime function | game call sites | heap use |
| --- | --- | --- |
| `fread` (`0528`), `fwrite` (`060c`), `fgetc`/`fputc`-style helpers (`0758`, `0786`, `09ca`) | 61, 44, 8 | `_filbuf`/`_flsbuf` -> `_getbuf` (`2702`) allocates the `FILE` buffer on first use |
| `fclose` (`03f4`) | 17 | frees that buffer (`16d0` -> `_nfree`) |
| `printf` (`0712`) | 7 | stdout buffer (`17e4`) |
| `fseek` (`0a3e`, `0abe`, `0ad8`), `exit` (`030d`), `11fa`, `0b1c` | 12 | flushing and buffer setup |
| exec (`0e2c`) | 1 | argument and environment block for the chain to OPENING/CLOSING |

`sprintf` (`0b48`) also passes through `_flsbuf`, but on a string stream (`DS:2d30`),
which doesn't allocate.

When the heap is full, `F0_1d1d_213e` grows it in steps of `_amblksiz` (`DS:2ab8 =
0x2000`), up to `DS:2776`. So the near heap holds only a few stdio buffers. An
emulator only has to give the program the full 64 KB of DGROUP, which `Start`
asks for anyway.

The debug menu's memory dialog (`F1_0000_1fd2`, `@MEMORY` in `DEBUG.TXT`) shows
these numbers: "Near Available" is `_memmax` (`F0_1d1d_0c28`), "Stack Available" is
`F0_1ae3_0021`, and "PSP at" is `DS:27b2`.

The header's minalloc (`0xbc9` paragraphs) makes DOS allocate just enough to reach
the top of the stack (linear `0x29f00` against a stack top of `0x29ee6`).
Everything above that up to 64 KB of DGROUP is optional. With less memory `si`
is smaller and `DS:2776` lower.

### Known variables

Sizes are words unless marked. The "see" column links to where the variable is
explained.

| address | meaning | see |
| --- | --- | --- |
| `0104` | independence won, show score / chain to CLOSING (`-w`) | [startup](startup.md#winning--w) |
| `0150` | demo-mode counter used every 4th turn, up to 25 | `F0_130d_0290` |
| `036c` | `-p` given | [startup](startup.md#command-line-switches) |
| `036e` (far ptr) | palette copy, initially `A000:FC00` | [timer](timer.md#water-color-cycling) |
| `0372` | water color cycling enabled | [timer](timer.md#water-color-cycling) |
| `0374` | color cycling throttle counter | [timer](timer.md#water-color-cycling) |
| `0376` (byte) | INT 8 divide-by-5 counter | [timer](timer.md#int-8-handler) |
| `0377` (byte) | INT 8 divide-by-3 counter (BIOS chain) | [timer](timer.md#int-8-handler) |
| `0379` | INT 8 handler installed | [timer](timer.md#tick-counter) |
| `037b`/`037d` | SS:SP saved by INT 8 | [timer](timer.md#int-8-handler) |
| `037f`/`0381` | SS:SP saved around the 60 Hz callback | [timer](timer.md#int-8-handler) |
| `0384..0482` | stack for the 60 Hz callback | [timer](timer.md#int-8-handler) |
| `0484..0582` | stack for the INT 8 handler | [timer](timer.md#int-8-handler) |
| `0800` | VGA timing calibrated | [timer](timer.md#vga-timing-calibration) |
| `0802` | frame length in PIT counts | [timer](timer.md#vga-timing-calibration) |
| `0804` | palette colors per frame (calibrated) | [timer](timer.md#vga-timing-calibration) |
| `0806` | palette bytes uploaded per vertical retrace, `3 x DS:804` | [timer](timer.md#vga-timing-calibration) |
| `0808` | DAC busy | [timer](timer.md#water-color-cycling) |
| `081c` | zeroed by `main` | - |
| `081e` | `-m` given; never read | [startup](startup.md#command-line-switches) |
| `0822` | exit value printed by `main` if nonzero | [startup](startup.md#main) |
| `0826` | demo idle timeout enabled (`-d`) | [startup](startup.md#demo-mode--d) |
| `0828` (byte) | demo mode (`-d`), also set by Alt-X / Alt-Q | [startup](startup.md#demo-mode--d) |
| `0829` (byte) | a saved game was loaded at start | `F26_011e_2778` |
| `082a` (byte) | started by OPENING/CLOSING (`-o`) | [startup](startup.md#command-line-switches) |
| `082b` (byte) | end the demo (`-z`, year > 1725) | [startup](startup.md#demo-mode--d) |
| `201e` | hot-seat unlocked (`-k`, `COLONIZE=MULTI`) | [startup](startup.md#hot-seat-multiplayer--k-colonizemulti) |
| `2604` | sound driver segment | [sound](sound.md#loading) |
| `2606` | `-l` given | [startup](startup.md#command-line-switches) |
| `2608` (byte) | sound driver letter | [startup](startup.md#configcol) |
| `260a..2616` | `CONFIG.COL` words; `260c` is the sound device code | [startup](startup.md#configcol) |
| `267a` (far ptr) | tick counter used by `F0_1c0c_0006` | [timer](timer.md#tick-counter) |
| `26a2` (byte) | sound driver loaded | [sound](sound.md#loading) |
| `26a3`/`26a5` | SS:SP saved around INT 21h/4B03h | [sound](sound.md#loading) |
| `26a7` | INT 21h/4B03h parameter block (load segment, relocation) | [sound](sound.md#loading) |
| `26ab` | `"$sound$"`, MCB name for the driver block | [sound](sound.md#loading) |
| `26b4..26c3` | queued sound requests, 8 words | [sound](sound.md#calling-the-driver) |
| `26c4` (byte) | number of queued requests | [sound](sound.md#calling-the-driver) |
| `26c5` (byte) | queue sound requests instead of sending them | [sound](sound.md#calling-the-driver) |
| `26c6` | `type` for `pFABcomp` (0 = binary); the packer isn't linked | [madspack](madspack.md#calling-convention) |
| `26c9` (byte) | set to `0xff` at startup, never read | [madspack](madspack.md#calling-convention) |
| `26ca..26e2` | MADSPACK codec table: entry type, far pointers to the FAB unpackers | [madspack](madspack.md#the-codec-table-at-ds26c926e2) |
| `26e4` | XMS available (never set, see `26e6`) | [above](#entry-point-and-overlay-manager) |
| `26e6` | disables the game's XMS check; `main` sets it to 1 | [above](#entry-point-and-overlay-manager) |
| `26e8` (far ptr) | XMS driver entry point | [above](#entry-point-and-overlay-manager) |
| `26f0..276f` | sound device table, 16 x 8 bytes | [startup](startup.md#configcol) |
| `2772` | top of the stack (`0xe940`) | [above](#stack-and-near-heap) |
| `2776` | last usable DGROUP offset, heap limit | [above](#stack-and-near-heap) |
| `2778..2783` | near heap descriptor | [above](#stack-and-near-heap) |
| `27b2` | PSP segment | [above](#near-heap-users) |
| `27cf/27d1/27d3` | `argc`, `argv`, `envp` | [startup](startup.md#startup-code) |
| `27e6` | stack-check limit, `0xa9b0` | [above](#stack-and-near-heap) |
| `2ab8` | `_amblksiz`, heap growth step (`0x2000`) | [above](#near-heap-users) |
| `2d06..2d08` | 3-byte buffer for palette rotation | [timer](timer.md#water-color-cycling) |
| `2d30..2d37` | string `FILE` used by `sprintf` | [above](#near-heap-users) |
| `5381` (byte) | game flags; `0x80` = more than one human player | [startup](startup.md#hot-seat-multiplayer--k-colonizemulti) |
| `5382` (byte) | game flags; `0x01` ends the demo, `0x80` set by options menu item 8 | - |
| `5383` (byte) | game option flags; `0x01` = water color cycling off | [timer](timer.md#water-color-cycling) |
| `538a` | year | `F0_130d_0290` |
| `538c` | season (two turns per year from 1600 on) | `F0_130d_0290` |
| `538e` | turn number | `F0_130d_0290` |
| `5394` | power whose turn it is | `F0_130d_0290` |
| `5398` | human player's power (0-3) | [startup](startup.md#hot-seat-multiplayer--k-colonizemulti) |
| `53c2` | game running; the main turn loop runs while it is set | `F0_130d_0290` |
| `540e + n*0x34` | record of European power n (0-3); leader name at +0 | `F26_011e_10ae` |
| `543f + n*0x34` (byte) | power n control: 0 human, 1 AI | [startup](startup.md#hot-seat-multiplayer--k-colonizemulti) |
| `8338` (dword) | raw PIT tick count, 608.8 Hz | [timer](timer.md#int-8-handler) |
| `84fe` | data path (`-p`) | [startup](startup.md#command-line-switches) |
| `8554` | string buffer, set to `"viceroy"` by `main`; also used in `Overlay_25_011b`, purpose unknown | [startup](startup.md#main) |
| `929e` | color cycle entry count, followed by the entries at `92a0` | [timer](timer.md#water-color-cycling) |
| `92c0` | color cycling throttle | [timer](timer.md#water-color-cycling) |
| `92c2` | total cycled colors | [timer](timer.md#water-color-cycling) |
| `92c4 + i*4` (dword) | color cycle entry i, next due tick | [timer](timer.md#water-color-cycling) |
| `92e4` (far ptr) | 60 Hz timer callback | [timer](timer.md#int-8-handler) |
| `92e8` (dword) | game tick counter, 60.88 Hz | [timer](timer.md#tick-counter) |
| `92ec` | BIOS chain postponed | [timer](timer.md#int-8-handler) |
| `92ee` | ticks missed while the callback was busy | [timer](timer.md#int-8-handler) |
| `92f0` | callback registered | [timer](timer.md#int-8-handler) |
| `92f2` | callback running | [timer](timer.md#int-8-handler) |
| `92f4` | music playing (driver tick enabled) | [sound](sound.md#calling-the-driver) |
| `92f6` | sound driver segment, 0 without a driver; enables status polling | [sound](sound.md#loading) |
| `a654..a667` | five far pointers into the sound driver | [sound](sound.md#calling-the-driver) |

## Outside DGROUP

| address | use |
| --- | --- |
| `A000:0000..F9FF` | mode 13h screen |
| `A000:FC00..FEFF` | palette copy (256 x RGB), past the visible screen |
| `0040:006c` | BIOS tick count, used when the game timer isn't installed |
| `0040:0017` | BIOS keyboard flags; the INT 8 handler clears NumLock (bit 5) |

## Open questions

- How the debug menu (`DEBUG.TXT`, `F1_0000_1fd2` and its neighbours) is reached.
  The memory dialog has no direct callers.
- The full layout of the per-power record at `540e`.
