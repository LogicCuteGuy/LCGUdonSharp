# ScriptableObject data

> Documentation version: **0.3.7** · [All examples](../README.md) · [Package guide](../../README.md)

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

## What runs in Udon

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

Data fields are read-only in Udon. Each array field read returns a fresh shallow
copy, including preserving null arrays. Read it once into a local variable for
loops; repeated reads allocate repeatedly. Referenced Unity objects retain their
normal mutable APIs; snapshotting does not make a material or GameObject immutable.

Unsupported:

- Writing a data field, properties, instance/static methods, and Unity object APIs on the custom data asset.
- `new`, `ScriptableObject.CreateInstance`, casts to other asset/object types, and polymorphic asset references.
- Nested ScriptableObject fields/references, arbitrary custom classes, collections, multidimensional/jagged data arrays and `[SerializeReference]` fields.
- `[UdonSynced]` on data asset fields/arrays, saving changes back to an asset, or updating snapshots from asset edits during play.

Copy values into ordinary gameplay state when you need to modify or synchronize
them. The C# editor proxy still holds the original asset assignment; heap-to-proxy
reads never overwrite the source asset or replace that assignment with a snapshot.

The shop is a local interaction example. It does not synchronize purchases
between players.
