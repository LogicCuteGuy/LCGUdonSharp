# LCG Manual Packet Networking Examples

> Documentation version: **0.3.1** · [All examples](../README.md) · [Package guide](../../README.md)

These three examples demonstrate the `[LCGPacket]` attribute system for
manual networked state and method delivery inside an `LCGNetworkZone`.

## Examples

### LCGPacketFieldShowcase

A single `[LCGPacket]` integer field with a verified-sender callback.

- **AddOne / AddThreeCoalesced** — assign the field repeatedly; only the
  latest value is sent per frame.
- **ForceResend** — sends the current value even when it has not changed.
- **OnScoreChanged** — callback receives the `VRCPlayerApi` sender.
- Wire UI buttons or use `Interact()` to increment.

### LCGPacketMethodShowcase

Packet-delivered methods with string and `Vector3` parameters.

- **BroadcastToOthers** — `SendCustomNetworkEvent(NetworkEventTarget.Others, ...)`
  is lowered to mailbox delivery because the target method has `[LCGPacket]`.
- **SendToFirstOtherPlayer** — `SendLCGNetworkEvent(player, ...)` targets a
  single recipient.
- **RunLocalOnly** — a direct call never creates a packet.

### LCGZoneObjectShowcase

A `VRC_ObjectSync`-like object inside an `LCGNetworkZone`. The build processor
replaces `VRC_ObjectSync` with a generated manual relay.

- **MoveAndSync / RotateAndSync / RepositionAndSync** — transform changes are
  sent to zone members via `LCGNetwork.RequestObjectSync(gameObject)`.
- **MoveLocalOnly** — deliberately omits the sync call so only the owner
  sees the change.

## Scene setup

1. Fix any unrelated C# compilation errors and let Unity finish compiling.
2. Open `../TestLCGUdonSharp.unity`, or manually add the components to scene
   objects (each showcase `.cs` already ships with its paired
   `UdonSharpProgramAsset`, same basename):
   - Place an `LCGNetworkZone` on a trigger collider that covers the play area.
   - Add the desired showcase behaviour to a child object.
   - For `LCGZoneObjectShowcase`, add a `VRC_ObjectSync` component to the
     synced child — the build processor replaces it automatically.
3. Enter Play Mode. Play Mode and Build/Test scene copies receive one
   `LCGRuntime`, one PlayerObject mailbox template, and ownership guards.

## Ownership

All packet field writes require object ownership. Call
`Networking.SetOwner(Networking.LocalPlayer, gameObject)` before writing.
The `TakeOwnership` method on each showcase demonstrates this.

## Diagnostics

LCG diagnostic logging is off by default. Enable
**Edit > Project Settings > Udon Sharp > Debugging > LCG network diagnostics**
before compiling to include pickup and packet-delivery output. Recompile
UdonSharp programs after changing the setting.
