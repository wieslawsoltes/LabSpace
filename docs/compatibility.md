# Compatibility ledger

LabSpace targets the familiar LabVIEW workflow, but **0.1.0-alpha.1 is not complete or pixel-identical LabVIEW parity**. This ledger separates working implementation from unsupported capabilities. NI and LabVIEW are trademarks of their owners. This independent project includes no NI source, icons, driver binaries or runtime.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workbench | Classic menus/toolbars, project tree, VI tabs, paired front panel/diagram, split view, context help, properties, error list | Every original dialog, palette, option, shortcut, floating OS window and exact visual layout |
| Front panel | Numeric fields, knobs, sliders, gauges, switches, LEDs, strings, numeric arrays, waveform graphs/charts, cursor, move/resize | Full control family, custom-control editor, clusters, trees/tables, decorations, typedefs, event semantics and native accessibility peers for each item |
| Diagram | Node insertion, selection/marquee, dragging, typed terminals, orthogonal wires, fan-out, probes, duplicate/copy/paste, simple clean-up | Full wire editing/branch junction UX, obstacle-aware routing, original icon editor, connector panes and full G editing semantics |
| Types | Finite doubles, Boolean, UTF-16 strings, numeric arrays, waveforms with rate/start metadata | Integer widths/coercion dots, clusters, variants, enums, references, errors as typed clusters, complex numbers, multidimensional/general arrays |
| Functions | Arithmetic, comparisons, Boolean, string, array, signal generation, gain/offset, block moving average, RMS, peak-to-peak, FFT and waveform extraction | NI's complete function/analysis library, expression/formula nodes, native DLL/.NET/Python nodes, comprehensive units and type coercion |
| Structures | Editable numeric embedded For, While, Case and SubVI bodies, explicit frame feedback | Arbitrary typed tunnels, auto-indexing, LabVIEW shift registers, sequence locals, events, queues/channels, linked VI libraries, polymorphism and reentrancy |
| Execution | Typed validation, cached topological plan, bounded deterministic managed execution, cancellation API, runtime node errors, continuous simulation | LabVIEW compiler/runtime compatibility, exact G scheduling, parallel execution, compilation to native code, hard real time, FPGA and GPU compute |
| Debugger | Top-level step-over, highlight execution, breakpoints, wire probes, validation navigation | Nested step-into/out, conditional breakpoints, watch expressions, VI profiling and execution-trace equivalence |
| Files | Versioned source-generated LabSpace JSON, strict model limits, complete native-format round trips, CSV waveform export, recovery | NI .vi/.ctl/.lvproj/.lvlib/.lvclass parsing, TDMS/LVM interchange, native VI bytecode or file-format compatibility |
| Hardware | Explicit simulated sources | NI-DAQmx, VISA, SCPI hardware transport, serial/GPIB/USB drivers, hardware configuration, calibration and physical I/O |
| Deployment | Shared Uno desktop and real browser hosts, reproducible build and Pages workflows | Signed installers, store packages, every supported browser/OS combination and physical-GPU performance certification |

## Reference behavior

The implementation uses public NI documentation for workflow reference, including [LabVIEW Block Diagram Explained](https://www.ni.com/en/shop/labview/labview-block-diagram-explained.html) and the [LabVIEW product overview](https://www.ni.com/en/shop/labview.html). Source and graphics were independently implemented. Compatibility is assessed against documented behavior, not against proprietary implementation details.

The selected workflow is the classic desktop front-panel/block-diagram environment. No claim is made that the current visual theme matches every NI version, platform, theme or scale factor.

## Adoption guidance

Use this release for experimentation, education, graphical-programming prototypes and development of the reusable components. Do not treat simulated values as acquired measurements or deploy the current runtime for safety-critical or hard-real-time equipment control. Hardware integrations require separate design, security review and validation before use.
