# Development and validation

## Prerequisites

Use .NET SDK 10.0.401, resolved by `global.json`, and Uno SDK 6.7.30. Install `wasm-tools` to build the browser target. Browser tests use Node 22 and the pinned Playwright test package. The desktop-only build avoids requiring a WebAssembly workload.

```sh
python3 scripts/fetch-assets.py
dotnet test tests/LabSpace.Tests -c Release
dotnet build src/LabSpace.App -f net10.0-desktop -c Release -p:LabSpaceDesktopOnly=true
```

The font-fetch script verifies immutable Git blob hashes and retrieves the OFL license alongside the font. Do not copy local system fonts into the project. Source code does not include NI assets.

## Browser

```sh
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/LabSpace.App -f net10.0-browserwasm -c Release -o artifacts/publish -p:WasmShellWebAppBasePath=/LabSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Open `http://127.0.0.1:4173/LabSpace/`. The collector requires an actual `.wasm` distribution and writes build provenance. Serving an arbitrary HTML shell is not considered a successful application build.

```sh
npm install --ignore-scripts
npx playwright install --with-deps chromium
npm run test:browser
```

`LABSPACE_URL` points the same acceptance suite at a deployed site. Tests opt into `?test=1`, which enables **read-only** state and hit-target diagnostics. They interact through real pointer/keyboard events and downloaded files, not direct mutation APIs. Normal mode does not publish these diagnostics. The opt-in flag is a debugging convenience, not an access-control boundary.

## Engine tests

The test suite covers numeric and comparison kernels, type/driver/cycle checks, unwired errors, feedback, real example execution, nested loops/cases/subVIs, cancellation and shared execution budgets, stepping, JSON limits/round trips, array immutability, stable RMS, FFT amplitude/bin behavior, history, deletion, duplication, gesture transactions and breakpoint continuation.

Headless browser tests cover both views, actual node dragging with undo, terminal wiring, palette insertion, continuous execution, JSON download and recovery. Screenshots are produced for review. A headless render is not evidence of a physical GPU or pixel parity with NI LabVIEW.

## CI and release

`engine.yml` provides a lightweight engine gate. `build.yml` builds and tests the browser application and builds the shared desktop host on Windows, Linux and macOS. Pages deployment depends on successful browser and desktop jobs, validates the artifact's commit, and runs acceptance against the live URL. Source snapshots and validation artifacts are retained by GitHub Actions.

`release.yml` runs for `v*` tags or a manual build. It validates the engines, packs the nine reusable libraries and publishes the browser distribution as artifacts. A tagged run attaches these to a GitHub release. Packages are not automatically pushed to NuGet; successful packing is not a package-publication claim.

## Performance

Selection/geometry edits must not compile the graph. Inspectors refresh on document or selection changes, not every signal frame. Wire geometry and font objects are reused, chart history is bounded, and waveform paths are limited to the current pixel budget. Engine milliseconds exclude rendering, UI layout, paused time and browser startup.

Run `dotnet run --project tools/LabSpace.Benchmarks -c Release` for a small reproducible engine/DSP baseline. Compare results on the same runtime and machine. The benchmark reports managed allocations and execution wall time; it does not measure physical GPU throughput or promise a frame rate.

## Extending the system

Add value/model concepts in Core, signal kernels in Signals, catalog definitions and execution dispatch in Dataflow, serialization changes in Documents, commands in Editing, drawing in Skia and interaction in Controls. Bump the document format for breaking schema changes. Add tests for invalid inputs, round trips, cancellation and undo together with each new feature. Keep platform services behind `IProjectStorage` or new explicit injected contracts rather than introducing browser globals into the engine.
