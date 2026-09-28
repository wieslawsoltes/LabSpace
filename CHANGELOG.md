# Changelog

## 0.1.0-alpha.1 — 2026-09-28

Initial independent LabSpace implementation:

- Typed virtual instrument model, strict graph validation, cached topological execution, explicit frame feedback and bounded nested numeric structures.
- Signal generation, waveform operations, block moving average, stable RMS, peak-to-peak and one-sided Hann-windowed FFT.
- Transactional graphical editing, typed wiring, selection/marquee, duplication, undo/redo and dependency-rank layout.
- Original Skia-rendered front-panel instruments and block diagrams in a real Uno desktop/WebAssembly studio.
- Project explorer, VI tabs, split view, function/control palettes, properties, context help, error list and run/debug controls.
- Versioned LabSpace JSON import/export, local recovery and waveform CSV export.
- Separate packable libraries, engine/browser tests, three-OS desktop build and GitHub Pages/release workflows.

This is not full NI LabVIEW compatibility. See the compatibility ledger for unsupported language, file-format, hardware and UI capabilities.
