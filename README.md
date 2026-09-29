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

## 0.3 — Structured programs and cross-VI debugging

**0.3.0-alpha.1** adds executable numeric/string/Boolean/error Case Structures, ordered sequence frames and typed sequence locals, a compiled scalar Formula Node, error-cluster and complex-number controls and functions. Staged editors validate changes before Apply, named terminals support right-click creation and branching, and nested debugging includes step-out. Structure frame selectors, actual-body previews and resize handles are shared by the desktop and browser editors.

Open **Case Dispatch.vi**: the command `run` selects a frame producing `42`. Open **Sequence Pipeline.vi**: an earlier frame writes `21`, a later formula computes `84`. **Errors and Complex.vi** displays a real typed error cluster and computes the magnitude of `3 + 4i` as `5`. Edit their case labels, formula, error code/source or real/imaginary components to change actual results. These are executable diagrams, not animations.

**Formula Control Flow.vi** evaluates a bounded `for` loop with `continue` into separately wired results `84` and `9`. Open **View → Debug window** to inspect its probe, switch to another VI and use **Locate** to return. Debugging flags and probe/breakpoint overrides are session state: toggling them does not dirty source or create Undo records.

![Cross-VI Debug window](https://wieslawsoltes.github.io/LabSpace/screenshots/debug-window.png)

[Debugger guide](docs/debugger.md) · [Advanced structures guide](docs/advanced-structures.md) · [Typed loops and registers](docs/typed-structures.md) · [Compatibility ledger](docs/compatibility.md)

![Sequence with executable frames](https://wieslawsoltes.github.io/LabSpace/screenshots/sequence.png)

LabSpace is a working independent implementation inspired by the classic NI LabVIEW workflow. **It is not yet a complete, pixel-identical or binary-compatible LabVIEW replacement.** NI VIs, the entire G language, instrument drivers and real-time/FPGA targets are not implemented. The [compatibility ledger](docs/compatibility.md) records exact supported behavior and remaining boundaries. No NI source code, proprietary artwork, drivers or runtime are included.

## Explore the application

| Area | Working functionality |
| --- | --- |
| Studio | Project explorer, VI tabs, paired Front Panel / Block Diagram editors, split view, searchable palettes, context help, properties, errors and keyboard commands |
| Front panel | Numeric controls, knobs, sliders, gauges, switches, LEDs, strings, numeric arrays, waveform graphs/charts, error-cluster and complex controls/indicators, cursors, positioning and resizing |
| Diagram | Typed named terminals, single-driver input wiring, output fan-out, drag/marquee selection, pan/zoom, duplication, deletion, probes and undo/redo |
| Structures | Multi-case dispatch, sequential frames/locals, For/While/Boolean Case/embedded SubVI contracts; typed ordinary tunnels; numeric input/output indexing; conditional collection; array concatenation; stacked register state |
| Execution | Cached topological plans, explicit feedback, cooperative nested activations, shared budgets, run/continuous/pause/abort, step-over/step-into and nested breakpoints, scoped cross-VI probes and session-only debug options |
| Functions | Arithmetic/transcendentals, Boolean logic, typed Select, Unicode string transforms, numeric array transforms, waveform construction, simulated signals, filtering, RMS, peak-to-peak FFT, compiled scalar formulas, errors and complex arithmetic |
| Documents | Version-3 JSON, version-1/2 migration, source-generated serialization, bounded imports/history, waveform CSV, browser IndexedDB and native recovery |

Eight executable examples are included: **Signal Analysis.vi**, **Arithmetic.vi**, **Stateful Loop.vi**, **Indexed Accumulator.vi**, **Case Dispatch.vi**, **Sequence Pipeline.vi**, **Errors and Complex.vi**, and **Formula Control Flow.vi**. The plots and indicators display results from their real dataflow graphs, not decorative sample animation. Acquisition is explicitly simulated.

Press **Ctrl+Space**, search for a function/control, press **Enter**, then click to place it. **Escape** cancels without editing. Right-click a canvas for the corresponding context palette. **Ctrl+E** switches editors; **Ctrl+R** runs; **F6** runs continuously; **F10/F11** step over/into; **Ctrl+F11** steps out. Canvas undo and clipboard commands do not replace text-field editing behavior.

## Rendering and performance

Both editors render with **SkiaSharp through Uno's `SKCanvasElement`**. The browser is a real C#/Uno WebAssembly application, not an HTML imitation of the interface. Browser and native desktop hosts share the model, engines, rendering code and workbench.

Rendering uses retained drawing resources, visible-area culling, cached wire paths, cached `SKPicture` structure previews and extrema-preserving waveform decimation. Immutable connector contracts are resolved through a weak identity cache. Graphs compile only when needed; pointer geometry edits do not repeatedly compile the dataflow graph.

The workbench targets a 40 ms execution timer and yields nested work after 4,096 transitions or roughly 8 ms checked after the first 16 transitions. Individual kernels still run to completion. Hardware acceleration/fallback depends on the Uno host. **DSP/dataflow run in managed C#, not WebGPU compute**, and these scheduling limits are not hard-real-time guarantees.

Run `tools/LabSpace.Benchmarks` for reproducible engine/FFT timing and allocation measurements. These are managed-engine measurements, not GPU or UI frame times. See [architecture and performance](docs/architecture.md).

## Download

Every [release](https://github.com/wieslawsoltes/LabSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `LabSpace-<version>-win-x64.zip` | `LabSpace-<version>-win-arm64.zip` |
| macOS | `LabSpace-<version>-osx-x64.tar.gz` | `LabSpace-<version>-osx-arm64.tar.gz` |
| Linux | `LabSpace-<version>-linux-x64.tar.gz` | `LabSpace-<version>-linux-arm64.tar.gz` |

Extract and run `LabSpace` (`LabSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine LabSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS`. Releases also include the browser distribution and engine benchmarks.

The libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=LabSpace), e.g. `dotnet add package LabSpace.Core`.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `LabSpace.Core` | Models, immutable typed values, node catalog, named terminals and immutable structure contracts |
| `LabSpace.Signals` | Signal generation, transformations, filtering, stable RMS and FFT |
| `LabSpace.Dataflow` | Graph validation/compilation, bounded formula bytecode, named-output execution, nested activations, register/feedback state and budgets |
| `LabSpace.Documents` | Versioned source-generated JSON, validation/migration and executable examples |
| `LabSpace.Editing` | UI-independent editing, transactions, history, wiring, connector synchronization and debugging session |
| `LabSpace.Skia` | Instrument/diagram rendering, geometry, cached previews, icons and plot decimation |
| `LabSpace.Storage` | Host-neutral storage contract and protected recovery behavior |
| `LabSpace.Controls` | Uno canvases, chrome, palette, Quick Drop, staged editors, cross-VI Debug window and property inspector |
| `LabSpace.Workbench` | Complete reusable studio, commands, navigation, execution and recovery scheduling |

All nine libraries are published to NuGet.org with symbols. The host application and tests are not library packages.

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

The release workflow runs for `v*` tags, matching main-branch changes or a supplied manual version. It runs storage and engine tests, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), packs all nine versioned libraries with symbols, audits resolved Skia versions, publishes a browser distribution, records engine benchmarks and emits `SHA256SUMS`. Tags attach all assets to a GitHub Release (prerelease for `-` versions) and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Other runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing. Code-signed desktop installers and physical hardware/GPU certification are not included.

## Files, safety and licensing

Save projects as **`.labspace.json`**. Version-1 and version-2 files migrate to version 3. Earlier LabSpace releases cannot read version-3 files; retain original copies when migrating. NI `.vi`, `.ctl` and `.lvproj` files are not imported. Local recovery is not a substitute for explicit saved copies. Imported JSON does not evaluate script or install drivers.

The application is intended for experimentation, education and component development. It is not validated for safety-critical equipment or physical/hard-real-time control.

[MIT license](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Changelog](CHANGELOG.md)
