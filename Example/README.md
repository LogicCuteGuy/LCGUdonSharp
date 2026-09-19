# LCG Manual Packet Networking showcase

The example scripts are available directly from the package's `Example/` folder.
The included `.asmdef` and `.USharp.asset` files register this folder as a UdonSharp
assembly; keep both files with the scripts.

The scripts are intentionally scene-ready rather than tied to a prefab. This makes the
network boundary visible and lets you test different Zone sizes and exit modes.

## Suggested hierarchy

```text
LCG Network Zone
├── Field Showcase
├── Method Showcase
├── Object Controller
└── Synced Cube
```

1. Create `LCG Network Zone`, add a `BoxCollider`, enable **Is Trigger**, and add
   `LCGNetworkZone`. Make the collider large enough for players to enter.
2. Create the three showcase child objects plus `Synced Cube`, then add the matching
   showcase behaviour to each showcase object.
3. Add `VRC Object Sync` to `Synced Cube`. A `Rigidbody` is optional. Assign this
   object to **Synced Object** on `LCGZoneObjectShowcase`.
4. Optionally add a world-space Canvas with legacy UI `Text` and `Button` components.
   Assign the Text fields, then wire Buttons to the public no-argument methods.
5. Keep every showcase behaviour on **No Variable Sync**. Do not add `[UdonSynced]`
   fields below the Zone.

The runtime and per-player mailbox are injected automatically into the Build & Test
scene copy. Do not add them to the authoring scene.

## Two-client test checklist

Enter the Zone with both clients before testing scoped delivery.

### Packet field

- On client A, call `TakeOwnership`, then `AddOne`. Client B should show the new score
  and the verified sender name.
- Call `AddThreeCoalesced`. Client B should jump by three after one coalesced field
  update, rather than observing three intermediate values.
- Call `ForceResend` without changing the score. Client B should receive the callback.

### Packet method

- Call `BroadcastToOthers` on client A. Client B should receive it; client A should not.
- Call `SendToFirstOtherPlayer`. Only the chosen player's mailbox should receive it.
- Call `RunLocalOnly`. Only the calling client should log/display the message.

### Zone object sync

- On client A, call `TakeOwnership`, then `MoveAndSync`, `RotateAndSync`, or
  `RepositionAndSync`. Client B should receive the requested state.
- Call `MoveLocalOnly`. The object should move only on client A because no manual sync
  request was made.
- Leave the Zone and try to take ownership or send protected state. The Zone guard
  should reject it. Re-enter and verify that a current snapshot is received.

All packet sends in this sample are event-driven. There is no Continuous sync,
periodic transform polling, or timer-generated network traffic.
