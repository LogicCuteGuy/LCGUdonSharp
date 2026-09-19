# LCGUdonSharp Changelog

## Unreleased

- Added manual `[LCGPacket]` field and method lowering with targeted PlayerObject mailbox delivery.
- Added validated versioned packet frames, authority checks, replay protection, field coalescing, callbacks, and forced field sends.
- Added `LCGNetworkZone` build-scene injection, membership-scoped recipients, ownership guards, exit modes, and manual object state packets.
- Added fail-closed build validation for unsupported Continuous, Udon Graph, overlapping-zone, and zone-native-sync configurations.

## 0.1.0

- Moved the interface-enabled UdonSharp clone out of `com.vrchat.worlds` into `LCGUdonSharp`.
- Added automatic, idempotent setup with backup, repair, and restore commands.
