# LCG Manual Packet Networking Examples

> Documentation version: **0.3.8** · [All examples](../README.md) · [Package guide](../../README.md)

These examples demonstrate native VRChat networking and the `[LCGPacket]` attribute system for
manual networked state and method delivery inside an `LCGNetworkZone`.

## Native + LCG starter prefab

Drag **`NetworkExamples.prefab`** into a world scene with a floor and a VRC Scene
Descriptor. It wires three interactables, program assets, lights, status labels,
and a zone. See the [Thai setup and two-client checklist](README.th.md).

- **NativeNetworkLamp** uses a Manual `[UdonSynced]` bool outside the zone.
  Non-owners send a native `[NetworkCallable]` request to the existing owner;
  the owner applies the change, calls `RequestSerialization`, and receivers
  apply it in `OnDeserialization`. Native state includes late joiners.
- **LCGNetworkLamp** uses an owner-authorized packet field inside the zone,
  a packet toggle request to the existing owner, and a verified-sender callback.
  `_PingFirstOtherMember` demonstrates targeted delivery. Zone entry requests
  current field snapshots; state lasts for the session.
- **LCGNetworkMovingCube** acquires ownership on interaction and calls
  `LCGNetwork.RequestObjectSync(gameObject)` after moving. The source prefab has
  `VRC_ObjectSync`; Play/Build replaces it with the LCG motion relay.

Both lamps throttle accepted toggles at their owner and keep ownership stable
between clicks. Keep the native lamp outside the zone. Test cross-client
delivery, re-entry, late join, and owner departure with multiple VRChat clients.

## Examples

### HighBandwidthExamples prefab

`HighBandwidthExamples.prefab` contains two opt-in load generators. Both start
stopped; click each controller to start or stop. The test scene includes it near
spawn at `(-9, 0, 0)`. Walk inside its LCG trigger before clicking the LCG controller.

- **NativeHighBandwidthExample** sends a reusable Manual `[UdonSynced] byte[]`
  outside the zone. Defaults: 2048 payload bytes, 4 requested samples/second.
  Inspector bounds are also enforced at runtime: 64–8192 bytes, 1–10 Hz.
  Only the owner writes; other players ask the current owner to toggle.
  It waits for `OnPostSerialization` before changing the buffer again, pauses
  new samples while clogged, and skips missed samples instead of bursting.
  Successful callback `byteCount` feeds the serialized B/s meter. This includes
  serialization overhead and is not a wire-throughput or remote-delivery meter.
  Receivers show snapshot count/revision; native synced state includes late joiners.
- **LCGHighBandwidthExample** animates up to 32 cubes with `VRC_ObjectSync`
  converted into LCG relays at Play/Build. Defaults: 16 active cubes at 20 Hz
  (320 requested object samples/second on a fast enough frame rate). Edit
  `Active Objects` and `Sample Hz` to compare 4×10, 16×20, and 32×30.
  The owner acquires cube ownership once when starting. Each sample calls
  `LCGNetwork.RequestObjectSync`; receiver interpolation handles presentation.
  The dashboard shows locally produced samples, local owned-object count, and
  the **scene-wide** router queue/dispatched-batch/last-batch-byte counters.
  These router counters read compiler/runtime registers and are diagnostic
  implementation details, not a public API contract or delivery acknowledgment.

LCG deliberately offers more motion than the router can send: it keeps the
latest sample per object/recipient rather than retaining a motion history. The
existing motion budget remains approximately 6 KB/s and 40 dispatches/s, with
900-byte maximum batches. Those are scene-wide motion limits; they do not cap
native sync or other LCG traffic. Raising producer Hz does not raise the budget.
Remote traffic requires another member in the zone; one-player ClientSim can
produce local samples while the remote queue/batch counters remain zero.

For a multiplayer check, start one generator at a time, enter the zone with a
second client, compare motion/revisions, stop, exit/re-enter, join late, and let
the owner leave. Confirm the current snapshot resumes correctly and the queue
stays bounded. Increasing player count increases per-recipient LCG work.
This is a load example, not a claim of measured multiplayer bandwidth or FPS.
In this project's single-player ClientSim, ordinary native behaviours do not
receive automatic serialization callbacks, so the bulk example remains pending
after its first request. Use VRChat Build & Test for the native stream/rate meter.
Injecting callbacks in an editor test checks the handler, not network throughput.

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
