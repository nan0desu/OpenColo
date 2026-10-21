# VICEROY.EXE timer

How the game keeps time: its own INT 8 handler on a reprogrammed PIT, and the
water color cycling that runs from it. Names and address conventions are
described in [memory.md](memory.md).

## Tick counter

`F0_1c0c_0006` returns the 32-bit value at the far pointer `DS:267a`. In the data
image that pointer is `0040:006c`, the BIOS tick count. While the game runs it
points to the game's own counter at `DS:92e8`.

The first thing `game_main` (`F26_011e_2d46`) does after `F30_0000_006a` is call
`F0_1a29_015b`. That function:

- zeroes the counters,
- saves the old INT 8 vector at `CS:0000` of segment `0a29` and installs the
  handler at `0a29:0004`,
- programs PIT channel 0 with divisor `0x7a8` (`F0_1c10_0008`: mode `0x36` to
  port 43h, then the divisor to port 40h). 1193182 / 1960 gives about 608.8 Hz,
- points `DS:267a` at `DS:92e8` and sets `DS:379` to mark the handler as installed.

`F0_1a29_01d1` undoes all of this: it restores the vector, sets the PIT divisor
back to 0 (18.2 Hz), and points `DS:267a` at `0040:006c` again. It is called at
the end of `game_main`, before `exit(3)` in `F29_0000_014e`, and in the chain
function `F0_130d_000a` (see [startup.md](startup.md#the-program-chain)).

## INT 8 handler

The handler isn't in the generated code, because no thunk leads to it. Read from the
raw bytes, one PIT tick (608.8 Hz) does this:

1. `DS:8338` (32-bit) is incremented. `F0_1c0c_0022` returns it. On odd values
   the handler sends EOI and returns.
2. On even values (304.4 Hz) it switches to its own stack at `DS:0484+0xfe`. If
   `DS:92f4` is set, it calls `1059:0067`, the sound driver's music tick (see
   [sound.md](sound.md#calling-the-driver)).
3. Every 5th of those ticks (60.88 Hz, counter `DS:376`):
   - if `DS:92f6` (the driver segment) is nonzero, it calls `1059:0063`, the
     driver status, and the result sets or clears `DS:92f4`;
   - it increments `DS:92e8`, the game tick counter.
4. If a callback is registered (`F0_1a29_021b`, far pointer at `DS:92e4`, enabled
   when `DS:92f0` is nonzero), it is called at 60.88 Hz on a second stack at
   `DS:0384+0xfe`. `DS:92f2` guards against re-entry, and ticks missed while the
   callback is busy are counted in `DS:92ee`. The only callback ever registered is
   the water color cycler `0a0a:007a` (see below), registered by `F0_1262_0142`.
5. Every 3rd 60.88 Hz tick (counter `DS:377`), the handler chains to the old BIOS
   INT 8 handler. Before that it clears bit 5 of `0040:0017`, which turns NumLock
   off. When a callback is running, the chain is postponed to the next tick
   through `DS:92ec`.

Consequences:

- The BIOS sees 20.29 ticks per second instead of 18.2, so the DOS clock runs about
  11.5% fast while the game is running.
- The game's time unit is 1/60.88 s. Delays elsewhere use this unit: the `0x78`
  idle timeout in the input loop is about 2 s, and the demo's `0x3840` limit is
  about 3 min 57 s (see [startup.md](startup.md#demo-mode--d)).
- The emulator has to deliver IRQ 0 at the rate the game programmed into the PIT.
  Game time, music and color cycling all come from this interrupt; nothing polls
  a clock.

## VGA timing calibration

Before the timer is installed, `game_main` calls `F30_0000_006a`, which measures
the display with the PIT still at its default rate:

1. 32 times: wait for the start of vertical retrace (bit 3 of port 3DAh), latch
   PIT channel 0 (`out 43h, 0`, then two reads of port 40h), wait for the next
   retrace, and latch again. The average difference is the frame length in PIT
   counts, stored in `DS:802`.
2. A search over the number of palette colors that can be written in one frame
   (`F30_0000_0192`, timing `F30_0000_0000`), starting at 128 with a step of 64
   that halves each round, for at most 64 rounds.
3. `DS:804` = 14 x that count, or `0x20` if the search never found a count that
   fits. `DS:806 = 3 x DS:804` is the number of palette bytes written per vertical
   retrace by `F0_1ade_0004` and `F0_1c2e_0022`. `DS:800 = 1` marks the
   calibration as done.

An emulator needs a working PIT channel 0 latch and readback and a retrace bit
that changes with time. Otherwise the measured frame length is 0 or the loop
never ends.

## Water color cycling

The 60.88 Hz callback `0a0a:007a` rotates palette ranges. This is the
"Water Color Cycling" item in the game options menu (`@GAMEOPTIONS` in `GAME.TXT`).

**Setup in `game_main`, just before the main turn loop:**

1. `F30_0051_0004` reads `0x22` bytes of `CYCLE.DAT` into `DS:929e`: a 16-bit
   entry count, then 8 entries of 4 bytes at `DS:92a0`:

   | byte | meaning |
   | --- | --- |
   | 0 | number of colors in the range |
   | 1 | current phase (reset to 0 at setup) |
   | 2 | first palette index |
   | 3 | delay between steps, in game ticks |

2. `F0_1a0a_0004(enable)` sets every entry's next due time (32-bit, at
   `DS:92c4 + i*4`) to the current tick and clears the phases. It sums the color
   counts into `DS:92c2` and sets the throttle `DS:92c0`: 3 if more than 16 colors
   cycle, 0 otherwise. Finally it stores `enable` in `DS:372`.
3. `F0_1262_0142` registers the callback with the timer.

The shipped `CYCLE.DAT` has a count of 1. Its only real entry is 8 colors, indices
120-127, with a delay of 35 ticks: one step every 0.575 s, a full cycle in 4.6 s.
The other seven slots hold leftover bytes, apparently a slice of x86 code, and are
never read.

**Each callback** (when `DS:372` is set):

- It skips the call unless `DS:374` has counted up to the throttle `DS:92c0`, and
  also skips while `DS:808` is set. `DS:808` is the "DAC busy" flag, set by the
  palette upload routines `F0_1ade_0004` and `F0_1c2e_0022` while the main
  thread writes ports 3C8h/3C9h. Those routines write `DS:806` bytes per vertical
  retrace (see [VGA timing calibration](#vga-timing-calibration)).
- For every entry that is due, it moves the due time to now and rotates the
  entry's RGB triples one step forward in the palette copy. The far pointer to
  that copy is at `DS:36e`, initially `A000:FC00`, which is VGA memory past the
  64000 visible bytes. The last color goes into a 3-byte buffer at `DS:2d06`,
  the rest move up one slot (`std; rep movsb`), and the buffer goes into the
  first slot. The phase advances modulo the length.
- If anything rotated, it uploads `DS:92c2` colors starting at the first entry's
  start index through `F0_1c2e_0022`, which waits for vertical retrace, and resets
  `DS:374`. This upload assumes all ranges form one block starting at entry 0. That
  holds for the single shipped entry.

**On/off.** Bit `0x01` of `DS:5383` means "cycling off". The options menu
(`Overlay_1`, around line 4035) sets it when item 7 is unchecked and then
recomputes `DS:372 = !(DS:5383 & 1)`. Dialogs that replace the palette
(`Overlay_23`, `Overlay_5`, `Overlay_6`, `Overlay_22_027e`, ...) save `DS:372`,
clear it while they run, and restore it afterwards. The chain function clears it
before `exec`.

## Open questions

- What the timed loop `F30_0000_0000` measures exactly, and why the result is
  multiplied by 14.
- Which map graphics use palette indices 120-127 (presumably the ocean tiles).
