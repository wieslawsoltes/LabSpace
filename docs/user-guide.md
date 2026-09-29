# User guide

## Open a working project

The default project contains **Signal Analysis.vi**, **Arithmetic.vi**, **Stateful Loop.vi**, and **Indexed Accumulator.vi**. Signal Analysis runs once at startup. Its acquired waveform, RMS value, alarm, spectrum and history come from the real diagram. The source is explicitly simulated; no hardware is connected.

The project explorer and document tabs select VIs. **Front Panel** is the instrument interface; **Block Diagram** is its program. **Ctrl+E** switches editors and Split shows both. Wheel zooms around the pointer; middle-button dragging pans; Shift+wheel pans horizontally. Fit frames the current content.

## Operate and design a front panel

Run uses current control values. In Signal Analysis, drag Amplitude or click Frequency to edit it, then Run again. Run Continuously updates on a target 40 ms timer; pause/abort remain available. Click a graph to position its value cursor. Simulated sampling time is not a hardware clock.

Toggle **Edit Front Panel** to arrange controls. Drag a control to move it and its lower-right corner to resize it. Properties edits labels, ranges, dimensions and widget style. One drag creates one undo unit. Canceling the gesture restores the original state.

**Ctrl+Space** opens Quick Drop for controls. Type a name, use Up/Down to choose, press Enter and click the canvas to place it. Escape cancels. Right-click also opens the context palette; palette tiles insert directly. New controls and corresponding diagram terminals are created together. Indicators require a matching input wire before the diagram can run.

## Connect a program

Choose Block Diagram. Ctrl+Space searches functions and puts the chosen function on the cursor for placement. Click an output terminal, then an input, or drag between them. Structure outputs are individually named; choose the terminal for the value needed downstream. Connecting an occupied input replaces its source in one undoable edit. Outputs may fan out.

Numeric wires are orange, Boolean green, string pink and waveform brown. Numeric arrays use thicker orange/brown lines. Incompatible types are rejected. The compiler reports missing inputs, invalid named terminals and combinational cycles rather than silently producing default measurements. A Feedback node explicitly carries state across invocations.

Click a node to select it, Ctrl+click to extend selection, or drag a marquee on blank canvas. Move selected nodes, nudge with arrow keys, duplicate, delete, copy/paste or undo. Geometry edits do not repeatedly compile the program. Clean Up arranges nodes by dependency rank; it is not full obstacle-aware NI wire routing.

## Configure tunnels and registers

Select a For, While, Case or SubVI and open **Tunnels and shift registers** from the toolbar or Properties. Declare input/output names and types, collection modes, explicit output defaults and register histories. Changes remain a draft until Apply. Cancel leaves the document untouched. Apply synchronizes connector nodes and records one undo transaction.

Removing or renaming a terminal removes its attached wires; Undo restores them. Changing a type may leave a wire invalid until you rewire it. Required outputs must be wired unless you explicitly permit an unwired default.

Double-click a structure to edit its actual embedded body, shown in the cached thumbnail. Wire typed Connector Inputs to functions and Connector Outputs. **Parent Diagram** returns. TRUE/FALSE buttons choose the Case branch to edit; execution still follows its wired Boolean selector.

Ordinary tunnels/registers support numbers, Booleans, Unicode strings, numeric arrays, waveforms, error clusters and complex values. Numeric input auto-indexing gives each For iteration one array element; the shortest indexed array and explicit count bound execution. While indexing supplies zero after an array ends and does not terminate the loop. Outputs return their last value, collect numbers, concatenate numeric arrays, or include only iterations whose named Boolean condition is true.

At zero For iterations, collections are empty, last-value tunnels return type defaults and registers return initial/prior values. Initialized registers reset for every invocation. Uninitialized registers retain successfully committed state at their invocation path until reset, code-affecting edits, Undo/Redo or VI/body navigation. Stacked histories expose `state`, `state:1`, `state:2`, and later entries.

A While Loop requires one Loop Condition and runs at least once. Its condition can mean Stop when TRUE or Continue while TRUE. Conditional For loops combine that terminal with their count/indexing bound. The terminating iteration contributes outputs. Both loop types are bounded to 10,000 iterations and share the root node budget. Legacy numeric bodies remain supported and migrate when you apply their connector draft. See [typed structures](typed-structures.md) for exact rules.

