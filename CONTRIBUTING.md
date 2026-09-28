# Contributing

Keep engines independent of Uno and platform APIs. Add bounded, testable operations rather than cosmetic controls that do not execute. Every new editable concept needs a document representation, validation, serialization, undo/redo and regression tests. UI work should include keyboard behavior and real browser pointer tests.

Run engine tests, desktop builds and browser acceptance before proposing a release. Update `docs/compatibility.md` whenever a parity boundary changes. Do not describe a feature as complete merely because a menu entry exists. Keep measurements reproducible and distinguish simulated acquisition from physical data.

Use original code and artwork or permissively licensed dependencies with retained notices. Do not contribute proprietary NI source, icons, driver binaries, runtime components or copied examples. NI documentation may be referenced to explain expected public behavior.

Report bugs with the build-info commit, browser/OS, steps, a minimal `.labspace.json` project and relevant console errors. Remove confidential measurements or project data before posting public issues.
