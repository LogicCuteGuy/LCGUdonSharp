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

Add `LCGNetworkZone` to a trigger collider to restrict descendant packet recipients and ownership to players in the trigger. Build/Test scene copies receive one `LCGRuntime`, one PlayerObject mailbox template, ownership guards, and manual `VRCObjectSync` replacements; authoring scenes are not modified. Transform changes are local until `LCGNetwork.RequestObjectSync(gameObject)` is called.

Networking is event-driven: there is no Continuous sync or transform polling. A build fails closed for Continuous behaviours, networked Udon Graph behaviours, unrelated overlapping zones, and currently for `[UdonSynced]` fields under a zone. The last case prevents native VRC sync from escaping the zone until generated per-zone program variants are implemented. Native manual `[UdonSynced]` outside zones is unchanged.
