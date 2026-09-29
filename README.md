<div align="center">

# LabSpace

**A graphical instrumentation studio for desktop and the browser.**

Build front panels, connect typed block diagrams, inspect live values and run simulated signal-processing programs in a shared Uno Platform application.

[Open the studio](https://wieslawsoltes.github.io/LabSpace/) · [User guide](docs/user-guide.md) · [Typed structures](docs/typed-structures.md) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md)

[![Build](https://github.com/wieslawsoltes/LabSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/LabSpace/actions/workflows/build.yml)
[![Engine tests](https://github.com/wieslawsoltes/LabSpace/actions/workflows/engine.yml/badge.svg)](https://github.com/wieslawsoltes/LabSpace/actions/workflows/engine.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/LabSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/LabSpace.Core.svg)](https://www.nuget.org/packages/LabSpace.Core)

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

## NuGet packages

The studio is built from nine MIT-licensed packages that are versioned and released together; the host application and tests are not packages. Seven packages (`Core` through `Skia`) target plain `net10.0` and have no UI dependency; only `LabSpace.Skia` pulls in SkiaSharp (3.119.4, the Uno-compatible build). `Controls` and `Workbench` are Uno Platform libraries targeting `net10.0-desktop` and `net10.0-browserwasm`. Every package ships symbols to NuGet.org (`.snupkg`) with SourceLink.

```sh
dotnet add package LabSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [LabSpace.Core](https://www.nuget.org/packages/LabSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Core.svg)](https://www.nuget.org/packages/LabSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Core.svg)](https://www.nuget.org/packages/LabSpace.Core) | VI/diagram/panel models, immutable typed values, node catalog and structure contracts |
| [LabSpace.Storage](https://www.nuget.org/packages/LabSpace.Storage) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Storage.svg)](https://www.nuget.org/packages/LabSpace.Storage) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Storage.svg)](https://www.nuget.org/packages/LabSpace.Storage) | Host-neutral open/save/export/recovery contract and protected recovery |
| [LabSpace.Signals](https://www.nuget.org/packages/LabSpace.Signals) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Signals.svg)](https://www.nuget.org/packages/LabSpace.Signals) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Signals.svg)](https://www.nuget.org/packages/LabSpace.Signals) | Bounded signal generation, filtering, stable RMS and FFT |
| [LabSpace.Documents](https://www.nuget.org/packages/LabSpace.Documents) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Documents.svg)](https://www.nuget.org/packages/LabSpace.Documents) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Documents.svg)](https://www.nuget.org/packages/LabSpace.Documents) | Versioned source-generated JSON, validation/migration and executable example VIs |
| [LabSpace.Dataflow](https://www.nuget.org/packages/LabSpace.Dataflow) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Dataflow.svg)](https://www.nuget.org/packages/LabSpace.Dataflow) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Dataflow.svg)](https://www.nuget.org/packages/LabSpace.Dataflow) | Typed graph compiler, formula bytecode and deterministic, bounded execution |
| [LabSpace.Editing](https://www.nuget.org/packages/LabSpace.Editing) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Editing.svg)](https://www.nuget.org/packages/LabSpace.Editing) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Editing.svg)](https://www.nuget.org/packages/LabSpace.Editing) | UI-independent editing, transactions, history, wiring, execution and debugger session |
| [LabSpace.Skia](https://www.nuget.org/packages/LabSpace.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Skia.svg)](https://www.nuget.org/packages/LabSpace.Skia) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Skia.svg)](https://www.nuget.org/packages/LabSpace.Skia) | Instrument, plot and diagram rendering, geometry, cached previews and icons |
| [LabSpace.Controls](https://www.nuget.org/packages/LabSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Controls.svg)](https://www.nuget.org/packages/LabSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Controls.svg)](https://www.nuget.org/packages/LabSpace.Controls) | Uno canvases, chrome, palette, Quick Drop, staged editors, Debug window and inspector |
| [LabSpace.Workbench](https://www.nuget.org/packages/LabSpace.Workbench) | [![NuGet](https://img.shields.io/nuget/vpre/LabSpace.Workbench.svg)](https://www.nuget.org/packages/LabSpace.Workbench) | [![Downloads](https://img.shields.io/nuget/dt/LabSpace.Workbench.svg)](https://www.nuget.org/packages/LabSpace.Workbench) | Complete reusable Uno studio: commands, navigation, execution and recovery scheduling |

Dependencies (from project references):

```text
Core ← Signals ← Dataflow
Core ← Documents
Dataflow + Documents ← Editing ← Skia (+ SkiaSharp) ← Controls (Uno)
Controls + Storage ← Workbench
Storage has no dependencies
```

See [typed structures](docs/typed-structures.md) for contract definitions and resumable execution, and [architecture](docs/architecture.md) for layering.

### LabSpace.Core

The document and type model: projects, virtual instruments (VIs), diagrams of nodes and wires, front-panel items, immutable typed `Value`s (number, Boolean, string, array, waveform, error cluster, complex) and the catalog of node definitions with named terminals and structure contracts. No dependencies beyond .NET; no UI.

```sh
dotnet add package LabSpace.Core --prerelease
```

**Key types**

- `LabProject` / `VirtualInstrument` / `Diagram` / `Node` / `Wire` / `PanelItem` — the serializable program model.
- `Value` / `ValueKind` — immutable typed values: `Numeric`, `Bool`, `String`, `Vector`, `Series`, `ErrorValue`, `ComplexValue`.
- `NodeCatalog` — `All`, `Get(kind)`, `Resolve(node)` for terminal definitions.
- `StructureContract` — tunnels, indexing modes and shift registers for loops/structures.
- `StructureFrame` / `FormulaSignature` / `ErrorCluster` — case/sequence frames, formula terminals and error clusters.

**Usage**

```csharp
using LabSpace.Core;

Value wave = Value.Series([0, 1, 0, -1], sampleRate: 1000);
Value error = Value.ErrorValue(status: true, code: 42, source: "Acquire");
Value z = Value.ComplexValue(3, 4);
Console.WriteLine($"{wave.Kind}, |z| = {z.Complex.Magnitude}");   // Waveform, |z| = 5

NodeDefinition add = NodeCatalog.Get("add");
Console.WriteLine(string.Join(", ", add.Inputs.Select(p => $"{p.Name}: {p.Kind}")));   // x: Number, y: Number
```

### LabSpace.Storage

A small host-neutral contract for opening, saving, exporting and recovering projects, plus an in-memory implementation and a wrapper that stops automatic recovery from overwriting a recovery copy that failed to load until the user explicitly saves. No dependencies; no UI.

```sh
dotnet add package LabSpace.Storage --prerelease
```

**Key types**

- `IProjectStorage` — `OpenAsync`, `SaveAsync`, `ReadRecoveryAsync`, `WriteRecoveryAsync`, `ExportAsync`.
- `MemoryProjectStorage` — in-memory implementation for tests and headless hosts.
- `ProtectedRecoveryStorage` — suppresses recovery writes until an explicit save succeeds.

**Usage**

```csharp
using LabSpace.Storage;

var memory = new MemoryProjectStorage();
IProjectStorage storage = new ProtectedRecoveryStorage(memory);

await storage.WriteRecoveryAsync("{}");                       // ignored: nothing saved yet
await storage.SaveAsync("Demo.labspace.json", projectJson);   // saves and re-enables recovery
Console.WriteLine(memory.Recovery is not null);               // True
```

### LabSpace.Signals

Deterministic signal kernels used by the dataflow functions: sine/square/triangle generation with seeded noise, moving-average filtering, numerically stable RMS and a Hann-windowed radix-2 FFT magnitude spectrum. Depends on `LabSpace.Core` (for `Value`); no UI.

```sh
dotnet add package LabSpace.Signals --prerelease
```

**Key types**

- `SignalMath.Generate` — bounded waveform generation (2–65,536 samples, up to Nyquist).
- `SignalMath.MovingAverage`, `Transform` — filtering and per-sample transforms.
- `SignalMath.Rms` — overflow-safe RMS.
- `SignalMath.Spectrum` — magnitude spectrum of a power-of-two waveform.

**Usage**

```csharp
using LabSpace.Core;
using LabSpace.Signals;

Value sine = SignalMath.Generate(count: 1024, rate: 2000, frequency: 50, amplitude: 2,
    start: 0, shape: "Sine", noise: 0.05);
Value smooth = SignalMath.MovingAverage(sine, window: 8);
double rms = SignalMath.Rms(smooth.Samples);
Value spectrum = SignalMath.Spectrum(sine);        // 513 bins
```

### LabSpace.Documents

Persistence and examples: version-3 `.labspace.json` with source-generated serialization, validation, 8 MiB bounds and migration from versions 1 and 2, plus builders for the eight executable example VIs. Depends on `LabSpace.Core`; no UI.

```sh
dotnet add package LabSpace.Documents --prerelease
```

**Key types**

- `ProjectSerializer` — `Save`, `Load` (validating/migrating), `Clone`, `Validate`.
- `Examples` — `Create()` (the example project), `Blank`, `NewNode`, `Connect`.
- `StructuredExamples`, `AdvancedExamples`, `FormulaExamples` — loop, case/sequence, error/complex and formula VIs.

**Usage**

```csharp
using LabSpace.Core;
using LabSpace.Documents;

var vi = Examples.Blank("Sum.vi");
var x = Examples.NewNode("control", 80, 80); x.Label = "x"; x.Value = 3;
var y = Examples.NewNode("control", 80, 220); y.Label = "y"; y.Value = 7;
var sum = Examples.NewNode("add", 320, 130);
var output = Examples.NewNode("indicator", 570, 130); output.Label = "x + y";
vi.Diagram.Nodes.AddRange([x, y, sum, output]);
Examples.Connect(vi.Diagram, x, sum, "x");
Examples.Connect(vi.Diagram, y, sum, "y");
Examples.Connect(vi.Diagram, sum, output);

string json = ProjectSerializer.Save(new LabProject { Name = "Demo", Instruments = [vi] });
LabProject loaded = ProjectSerializer.Load(json);
```

### LabSpace.Dataflow

The execution engine: validates and compiles a diagram into a cached topological plan (typed terminals, single-driver inputs, explicit feedback), then executes it with named outputs, nested structure activations, shift-register state, a compiled scalar formula language and shared node/instruction budgets. Depends on `LabSpace.Signals`; no UI.

```sh
dotnet add package LabSpace.Dataflow --prerelease
```

**Key types**

- `GraphCompiler` — `Validate` (returns `Diagnostic`s) and `Compile` to a `CompiledGraph`.
- `DataflowRuntime` — `Run` to completion or `Start` a resumable `ExecutionFrame`; `Reset`, `FrameCompleted`.
- `ExecutionFrame` — `GetOutput(nodeId, output)`, `Step`, `StepInto`, `Completed`.
- `FormulaProgram` — `Compile(source, signature)` and bounded `Evaluate`.
- `ExecutionBudget`, `ExecutionLimitException`, `GraphValidationException`.

**Usage**

```csharp
using LabSpace.Dataflow;
using LabSpace.Documents;

var vi = StructuredExamples.IndexedAccumulator();
var loop = vi.Diagram.Nodes.Single(node => node.Kind == "for");
var graph = GraphCompiler.Compile(vi.Diagram);
var runtime = new DataflowRuntime();

using var cancellation = new CancellationTokenSource();
var frame = runtime.Run(graph, cancellation.Token, maximumNodes: 100_000);
Console.WriteLine(frame.GetOutput(loop.Id, "state").Number);                  // 15
Console.WriteLine(string.Join(", ", frame.GetOutput(loop.Id, "totals").Samples)); // 1, 3, 6, 10, 15

var formula = FormulaProgram.Compile("result = x * x + 1;", new());
Console.WriteLine(formula.Evaluate(_ => 3)["result"]);                        // 10
```

### LabSpace.Editing

The UI-independent editor model: `InstrumentSession` owns the project, active VI, navigation into structures, selection, bounded undo/redo, typed wiring, gestures, clipboard, execution (run/continuous/pause/abort/step) and the cross-VI debugger with probes and breakpoints. Depends on `Dataflow` and `Documents`; no UI.

```sh
dotnet add package LabSpace.Editing --prerelease
```

**Key types**

- `InstrumentSession` — `Add`, `Connect`, `SetValue`, `Delete`, `Undo`/`Redo`, `BeginGesture`/`EndGesture`, `Changed`.
- Execution — `Run(continuous)`, `Tick`, `Pause`, `Abort`, `Step`, `StepInto`, `StepOut`, `OutputValue`.
- Navigation — `Switch`, `Enter`/`Leave`, `SelectFrame`, `ConfigureStructure`, `ConfigureFormula`.
- Debugger — `ToggleProbe`, `ToggleBreakpoint`, `GetProbes`, `LocateProbe`, `ProbeSnapshot`.
- `SessionChange` — flags describing what changed.

**Usage**

```csharp
using LabSpace.Core;
using LabSpace.Documents;
using LabSpace.Editing;

var session = new InstrumentSession(new LabProject { Name = "Demo", Instruments = [Examples.Blank("Sum.vi")] });
var a = session.Add("control", 80, 80);          // also places a front-panel control
var b = session.Add("control", 80, 220);
var add = session.Add("add", 320, 130);
var result = session.Add("indicator", 570, 130);
session.Connect(a.Id, add.Id, "x");               // type-checked; rejects cycles/multiple drivers
session.Connect(b.Id, add.Id, "y");
session.Connect(add.Id, result.Id, "x");
session.SetValue(a.Id, 3); session.SetValue(b.Id, 7);

session.Run();                                    // continuous runs advance via session.Tick()
Console.WriteLine(session.OutputValue(result.Id)); // 10
session.Undo();
```

### LabSpace.Skia

Original SkiaSharp rendering for front panels (numerics, knobs, sliders, gauges, LEDs, switches, strings, arrays, error/complex clusters, graphs and charts with extrema-preserving decimation) and typed block diagrams (nodes, wires, structure previews, probes), plus geometry and icons. Draws an `InstrumentSession` into any `SKCanvas`. Depends on `LabSpace.Editing` and SkiaSharp; no UI framework.

```sh
dotnet add package LabSpace.Skia --prerelease
```

**Key types**

- `PanelRenderer(LabFonts)` — `Draw(canvas, session, viewport)` for the active front panel; `Cursors`.
- `DiagramRenderer(LabFonts)` — `Draw` the active block diagram; `HitWire`.
- `PlotRenderer`, `IconPainter`, `LabDrawing` — plots, icons and drawing primitives.
- `DiagramGeometry` — node bounds, terminal positions and wire routing.
- `LabFonts` — shared typeface (`Load(stream)`); `IDisposable`.

**Usage**

```csharp
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Skia;
using SkiaSharp;

var session = new InstrumentSession(Examples.Create());
session.Run();                                   // Signal Analysis.vi

using var fonts = new LabFonts();                // or fonts.Load(stream) for a custom typeface
using var panel = new PanelRenderer(fonts);
using var surface = SKSurface.Create(new SKImageInfo(1200, 700));
panel.Draw(surface.Canvas, session, new SKRect(0, 0, 1200, 700));

using var image = surface.Snapshot();
using var png = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("front-panel.png", png.ToArray());
```

### LabSpace.Controls

The interactive Uno controls: front-panel and block-diagram canvases with pan/zoom, placement, wiring and operation; searchable function palette; Quick Drop; staged structure, frame and formula editors; property inspector; and the cross-VI Debug window. Depends on `LabSpace.Skia`; requires Uno Platform (Skia renderer).

```sh
dotnet add package LabSpace.Controls --prerelease
```

**Key types**

- `FrontPanelSurface(session, fonts)` / `DiagramSurface(session, fonts)` — editable canvases; `Fit`, `SetZoom`, `ArmPlacement`.
- `FunctionPalette` — searchable palette; `SetControls`, `AddRequested`.
- `PropertyInspector(session)`, `DebugWindowControl(session)`, `QuickDropControl`.
- `StructureContractEditor`, `FrameEditorControl`, `FormulaEditorControl` — staged editors.
- `LabTheme`, `LabButton`, `VectorIcon` — chrome helpers.

**Usage**

```csharp
using LabSpace.Controls;
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Skia;

var fonts = new LabFonts();
var session = new InstrumentSession(Examples.Create());
var diagram = new DiagramSurface(session, fonts);
var palette = new FunctionPalette();
palette.AddRequested += (kind, widget) => diagram.ArmPlacement(kind);   // click to place

var layout = new Grid();
layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
layout.ColumnDefinitions.Add(new ColumnDefinition());
layout.Children.Add(palette);
Grid.SetColumn(diagram, 1); layout.Children.Add(diagram);
window.Content = layout;                          // your Uno Window
diagram.Loaded += (_, _) => diagram.Fit();
```

### LabSpace.Workbench

The complete studio as one `UserControl`: project explorer, VI tabs, paired or split Front Panel / Block Diagram editors, palettes, context help, properties, Debug window, commands and keyboard shortcuts, execution timer and recovery scheduling. Depends on `Controls` and `Storage`; requires Uno Platform (Skia renderer).

```sh
dotnet add package LabSpace.Workbench --prerelease
```

**Key types**

- `InstrumentWorkbench(InstrumentSession, IProjectStorage, LabFonts)` — the studio control; `IDisposable`.
- `Session`, `FrontPanel`, `BlockDiagram`, `Palette`, `Inspector`, `DebugWindow`, `Commands`.
- `SetView(StudioView)`, `ToggleView`, `ShowDebugWindow`, `Fit`.

**Usage**

```csharp
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Skia;
using LabSpace.Storage;
using LabSpace.Workbench;

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    var window = new Window { Title = "LabSpace" };
    var fonts = new LabFonts();
    IProjectStorage storage = new MemoryProjectStorage();   // or your platform implementation
    var workbench = new InstrumentWorkbench(new InstrumentSession(Examples.Create()), storage, fonts);
    workbench.SetView(StudioView.Split);
    window.Content = workbench;
    window.Closed += (_, _) => { workbench.Dispose(); fonts.Dispose(); };
    window.Activate();
}
```

`src/LabSpace.App` shows desktop and browser (IndexedDB) `IProjectStorage` implementations and font loading.

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
