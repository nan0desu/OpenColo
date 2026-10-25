# Sound driver interface

How VICEROY.EXE loads and calls its sound driver, and what happens without one.
Names and address conventions are described in [memory.md](memory.md). The driver
letter comes from `CONFIG.COL` (see [startup.md](startup.md#configcol)).

## Driver files

The drivers are separate MZ executables: `ASOUND.COL`, `GSOUND.COL`,
`PSOUND.COL`, `RSOUND.COL`. They are loaded as overlays, not run. Their load
image (after the 0x200-byte header) starts with:

| offset | contents |
| --- | --- |
| `0x28` | u16 segment of the entry points; 0 in the file, relocated by DOS at load time |
| `0x32` | five u16 entry point offsets |

| file | entries at `0x32` |
| --- | --- |
| `ASOUND.COL` | `19d2 1a35 1ad2 1af5 1b09` |
| `GSOUND.COL` | `1958 19bc 1a33 1a97 1aba` |
| `PSOUND.COL` | `0ac3 0b18 0bbc 0bf9 0c0d` |
| `RSOUND.COL` | `0969 09c6 0a3d 0a9d 0ac0` |

There is no `NSOUND.COL` or `ISOUND.COL`, although the `CONFIG.COL` device table
can select `N` and `I`.

## Loading

`game_main` loads the driver just before the main turn loop with
`F30_0055_003a`. The call passes AL = the driver letter (`DS:2608`),
DX = `DS:260a`, BX -> `"#SOUND.COL"` (`DS:23ba`), and the `CONFIG.COL` words
`DS:260c..2616` on the stack. The loader:

1. copies the template and replaces `#` with the letter;
2. builds the path: the current directory with `-l`, otherwise the data path
   (`F0_19f6_00b0`);
3. unloads any previous driver (`F30_0055_0002`);
4. loads the file with `F0_2047_000a`. That function allocates the largest free
   DOS block, names it `$sound$` in its MCB, loads the file with INT 21h/4B03h
   (load overlay), and shrinks the block to the image size. It returns the
   segment, or 0 on failure;
5. on success, `F0_2047_00b8` sets `DS:26a2 = 0xff` and copies the five entry
   points into far pointers `DS:a654..a667`. Then it calls the driver's init
   (`1059:0006`) with the `CONFIG.COL` values, retrying with different arguments
   while init fails;
6. on failure, `F0_2047_0106` points all five vectors at the stub `1047:0103`
   (`xor ax,ax; retf`);
7. finally `F0_1a29_0209(segment)` stores the driver segment (0 if there is none)
   in `DS:92f6` and clears `DS:92f4`.

`F30_0055_0002` unloads: it calls `1059:000a(0)`, clears `DS:92f6`,
calls the driver's stop entry (`1059:005f`), frees the block (`F0_2047_00e9`), and
points the vectors back at the stub. It runs at the end of `game_main`.

## Calling the driver

All game code goes through segment `1059`, which jumps through the vectors:

| entry | vector | driver entry | use |
| --- | --- | --- | --- |
| `1059:0006` | `DS:a654` | 1st | init, called by the loader |
| `1059:000a(n)` | `DS:a658` | 2nd | request `n`: a sound number or command code (see below) |
| `1059:005f` | `DS:a65c` | 3rd | stop, called before unloading and before `exec` |
| `1059:0063` | `DS:a660` | 4th | status. The INT 8 handler calls it at 60.88 Hz while `DS:92f6` is nonzero: a positive AX sets `DS:92f4`, a negative one clears it, 0 leaves it alone. |
| `1059:0067` | `DS:a664` | 5th | music tick; the INT 8 handler calls it at 304.4 Hz while `DS:92f4` is set |

The argument of `1059:000a` is used both ways. The game passes values like `0x25`
and `0x33` from screens, and `0`, `1` and `8` from the shutdown code. `8` returns
nonzero while the driver is busy. The exact meanings depend on the driver.

`1059:000a` can queue requests instead. `1059:0031` sets `DS:26c5 = 0xff`; after
that, requests are stored in `DS:26b4` (up to 8 words, count in `DS:26c4`) instead
of going to the driver. `1059:0037` clears the flag and sends the queued requests
in order.

`F0_2047_011f` returns `DS:26a2`, whether a driver is loaded. The chain function
`F0_130d_000a` uses it before `exec`: it calls `1059:000a(1)`, polls
`1059:000a(8)` up to 30000 times until it returns 0, then calls stop.

## Without a driver

With letter `N`, which is what you get without `CONFIG.COL` (device code 0), or
with `I`:

- the load fails because the file doesn't exist, and all five vectors point at
  the stub, which returns 0;
- `DS:92f6` stays 0, so the INT 8 handler never calls the status or music entries
  and `DS:92f4` stays 0;
- every request, stop and status call returns 0 at once; the busy poll before
  `exec` ends on the first try.

An emulator can start with no sound support at all. The vectors are BSS and are
zero until the loader runs, so a sound call before `F30_0055_003a` would jump to
`0000:0000`. None was found: the timer is installed earlier, but with `DS:92f6 = 0`
it doesn't call the driver.

## Requests

Read from `ASOUND.COL`; the other drivers have the same sample table.

The game asks for sounds through `F0_12d8_000e(AX = n)`. It sends numbers below
`10h` as they are, music (bit `20h`) only while `DS:a0` is set, and effects (bit
`40h`) only while `DS:a4` is set. A new game sets both; a saved game restores them.

The driver's request entry (`1a35` in `ASOUND.COL`, data segment `03c0`) dispatches
on `n` through jump tables in its code segment:

| `n` | table | meaning |
| --- | --- | --- |
| `0`-`8` | `cs:1b59` | commands: `0` silences all channels, `1` stops the music, `8` returns nonzero while music plays (the OR of the channels' active flags) |
| `20h`-`3Fh` | `cs:1b6b` | music |
| `40h`-`5Dh` | `cs:1bb9` | sound effects |
| `8020h`-`8026h` | `cs:1bab` | not called by VICEROY |

An effect handler sets AX to a digitized sample and calls `1bf6`, then sets CX to
FM data and jumps to `154b`, the AdLib part. Some effects have only one of the two.
Effect to sample:

| `n` | 40 | 41 | 42 | 43 | 44 | 45 | 48 | 49 | 4A | 4B | 4C | 4D | 4E | 4F | 50 | 51 |
| --- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- | -- |
| sample | 31 | 32 | 30 | 27 | 18 | 17 | 29 | 34 | 28 | 33 | 14 | 10 | 6 | 11 | 7 | 5 |

| `n` | 52 | 53 | 54 | 55 | 56 | 57 | 58 | 5B | 5C |
| --- | -- | -- | -- | -- | -- | -- | -- | -- | -- |
| sample | 12 | 19 | 13 | 20 | 9 | 16 | 21 | 22 | 8 |

`46h`, `47h`, `59h`, `5Ah` and `5Dh` are FM only.

## Digitized samples

`COLDIG.BIN` is 35 samples of unsigned 8-bit mono PCM back to back, without a header.
The driver loads the whole file into EMS at init (`0e3c`: `coldig.bin` at `ds:0058`,
INT 67h); without EMS there are no digitized effects. Its sample table is at `cs:019f`
in `ASOUND.COL`: 8 bytes per sample, a 32-bit offset and a 32-bit length. The count
(`cs:0093`) is worked out from the file size at init. The player (`cs:0ce2`, called
with the sample number) plays samples 0-4 at 19050 Hz and the others at 11025 Hz,
and queues them: a ring of 32 entries at `cs:0095`, played one after another, not
mixed.

| sample | offset | length | sample | offset | length | sample | offset | length |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 0 | 4960 | 12 | 351711 | 38912 | 24 | 780510 | 7941 |
| 1 | 4960 | 4952 | 13 | 390623 | 60739 | 25 | 788451 | 20992 |
| 2 | 9912 | 4833 | 14 | 451362 | 61684 | 26 | 809443 | 34623 |
| 3 | 14745 | 4683 | 15 | 513046 | 57327 | 27 | 844066 | 19299 |
| 4 | 19428 | 4718 | 16 | 570373 | 46480 | 28 | 863365 | 30207 |
| 5 | 24146 | 46745 | 17 | 616853 | 12040 | 29 | 893572 | 11071 |
| 6 | 70891 | 39737 | 18 | 628893 | 12545 | 30 | 904643 | 13629 |
| 7 | 110628 | 51364 | 19 | 641438 | 59707 | 31 | 918272 | 5455 |
| 8 | 161992 | 61684 | 20 | 701145 | 22418 | 32 | 923727 | 9407 |
| 9 | 223676 | 45999 | 21 | 723563 | 8557 | 33 | 933134 | 36299 |
| 10 | 269675 | 42299 | 22 | 732120 | 42496 | 34 | 969433 | 24322 |
| 11 | 311974 | 39737 | 23 | 774616 | 5894 | | | |

The effects use samples 5-14, 16-22 and 27-34. The music sequencer (`1d86`) also
plays samples, presumably the others; 15 isn't used by any effect.

The CD version also has a Windows installer with WAV files (effects and national
anthems), which the DOS game doesn't use.

## RSOUND.COL

The Roland driver, for device codes `30h` and `31h`. Read from the file; the offsets
are in its load image.

- One code segment at `0`, data at `333h` paragraphs (DS = load segment + `333h`).
  The image is `B44Ch` bytes, plus `200h` of BSS. 16 relocations.
- Init (`0969`) takes the seven `CONFIG.COL` words as arguments, `DS:260a` first.
  The first is the MPU-401 port (`330h`); the driver keeps it in `[5F]` and the
  status port in `[61]`. `035b` resets the MPU-401 (`FFh` to the status port, waits
  for bit 7 of the status to clear, expects `FEh` on the data port, five tries), then
  init sends `3Fh`, UART mode, through `01ed` (waits for bit 6 of the status to clear,
  writes the command, reads the data port until `FEh`). Everything after that is plain
  MIDI.
- The fifth word (`DS:2612`) is the digital device. When it is `11h`-`2Fh`, init calls
  `26c7` with words 4-7 (port, device, IRQ, DMA): a Sound Blaster driver that loads
  `COLDIG.BIN` into EMS (`29a2`, INT 67h) and stores 0 in `[50]` on success.
  `[50]` starts out as `FFFFh`.
- The request entry (`09c6`) dispatches through four tables in the code segment:
  `2afd` commands (highest in `[3C4C]`, 8), `2b0f` music (`[3C4E]`, 3Fh), `2b53`
  `8020h`-`8026h` (`[3C50]`), `2b63` effects (`[3C52]`, 5Dh).
- An effect handler sets AX to a sample and calls `3317`, which plays it through the
  Sound Blaster (`2846`) only while `[50]` is 0, then sets CX to MIDI data and goes to
  `05be`. The samples are those of `ASOUND.COL`, except that `5Ah` plays sample 15.
  `46h` and `47h` are MIDI only.
- The music is a byte code read through `[8144]`, interpreted by `0c7a`; opcodes
  `BEh`-`FFh` go through the table at `1938`. Opcode `C4h` and a word calls code in
  the driver: `2c8c` and `2d26` in the music data. Music handler `2c08` leaves `2c48`
  or `2c6a` in `[91A]`, which the tick (`2ac8`) calls.
- A Roland SysEx template (`F0 41 10 16 12`, data set to the MT-32) is at `[65]`.
  The string `[MpsColoniz]` at `[87]` goes to the MT-32's display at init.
- Stop (`0a3d`) resets the MPU-401 again and, in `0a6e`, reads the time from the RTC
  (INT 1Ah function 2) and sets the DOS time (INT 21h function 2Dh) from it. The game's
  timer makes the DOS clock run fast (see [timer.md](timer.md#int-8-handler)).

## In OpenColo

The driver code isn't part of VICEROY.EXE. The Disassembler's readable output has
`Seg0{A,G,P,R}Snd`, a partial attempt at the four `.COL` files.

RSOUND.COL runs as generated code (`RSound`, from the Disassembler's
`ProcessSoundDriverRunnable`) on an emulated MT-32 or CM-32L, when munt's
libmt32emu and the ROMs are there. Its code can only run at the segment it was
generated for, `9400h`, so `SoundDriver`:

- takes `9400h`-`9FFFh` away from DOS and loads the image there, relocated;
- writes the header fields the game reads into the game's block (`28h` entry segment
  `9400h`, `2Ah`/`2Ch` image end right after the header, so the block shrinks to it,
  and the five entry offsets at `32h`), so the game's vectors lead to `9400h`;
- registers the driver's functions in `CodeDispatch.Native` (`CreateRSound`) and
  connects an MPU-401 at `330h` that passes the MIDI bytes to munt.

The digitized effects are played by the host, not the Sound Blaster code: init runs
with the digital device argument set to 0, so the driver leaves the Sound Blaster and
EMS alone, and the request entry plays the effect's sample from `COLDIG.BIN` when
`CONFIG.COL` has a digital device. The MIDI part of each effect plays on the MT-32,
as in the original.

The C# copy of the game's INT 8 handler calls the status and tick entries like the
original (see [timer.md](timer.md#int-8-handler)).

Any other driver, and RSOUND.COL without munt or ROMs, gets a stand-in. When the game
loads it, `SoundDriver` writes the same header fields into the block, pointing at
five host functions in `CodeDispatch.Native`. The request entry plays the effect's
sample from `COLDIG.BIN` through OpenAL, queued like the original; music requests are
accepted and not played. Request `8` then answers "playing" until `0`, `1` or stop:
the music manager `F0_129f_00f6` polls it and starts the next piece as soon as it is
0, which without music would cycle through the pieces.

## Open questions

- The command codes `2`-`7`.
- The music data format of the other drivers.
