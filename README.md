<div align="center">

# LabSpace

**A graphical instrumentation studio for desktop and the browser.**

Build front panels, connect typed block diagrams, inspect live values and run simulated signal-processing programs in a shared Uno Platform application.

[Open the studio](https://wieslawsoltes.github.io/LabSpace/) · [User guide](docs/user-guide.md) · [Typed structures](docs/typed-structures.md) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md)

[![Build](https://github.com/wieslawsoltes/LabSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/LabSpace/actions/workflows/build.yml)
[![Engine tests](https://github.com/wieslawsoltes/LabSpace/actions/workflows/engine.yml/badge.svg)](https://github.com/wieslawsoltes/LabSpace/actions/workflows/engine.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

![LabSpace front panel](https://wieslawsoltes.github.io/LabSpace/screenshots/front-panel.png)

## 0.2 — Typed structures and classic editing

**0.2.0-alpha.1** adds named typed tunnels, numeric auto-indexing, conditional/concatenating output collection and initialized/uninitialized stacked shift registers. Nested execution is resumable, with step-into, nested breakpoints and cancellation checks even inside empty bodies. The studio adds **Quick Drop**, cursor placement, a staged connector editor, compact terminals, named-output wiring, a broken Run arrow and cached previews drawn from each structure's actual body.

Open **Indexed Accumulator.vi** and Run: samples `1,2,3,4,5` produce a final sum of `15` and running sums `[1,3,6,10,15]`. Select the loop, open **Tunnels and shift registers**, turn off **Initialized**, and Apply. Run twice to obtain `15` then `30`. Undo restores initialization and its attached wire.

![Typed structure and named outputs](https://wieslawsoltes.github.io/LabSpace/screenshots/typed-structure.png)

LabSpace is a working independent implementation inspired by the classic NI LabVIEW workflow. **It is not yet a complete, pixel-identical or binary-compatible LabVIEW replacement.** NI VIs, the entire G language, instrument drivers and real-time/FPGA targets are not implemented. The [compatibility ledger](docs/compatibility.md) records exact supported behavior and remaining boundaries. No NI source code, proprietary artwork, drivers or runtime are included.

## Explore the application

| Area | Working functionality |
| --- | --- |
| Studio | Project explorer, VI tabs, paired Front Panel / Block Diagram editors, split view, searchable palettes, context help, properties, errors and keyboard commands |
| Front panel | Numeric controls, knobs, sliders, gauges, switches, LEDs, strings, numeric arrays, waveform graphs/charts, cursors, positioning and resizing |
| Diagram | Typed named terminals, single-driver input wiring, output fan-out, drag/marquee selection, pan/zoom, duplication, deletion, probes and undo/redo |
| Structures | For/While/Boolean Case/embedded SubVI contracts; typed ordinary tunnels; numeric input/output indexing; conditional collection; array concatenation; stacked register state |
| Execution | Cached topological plans, explicit feedback, cooperative nested activations, shared budgets, run/continuous/pause/abort, step-over/step-into and nested breakpoints |
| Functions | Arithmetic/transcendentals, Boolean logic, typed Select, Unicode string transforms, numeric array transforms, waveform construction, simulated signals, filtering, RMS, peak-to-peak and FFT |
| Documents | Version-2 JSON, version-1 migration, source-generated serialization, bounded imports/history, waveform CSV, browser IndexedDB and native recovery |

Four executable examples are included: **Signal Analysis.vi**, **Arithmetic.vi**, **Stateful Loop.vi**, and **Indexed Accumulator.vi**. The plots and indicators display results from their real dataflow graphs, not decorative sample animation. Acquisition is explicitly simulated.

Press **Ctrl+Space**, search for a function/control, press **Enter**, then click to place it. **Escape** cancels without editing. Right-click a canvas for the corresponding context palette. **Ctrl+E** switches editors; **Ctrl+R** runs; **F6** runs continuously; **F10/F11** step over/into. Canvas undo and clipboard commands do not replace text-field editing behavior.

## Rendering and performance

Both editors render with **SkiaSharp through Uno's `SKCanvasElement`**. The browser is a real C#/Uno WebAssembly application, not an HTML imitation of the interface. Browser and native desktop hosts share the model, engines, rendering code and workbench.

Rendering uses retained drawing resources, visible-area culling, cached wire paths, cached `SKPicture` structure previews and extrema-preserving waveform decimation. Immutable connector contracts are resolved through a weak identity cache. Graphs compile only when needed; pointer geometry edits do not repeatedly compile the dataflow graph.

The workbench targets a 40 ms execution timer and yields nested work after 4,096 transitions or roughly 8 ms checked after the first 16 transitions. Individual kernels still run to completion. Hardware acceleration/fallback depends on the Uno host. **DSP/dataflow run in managed C#, not WebGPU compute**, and these scheduling limits are not hard-real-time guarantees.

Run `tools/LabSpace.Benchmarks` for reproducible engine/FFT timing and allocation measurements. These are managed-engine measurements, not GPU or UI frame times. See [architecture and performance](docs/architecture.md).

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `LabSpace.Core` | Models, immutable typed values, node catalog, named terminals and immutable structure contracts |
| `LabSpace.Signals` | Signal generation, transformations, filtering, stable RMS and FFT |
| `LabSpace.Dataflow` | Graph validation/compilation, named-output execution, nested activations, register/feedback state and budgets |
| `LabSpace.Documents` | Versioned source-generated JSON, validation/migration and executable examples |
| `LabSpace.Editing` | UI-independent editing, transactions, history, wiring, connector synchronization and debugging session |
| `LabSpace.Skia` | Instrument/diagram rendering, geometry, cached previews, icons and plot decimation |
| `LabSpace.Storage` | Host-neutral storage contract and protected recovery behavior |
| `LabSpace.Controls` | Uno canvases, chrome, palette, Quick Drop, staged connector editor and property inspector |
| `LabSpace.Workbench` | Complete reusable studio, commands, navigation, execution and recovery scheduling |

All nine libraries are packable. NuGet packages and symbols are workflow artifacts; this does **not** imply publication to a public NuGet feed. The host application and tests are not library packages.

### Use the engine independently

```csharp
using LabSpace.Dataflow;
using LabSpace.Documents;

var vi = StructuredExamples.IndexedAccumulator();
var loop = vi.Diagram.Nodes.Single(node => node.Kind == "for");
var graph = GraphCompiler.Compile(vi.Diagram);
var runtime = new DataflowRuntime();

using var cancellation = new CancellationTokenSource();
var frame = runtime.Run(graph, cancellation.Token, maximumNodes: 100_000);
Console.WriteLine(frame.GetOutput(loop.Id, "state").Number); // 15
Console.WriteLine(string.Join(", ", frame.GetOutput(loop.Id, "totals").Samples));
// 1, 3, 6, 10, 15
```

`InstrumentWorkbench(session, storage, fonts)` embeds the entire studio in another Uno application. Individual controls and engines are separately reusable. See [typed structures](docs/typed-structures.md) for contract definitions and resumable execution.

## Build and run

Pinned toolchain: **.NET SDK 10.0.401**, **Uno SDK 6.7.30 / WinUI 6.7.135**, and **SkiaSharp 3.119.4** with matching managed/native assets. The selected Skia release is Uno-compatible; do not independently upgrade only its managed assembly.

```bash
git clone https://github.com/wieslawsoltes/LabSpace.git
cd LabSpace
dotnet workload install wasm-tools --skip-manifest-update
python3 scripts/fetch-assets.py
dotnet test tests/LabSpace.Tests -c Release
```

The asset script retrieves content-pinned OFL assets and checks their hashes. No proprietary platform fonts are required.

```bash
# Windows, Linux or macOS native desktop host
dotnet run --project src/LabSpace.App -f net10.0-desktop \
  -p:LabSpaceDesktopOnly=true

# Browser distribution with the GitHub Pages project base path
dotnet publish src/LabSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/LabSpace/
python3 scripts/check-native-abi.py
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Open `http://localhost:4173/LabSpace/`. In another terminal, run the real-pointer acceptance suite:

```bash
npm install --ignore-scripts
npx playwright install chromium
npm run test:browser
```

See [development](docs/development.md) for dependencies, packaging, base paths, opt-in test diagnostics and deployment provenance.

## Quality and releases

GitHub Actions runs engine tests, three-OS desktop builds, a real Uno browser publish, keyboard/pointer acceptance, screenshot capture, and Pages deployment with a source-commit check. The same browser workflows run against the public deployment. Browser tests inspect read-only diagnostics but edit the application using real input; they do not call private model-mutating test functions.

The release workflow packages all nine libraries and symbols, audits resolved Skia versions, publishes a browser distribution and records engine benchmarks. Tagged runs create a GitHub prerelease. Signed desktop installers and physical hardware/GPU certification are not included.

## Files, safety and licensing

Save projects as **`.labspace.json`**. Version-1 files migrate to version 2; older LabSpace 0.1 cannot read version-2 files. NI `.vi`, `.ctl` and `.lvproj` files are not imported. Local recovery is not a substitute for explicit saved copies. Imported JSON does not evaluate script or install drivers.

The application is intended for experimentation, education and component development. It is not validated for safety-critical equipment or physical/hard-real-time control.

[MIT license](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Changelog](CHANGELOG.md)
