# OpenColo

Sid Meier's Colonization (DOS, VICEROY.EXE v3.0) running as C# on a virtual x86 CPU,
in the style of [OpenCivOne](https://codeberg.org/rhorvat/OpenCivOne).

The game code comes from the Disassembler, which turns
every function of VICEROY.EXE and its 31 overlays into C# calls on `IRB.VirtualCPU.VCPU`.
This repository is the host: the CPU, DOS/BIOS services, a mode 13h VGA, and an Avalonia window.

## Layout

- `src/VirtualCPU` - the CPU, memory and DOS/BIOS interrupts, taken from OpenCivOne (MIT, see `LICENSE.OpenCivOne`)
- `src/VirtualCPU/VCPUVGA.cs` - A000:0000 framebuffer and DAC palette, read by the UI
- `src/Game/OpenColoGame.cs` - root object the generated segment classes hang off; loads VICEROY.EXE,
  sets up the PSP and environment, and stands in for the game's timer interrupt and the mouse driver's event calls
- `src/Generated` - the Disassembler's `colonization-runnable` output (not in git, see below)
- `src/UI` - Avalonia window, BIOS key mapping
- `docs/startup.md` - how the original starts: OPENING/VICEROY/CLOSING chain, `main()`, CONFIG.COL, command-line switches
- `docs/timer.md` - the game's own INT 8 handler, PIT rate, tick counters, water color cycling
- `docs/memory.md` - VICEROY.EXE file and overlay layout, address conventions, known DGROUP variables
- `docs/madspack.md` - MADSPACK containers (.PIK/.SS/.FF), FAB decompression, the codec table
- `docs/sound.md` - loading the `#SOUND.COL` driver, the `1059` call interface, running without sound
- `tools/fab_check.py` - reference FAB decoder; checks every MADSPACK file in a game directory
  (`python3 tools/fab_check.py $OPENCOLO_PATH`)

## Build and run

The game code is generated, not kept here. Generate it with the Disassembler (run from its
repository root, the paths are relative) and copy it into `src/Generated`:

```sh
dotnet <Disassembler>/bin/Debug/net9.0/Disassembler.dll colonization-runnable
cp <Disassembler>/bin/Debug/net9.0/Output/ColonizationRunnable/*.cs src/Generated/
```

Or point the build at the output directory with `-p:GeneratedCodePath=<dir>`. Then:

```sh
mise exec -- dotnet build src/OpenColo.csproj
OPENCOLO_PATH=~/dos/COLONIZE mise exec -- dotnet run --project src/OpenColo.csproj
```

- `OPENCOLO_PATH` is the directory with the original DOS game files; without it the
  executable's own directory is used.
- `OPENCOLO_ARGS` is the VICEROY command line, `-c:N` by default: no sound driver
  (see [docs/startup.md](docs/startup.md#command-line-switches)). Don't add `-o` without
  OPENING: the start menu then takes its background from the screen OPENING left behind.
- `COLONIZE`, if set, is passed into the game's environment (`COLONIZE=MULTI`).
- `OPENCOLO_LOG=1` writes `Log.txt` next to the executable.
- F12 in the window writes `Dump.bin` (conventional memory), `Dump.txt` (registers and the
  functions being executed) and `Dump.png` (the 320x200 screen) next to the executable; so does
  a non-zero exit, and `Dump.vga` (the 64K at A000:0000, then the 768 byte DAC palette).
  Existing dumps are kept, the next ones are `Dump1`, `Dump2`, ...
  `OPENCOLO_CHECKS=1` turns on the debug checks in `OpenColoGame.AddDebugChecks`, which dump
  when a function is entered with arguments known to be bad.

The host starts VICEROY.EXE at the C runtime's `Start`, skipping the overlay manager. It
doesn't emulate the timer interrupt or the mouse driver; it runs their effect on the game
thread between memory accesses (`VCPU.DoEvents`): the BIOS tick count, or once the game has
installed its INT 8 handler, a C# copy of that handler that calls the game's own color cycling
callback; and the game's INT 33h event handler when the mouse moves. The PIT counter and the
VGA retrace bit follow real time, which is what the game's VGA timing calibration measures.

## Thanks

This port wouldn't exist without [Rajko Horvat](https://codeberg.org/rhorvat). His
[OpenCivOne](https://codeberg.org/rhorvat/OpenCivOne) brought Sid Meier's Civilization back
to life by turning the original executable into readable, runnable C#. It showed that the
same could be done for Colonization, and it is what started this project. Rajko also wrote
the first version of the Disassembler, which generates all of the game code here, and the
virtual CPU this host is built on. Thank you, Rajko, for the work and for sharing it.

## License

OpenColo is licensed under the GNU General Public License v3.0, see `LICENSE`.

`src/VirtualCPU` and `src/IRB` are based on OpenCivOne by Rajko Horvat and remain under the
MIT License, see `LICENSE.OpenCivOne`. Changes made to them here are under GPLv3.

The license covers this repository only. The code generated from VICEROY.EXE in
`src/Generated` is a translation of the original game. It isn't part of OpenColo and isn't
covered by this license.

## Legal

Sid Meier's Colonization, MicroProse and all other trademarks are the property of their
respective owners. This project is not affiliated with or endorsed by MicroProse or any
current rights holder. It is an unofficial fan port.

This repository contains no files from the original game: no executables, graphics,
sounds, music or text. To play, you need your own copy of the DOS version of Colonization.
Point `OPENCOLO_PATH` at its directory.
