# LCGUdonSharp

**Interface-enabled UdonSharp compiler for VRChat — hooks the SDK's UdonSharp instead of replacing it.**

[![Unity](https://img.shields.io/badge/Unity-2022.3-blue)](https://unity.com/)
[![VRChat Worlds SDK](https://img.shields.io/badge/VRChat_Worlds_SDK-3.10.6-orange)](https://github.com/VRChat/worlds)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE.md)
[![Package Version](https://img.shields.io/badge/version-0.3.10-informational)](package.json)

LCGUdonSharp extends the UdonSharp compiler with C# interfaces, synchronous compiler-managed `try`/`catch`, build-time `async/await` lowering, extended language constructs (`ref`/`out`, closed generics, LINQ closures, `dynamic`, `Span<T>`), and a manual packet networking layer — while keeping every modified source file inside `Packages/com.logiccuteguy.lcgudonsharp` instead of the VRChat SDK or `Assets`.

> **Status:** interfaces, synchronous exceptions, async lowering, and extended-language support are ready for world testing.

## Table of Contents

- [Features](#features)
- [Key terms](#key-terms)
- [How It Works](#how-it-works)
- [Installation & Setup](#installation--setup)
- [Usage](#usage)
- [Examples](#examples)
- [Supported vs. Rejected](#supported-vs-rejected)
- [Menu Commands](#menu-commands)
- [Troubleshooting](#troubleshooting)
- [Acknowledgements](#acknowledgements)
- [Contributing](#contributing)
- [License](#license)

---

## Features

| Feature | Description |
|---------|-------------|
| **C# Interfaces** | Source-defined interfaces implemented by `UdonSharpBehaviour` classes — method calls, parameters, return values, properties, multiple implementations, interface arrays. |
| **Async/Await** | Build-time lowering for `await Task.Yield()`, `await Task.Delay(int)`, and one VRChat SDK await per behaviour (string/image downloads, video, GPU readback, serialization, Creator Economy). |
| **Synchronous Exceptions** | Compiler-managed `try`/`catch`/`finally`, explicit throws, rethrow, and guarded null, index, and integral divide/modulo failures without relying on unavailable Udon exception opcodes. |
| **Extended Language** | `ref`/`out` (including `out var` and recursion), closed generics, interface diamonds, LINQ lambdas with captures, proven `dynamic`, array-backed `Span<T>`. |
| **C# Collections & JSON** | Exact `List<T>` and `Dictionary<TKey,TValue>` syntax lowered to `DataList`/`DataDictionary`, plus a VRCJson-backed `System.Text.Json` facade and manual synced-collection payloads. |
| **ScriptableObject Data** | Ordinary custom `ScriptableObject` assets, nested asset references and derived assets in base-typed fields/arrays. Read-only snapshots support typed field reads, `is`, `as` and checked casts; array reads return copies. See the [shop and equipment examples](Example/ScriptableObjects/README.md). |
| **Hook, not replace** | The SDK's bundled UdonSharp stays installed and unmodified. LCGUdonSharp joins its assemblies through `.asmref` files and redirects compilation with Harmony, so nothing is copied, backed up, or restored. |

---

## Key terms

These names appear in the docs, the folder layout, and Unity's UI. Two of them sound alike but are completely different assets:

| Term | What it is |
|------|------------|
| **Udon** | The stack-based VM that runs VRChat worlds. UdonSharp compiles your C# down to Udon assembly (UASM), which is what actually runs in-game. |
| **Program asset** (`UdonSharpProgramAsset`) | The asset paired with one U# script: `MyScript.cs` ↔ `MyScript.asset`, same folder, same basename. Its `sourceCsScript` points at the script, and the compiled Udon program is stored on it. **Assets > Create > U# Script** creates both files together and links them. |
| **U# assembly definition** (`UdonSharpAssemblyDefinition`) | A different asset that registers one Unity assembly definition (`.asmdef`) for UdonSharp compilation through its single `sourceAssembly` field. It does **not** hold compiled code. This package uses the `*.USharp.asset` suffix for these: `Example/LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset` and `UdonSharp/Runtime/LCGBehaviours/LogicCuteGuy.LCGUdonSharp.Runtime.USharp.asset`. |
| **Assembly scanning** | How the compiler finds your behaviours. `Assembly-CSharp` is always included. Every other assembly must be registered with a U# assembly definition, otherwise its `UdonSharpBehaviour` classes are invisible to UdonSharp even though Unity compiles them without errors. |
| **Extern** | A Unity/SDK method that maps to a built-in Udon node (`Debug.Log`, `transform.Rotate`, `Networking.SetOwner`, …). It executes as one native Udon instruction, so a fault raised inside an extern cannot be caught by `try`/`catch`. |

> **Rule of thumb:** one U# script needs one program asset with the **same name** in the **same folder**. One `.asmdef` needs one U# assembly definition, and its **name does not matter** (this package uses `*.USharp.asset`).

---

## How It Works

### 1. Compiler hook

Nothing in `com.vrchat.worlds` is edited or replaced:

```
Packages/com.logiccuteguy.lcgudonsharp
├─ Editor/UdonSharp.Editor.asmref    → compiles into the SDK's UdonSharp.Editor
│  ├─ Compiler/                      LCG compiler (namespace UdonSharp.LCGCompiler)
│  ├─ Serialization/                 List/Dictionary + ScriptableObject data serializers
│  └─ LCGUdonSharpHook.cs            Harmony entry hook
└─ Runtime/UdonSharp.Runtime.asmref  → adds VRCAsync, UdonException, System.Text.Json facade
```

On every domain reload `LCGUdonSharpHook` patches the stock `UdonSharpCompilerV1.Compile`, `CompileSync` and `WaitForCompile` so they run the LCG compiler. It also resets the LCG compiler's assembly cache along with the stock one, maps List/Dictionary/ScriptableObject data fields to Udon types, and registers their serializers. The stock compiler stays loaded but goes unused. Removing the package restores stock UdonSharp after a recompile.

The LCG compiler is pinned to the SDK's UdonSharp internals, so the package accepts Worlds SDK `>=3.10.6 <3.11.0`. If an SDK update renames a hooked method, the hook logs a `MissingMethodException` when the editor loads, and stock UdonSharp compiles instead, so LCG-only syntax shows up as stock compiler errors.

### 2. Compiler extensions

The LCG compiler is UdonSharp's compiler with these lowering passes added:

```
Your C# source
   │
   ├─ interfaces ──────────► program-variable / custom-event ABI calls
   ├─ async/await ─────────► frame-pool continuation lowering
   ├─ try/catch/finally ───► hidden payload state + guarded control flow
   ├─ ref/out, closures,
   │  closed generics,
   │  dynamic, Span<T> ────► concrete array/offset/length locals + loops
   └─ List/Dictionary/JSON ─► DataList/DataDictionary + VRCJson helpers
   │
   ▼
Udon assembly (runs in VRChat)
```

---

## Installation & Setup

Install through VCC/ALCOM, or add a local reference:

```json
"com.logiccuteguy.lcgudonsharp": "file:../path/to/com.logiccuteguy.lcgudonsharp"
```

Open Unity and let it compile. There is no installer and nothing to set up. Examples are optional samples.

### Requirements

- Unity **2022.3**
- VRChat Worlds SDK **3.10.6** (`>=3.10.6 <3.11.0`)

### Upgrading from 0.4.x

0.4.x deleted the SDK's bundled UdonSharp and ran a copy from this package. If an SDK update restored the bundled copy, Unity gave its files new GUIDs, while your program assets still point at the original ones. Either way, the SDK copy has to come back intact:

1. Close Unity.
2. Update LCGUdonSharp to 0.5.0. This removes the 0.4.x compiler copy.
3. Reinstall `com.vrchat.worlds` in VCC/ALCOM so `Integrations/UdonSharp` is present with its original metas.
4. Open Unity and run **VRChat SDK > Udon Sharp > Refresh All UdonSharp Assets**.

`SendLCGNetworkEvent`, `ForceSendPacket`, `LCGNetwork.RequestObjectSync` and the **LCG network diagnostics** setting were removed with the compiler-side networking.

### Uninstalling

Remove the package. The SDK's UdonSharp was never modified, so it compiles on its own after the next domain reload. Scripts that use LCG-only syntax then report errors from the stock compiler.

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

### Synchronous exception handling

```csharp
try
{
    LoadSlot(selectedIndex);
}
catch (ArgumentException exception)
{
    Debug.LogError(exception.Message);
}
catch (UdonException exception)
{
    Debug.LogError(exception.Operation + ": " + exception.Message);
}
finally
{
    isBusy = false;
}
```

The compiler recognizes explicit `throw new` statements and guards null receivers, array/string indices, and integral division or modulo inside protected code and its same-behaviour call graph. `UdonException` carries `Kind`, `Code`, `Operation`, and `Message`; standard catch variables expose only `Message`. Catch-all clauses and `throw;` are supported, and all abrupt exits run applicable `finally` blocks.

This is synchronous emulation. A real fault raised inside an Udon extern cannot be intercepted and still halts that behaviour. Custom events, network calls, other behaviours, floating-point division, overflow, casts, and SDK domain failures are exception boundaries or outside v1. `await` inside `try` is rejected with a targeted diagnostic; existing asynchronous callback/result behavior is unchanged. See [`ExceptionHandlingExample`](Example/ExtendedLanguage/ExceptionHandlingExample.cs).

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

### Collections, JSON, bytes, and bits

Exact `List<T>` and `Dictionary<TKey,TValue>` types can be written with ordinary C# syntax. The compiler lowers them to VRChat `DataList`, `DataDictionary`, and `DataToken` operations; existing code that directly uses those SDK types or `VRCJson` is left unchanged.

Version 0.3.x also serializes collection fields and arrays of collections between their C# proxy values and lowered Udon storage. Null and empty collections remain distinct, nested collections preserve their container types, and primitive or enum elements retain their exact token representation.

```csharp
using System.Collections.Generic;
using System.Text.Json;

List<int> values = new List<int> { 1, 2, 3 };
Dictionary<string, int> scores = new Dictionary<string, int>
{
    { "alpha", 10 },
};

scores["count"] = values.Count;
string json = JsonSerializer.Serialize(scores,
    new JsonSerializerOptions { WriteIndented = true });
Dictionary<string, int> copy =
    JsonSerializer.Deserialize<Dictionary<string, int>>(json);
```

Supported collection members include constructors, initializers, count/capacity, typed indexers, `foreach`, the common add/insert/remove/search operations, `TryGetValue`, dictionary keys/values, and typed `ToArray`. Collection interfaces, derived collection classes, custom comparers, nullable collection annotations, and collection LINQ remain unsupported.

String-key dictionaries serialize as normal JSON objects. Other JSON-safe key types use the versioned `$lcgDictionary` entry envelope. JSON rejects object references, NaN, and Infinity as VRCJson does; integer targets also require a finite, integral, in-range JSON number. `TrySerialize` and `TryDeserialize` return an error string, while `Serialize` and `Deserialize` throw compiler-managed `JsonException`.

For network synchronization, use a non-Inspector field on a Manual-sync behaviour:

```csharp
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SharedValues : UdonSharpBehaviour
{
    [UdonSynced, System.NonSerialized]
    private List<int> values = new List<int>();

    public void AddValue(int value)
    {
        values.Add(value);
        RequestSerialization(); // ownership and sending stay explicit
    }
}
```

The compiler adds one hidden synced JSON string and composes it into `OnPreSerialization` and `OnDeserialization`. An empty payload means `null`; `[]` and `{}` mean empty collections. Decode failures keep the current value, and encode failures keep the last valid payload. Continuous sync, `FieldChangeCallback`, Inspector serialization, and statically non-JSON-safe synchronized elements are rejected.

Normal SDK-supported binary APIs remain available: `byte[]`, bitwise operators, `BitConverter`, `Buffer.BlockCopy`, UTF-8 encoding, and `DataToken.Bitcast`. The intrinsic facade intentionally occupies `System.Text.Json`; installing a separate real `System.Text.Json` assembly can create a namespace/type conflict. Calls made outside compiler-lowered UdonSharp behaviours throw `NotSupportedException`.

---

## Examples

The `Example/` folder contains runnable scenes and scripts for every feature. Open **`Example/TestLCGUdonSharp.unity`** or follow each folder's README — [`Example/README.md`](Example/README.md) maps the whole folder and explains the two different `.asset` kinds that sit next to the scripts.

| Example | Feature | Description |
|---------|---------|-------------|
| [`Example/AsyncAwait`](Example/AsyncAwait/README.md) | Async lowering | `Task.Yield()`, `Task.Delay`, string/image/video awaits, GPU readback, serialization, Creator Economy. |
| [`Example/Interfaces`](Example/Interfaces/README.md) | Interface MVP | `INumberOperation` with Add/Multiply implementations invoked through the interface. Input `10` → `15`, `30`. |
| [`Example/GenericRestrictions`](Example/GenericRestrictions/README.md) | Collections and restrictions | Runnable collection/JSON/binary example plus rejected open generics, interfaces, multiple bases, and `Task<T>` notes. |
| [`Example/ExtendedLanguage`](Example/ExtendedLanguage/README.md) | Extended C# | `try`/`catch`/`finally`, `ref`/`out`, closed generics & interface diamonds, LINQ closures, `dynamic`, `Span<T>`. |

### Quick start: run an example

1. Fix any unrelated C# compilation errors and let Unity finish compiling.
2. Open **`Example/TestLCGUdonSharp.unity`**, or add an example component to a GameObject in your own scene — every example `.cs` already ships with its paired `UdonSharpProgramAsset` (same basename).
3. Enter Play Mode and inspect the Console.

<details>
<summary><b>Required: program assets and assembly registration</b></summary>

Two different assets are involved (definitions in [Key terms](#key-terms)):

1. **One program asset per script.** Every U# `.cs` needs its paired `UdonSharpProgramAsset`: `MyScript.cs` ↔ `MyScript.asset`, same folder, same basename, `sourceCsScript` linked. Only **Assets > Create > U# Script** creates the pair together. Files written straight to disk never generate the `.asset`, so copy it along with the script or recreate the script through that menu. A missing program asset shows *"The associated script cannot be loaded"* and the script is skipped by Udon compilation.
2. **One assembly registration per `.asmdef`.** The compiler always scans `Assembly-CSharp`. Any other assembly must be registered by a `UdonSharpAssemblyDefinition` asset whose `sourceAssembly` points at the `.asmdef` — create one with **Assets > Create > U# Assembly Definition** (select the `.asmdef` first and it is wired automatically). The examples are registered this way through `Example/LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset`; without it, `LogicCuteGuy.LCGUdonSharp.Examples.asmdef` would compile in Unity but never reach Udon.

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
- Exact `List<T>` and `Dictionary<TKey,TValue>` with common members, iteration, nesting, JSON conversion, and Manual-mode JSON synchronization
- `byte[]`, bitwise operators, `BitConverter`, `Buffer.BlockCopy`, UTF-8 encoding, and `DataToken.Bitcast`
- Synchronous `try`/`catch`/`finally`, approved typed catches, catch-all, explicit `throw new`, rethrow, and guarded null/bounds/integral-zero failures

</details>

<details>
<summary><b>Rejected at build time (with diagnostics)</b></summary>

- Open generics (`typeof(Converter<>)`), generic behaviours, generic heap objects other than the exact lowered collections
- Collection interfaces, derived collections, custom comparers, nullable collections, Inspector-serialized collections, and collection LINQ
- Generic interfaces/methods, interface inheritance, default/static interface members, events, indexers, explicit interface implementations
- Nested awaits, explicit returns in async, direct `Task<T>` result assignment, multiple simultaneous SDK awaits
- `await` inside `try`, catch filters, arbitrary thrown expressions, unsupported exception types/constructors/members, and catch variables that escape their catch
- Catching native extern/VM faults, cross-behaviour or custom-event propagation, floating-point divide-by-zero, overflow, invalid casts, or SDK domain failures

</details>


---

## Menu Commands

| Command | Purpose |
|---------|---------|
| **Edit > Project Settings > Udon Sharp > LCGUdonSharp** | Async task frame capacity (1-256 frames per behaviour), saved in `ProjectSettings/LogicCuteGuy.LCGUdonSharp.json`. |
| **Assets > Create > U# Script** | Create a U# script **and** its paired program asset, already linked. Must be saved under `Assets/` or `Packages/`; anything else is refused. |
| **Assets > Create > U# Assembly Definition** | Create a `UdonSharpAssemblyDefinition` registration asset. With the `.asmdef` selected it is assigned to `sourceAssembly` automatically. |
| **VRChat SDK > Udon Sharp > Refresh All UdonSharp Assets** | Recompile every UdonSharp program asset in the project. |
| **VRChat SDK > Udon Sharp > Force Upgrade** | Run the UdonSharp asset upgrader. |
| **VRChat SDK > Udon Sharp > Class Exposure Tree** | Show which C# types and members are exposed to Udon. |
| **VRChat SDK > Udon Sharp > Node Definition Grabber** | Dump the built-in Udon node (extern) definitions. |
| **VRChat SDK > Udon Sharp > Parse Logs from File** | Map Udon log output read from a file back to C# sources. |
| **Edit > Easy Event Editor Settings** | Configure the Easy Event Editor used by the UdonSharp inspectors. |

There are no example-builder menu commands; the example scene and paired `.asset` files ship with the package.

### Diagnostics

| Symptom | Fix |
|---------|-----|
| Script compiles in Unity but UdonSharp ignores it | Its assembly is not registered. `Assembly-CSharp` is always scanned; scripts inside an `.asmdef` need a `UdonSharpAssemblyDefinition` (**Assets > Create > U# Assembly Definition**) pointing at that `.asmdef`. |
| Edited a U# assembly definition but nothing recompiles | Changing `sourceAssembly` resets the compiler's assembly cache — run **VRChat SDK > Udon Sharp > Refresh All UdonSharp Assets** to rebuild. |

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| *"The associated script cannot be loaded"* | The `.cs` is missing its paired `UdonSharpProgramAsset`. Use **Assets > Create > U# Script** so Unity generates both files together, or copy the `.asset` along with the `.cs`. |
| `MissingMethodException` from `LCGUdonSharpHook` on load | The installed Worlds SDK changed a hooked UdonSharp method. Use an SDK version inside this package's range. |
| Program assets lost their script after upgrading from 0.4.x | The SDK's UdonSharp metas carry regenerated GUIDs. Reinstall `com.vrchat.worlds` from VCC, then refresh all UdonSharp assets. |
| Need to uninstall | Remove the package; nothing else to undo. |

---

## Acknowledgements

LCGUdonSharp builds on [UdonSharp](https://github.com/MerlinSan/UdonSharp), originally created by Merlin, and on the VRChat Worlds SDK. It hooks the SDK's UdonSharp rather than replacing it, so existing projects keep the SDK's assemblies and asset GUIDs.

## Contributing

Bug reports, feature requests, and pull requests are welcome in the project repository. When reporting a compiler diagnostic, include the Unity version, the Worlds SDK version, and the smallest snippet that reproduces the issue.

## License

[MIT](LICENSE.md) © 2026 LogicCuteGuy

See [CHANGELOG.md](CHANGELOG.md) for release history.
