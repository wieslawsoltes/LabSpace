# Cross-VI Debug window

Open **View → Debug window** or **Debug** beside the probe toolbar command. This modeless pane leaves both editors usable and never opens automatically on a breakpoint. The table enumerates named probes in all loaded VIs, including non-visible Case/Sequence frames. Filter by VI, diagram or terminal, Locate a wire, Remove a probe, or Clear retained values.

A probe key includes `(instrument ID, activation path, wire ID)`. Node IDs alone are insufficient because they may repeat in other diagrams or VIs. Values resolve the particular output terminal; the legacy `result` alias denotes its primary output. Live values are taken from the matching activation, not an unrelated root diagram. Completed activations retain their latest values. Repeated iterations replace the prior observation at the same invocation path rather than accumulating a trace.

## Session and source separation

Allow debugging, Retain wire values, breakpoints and probe overrides are session settings. They do not create source edits, dirty markers or Undo entries. Existing imported model markers remain defaults but can be overridden for the session. These settings are not written into NI `.lvprojstate` files or persisted across reload. Turning debugging off gates stepping, breakpoints, highlight and capture. Turn it back on to resume those facilities; Run remains available.

Locating a probe inside the same VI preserves its paused execution. Switching to a different VI aborts the active root, as ordinary VI navigation does. Captured values survive navigation. Code-affecting edits invalidate that VI's retained values; Undo/Redo clears retained observations to avoid showing results from another document state. Replacing the project clears markers and captures. Geometry changes do not invalidate computed values.

## Bounds and performance

At most 512 probes are enumerated; the filtered table shows at most 128 at once. The historical cache retains at most 128 completed frames with conservative payload accounting capped at 16 MiB. Least-recently captured frames are evicted. Immutable payloads are shared, not copied per probe. These bounds cover the debugger cache, not total process/runtime/UI memory.

The table retains row presenters and refreshes at most every 200 ms while visible and dirty. Closing it stops refresh/layout work; the common timer only checks its visibility. Capture remains separately controlled by Allow debugging and Retain wire values. Read-only test diagnostics may enumerate probes independently in `?test=1` mode.

A retained observation is not a current measurement and may belong to a child of a later-aborted root. Pending feedback/register values still commit only after root success; the debugger is an observation facility, not that transaction's commit log. No external hardware is read.

## Executable example

Open **Formula Control Flow.vi**, Run, then show Debug. Count=10 and gain=2 produce result=84 and iterations=9. Switch to Arithmetic.vi: the original result probe remains marked Retained. Locate returns to the original wire. Clear values removes observations; Run repopulates them.

## Embedding

`DebugWindowControl(InstrumentSession)` is in `LabSpace.Controls`. Call `Refresh()` from a visible/dirty scheduling policy; it owns no timer. `Fields` exposes stable named elements for host diagnostics. `InstrumentSession.GetProbes()`, `LocateProbe(DebugAddress)`, `RemoveProbe(DebugAddress)`, `SetDebuggingEnabled(bool)` and `SetRetainWireValues(bool)` are independent of Uno rendering. The complete Workbench supplies the modeless pane and coalesced scheduling.

Reference behavior: NI's [LabVIEW 2026 Q3 changes](https://www.ni.com/docs/en-US/bundle/labview/page/labview-changes.html) describe cross-VI probe visibility and no automatic Debug-window activation at breakpoints. This implementation follows those workflows, not all NI debugger, source-only-file or persistence semantics.