## Try the indexed accumulator

Open **Indexed Accumulator.vi** and Run. Samples `1,2,3,4,5` produce final sum `15` and running sums `[1,3,6,10,15]`. Select its loop, open the connector editor, turn off Initialized and Apply. Run twice: the final values become `15` then `30`. Undo restores initialization and its external wire. Double-click the loop to edit the real addition and connectors.

## Debug execution

A broken Run arrow opens the error list. Resolve errors before running. **F10** steps over the next top-level node; **F11** advances nested execution. The context-help pane shows the deepest active frame, its next node and recent values without changing which body you are editing. Starting/returning from a structure is a transition, so not every F11 evaluates another primitive.

Set breakpoints in Properties or Operate. For a breakpoint inside a body, return to its caller before running it. Execution stops before that node; Run resumes. Highlight advances one activation transition per timer tick. Select a wire and attach a probe to inspect its named output. Abort discards the active frame. Pending feedback and uninitialized-register dictionaries commit only after successful root execution; this does not roll back arbitrary side effects or the random generator.

Large nested graphs yield between transitions to keep input responsive. An individual kernel still runs to completion: scheduling is cooperative, not preemptive or hard real time. Step-over may complete an entire bounded structure before returning.

## Save, recover and export

Save downloads **`.labspace.json`** with all VIs, diagrams, contracts and panel layouts. Version-1 and version-2 files migrate to version 3 on load. Earlier LabSpace releases cannot read version-3 files; retain original copies. NI `.vi`, `.ctl`, `.lvproj` and related formats are not parsed.

Browser recovery is periodic per-origin IndexedDB; native recovery uses application data. Invalid recovery is preserved instead of silently overwritten. Keep explicit saved files. Replacing a dirty project requires confirmation. Browser Open remains tied to the initiating user action so file pickers work.

Run and select a waveform/array before Export Waveform CSV. When no suitable selection is available, the first waveform result is used. FFT waveforms represent frequency on their horizontal axis. Imports never evaluate embedded script or install drivers.

## Keyboard reference

| Shortcut | Action |
| --- | --- |
| Ctrl+E | Switch Front Panel / Block Diagram |
| Ctrl+R / F6 | Run / run continuously |
| F10 / F11 | Step over / step into |
| Ctrl+Space | Quick Drop; Enter then click to place |
| Ctrl+S / Ctrl+O / Ctrl+N | Save / open project / new VI |
| Ctrl+Z / Ctrl+Y on canvas | Undo / redo |
| Ctrl+C / Ctrl+V / Ctrl+D on canvas | Copy / paste / duplicate |
| Ctrl+A / Delete on canvas | Select all / delete |
| Arrow keys on diagram | Nudge by ten world units |
| Escape | Cancel gesture, placement or Quick Drop |

Some browser/OS shortcuts take precedence. Equivalent toolbar/menu actions remain available. Text fields retain their own text editing and clipboard behavior.

## Signal and compatibility notes

Simulated sources support sine, square and triangle waveforms with optional deterministic noise. Frequency must not exceed Nyquist. Moving Average is causal but block-local. RMS is scaled to avoid unnecessary overflow. FFT uses a periodic Hann window and coherent-gain-corrected one-sided amplitudes; its sample count must be a power of two.

This is a usable independent subset, not complete NI binary, G-language, hardware, GPU-compute, FPGA or real-time compatibility. See the [compatibility ledger](compatibility.md) before adopting it for a workflow.


## Multi-frame programming and typed values (0.3)

See [advanced structures](advanced-structures.md) for executable cases/sequences, staged frame and formula editors, error/complex controls, terminal context creation and step-out. New projects include eight examples, including Formula Control Flow.vi (84 and 9 at its defaults). Existing recovery projects retain their own examples; use **File → Load example project** after saving work to load the expanded example collection.

## Cross-VI debugging and browser-storage validation

See [Debugger](debugger.md) for session-only overrides, scoped retained values, activation navigation, bounds and reusable controls. Run `npm run test:storage` for dependency-free IndexedDB connection/transaction and file-picker lifecycle regressions. Recovery writes are acknowledged only when the transaction commits; malformed saved records are rejected rather than replaced with an empty project. Browsers remain subject to storage quota/eviction: keep explicit project saves.
