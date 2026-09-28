# Architecture

LabSpace separates the virtual instrument model, execution, editing, rendering and platform services. The browser is the real Uno WebAssembly application: there is no parallel JavaScript implementation of the dataflow engine.

```text
                  Desktop host                 Browser host
               Win32 / X11 / macOS             .NET WebAssembly
                      │                             │
                      └───────── App ──────────────┘
                                  │
                          Workbench + Storage
                                  │
                  Controls: diagram / panel / palette / inspector
                                  │
                         Skia render components
                                  │
                         Editing / transactions
                            ┌─────┴──────┐
                        Dataflow      Documents
                            │            │
                          Signals        │
                            └─────┬──────┘
                                 Core
```

## Component contracts

`LabSpace.Core` contains plain C# project, VI, node, wire and front-panel models. No Uno references are required to create or execute a project. Runtime values are immutable and array factories copy their inputs. Editing models are deliberately mutable inside a session transaction; hosts must not mutate them from background threads.

`LabSpace.Signals` contains bounded numerical kernels. `Generate` supports sine, square and triangle sources plus deterministic noise. `Rms` uses scaled sum-of-squares; `Spectrum` is an iterative radix-2 FFT with a periodic Hann window, one-sided amplitude normalization and correct DC/Nyquist treatment. Moving-average state currently resets at each input block.

`LabSpace.Dataflow` validates and compiles a typed graph to a deterministic topological order. Every input has at most one source; outputs may fan out. Types must match exactly. Required disconnected terminals, dangling wires, multiple drivers, unknown functions and combinational cycles produce structured diagnostics. An explicit Feedback node breaks a cycle and supplies the previous completed frame's value. Compilation is cached by the editing session until a structural edit or document replacement.

`ExecutionFrame.Step` evaluates one top-level node. Nested structures run as step-over operations, sharing the root cancellation token and node budget. For, While, Case and embedded SubVI bodies expose numeric connector input/output nodes. Case evaluates only its selected branch. Runtime exceptions include node identity and label. Incomplete frames do not commit pending feedback values.

`LabSpace.Documents` uses a source-generated, versioned JSON serializer. It applies UTF-8 byte, object-count, nesting, coordinate and numeric limits before accepting a project. The format is original to LabSpace; the `.vi` names in the UI are human-readable instrument names, not NI binary payloads.

`LabSpace.Editing.InstrumentSession` owns the current instrument, nested navigation, selection, probes, breakpoints, history, compiled execution plan and bounded chart history. Structural commands are atomic and undoable. Drag gestures preview directly in the model but retain one before-image and create one history item on release. Undo and redo have count and memory bounds. Selection and viewport changes do not create history or recompile the diagram.

`LabSpace.Skia` owns rendering only. Diagram and instrument renderers accept a canvas, session and visible world rectangle. Cached wire paths are rebuilt when geometry changes, not every execution frame. Off-screen nodes and wires are culled. Waveform traces use min/max pixel buckets, preserving extrema rather than simply dropping every nth sample. Fonts and paints are retained and disposed by their owners.

`LabSpace.Controls` supplies reusable Uno surfaces, viewport transforms, custom classic toolbar buttons, original vector icons, panes, function/control palettes and the property inspector. Pointer input uses the same DIP-to-world transform as rendering. Front-panel controls and block-diagram terminals share node IDs and data. Standard text-input, file-picker and popup primitives are retained for platform input services. Canvas elements have keyboard operation but not yet a full per-terminal accessibility tree.

`LabSpace.Workbench.InstrumentWorkbench` composes the editors, project explorer, document tabs, menu/toolbar commands, property inspector, context help and error list. The host injects `IProjectStorage` and loaded `LabFonts`. The studio owns its execution/recovery timers and unsubscribes when disposed.

## Rendering decision

The implementation uses **SkiaSharp through Uno's `SKCanvasElement`**, integrated with Uno's supported desktop and browser renderers. This is a practical shared 2D rendering path for text-heavy diagrams, instruments and plots. Hardware acceleration is host-dependent; a browser without an appropriate graphics context can fall back to software. CI's headless Chromium may use SwiftShader. A successful headless test is therefore not a hardware-GPU performance measurement.

Uno SDK 6.7.30 and Uno WinUI 6.7.135 are the stable packages verified on September 28, 2026. The compatible SkiaSharp 3.119.4 managed/native line is used rather than forcing the newer standalone SkiaSharp 4.x ABI into Uno. LabSpace does not currently implement a custom WebGPU compute engine, Metal renderer, or Vulkan renderer. The dataflow and signal kernels execute in managed C#, not on the GPU.

## Execution and state

The host timer targets 40 ms between continuous execution requests. This is a UI-friendly simulator schedule, not hard real time. The runtime's logical time advances per completed frame and is separate from wall-clock performance measurements. Elapsed execution milliseconds come from `Stopwatch` and exclude paused time between top-level steps. Layout, rendering, GC, browser compilation and timer scheduling are not included in this engine-only measurement.

The initial structures are intentionally a numeric subset: there are no arbitrary typed tunnels, LabVIEW shift-register semantics, auto-indexing arrays, sequence locals, event structures, queues or reentrant call instances. Embedded SubVI bodies are reusable source objects, not linked external NI VI dependencies. See [compatibility](compatibility.md) for the complete boundary.

## Persistence

Browser recovery uses an IndexedDB transaction; explicit save uses a downloadable JSON Blob. Browser import reads a user-selected local file and does not evaluate scripts. Desktop recovery uses a temporary file followed by an atomic replacement in the user's application-data folder. Desktop import/export uses Uno file-picker APIs; exact native picker behavior depends on the OS host and installed desktop services.

Recovery is periodically written after edits; explicit saves remain necessary for separate, portable backups. A successful download initiation cannot prove the browser user retained the downloaded file. Browser site-data clearing removes recovery.

## Safety bounds

| Resource | Limit |
| --- | --- |
| Project JSON | 8 MiB UTF-8 |
| Instruments | 64 |
| Nodes per diagram | 4,096 |
| Nodes per project including nested bodies | 16,384 |
| Wires per diagram | 16,384 |
| Nested diagram depth | 12 |
| Samples per value | 65,536 |
| For/While iterations | 10,000 |
| Evaluated nodes per root frame | 100,000 |
| History | 64 snapshots and 32 MiB per direction |
| Chart history | 4,096 samples per chart |

These bounds reduce accidental resource exhaustion; they are not a formal sandbox proof. Physical equipment control and safety-critical operation are outside this release's scope.
