# ScriptableObject data

> Documentation version: **0.3.8** · [All examples](../README.md) · [Package guide](../../README.md)

Use ordinary Unity `ScriptableObject` classes and assets directly in UdonSharp
fields. No extra base class, attribute, or manual copying is required:

```csharp
[CreateAssetMenu(menuName = "My World/Item")]
public class ItemData : ScriptableObject
{
    public string displayName;
    public int price;
    public string[] descriptions;
    public Texture2D icon;
}

public class Shop : UdonSharpBehaviour
{
    public ItemData item; // Assign the .asset in the Inspector.
    public override void Interact()
    {
        if (item == null) return;
        Debug.Log(item.displayName + ": " + item.price);
    }
}
```

Include `using UnityEngine;` and `using UdonSharp;` in your files. Keep the usual
U# program asset and assembly registration for the **behaviour**. The data class
does not need a program asset and may live in a separate ordinary C# assembly.

## Run the shop

Open `../TestLCGUdonSharp.unity`. The `ScriptableObjectShopExample` prefab is at
world position `(7, 0, 3)`. Enter Play Mode with ClientSim and interact with its
pink board. It starts with 100 coins; purchases cost 35, leaving 65 and then 30.
A third purchase displays **Not enough coins**. Name, price, rarity, color and
descriptions come from `StrawberryMilk.asset`; the next catalog entry comes from
`GreenTea.asset`.

You can also drag `ScriptableObjectShopExample.prefab` into your own world.
Replace its `item` and `catalog` asset references in the Inspector. Change the
assets before entering Play Mode/building to change the baked values.
Calling `TestArrayCopy` displays whether modifying a local description array
leaves the original snapshot intact.

## Nested assets and polymorphic references

`ItemDefinition` is an ordinary abstract ScriptableObject base class. Its `economy`
field refers to a separate `EconomyData` asset, and its `upgrades` array can contain
derived definitions. Assign `WeaponDefinition` and `SpellDefinition` assets to
behaviour fields declared as `ItemDefinition` or `ItemDefinition[]`:

```csharp
public ItemDefinition item;

public override void Interact()
{
    Debug.Log(item.economy.price); // Nested asset.
    if (item is WeaponDefinition weapon)
        Debug.Log(weapon.damage);
    SpellDefinition spell = item as SpellDefinition; // Null if incompatible.
    if (spell != null) Debug.Log(spell.power);
}
```

The new `ScriptableObjectEquipmentExample` prefab is at `(12, 0, 3)` in the same
scene. Its base-typed `item` holds `TrainingSword.asset`; its base-typed catalog
also contains `FireSpell.asset`. Each definition references its own economy
asset. The sword costs 35 coins and has 45 damage; the spell costs 20 coins and
has 80 power / 12 mana. Click its board to buy. The adjacent switch cycles items.
`SelectNext` switches the selection, and `TestDataFeatures` checks nested reads,
inherited fields, `is`/declaration patterns, `as`, successful/null/invalid explicit
casts, and defensive copies of a nested polymorphic asset array.

Upcasts preserve the snapshot. Downcasts check the baked runtime type;
incompatible `as` returns null, and incompatible explicit casts raise a
compiler-managed `InvalidCastException`. Null casts stay null; null type tests
return false. Properties and virtual methods on data assets are still unsupported:
choose gameplay behavior in UdonSharp using type tests or data fields.

## Snapshot behavior

During proxy serialization/build, custom data assets become `object[]` snapshots
with deterministic field ordering. Field reads such as `item.price` compile to
snapshot reads. Each behaviour receives its own snapshot; assigning the same
asset to multiple fields does **not** promise shared reference identity. Rebuild
the world after changing asset data or its field schema.

The compiler detects custom ScriptableObjects automatically. SDK types already
exposed to Udon, such as `UdonProduct`, keep their native behavior.

Supported data:

- Public instance fields and private `[SerializeField]` fields, including inherited fields.
- Primitive numbers, booleans, characters, strings and enums.
- `Vector2/3/4`, `Quaternion`, `Color/Color32`, `Rect`, `Bounds`, `Matrix4x4`, `LayerMask` and `VRCUrl`.
- Udon-supported Unity object references such as textures, audio clips and materials.
- One-dimensional arrays of supported field types, plus behaviour fields containing arrays of data assets.
- Nested custom ScriptableObject fields and arrays, including derived assets assigned to custom base types.

Data fields are read-only in Udon. Each array field read returns a fresh shallow
copy, including preserving null arrays. Read it once into a local variable for
loops; repeated reads allocate repeatedly. Referenced Unity objects retain their
normal mutable APIs; snapshotting does not make a material or GameObject immutable.

Unsupported:

- Writing a data field, properties, instance/static methods, and Unity object APIs on the custom data asset.
- `new`, `ScriptableObject.CreateInstance`, casts to `object`/native asset types, interfaces, and data-array covariance.
- Native methods on data-asset arrays (`GetValue`, `SetValue`, `Clone`, `GetType`); use typed indexing and array length instead. This prevents exposing mutable snapshot internals through `object`.
- Cyclic asset references (A → B → A) and nesting beyond 128 assets; baking reports an error instead of recursing indefinitely.
- Arbitrary custom classes, collections, multidimensional/jagged data arrays and `[SerializeReference]` fields.
- `[UdonSynced]` on data asset fields/arrays, saving changes back to an asset, or updating snapshots from asset edits during play.

Copy values into ordinary gameplay state when you need to modify or synchronize
them. The C# editor proxy still holds the original asset assignment; heap-to-proxy
reads never overwrite the source asset or replace that assignment with a snapshot.

Field layouts put base-class fields first and include a runtime type tag. Rebuild
all Udon programs and rebake scene/prefab data after updating this compiler; old
snapshots use a different layout. Repeated asset references inside a single baked
graph share a snapshot. Separate top-level behaviour fields are baked separately.

The shop is a local interaction example. It does not synchronize purchases
between players.
