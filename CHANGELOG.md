# LCGUdonSharp Changelog

## Unreleased

- Added synchronous compiler-managed `try`/`catch`/`finally`, typed and catch-all handlers, explicit throws and rethrows, `UdonException` payloads, protected same-behaviour propagation, and guarded null, bounds, and integral divide/modulo failures.
- Added exported-event cleanup/logging for uncaught emulated exceptions and targeted diagnostics for unsupported exception syntax and `await` inside `try`.
- Added manual `[LCGPacket]` field and method lowering with targeted PlayerObject mailbox delivery.
- Added validated versioned packet frames, authority checks, replay protection, field coalescing, callbacks, and forced field sends.
- Added `LCGNetworkZone` build-scene injection, membership-scoped recipients, ownership guards, exit modes, and manual object state packets.
- Added fail-closed build validation for unsupported Continuous, Udon Graph, overlapping-zone, and zone-native-sync configurations.

## 0.1.0

- Moved the interface-enabled UdonSharp clone out of `com.vrchat.worlds` into `LCGUdonSharp`.
- Added automatic, idempotent setup with backup, repair, and restore commands.
