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
