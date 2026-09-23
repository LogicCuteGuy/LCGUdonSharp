# LCGUdonSharp Changelog

## 0.3.4 - 2026-09-23

- Refresh the collection example program asset to match its current collection fields and Manual synchronization mode.
- Update the example scene serialization with the JSON and binary round-trip result fields.
- Align package and example documentation version markers with the release.

## 0.3.3 - 2026-09-23

- Fix release packaging: include the complete LCG compiler under `Payload~/UdonSharp` instead of distributing a raw source archive.
- Keep examples as optional samples and editor tests out of the initial package import so the installer can run with the stock SDK.
- Reject missing compiler features, dependencies, and invalid metadata before replacing the installed compiler.
- Add packaging regression tests and automated validated release assets.

## 0.3.2 - 2026-09-23

- Updated the package guide and every example README for the 0.3.2 release.
- Kept documentation-version markers and navigation links consistent across all runnable example guides.

## 0.3.1 - 2026-09-23

- Refreshed every README with a consistent documentation-version marker and navigation links between the package guide and runnable examples.
- Clarified that the example documentation applies to the 0.3.x feature set.

## 0.3.0 - 2026-09-23

- Added editor proxy serialization for exact C# collections, including nested collections, collection arrays, exact primitive and enum tokens, and distinct null/empty values.
- Added semantic lowering for exact `List<T>` and `Dictionary<TKey,TValue>` types to VRChat data containers, including constructors, initializers, typed access, iteration, common mutation/search APIs, and familiar compiler-managed failures.
- Added a VRCJson-backed `System.Text.Json` intrinsic facade, tagged non-string dictionary keys, strict numeric conversion, Manual-mode JSON synchronization for collection fields, and runnable JSON/byte/bit examples.
- Added synchronous compiler-managed `try`/`catch`/`finally`, typed and catch-all handlers, explicit throws and rethrows, `UdonException` payloads, protected same-behaviour propagation, and guarded null, bounds, and integral divide/modulo failures.
- Added exported-event cleanup/logging for uncaught emulated exceptions and targeted diagnostics for unsupported exception syntax and `await` inside `try`.
- Added manual `[LCGPacket]` field and method lowering with targeted PlayerObject mailbox delivery.
- Added validated versioned packet frames, authority checks, replay protection, field coalescing, callbacks, and forced field sends.
- Added `LCGNetworkZone` build-scene injection, membership-scoped recipients, ownership guards, exit modes, and manual object state packets.
- Added fail-closed build validation for unsupported Continuous, Udon Graph, overlapping-zone, and zone-native-sync configurations.
- Added `Example/TestLCGUdonSharp.unity` with per-feature example folders (async/await, interfaces, extended language, generic restrictions, packet networking), compiled through the registered `LogicCuteGuy.LCGUdonSharp.Examples` assembly with a paired `UdonSharpProgramAsset` per script.
- Documented program-asset and U# assembly-definition naming, assembly scanning rules, and the full menu command set.

## 0.1.0

- Moved the interface-enabled UdonSharp clone out of `com.vrchat.worlds` into `Packages/com.logiccuteguy.lcgudonsharp/UdonSharp`, leaving the SDK and `Assets/` untouched.
- Added automatic, idempotent setup with the `Tools > LCGUdonSharp > Install or Repair` and `Tools > LCGUdonSharp > Restore VRChat UdonSharp and Disable Auto Setup` commands, SDK backups under `Library/LogicCuteGuy.LCGUdonSharp/Backups`, state in `ProjectSettings/LogicCuteGuy.LCGUdonSharp.json`, and handling for the legacy install locations of earlier versions.
