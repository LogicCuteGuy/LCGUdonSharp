# LCGUdonSharp

**Interface-enabled UdonSharp compiler for VRChat — installed as its own package instead of patching `com.vrchat.worlds`.**

[![Unity](https://img.shields.io/badge/Unity-2022.3-blue)](https://unity.com/)
[![VRChat Worlds SDK](https://img.shields.io/badge/VRChat_Worlds_SDK-3.10.5-orange)](https://github.com/VRChat/worlds)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE.md)
[![Package Version](https://img.shields.io/badge/version-0.2.0-informational)](package.json)

LCGUdonSharp extends the UdonSharp compiler with C# interfaces, build-time `async/await` lowering, extended language constructs (`ref`/`out`, closed generics, LINQ closures, `dynamic`, `Span<T>`), and a manual packet networking layer — while keeping every modified source file inside `Packages/com.logiccuteguy.lcgudonsharp` instead of the VRChat SDK or `Assets`.

## Table of Contents

- [Features](#features)
- [How It Works](#how-it-works)
- [Installation & Setup](#installation--setup)
- [Usage](#usage)
- [Examples](#examples)
- [Supported vs. Rejected](#supported-vs-rejected)
- [Menu Commands](#menu-commands)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Features

| Feature | What you get |
|---------|--------------|
| **C# Interfaces** | Source-defined interfaces implemented by `UdonSharpBehaviour` classes — method calls, parameters, return values, properties, multiple implementations, interface arrays. |
| **Async/Await** | Build-time lowering for `await Task.Yield()`, `await Task.Delay(int)`, and one VRChat SDK await per behaviour (string/image downloads, video, GPU readback, serialization, Creator Economy). |
| **Extended Language** | `ref`/`out` (including `out var` and recursion), closed generics, interface diamonds, LINQ lambdas with captures, proven `dynamic`, array-backed `Span<T>`. |
| **Manual Packet Networking** | `[LCGPacket]` fields and methods with versioned frames, authority checks, replay protection, field coalescing, verified-sender callbacks, targeted PlayerObject delivery. |
| **Network Zones** | `LCGNetworkZone` scopes packet recipients and ownership to a trigger volume; manual object-sync replaces `VRC_ObjectSync` inside zones. |
| **Clean Installation** | Automatic, idempotent setup with backup/restore — no modified files inside `com.vrchat.worlds` or `Assets`. |

---

## How It Works

### 1. Automatic installer

After the package is imported, a small bootstrap assembly (`Editor/LCGUdonSharpInstaller.cs`) runs automatically:

```
1. Verify VRChat Worlds SDK 3.10.5 is installed
2. Back up SDK-bundled Integrations/UdonSharp
   → Library/LogicCuteGuy.LCGUdonSharp/Backups
3. Install the interface-enabled compiler at
   → Packages/com.logiccuteguy.lcgudonsharp/UdonSharp
4. Remove the SDK-bundled copy so Unity sees
   only ONE set of UdonSharp.* assemblies
```

Setup is **idempotent** — if an SDK/package refresh restores the bundled copy, setup runs again. On any SDK version other than `3.10.5`, setup stops instead of modifying an untested package.

### 2. Compiler extensions

The compiler keeps the original `UdonSharp` namespaces and assembly names, then adds lowering passes:

```
Your C# source
   │
   ├─ interfaces ──────────► program-variable / custom-event ABI calls
   ├─ async/await ─────────► frame-pool continuation lowering
   ├─ ref/out, closures,
   │  closed generics,
   │  dynamic, Span<T> ────► concrete array/offset/length locals + loops
   └─ [LCGPacket] ─────────► versioned packet frames + mailbox delivery
   │
   ▼
Udon assembly (runs in VRChat)
```

### 3. Manual packet networking

`[LCGPacket]` does **not** use native Udon variable sync. Instead:

- Assigning a packet field **queues** a send — repeated assignments in one frame are coalesced to the latest value; unchanged values are skipped; `ForceSendPacket(nameof(field))` bypasses suppression.
- Packet methods are delivered through a PlayerObject mailbox; `SendCustomNetworkEvent(...)` is lowered to packet delivery only when the target method has `[LCGPacket]`.
- Frames are versioned and validated with authority checks and replay protection.
- Owners provide snapshots when players are restored; departing-player state is discarded.

---

## Installation & Setup

### Requirements

- Unity **2022.3**
- VRChat Worlds SDK **3.10.5** (strict — other versions are refused)

### Steps

1. Add the package to your project (VPM manifest or local package reference):

   ```json
   "com.logiccuteguy.lcgudonsharp": "file:../path/to/com.logiccuteguy.lcgudonsharp"
   ```

2. Open Unity and let it compile. The installer verifies the SDK, backs up the bundled UdonSharp, and installs the compiler automatically.

3. (Optional) Force setup any time via **Tools > LCGUdonSharp > Install or Repair**.

4. Build/test your world as usual.

### Uninstalling

> **Important:** Use **Tools > LCGUdonSharp > Restore VRChat UdonSharp and Disable Auto Setup** *before* removing the package. This restores the backed-up SDK copy and removes the generated compiler folder.

---

## Usage

### Interfaces

Define a plain C# interface and implement it on `UdonSharpBehaviour` classes:

```csharp
public interface INumberOperation
{
    int Apply(int value);
    int LastResult { get; }
}
```

```csharp
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class AddNumberOperation : UdonSharpBehaviour, INumberOperation
{
    [SerializeField] private int amount = 5;
    private int _lastResult;

    public int LastResult => _lastResult;

    public int Apply(int value)
    {
        _lastResult = value + amount;
        return _lastResult;
    }
}
```

Dispatch through the interface (Unity cannot serialize interface fields, so store concrete components and assign to interface locals at runtime):

```csharp
INumberOperation firstOperation  = addOperation;       // AddNumberOperation
INumberOperation secondOperation = multiplyOperation;  // MultiplyNumberOperation

int added      = firstOperation.Apply(input);   // 10 → 15
int multiplied = secondOperation.Apply(input);  // 10 → 30
```

<details>
<summary><b>Interface limitations</b></summary>

Not supported: generic interfaces or methods, interface inheritance, default/static interface members, events, indexers, explicit implementations, `[NetworkCallable]` interface implementations, and member names colliding with built-in Udon events.

</details>

### Async/Await

```csharp
public override async void Interact()
{
    Debug.Log("[Async/Await] Interact started.");

    await Task.Yield();
    Debug.Log("[Async/Await] Continued on the next frame.");

    await Task.Delay(1000);
    Debug.Log("[Async/Await] Continued after one second.");
}
```

SDK awaits keep the traditional callbacks — the legacy callback body runs first, then the generated continuation runs afterward:

```csharp
public override async void Interact()
{
    await VRCAsync.LoadImageAsync(_downloader, imageUrl, targetMaterial);
    // Runs after OnImageLoadSuccess/OnImageLoadError:
    Debug.Log("Texture: " + _lastDownloadedTexture);
}

public override void OnImageLoadSuccess(IVRCImageDownload result)
{
    _lastDownloadedTexture = result.Result;   // store the SDK result here
}
```

Supported awaits: parameterless `async void` methods, straight-line `Task.Yield()`, constant positive `Task.Delay(int)`, and **one VRChat SDK await per behaviour**:

| SDK await | Example |
|-----------|---------|
| `VRCAsync.LoadStringAsync(url, out result)` | [`AsyncStringDownloadExample`](Example/AsyncAwait/AsyncStringDownloadExample.cs) — `out` overload copies the SDK result into an instance field before the continuation. |
| `VRCAsync.LoadImageAsync(...)` | [`AsyncImageDownloadExample`](Example/AsyncAwait/AsyncImageDownloadExample.cs) — correlated by `IVRCImageDownload` request identity. |
| `VRCAsync.LoadVideoAsync` / `WaitForVideoEndAsync` | [`AsyncVideoLoadExample`](Example/AsyncAwait/AsyncVideoLoadExample.cs), [`AsyncVideoEndExample`](Example/AsyncAwait/AsyncVideoEndExample.cs) |
| `VRCAsync.RequestGPUReadbackAsync` | [`AsyncGpuReadbackExample`](Example/AsyncAwait/AsyncGpuReadbackExample.cs) |
| `VRCAsync.RequestSerializationAsync` | [`AsyncSerializationExample`](Example/AsyncAwait/AsyncSerializationExample.cs) |
| Creator Economy lists | [`AsyncAvailableProductsExample`](Example/AsyncAwait/AsyncAvailableProductsExample.cs), [`AsyncPurchasesExample`](Example/AsyncAwait/AsyncPurchasesExample.cs), [`AsyncProductOwnersExample`](Example/AsyncAwait/AsyncProductOwnersExample.cs) |

Constraints: each async method is single-flight (a second call while the continuation is pending is ignored); string completion is correlated by URL, so don't start another request with the same URL on the behaviour while one is pending. Locals, parameters, nested awaits, explicit returns, and assigning `Task<T>` results directly still produce build diagnostics. Full details in [`Example/AsyncAwait/README.md`](Example/AsyncAwait/README.md).

### Extended language

```csharp
// ref/out — locals, fields, array elements, out var, recursion
Swap(ref values[0], ref values[1]);
AddUntil(ref values[0], target, out int recursiveCalls);

// LINQ closures — lowered to loops at build time, no delegates at runtime
int[] result = values
    .Where(value => value >= minimum)
    .Select(value => value * localScale)
    .ToArray();
```

Also supported: closed generic static helpers, closed generic interface diamonds, proven `dynamic`, array-backed local `Span<int>`.

### Manual packet networking

**Coalesced field with callback:**

```csharp
[LCGPacket(
    Authority = LCGPacketAuthority.ObjectOwner,
    Callback = nameof(OnScoreChanged))]
[SerializeField] private int score;

public void OnScoreChanged(VRCPlayerApi sender) { /* verified sender */ }

// Repeated assignments in one frame send only the latest value:
score++; score++; score++;        // → one packet with final value
ForceSendPacket(nameof(score));   // → bypass suppression, always send
```

**Packet methods — broadcast vs. targeted:**

```csharp
// Broadcast to everyone else (lowered to mailbox delivery because of [LCGPacket])
SendCustomNetworkEvent(NetworkEventTarget.Others,
    nameof(ReceiveAnnouncement), "Broadcast #1", transform.position);

// Targeted delivery to one player
SendLCGNetworkEvent(target, nameof(ReceiveAnnouncement),
    "Targeted #1", transform.position);

// Direct call is always local — no packet created
ReceiveAnnouncement("Local-only #1", transform.position);

[LCGPacket(Authority = LCGPacketAuthority.Any)]
public void ReceiveAnnouncement(string message, Vector3 origin) { }
```

Public `void` methods support up to eight supported arguments. Packet field writes require object ownership — call `Networking.SetOwner(Networking.LocalPlayer, gameObject)` first.

**Network zones:** add `LCGNetworkZone` to a trigger collider to restrict descendant packet recipients and ownership to players inside the trigger. Inside zones, `VRC_ObjectSync` is replaced with a manual relay; script transforms sync on demand via `LCGNetwork.RequestObjectSync(gameObject)` (pickups sync automatically while held).

Zone colliders in **separate hierarchies may overlap** — each scene object belongs to its nearest ancestor zone. Zone colliders in the same parent/child hierarchy may **not** overlap; that configuration fails the build before helpers are generated.

<details>
<summary><b>PlayerObject networking notes</b></summary>

- `[LCGPacket]` fields/methods work on `VRCPlayerObject` templates; each clone is addressed by template + owning player.
- Wait for `OnPlayerRestored`, then use `Networking.FindComponentInPlayerObjects(player, templateBehaviour)`.
- Use `Authority = LCGPacketAuthority.ObjectOwner` for fields only their player should change (default is `Any`).
- PlayerObject templates and `LCGNetworkZone` must be in **separate hierarchies** — combining them fails the build.
- Packet fields are session-only: not persisted by VRChat, not replayed for late joiners.
- After upgrading, recompile all UdonSharp programs and rebuild the world (protocol version 2).

</details>

---

## Examples

The `Example/` folder contains runnable scenes and scripts for every feature. Open **`Example/TestLCGUdonSharp.unity`** or follow each folder's README.

| Example | Feature | Description |
|---------|---------|-------------|
| [`Example/AsyncAwait`](Example/AsyncAwait/README.md) | Async lowering | `Task.Yield()`, `Task.Delay`, string/image/video awaits, GPU readback, serialization, Creator Economy. |
| [`Example/Interfaces`](Example/Interfaces/README.md) | Interface MVP | `INumberOperation` with Add/Multiply implementations invoked through the interface. Input `10` → `15`, `30`. |
| [`Example/Networking`](Example/Networking/README.md) | LCG manual packets | Coalesced packet fields with callbacks, broadcast/targeted packet methods, zone-scoped object sync. |
| [`Example/GenericRestrictions`](Example/GenericRestrictions/README.md) | Build-time diagnostics | Bad/good pairs for open generics, `List<T>`, interface contracts, multiple bases, `Task<T>`. |
| [`Example/ExtendedLanguage`](Example/ExtendedLanguage/README.md) | Extended C# | `ref`/`out`, closed generics & interface diamonds, LINQ closures, `dynamic`, `Span<T>`. |

### Quick start: run an example

1. Fix any unrelated C# compilation errors and let Unity finish compiling.
2. Open **`Example/TestLCGUdonSharp.unity`**, or add an example component to a GameObject in your own scene — every example `.cs` already ships with its paired UdonSharp `.asset`.
3. Enter Play Mode and inspect the Console.

<details>
<summary><b>⚠️ UdonSharp <code>.asset</code> gotcha</b></summary>

Every UdonSharp `.cs` needs a paired `.asset` UdonSharpProgramAsset (same basename). Files created directly on the filesystem do **not** auto-generate it — only Unity's **Assets > Create > U# Script** does. A missing `.asset` shows *"The associated script cannot be loaded"* and skips Udon compilation.

</details>

---

## Supported vs. Rejected

<details>
<summary><b>Supported</b></summary>

- Source-defined C# interfaces (implicit implementation, parameters, returns, properties, arrays of interfaces, diamonds via closed generics)
- `async void` with `Task.Yield()`, constant `Task.Delay(int)`, one SDK await per behaviour
- `ref` / `out` / `out var`, recursive methods, array-element by-ref
- Closed generic static helpers and closed generic interfaces
- LINQ `Where`/`Select`/`ToArray` with capturing lambdas (lowered to loops)
- Proven `dynamic` (single concrete type substituted at build time)
- Array-backed local `Span<T>` (indexing, `Length`, `Fill`, `ToArray`, `Clear`)
- `[LCGPacket]` fields and `void` methods (≤ 8 args), zones, PlayerObject mailboxes

</details>

<details>
<summary><b>Rejected at build time (with diagnostics)</b></summary>

- Open generics (`typeof(Converter<>)`), generic behaviours, generic heap objects, `List<T>`
- Generic interfaces/methods, interface inheritance, default/static interface members, events, indexers, explicit interface implementations
- Nested awaits, explicit returns in async, direct `Task<T>` result assignment, multiple simultaneous SDK awaits
- Continuous Rigidbody behaviours and networked Udon Graph behaviours **inside zones**
- Overlapping parent/child zones; `[UdonSynced]` fields under a zone (fail closed until per-zone variants ship)

</details>

Zone colliders in separate hierarchies may overlap. Parent/child zone colliders cannot overlap; scene objects otherwise belong only to their nearest ancestor `LCGNetworkZone`.

---

## Menu Commands

| Menu | Action |
|------|--------|
| **Tools > LCGUdonSharp > Install or Repair** | Force the installer to re-run (backup + install + remove bundled copy). |
| **Tools > LCGUdonSharp > Restore VRChat UdonSharp and Disable Auto Setup** | Restore the SDK-bundled copy and remove the generated compiler folder — run this **before** uninstalling. |

There are no example-builder menu commands; the example scene and paired `.asset` files ship with the package.

### Diagnostics

LCG network logging is off by default. Enable **Edit > Project Settings > Udon Sharp > Debugging > LCG network diagnostics** before compiling to include pickup and packet-delivery output, then recompile UdonSharp programs.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| *"The associated script cannot be loaded"* | The `.cs` is missing its paired UdonSharp `.asset`. Use **Assets > Create > U# Script** so Unity generates both files together. |
| Installer stops immediately | SDK version must be exactly `3.10.5`. Setup refuses other versions by design. |
| Duplicate `UdonSharp.*` assemblies | Run **Tools > LCGUdonSharp > Install or Repair** — the SDK's bundled copy may have been restored. |
| Packet fields not syncing after upgrade | Protocol is versioned (v2): recompile all UdonSharp programs and rebuild the world. |
| Build fails around `LCGNetworkZone` | Zones fail closed on Continuous bodies, Udon Graph behaviours, overlapping parent/child zones, `[UdonSynced]` under a zone, and PlayerObject templates sharing a hierarchy. Zones in separate hierarchies may overlap. |
| Need to uninstall | Run **Restore VRChat UdonSharp and Disable Auto Setup** first, then remove the package. |

---

## License

[MIT](LICENSE.md) © 2026 LogicCuteGuy

See [CHANGELOG.md](CHANGELOG.md) for release history.
