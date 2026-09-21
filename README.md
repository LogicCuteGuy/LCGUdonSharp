# LCGUdonSharp

This package installs the interface-enabled UdonSharp compiler without keeping modified source files inside `com.vrchat.worlds` or `Assets`.

## Automatic setup

After this package is imported, its small bootstrap assembly automatically:

1. verifies that VRChat Worlds SDK `3.10.5` is installed;
2. backs up the SDK-bundled `Integrations/UdonSharp` folder under `Library/LogicCuteGuy.LCGUdonSharp/Backups`;
3. installs the cloned compiler at `Packages/com.logiccuteguy.lcgudonsharp/UdonSharp`; and
4. removes the SDK-bundled copy so Unity sees only one set of `UdonSharp.*` assemblies.

The compiler sources keep their original `UdonSharp` namespaces and assembly names. Setup is idempotent and runs again after an SDK/package refresh if the SDK restores its bundled copy.

Use **Tools > LCGUdonSharp > Install or Repair** to force setup. Use **Restore VRChat UdonSharp and Disable Auto Setup** before removing this package; that command restores the backed-up SDK copy and removes the generated compiler folder.

Setup deliberately stops on an SDK version other than `3.10.5` instead of modifying an untested package.

## Interface MVP

- Source-defined C# interfaces implemented implicitly by `UdonSharpBehaviour` classes.
- Interface method calls across behaviours through UdonSharp's existing program-variable/custom-event ABI.
- Parameters, return values, simple properties, multiple implementations, and interface arrays.

Not supported: generic interfaces or methods, interface inheritance, default or static interface members, events, indexers, explicit implementations, `[NetworkCallable]` interface implementations, and member names that collide with built-in Udon events.

## LCG manual packet networking (experimental)

`[LCGPacket]` fields are sent manually when assigned. Repeated assignments in one frame are coalesced, unchanged encoded values are skipped, and `ForceSendPacket(nameof(field))` bypasses that suppression. A callback, when configured, receives the verified `VRCPlayerApi` sender.

```csharp
[LCGPacket(Authority = LCGPacketAuthority.ObjectOwner, Callback = nameof(OnHealthChanged))]
private int health;

public void OnHealthChanged(VRCPlayerApi sender) { }
```

Public `void` methods with up to eight supported arguments may also use `[LCGPacket]`. Direct calls remain local. `SendCustomNetworkEvent(...)` is lowered to mailbox delivery only when its target method has `[LCGPacket]`; `SendLCGNetworkEvent(player, ...)` provides targeted delivery.

### User PlayerObjects

`[LCGPacket]` fields and methods can be placed on a `VRCPlayerObject` template or its children. LCG addresses each runtime clone by its template receiver and owning player. Field coalescing, unchanged-value suppression, and duplicate detection are independent for each clone; `ObjectOwner` authority checks the clone's owner. Prefer `ObjectOwner` for fields that only their player should change (the attribute defaults to `Any`).

Wait for `OnPlayerRestored` before using a player's clone, and obtain it with `Networking.FindComponentInPlayerObjects(player, templateBehaviour)`. Calling an LCG event on that reference addresses that same player's clone on every recipient. `SendLCGNetworkEvent(recipient, ...)` selects the recipient client; it does not change which clone the reference identifies. Calls on the inactive template are rejected.

Owners provide current packet-field snapshots when players are restored. Queued work and duplicate state for departing players are discarded. These are session snapshots: `[LCGPacket]` fields are not automatically saved by VRChat persistence, and packet methods are not replayed for late joiners.

PlayerObject templates and `LCGNetworkZone` must be in separate hierarchies. Combining them fails the build before helpers are generated, because zone ownership transfer and cloned zones are not supported. Packet networking uses protocol version 2 with a clone-owner field; recompile all UdonSharp programs and rebuild the world after upgrading.

Add `LCGNetworkZone` to a trigger collider to restrict descendant packet recipients and ownership to players in the trigger. Play Mode and Build/Test scene copies receive one `LCGRuntime`, one PlayerObject mailbox template, ownership guards, and manual `VRCObjectSync` replacements; authoring scenes are not modified. In Play Mode, the generated roots appear as `__LCGRuntime` and `__LCGRuntimePlayer` and are removed when you stop playing. Scripted transform changes are local until `LCGNetwork.RequestObjectSync(gameObject)` is called; pickups send movement automatically as described below.

Play Mode setup requires scene reload (Unity's default). When ClientSim is installed, its initial startup is deferred until scene processing finishes so it can discover and clone the generated PlayerObject mailbox.

Pickups automatically request object sync up to ten times per second while held and after release until their rigidbody sleeps. Only the owner sends, and zone membership still restricts delivery. Other scripted transform changes require `LCGNetwork.RequestObjectSync(gameObject)`.

On zone entry or re-entry, the entering player requests current state and each object owner also sends a snapshot when it observes that entry. This refreshes stationary objects and packet fields even when the clients receive trigger events in different orders.

LCG diagnostic logging is off by default. Enable **Edit > Project Settings > Udon Sharp > Debugging > LCG network diagnostics** before compiling to include pickup and packet-delivery output. When disabled, those log branches are omitted from the generated Udon programs. Recompile UdonSharp programs after changing the setting. VRChat's own logs and compiler errors are unaffected.

Networking uses manual packets, with movement sampling active only during pickup motion. A build fails closed for Continuous behaviours, networked Udon Graph behaviours, unrelated overlapping zones, and currently for `[UdonSynced]` fields under a zone. The last case prevents native VRC sync from escaping the zone until generated per-zone program variants are implemented. Native manual `[UdonSynced]` outside zones is unchanged.

## Examples

The `Example` folder contains runnable scenes and scripts that exercise each
feature.

| Example | Feature | Description |
|---------|---------|-------------|
| [`Example/AsyncAwait`](Example/AsyncAwait/README.md) | Async lowering | Runnable `Task.Yield()` and `Task.Delay(int)` continuation example. |
| [`Example/Interfaces`](Example/Interfaces/README.md) | Interface MVP | Defines `INumberOperation` with two implementations (`AddNumberOperation`, `MultiplyNumberOperation`). `InterfaceExampleRunner` invokes both through the interface and reads the property. |
| [`Example/Networking`](Example/Networking/README.md) | LCG manual packets | Three showcases — coalesced packet fields with callbacks, ordered packet methods with broadcast and targeted delivery, and zone-scoped manual object sync. |
| [`Example/GenericRestrictions`](Example/GenericRestrictions/README.md) | Generic build-time diagnostics | Bad/good pairs for open generics, generic behaviours and heap objects, `List<T>`, interface contracts, multiple bases, and the `Task<T>` compiler-handle exception. |
| [`Example/ExtendedLanguage`](Example/ExtendedLanguage/README.md) | Extended C# lowering | Runnable U# examples for `ref`/`out`, closed generics and interface diamonds, LINQ lambdas with captures, proven `dynamic`, and array-backed local spans. |

Each runnable example has its own `README.md` with setup steps and usage notes.
The generic-restriction entry is a reference guide whose failing snippets remain
in Markdown so the package itself continues to compile.
