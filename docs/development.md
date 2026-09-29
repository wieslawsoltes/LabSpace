# Development and validation

## Toolchain

Use the pinned .NET SDK 10.0.401 and Uno SDK 6.7.30 in `global.json`. Browser builds require `wasm-tools`; acceptance uses Node 22 and pinned Playwright. A desktop-only build avoids installing the browser workload.

```sh
python3 scripts/fetch-assets.py
dotnet test tests/LabSpace.Tests -c Release
dotnet build src/LabSpace.App -f net10.0-desktop -c Release -p:LabSpaceDesktopOnly=true
```

The asset script verifies immutable Git blob hashes and downloads the OFL license with its font. Do not copy system fonts or NI artwork into the repository. Managed/native Skia packages are pinned together at 3.119.4; do not upgrade one independently.

## Browser build and acceptance

```sh
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/LabSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/LabSpace/
python3 scripts/check-native-abi.py
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Open `http://127.0.0.1:4173/LabSpace/`. The collector selects the published WebAssembly distribution and records the commit plus versions derived from `Directory.Build.props`/`global.json`. It rejects overlapping source/destination directories and invalid commit metadata. An arbitrary HTML shell is not a successful application build.

```sh
npm install --ignore-scripts
npx playwright install --with-deps chromium
npm run test:browser
```

Set `LABSPACE_URL` to run the same suite against a deployed site. Tests enable `?test=1` for **read-only** state and hit-target diagnostics. Normal mode does not publish them. Tests use real keyboard/pointer input and downloads, not private model-mutation functions. The flag is a debugging convenience, not an access-control boundary.

Browser output includes screenshots, failure traces, the HTML report and machine-readable `artifacts/test-results/results.json`. Tests fail on page errors. Headless Chromium uses SwiftShader where necessary; this is not physical-GPU performance or pixel-identical NI certification.

## Regression coverage

Engine tests cover calculations, compile/type/driver/cycle errors, feedback, examples, nested bodies, shared budgets, stepping, JSON limits/migration, immutable payloads, RMS/FFT, transactions, copy/paste and undo. Version 0.2 adds named outputs, typed contracts, indexed/conditional/concatenated collection, stacked register history, initialization/persistence, zero iterations, failure rollback and cancellation in empty bodies.

Browser workflows cover both editors, pointer wiring/dragging, undo, palette insertion, continuous run/abort, knob-driven results, JSON/recovery, nested body navigation, Quick Drop placement, named-output wiring, staged connector Apply/Cancel, persistent register results and step-into inspection. A dedicated focus regression opens Quick Drop after a toolbar button receives focus.

## Continuous integration and packaging

`engine.yml` is the lightweight engine gate. `build.yml` builds/tests the real browser application and builds the shared native host on Windows, Linux and macOS. Pages depends on successful browser and desktop jobs, checks the artifact's commit and runs the same browser acceptance against the public URL. Source snapshots and validation artifacts are retained in Actions.

`release.yml` runs on matching main-branch package/toolchain changes, `v*` tags, or manual dispatch. It tests the engines, publishes single-file desktop executables for six runtimes, packs all nine versioned libraries/symbols, audits resolved Skia versions, publishes a browser distribution and records benchmarks. Tagged runs attach assets to a GitHub Release and push the packages to NuGet.org via Trusted Publishing (`NuGet/login` OIDC in the `nuget` environment, account from the `NUGET_USER` variable); other runs publish nothing.

## Performance and extension rules

Selection and geometry changes must not repeatedly compile the graph. Inspectors refresh on document/selection changes rather than every signal frame. Wire paths, typefaces and actual-body previews are cached; history and collections are bounded. Cooperative nested work yields to the UI while individual kernels remain non-preemptive.

Run `dotnet run --project tools/LabSpace.Benchmarks -c Release` for managed engine/FFT timing and allocation measurements. Compare on the same machine/runtime; the results do not measure GPU or UI frame time.

Put models/types and catalog definitions in Core, DSP in Signals, validation/execution in Dataflow, serialization in Documents, transactions in Editing, rendering in Skia, input/components in Controls and composition in Workbench. Use immutable contracts with `NodeCatalog.Resolve(node)` for instance-specific ports. Bump the native document format for breaking schema changes and add invalid-input, round-trip, cancellation and undo tests. Keep platform services behind injected interfaces such as `IProjectStorage` rather than browser globals inside engines.

## Cross-VI debugging and browser-storage validation

See [Debugger](debugger.md) for session-only overrides, scoped retained values, activation navigation, bounds and reusable controls. Run `npm run test:storage` for dependency-free IndexedDB connection/transaction and file-picker lifecycle regressions. Recovery writes are acknowledged only when the transaction commits; malformed saved records are rejected rather than replaced with an empty project. Browsers remain subject to storage quota/eviction: keep explicit project saves.
