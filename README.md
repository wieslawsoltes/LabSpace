# LabSpace

**Graphical programming. Live instruments. One shared C# engine.**

[Open the browser studio](https://wieslawsoltes.github.io/LabSpace/) · [Architecture](docs/architecture.md) · [User guide](docs/user-guide.md) · [Compatibility](docs/compatibility.md)

LabSpace is an independent, MIT-licensed visual instrumentation workbench built with Uno Platform and SkiaSharp. It brings the classic front-panel / block-diagram workflow to desktop and WebAssembly, with original custom controls, a typed executable dataflow graph, signal processing, and local-first project files.

This is an **early functional implementation**, not NI LabVIEW or a binary-compatible replacement. NI and LabVIEW are trademarks of their respective owners; this project is not affiliated with or endorsed by NI. No NI source, artwork, runtime, drivers, or proprietary file parsers are included. See the explicit compatibility boundary before adopting it.

## Stack

.NET SDK **10.0.401**, Uno SDK **6.7.30**, Uno WinUI **6.7.135**, and the Uno-compatible SkiaSharp **3.119.4** managed/native pair. The newest standalone SkiaSharp major is deliberately not mixed with Uno's native ABI. Diagrams and instruments use `SKCanvasElement` in the actual Uno application, not an HTML imitation. Hardware acceleration depends on the host and browser; software fallback is supported.

## Build

```sh
dotnet workload install wasm-tools --skip-manifest-update
python3 scripts/fetch-assets.py
dotnet test tests/LabSpace.Tests -c Release
dotnet run --project src/LabSpace.App -f net10.0-desktop
dotnet publish src/LabSpace.App -f net10.0-browserwasm -c Release -o artifacts/publish -p:WasmShellWebAppBasePath=/LabSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Use a modern browser at `http://localhost:4173/LabSpace/`. A desktop-only build is available with `-p:LabSpaceDesktopOnly=true` and does not need the WebAssembly workload. Font assets are fetched from an OFL-licensed upstream and verified against immutable content hashes; they are not copied from a developer machine.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| LabSpace.Core | Typed values, nodes, terminals, wires, VIs and panel models |
| LabSpace.Signals | Signal generation, stable RMS, filters and FFT |
| LabSpace.Dataflow | Validation, compilation, bounded execution, stepping and feedback state |
| LabSpace.Documents | Versioned JSON, limits, round trips and example projects |
| LabSpace.Editing | Transactions, bounded undo/redo, wiring, selection and layout |
| LabSpace.Skia | Diagram, wire, instrument and plot rendering |
| LabSpace.Storage | Host-independent storage contracts |
| LabSpace.Controls | Custom Uno canvas surfaces and classic UI components |
| LabSpace.Workbench | Composable project explorer, palettes, inspectors and execution UI |

All libraries are packable. CI produces packages as artifacts; this does not imply they have already been published to NuGet. Desktop and browser share the same engine and workbench.

## Development

See [development](docs/development.md), [security](SECURITY.md), and [contributing](CONTRIBUTING.md). CI runs engine tests, browser interaction tests, desktop builds and Pages deployment. Tagged releases package the reusable libraries and browser distribution. Performance results must include the runtime and workload; simulated signals are explicitly labeled and are not physical DAQ measurements.
