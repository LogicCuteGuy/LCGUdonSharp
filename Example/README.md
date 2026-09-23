# LCGUdonSharp examples

> Documentation version: **0.3.4** · [Package guide](../README.md)

Runnable UdonSharp behaviours for the LCGUdonSharp 0.3.x feature set. Open **`TestLCGUdonSharp.unity`** to get a scene with the networking showcases wired up, or add any example component to a GameObject in your own scene.

## Folder map

| Folder | Feature | Scripts | Guide |
|--------|---------|---------|-------|
| [`Networking/`](Networking/README.md) | Manual packet networking + `LCGNetworkZone` | `LCGPacketFieldShowcase`, `LCGPacketMethodShowcase`, `LCGZoneObjectShowcase` | [Networking guide](Networking/README.md) |
| [`Interfaces/`](Interfaces/README.md) | C# interfaces on `UdonSharpBehaviour` | `InterfaceExampleRunner`, `AddNumberOperation`, `MultiplyNumberOperation` | [Interfaces guide](Interfaces/README.md) |
| [`AsyncAwait/`](AsyncAwait/README.md) | Build-time `async`/`await` lowering | `AsyncYieldDelayExample`, `AsyncStringDownloadExample`, `AsyncImageDownloadExample`, `AsyncVideoLoadExample`, `AsyncVideoEndExample`, `AsyncGpuReadbackExample`, `AsyncSerializationExample`, `AsyncAvailableProductsExample`, `AsyncPurchasesExample`, `AsyncProductOwnersExample` | [Async/await guide](AsyncAwait/README.md) |
| [`ExtendedLanguage/`](ExtendedLanguage/README.md) | `ref`/`out`, closed generics, LINQ closures, `dynamic`, `Span<T>`, `try`/`catch` | `RefOutExample`, `GenericHierarchyExample`, `LinqClosureExample`, `DynamicExample`, `SpanExample`, `ExceptionHandlingExample` | [Extended language guide](ExtendedLanguage/README.md) |
| [`GenericRestrictions/`](GenericRestrictions/README.md) | Normal C# collections plus rejected generic/hierarchy shapes | `OpenGenericsExample`, `GenericBehavioursExample`, `GenericHeapObjectsExample`, `ListTypesExample`, `InterfaceMembersExample`, `MultipleConcreteBasesExample` | [Collections and restrictions guide](GenericRestrictions/README.md) |

Root-level files:

| File | What it is |
|------|------------|
| `TestLCGUdonSharp.unity` | Demo scene with the showcases ready to run. |
| `LogicCuteGuy.LCGUdonSharp.Examples.asmdef` | Unity assembly definition that compiles every example script into one assembly. |
| `LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset` | `UdonSharpAssemblyDefinition` registration pointing `sourceAssembly` at the asmdef above. This is what makes the whole folder visible to the UdonSharp compiler. |

## The two different `.asset` kinds

Two unrelated asset types sit next to the scripts. Mixing them up is the most common setup mistake (full definitions in the root README's [Key terms](../README.md#key-terms)):

1. **Program assets** (`UdonSharpProgramAsset`) — one per script, **same basename**: `RefOutExample.cs` ↔ `RefOutExample.asset`. The `.asset` links `sourceCsScript` to the `.cs` and stores the compiled Udon program. Only **Assets > Create > U# Script** creates the pair together; a `.cs` written straight to disk has no program asset and shows *"The associated script cannot be loaded"*. Every example script here already ships with its program asset.
2. **Assembly registration** (`UdonSharpAssemblyDefinition`) — one per `.asmdef`, and its **name does not matter** (this package uses the `*.USharp.asset` suffix). It only holds a `sourceAssembly` reference. The compiler always scans `Assembly-CSharp`; every other assembly must be registered like this or its behaviours are invisible to UdonSharp even though Unity compiles them fine.

## Running the examples

1. Fix any unrelated C# compilation errors and let Unity finish compiling.
2. Open **`TestLCGUdonSharp.unity`**, or add example components to GameObjects in your own scene and wire their serialized fields (each folder's README lists exactly what to assign).
3. Enter Play Mode and watch the Console. The networking examples need a second player (or a separate client/test build) to show remote behaviour, and the zone showcase needs an `LCGNetworkZone` trigger collider covering the play area.

## Copying examples into your own code

- Copy the `.cs` **and** its same-named `.asset` together, or recreate the script with **Assets > Create > U# Script** and paste the code in.
- Scripts dropped under `Assets/` without an `.asmdef` land in `Assembly-CSharp`, which UdonSharp scans automatically.
- Scripts you move into your own `.asmdef` need that `.asmdef` registered with **Assets > Create > U# Assembly Definition** (select the `.asmdef` first so `sourceAssembly` is filled in).
- The networking examples depend on the LCG runtime pieces (`LCGRuntime`, `LCGNetworkZone`, `LCGNetwork`, `LCGManualObjectSync`) in `UdonSharp/Runtime/LCGBehaviours/`, themselves registered through `LogicCuteGuy.LCGUdonSharp.Runtime.USharp.asset`.

## See also

- Root [README](../README.md): [Features](../README.md#features), [Supported vs. Rejected](../README.md#supported-vs-rejected), [Troubleshooting](../README.md#troubleshooting).
- [CHANGELOG](../CHANGELOG.md) for release history.
