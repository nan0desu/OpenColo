# VICEROY.EXE startup and command line

How Colonization (DOS, VICEROY.EXE v3.0) gets from DOS to the game, and what its
command-line switches do. Everything here was read from the binary. Names, address
conventions and the list of known `DS:` variables are in [memory.md](memory.md);
the timer and color cycling are in [timer.md](timer.md).

## The program chain

The player never starts VICEROY.EXE directly. The game is four programs that hand
control to each other with `exec`:

```text
COLONIZE.BAT  ->  opening -g  ->  viceroy -o[d][l] -p:<path>
COLDEMO.BAT   ->  opening -d:0 ->  (same, with d)

viceroy  --demo time is up-->  opening -o[l][d] -p:<path>
viceroy  --independence won-->  closing -gok[l] -p:<path>  ->  viceroy -ow[l] -p:<path>
```

The OPENING and CLOSING command lines are string literals in those executables.
VICEROY builds its two in `F0_130d_019e` (to OPENING) and `F0_130d_0222` (to CLOSING)
and runs them through `F0_130d_000a`. That function splits the string on spaces
into at most 10 arguments, shuts down sound and graphics, calls `exec`, and on
failure prints `Chain failed.` and exits with code 3.

OPENING.EXE has a usage text listing its own switches:

```text
  -c:x       Specify sound card letter.
  -d:0       Activate self running demo.
  -f         Show frame numbers.
  -g         Chain to game on completion.
  -l         Sound drivers load locally.
  -m         Show free memory.
  -p:path    Specify data file path.
```

## Startup code

