# Compatibility ledger

LabSpace targets the classic LabVIEW workflow, but **0.3.0-alpha.1 is not complete or pixel-identical LabVIEW parity**. This ledger separates working implementation from unsupported capabilities. This independent project includes no NI source, icons, driver binaries or runtime.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workbench | Classic menus/toolbars, project tree, VI tabs, paired editors, split view, help/properties/errors, broken Run arrow, keyboard Quick Drop, terminal context commands, staged formula/frame/value editors and right-click palettes | Every original dialog, palette, option, shortcut, floating OS window and exact visual layout |
| Front panel | Numeric fields, knobs, sliders, gauges, switches, LEDs, strings, numeric arrays, graphs/charts, error-cluster and complex controls/indicators, cursor and move/resize | Full control family, custom-control editor, general clusters, tables/trees, decorations, typedefs, event semantics and per-item accessibility peers |
| Diagram | Insertion, selection/marquee, dragging, typed named terminals, orthogonal wires, fan-out, probes, copy/paste, cleanup, structure/formula resizing, frame selectors, typed terminal creation/branching and cached actual-body previews | Full branch/junction editing UX, obstacle-aware routing, original icon editor, NI connector-pane patterns and full G editing semantics |
| Types | Finite doubles, Boolean, managed Unicode strings, numeric 1D arrays, waveforms with rate/start metadata, typed status/code/source error clusters and finite complex doubles | Integer widths/coercions, general clusters, variants, enums, references and general/multidimensional arrays |
| Functions | Arithmetic/transcendentals, typed Select, Boolean logic, Unicode string transforms, numeric array transforms, waveform construction, signals, gain/offset, moving average, RMS, peak-to-peak FFT, compiled scalar Formula Nodes, error operations and complex arithmetic | Complete NI function/analysis library, complete Formula Node syntax/types, DLL/.NET/Python nodes, units and full coercion rules |
| Structures | For/While/Boolean Case/embedded SubVI; typed ordinary tunnels; numeric auto-indexing; conditional collection; array concatenation; initialized/uninitialized stacked registers; unwired defaults; conditional For; feedback; multi-case Boolean/numeric/string/error dispatch; ordered sequence frames and typed locals | General indexed arrays, full NI tunnel/default/coercion rules, enum Cases, flat-sequence editing, event structures, queues/channels, linked VI libraries, polymorphism and reentrancy |
| Execution | Typed validation, cached plan, bounded managed execution, cancellation including empty bodies, cooperative nested activations, staged feedback/register state and continuous simulation | NI compiler/runtime compatibility, exact G scheduling, parallel execution, native compilation, hard real time, FPGA and GPU compute |
| Debugger | Step-over, nested step-into/step-out, active-frame values, highlight, nested breakpoints, session-only debugger overrides and a modeless cross-VI named-output probe table with bounded retained values | Persistent .lvprojstate, conditional breakpoints, watch expressions, VI profiling and full trace equivalence |
| Files | Version-3 source-generated LabSpace JSON, version-1/2 migration, model limits, native-format round trips, CSV and recovery | NI .vi/.ctl/.lvproj/.lvlib/.lvclass parsing, TDMS/LVM and NI bytecode compatibility |
| Hardware | Explicit simulated sources | NI-DAQmx, VISA, SCPI transport, serial/GPIB/USB drivers, configuration, calibration and physical I/O |
| Deployment | Shared Uno desktop/browser hosts, build and Pages workflows | Signed installers, store packages, all browser/OS combinations and physical-GPU certification |

## Structure semantics

Non-indexed tunnels and registers support the seven listed kinds. Indexing and conditional indexing collect numeric scalar elements; concatenation collects numeric arrays. This does not imply general LabVIEW array/type compatibility.

For loops take the minimum of an explicit nonnegative integral count and indexed input lengths. Zero iterations return initial/prior register state, empty collections and type defaults for last-value tunnels. While loops execute at least once; indexed input beyond an array supplies zero. Output collection includes the final stopping iteration. Conditions can mean stop-when-true or continue-while-true.

Uninitialized histories persist within one runtime and invocation path. Reset, code-affecting edits, Undo/Redo and VI/body navigation reset execution state. Pending register/feedback dictionaries commit only when the root frame succeeds. Random-generator advancement is not transactionally rolled back. Individual kernels are non-preemptive even though nested execution yields cooperatively.

SubVIs remain embedded diagrams, not linked NI VIs. [Typed structures](typed-structures.md) documents the API, migration, limits and exact collection behavior.

## Multi-frame, formula and new value boundaries

Multi-case selectors support Boolean, signed-32-bit-rounded numeric values, ordinal strings and error status/code. Numeric ranges are inclusive; string ranges exclude the upper endpoint. A single default handles unmatched values. Only the matching diagram executes, regardless of the currently visible frame. Sequence locals have one writer and can be read only in later frames of the same sequence; sequence outputs publish after every frame completes. Arbitrary references, general clusters, event structures and cross-VI scheduling are not implied.

The Formula Node is a bounded scalar subset, not the entire NI Formula Node language. It supports expressions, local/output assignments, named double inputs/outputs, short-circuit Boolean expressions, lazy ternary branches and a documented math-function set. Inputs are read-only. Bounded if/else, for/while/do loops, break/continue, lexical double/float64 declarations, compound assignments and update statements are implemented with definite-assignment analysis, instruction fuel and cancellation checks. General C types, arrays, native calls and external code are not implemented. `ln` is natural logarithm, `log`/`log10` base ten, and `log2` base two. Non-finite values and division by zero report runtime errors. See [advanced structures](advanced-structures.md).

Error clusters contain immutable Boolean status, signed-32-bit code and source; a nonzero code with false status is a warning. Merge selects the first error, otherwise first warning. This does not install hardware drivers or provide automatic NI-wide error propagation. Complex values contain finite real/imaginary doubles; general numeric polymorphism and complex arrays are not implemented.

## Reference behavior

The reference release is **LabVIEW 2026 Q3**, including the documented Patch 1 fixes, documented in [LabVIEW changes](https://www.ni.com/docs/en-US/bundle/labview/page/labview-changes.html). Its Unicode-related changes and default terminal label positions inform the implementation. LabSpace uses left-middle control labels and right-middle indicator labels, but does not implement complete 2026 Q3 byte-string/Unicode/PCRE2 semantics.

Quick Drop follows NI's documented [search and cursor-placement workflow](https://www.ni.com/en/support/documentation/supplemental/08/boost-labview-productivity-with-quick-drop.html); its full shortcut/plugin system is not implemented. Other public references include [Block Diagram Explained](https://www.ni.com/en/shop/labview/labview-block-diagram-explained.html) and the [LabVIEW overview](https://www.ni.com/en/shop/labview.html). Source and graphics are independently implemented. No claim is made that the current visual theme matches every version, platform, theme or scale factor.

## Adoption and verification

Use this release for experimentation, education, graphical-programming prototypes and reusable-component development. Do not treat simulated values as acquired measurements or deploy this runtime for safety-critical/hard-real-time control. Hardware integrations require separate design and validation.

Engine tests and real-pointer browser acceptance are automated. Three-OS desktop builds do not establish complete native-interaction compatibility. Headless browser rendering, including software GPU emulation, is not physical-GPU performance certification.
