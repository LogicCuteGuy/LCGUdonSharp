# LCGUdonSharp Changelog

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
