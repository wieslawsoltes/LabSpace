# Changelog

## 0.2.0-alpha.1

- Named typed input/output contracts for For, While, Boolean Case and embedded SubVI structures.
- Numeric input/output auto-indexing, conditional output collection, array concatenation, optional output defaults and conditional For termination.
- Initialized/uninitialized stacked shift registers with call-site state and root-frame commit semantics.
- Cooperative nested activations, nested breakpoints, step-into and active-frame value inspection; cancellation checks include empty loop bodies.
- Quick Drop (Ctrl+Space), right-click palettes and real pointer placement on both canvases. Modified Space routing also works when a toolbar/tab button owns focus.
- Staged connector editor, synchronized typed body terminals and atomic Apply/Cancel/Undo behavior.
- Compact terminals, control/indicator label positioning, named-output hit targets, scalar/array wire widths, a broken Run arrow and cached real-body structure previews.
- Extended numeric, Boolean, string, array and waveform kernels, plus Indexed Accumulator.vi.
- Version-2 project files with version-1 migration, expanded engine/browser tests, machine-readable browser reports and updated in-app/API documentation.

This increment does not add NI binary VI compatibility, hardware drivers, FPGA/real-time targets or complete G-language/UI parity.

## 0.1.0-alpha.1 — 2026-09-28

Initial independent implementation:

- Typed VI model, graph validation, cached topological execution, feedback and bounded nested numeric structures.
- Signal generation/transforms, moving average, stable RMS, peak-to-peak and Hann-windowed FFT.
- Transactional editing, typed wiring, selection, duplication, undo/redo and dependency-rank layout.
- Skia front-panel instruments and block diagrams in real Uno desktop/WebAssembly hosts.
- Project explorer, VI tabs, split view, palettes, properties, help, errors and run/debug controls.
- JSON persistence, local recovery and waveform CSV.
- Separate packable libraries, engine/browser tests, three-OS builds and Pages/release workflows.