The MZ entry point is the overlay manager (`110d:071d`). It sets itself up (EMS,
XMS, memory) and jumps to `Start`; see
[memory.md](memory.md#entry-point-and-overlay-manager).

`Start` (`Segment_1d1d.Start`, `0d1d:0150`) is the stock Microsoft C crt0. It checks for DOS 2.0 or
newer, moves the stack into DGROUP (see [memory.md](memory.md#stack-and-near-heap)), shrinks the program's memory block
(`INT 21h/4Ah`), and zeroes BSS (`DS:2cc6..a8b0`). It then runs the C runtime
initializers and calls `main(argc, argv, envp)` with the values stored at
`DS:27cf/27d1/27d3`, followed by `exit(ax)`.

## main()

`main` is in overlay 25, sub-segment `00e7`, at offset `025a`:
`Overlay_25_00e7.F25_00e7_025a`. Its resident thunk is `181f:0000`. Equivalent C:

```c
int main(int argc, char **argv)
{
    *(int *)0x26e6 = 1;
    F0_1ae3_0006();          // zero the unused stack (stack-usage measurement)
    F0_2085_0004();          // register the FAB unpackers, see madspack.md
    *(int *)0x81c = 0;

    char *p = getenv("COLONIZE");
    if (stricmp(p, "MULTI") == 0)
        multi = 1;           // DS:201e

    F25_00e7_000a();         // empty function (just retf)
    read_config();           // F25_00e7_0188

    for (int i = 1; i < argc; i++)
        if (strchr("-/", argv[i][0]))
            for (p = argv[i]; *p; p++)
                parse_option(&p, argv[0]);   // F25_00e7_0054

    strcpy(save_name /* DS:8554 */, "viceroy");
    game_main();             // F26_011e_2d46
    if (exit_value /* DS:822 */)
        printf("Exit value: %d\n", exit_value);
}
```

The generated C# for this function is not reliable. `lea ax,[bp-2]; push ax` comes
out as pushing the value of `Local_2`, the `ENTER` operands are dropped, and the
jump table in `parse_option` is left as a bare `JmpUInt16`. Check the raw bytes
when it matters.

### CONFIG.COL

`read_config` opens `CONFIG.COL` with mode `"rb"` and reads up to seven 16-bit
words into `DS:260a..2616`. It stops at the first short read. If the file is
missing, the defaults compiled into the binary are kept:
`0330 0000 0002 0220 0000 0007 0001`.

The second word (`DS:260c`) is the sound device code. `F0_205f_000c` looks it up
in a 16-entry table at `DS:26f0` (8 bytes per entry, terminated by `ff`) and stores
the sound driver letter in `DS:2608`. If the code isn't in the table, the letter
is `N`. The driver file name is `#SOUND.COL` with `#` replaced by that letter.

| code | letter | port/IRQ/DMA bytes in the entry |
| --- | --- | --- |
| 00 | `N` (no sound) | - |
| 01 | `I` | - |
| 10 | `A` | 388h |
| 11, 12, 18 | `A` | 220h, IRQ 7, DMA 1 |
| 19 | `A` | 388h, IRQ 7, DMA 1 |
| 20 | `P` | 220h, IRQ 7, DMA 1 |
| 21, 22 | `P` | 388h, IRQ 7, DMA 1 |
| 30, 31 | `R` | 330h |
| 38 | `G` | 330h |

## Command-line switches

Arguments that start with `-` or `/` are scanned one character at a time. Each
letter is uppercased and dispatched through the jump table at `CS:0156` in
overlay 25 segment `00e7`, which covers `C` to `Z`. A value follows a colon
(`-p:C:\GAMES\COLONIZE`), and several switches can share one argument (`-ow`).
The helper `F25_00e7_000c` skips ahead to the colon.

| switch | flag | effect |
| --- | --- | --- |
| `-o` | `DS:82a` (byte) | Started by OPENING or CLOSING. The start menu (`F26_011e_2778`) then doesn't upload the palette of `OPENMENU.PIK` and copies the top 176 rows of its background from the screen (`F0_1b8f_0006`), where OPENING left them. Started any other way, the background is black until the menu is redrawn. |
| `-d` | `DS:828` (byte), `DS:826` | Self-running demo. See below. |
| `-w` | `DS:104` | Coming back from CLOSING. See below. |
| `-z` | `DS:82b` (byte) | End the demo at the next check. |
| `-l` | `DS:2606` | Load the sound driver from the current directory instead of the data path (`Overlay_30_0055`). Passed on to OPENING and CLOSING. |
| `-p`, `-p:path` | `DS:36c`, path in `DS:84fe` | Data file path. Without a value, the directory part of `argv[0]` is used. The path resolver `F0_19f6_00b0` prepends it to file names. |
| `-k` | `DS:201e` | Unlocks the hidden hot-seat multiplayer start. Same as `COLONIZE=MULTI` in the environment (compared case-insensitively). See below. |
| `-c:x` | `DS:2608` (byte) | Sets the sound letter to `N` whatever `x` is. OPENING doesn't pass `-c` on, so this only matters when VICEROY is started by hand. |
| `-m:name` | `DS:81e` | Has no effect. The name is copied to `DS:8554` and then overwritten with `"viceroy"` in `main`, and nothing reads `DS:81e`. |

Letters without a case (`E`-`J`, `N`, `Q`-`V`, `X`, `Y`) are ignored.

### Demo mode (`-d`)

- The start menu code loads save slot 5 (`COLONY05.SAV`, which OPENING also
  references) and hands all four colonial powers to the AI (player table
  `DS:543f + n*0x34`).
- Animation timing switches to the demo values (`F0_129f_*`).
- In the input loop (`Segment_1262`), any key or mouse click runs
  `F29_0000_014e` and then `exit(3)`.
- In the main turn loop (`F0_130d_0290`), the demo ends when bit 0 of
  `DS:5382` is set, when the year (`DS:538a`) passes 1725, or when `-z` was given.
  It also ends when the game tick counter (`F0_1c0c_0006`, see [timer.md](timer.md#tick-counter))
  reaches `0x3840`: 14400 ticks at 60.88 Hz is about 3 min 57 s after
  `game_main` started. When the demo ends, VICEROY saves to slot 5 (`F26_00be_0034(5)`) and
  chains back to `opening -od`. The demo therefore loops, and each round picks up
  where the previous one stopped.
- Alt-X and Alt-Q (scan codes `0x12d`, `0x110`) set the same `DS:828` flag in a
  normal game, so they quit immediately through the same `exit(3)` path.

`DS:826` is set by `-d` too. It enables an idle timeout of `0x78` ticks (about
2 s) in the input loop. The main loop has code that clears it, but that code only runs when
`DS:828` is 0, and nothing ever clears `DS:828`.

### Winning (`-w`)

When the king's army is defeated (the `KINGLOSE` message in `Overlay_3_01f9`), the
game sets `DS:104`. At the end of that turn, the main loop saves to slot 10
(`F26_00be_0034(10)`; the same function writes the slot 8/9 autosaves in `F0_130d_0172`) and
chains to `closing -gok`. CLOSING plays the ending and chains back to
`viceroy -ow`. On startup with `DS:104` set, the start menu code loads slot 10,
and the main loop shows the `SCORED` message. If the player picks the second
answer, the game continues; otherwise it exits.

### Hot-seat multiplayer (`-k`, `COLONIZE=MULTI`)

`DS:201e` is read in only one place: the nation picker `F25_0000_0c2a`, called
from the new-game setup `F26_011e_10ae`.

- The picker draws the `NATIONS` picture. If that fails, it falls back to the
  `PICKNATION` text menu, which only has the four powers. Arrow keys cycle the
  choice through 0-3 (`DS:5398`). Mouse clicks are hit-tested against portrait
  rectangles from `F25_0000_0992`:
  `x = 0x70 + (i % 2) * 0x63`, `y = 0x0d + (i / 2) * 0x5b`, 88x82 pixels each.
- Normally `i` runs over 0-3, a 2x2 grid. With `DS:201e == 1` the loop runs to 4.
  The fifth rectangle is at x=112, y=195, so only a 5-pixel strip of it is on the
  320x200 screen, under the left column of portraits. Clicking there sets
  `DS:5398 = 4`. The highlight function `F25_0000_09c6` ignores anything above 3,
  so nothing is drawn. Enter then confirms the choice.
- `F26_011e_10ae` keeps the raw choice and resets `DS:5398` to 0 if it is above 3.
  It then sets all four powers to AI (`DS:543f + n*0x34 = 1`). If the choice was
  4, it opens the `@MULTI` dialog from `GAME.TXT` ("Multiplayer Start Selected /
  Select powers to be controlled by human players.") with a checkbox for each of
  the four powers. Every checked power becomes human (`= 0`) and the last one
  becomes `DS:5398`. If none is checked, England is human. With more than one
  human player, bit `0x80` of `DS:5381` is set.
- With that bit set, the main turn loop (`F0_130d_0290`) shows `@MULTINEXT`
  ("{power} Player Turn / Press any key for {power} player's turn.") before each
  human power's turn. `GAME.TXT` also has `@MULTIREV`: declaring independence
  turns the game back into a single-player game.

## Open questions

- Which sound cards device codes `01`, `2x` (`P`) and `38` (`G`) stand for.
