# User guide

## Start with the examples

Open the [browser studio](https://wieslawsoltes.github.io/LabSpace/). The initial project contains three working virtual instruments: **Signal Analysis**, **Arithmetic**, and **Stateful Loop**. The startup VI executes once so its indicators contain real calculated results. All acquisition is simulated; no physical hardware is accessed.

Use the project explorer or document tabs to switch VIs. **Front Panel** shows controls and indicators. **Block Diagram** shows the executable graph. **Split** shows both. Ctrl+E switches between the first two views.

## Operate an instrument

In Signal Analysis, drag the amplitude knob or click the frequency numeric field. Press **Run** to calculate a frame or **Run Continuously** to repeat. The filtered waveform, RMS voltage, limit alarm, spectrum and bounded chart history are connected to the actual diagram outputs. **Abort** stops repetition. **Pause** pauses and resumes the current session. Click a graph to place a sample cursor.

A knob's drag sensitivity spans its configured range over approximately 180 DIP of combined horizontal/vertical movement. Numeric field spinners increment by one. Value dialogs accept invariant-culture decimal points. A control's minimum and maximum govern pointer operation; the engine separately validates function-specific constraints, such as the signal source's Nyquist limit.

## Create and wire a VI

Choose **File → New VI**. In the front panel, click a control or indicator in the palette. Each added control has a corresponding block-diagram terminal. In the diagram, click a function from the Functions palette to insert it near the center of the viewport. Search filters the available functions.

Wire by clicking an output terminal on the right of a node, then an input on the left of another node. Dragging directly between terminals also works. The square's color indicates its type: orange numeric, green Boolean, pink string, brown waveform. Outputs can feed multiple inputs. Connecting a new source to an already-wired input replaces its previous source in one undoable operation. Type mismatches and combinational cycles are rejected; use an explicit Feedback node for previous-frame state.

Select a wire and press **Probe** to inspect its most recent value. Select a function and open **Properties** to edit its label, constants, source configuration, filter window or unwired optional-input defaults. Required unwired inputs appear in the error list and prevent execution.

## Edit layout

Drag node bodies to move them on the diagram's ten-unit grid. Ctrl-click adds or removes nodes from a selection. Drag a marquee in empty diagram space to select multiple nodes. Arrow keys nudge a focused diagram selection. **Clean Up Diagram** lays out nodes by dependency rank. The layout algorithm is intentionally simple; wires are orthogonally routed but not fully obstacle-avoiding.

On the front panel, toggle **Edit Front Panel** before moving controls. Drag the lower-right corner to resize. Properties exposes labels, numeric bounds, dimensions and compatible widget styles. Numeric nodes can use numeric fields, knobs, sliders and gauges; Boolean nodes can use switches or LEDs. Front-panel and diagram deletion are linked and undoable.

Mouse wheel zooms around the pointer. Middle-button drag pans. Shift+wheel pans horizontally. **Fit to Window** frames all content. Large content is drawn within a clipped viewport; visible traces are decimated to pixel resolution.

## Debug execution

**Run** executes a topological frame. **Highlight Execution** executes one top-level node per timer tick, making the sequence visible. **Single Step** executes the next top-level node without continuous progression. A nested structure is currently one step-over operation.

Select a node and set its breakpoint in Properties or the Operate menu. Execution stops before the node runs; Run resumes. Probes expose the last computed source value on selected wires. Errors include the responsible node when available. The error-list button shows structured compile diagnostics and lets you select the affected node.

Feedback outputs read state from the previous completed root frame. New feedback input values are committed only after the root frame completes successfully. Abort discards an incomplete frame. A structural edit resets execution and graph state; geometry and selection edits do not recompile the graph.

## Nested structures

Double-click a For Loop, While Loop, Case Structure or SubVI to enter its real nested body. **Parent Diagram** returns to the containing diagram. Connector Input nodes read `state`, `i` or `x` from the invocation. Exactly one numeric Connector Output returns a result. A While Loop additionally needs exactly one Boolean Loop Condition terminal.

For Loop carries the numeric state through an integer count of iterations. While Loop runs at least once and stops when its condition becomes true; exceeding 10,000 iterations reports an error. Case Structure executes only its selected branch; Properties opens its FALSE alternative. The supplied bodies illustrate numeric state flow and are editable with the same node tools. These are not full LabVIEW tunnel, auto-indexing or shift-register semantics.

## Files

**Save Project** downloads a `.labspace.json` file containing all VIs, diagrams and panel layouts. **Open Project** reads this format. It does not parse NI `.vi`, `.ctl` or `.lvproj` binary files. Unsupported versions, oversized files and malformed models are rejected rather than silently imported as partial documents.

Browser recovery is stored in IndexedDB on the same origin. Desktop recovery is stored under the current user's application-data directory. Recovery is periodic, so keep explicit saves of important work. Export Waveform CSV writes the selected waveform or first available waveform result, using invariant numeric formatting. Imported projects are data; they cannot run JavaScript or install hardware drivers.

## Keyboard reference

| Shortcut | Action |
| --- | --- |
| Ctrl+E | Switch front panel / block diagram |
| Ctrl+R | Run |
| F6 | Run continuously |
| F10 | Single top-level step |
| Ctrl+S / Ctrl+O / Ctrl+N | Save / open / new VI |
| Ctrl+Z / Ctrl+Y on canvas | Undo / redo |
| Ctrl+C / Ctrl+V on canvas | Internal graph copy / paste |
| Ctrl+D on canvas | Duplicate selected subgraph |
| Ctrl+A on canvas | Select all nodes |
| Delete on canvas | Delete selection |
| Arrows on diagram | Nudge selection |
| Escape on canvas | Cancel the current gesture |

Some browser or OS shortcuts take precedence. Equivalent toolbar and menu commands remain available. Text fields retain native text editing services. Canvas copy/paste is internal to the session, not a general-purpose clipboard format.
