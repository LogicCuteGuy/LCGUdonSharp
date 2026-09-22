# Generic and hierarchy build-time restrictions

> Documentation version: **0.3.2** · [All examples](../README.md) · [Package guide](../../README.md)

This guide shows every generic or hierarchy shape that LCGUdonSharp rejects at
build time and the supported replacement. Rejected examples are self-contained
illustrations with the relevant declarations included. They remain in Markdown
intentionally so the package's normal example assembly continues to compile;
use one case at a time when testing a diagnostic in a Unity project.

## Runnable U# scripts

These scripts are active members of the registered UdonSharp example assembly
(`LogicCuteGuy.LCGUdonSharp.Examples.asmdef`, registered for UdonSharp
compilation by `LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset`, see
[Key terms](../README.md#key-terms)).
Each runs the supported pattern and keeps the corresponding rejected syntax as
comments so Unity can still compile the package. Every script has a matching
`UdonSharpProgramAsset`, and `Interact()` runs its active demonstration.

| Restriction | UdonSharp example |
|-------------|-------------------|
| Open generics | [`OpenGenericsExample.cs`](OpenGenericsExample.cs) |
| Generic behaviours | [`GenericBehavioursExample.cs`](GenericBehavioursExample.cs) |
| Generic heap objects | [`GenericHeapObjectsExample.cs`](GenericHeapObjectsExample.cs) |
| Collections, JSON, bytes, and bits | [`ListTypesExample.cs`](ListTypesExample.cs) |
| Unsupported interface members | [`InterfaceMembersExample.cs`](InterfaceMembersExample.cs) |
| Multiple concrete bases | [`MultipleConcreteBasesExample.cs`](MultipleConcreteBasesExample.cs) |

## Open generics

An unbound type, or a runtime type that still contains a type parameter, has no
single Udon representation.

**Rejected**

```csharp
using System;
using UdonSharp;

public class Converter<T> { }
public static class GenericTools<T> { }

public class OpenGenericExamples : UdonSharpBehaviour
{
// Rejected: Open generic type 'Converter<>' is not supported by U#.
    public Type GetDefinition() { return typeof(Converter<>); }

// Rejected inside a generic method: T is still open at runtime.
    public Type GetRuntimeType<T>() { return typeof(GenericTools<T>); }
}
```

Use a concrete type at each call site. Generic helpers must be specialized at
build time; do not inspect or store their open runtime type.

**Replacement**

```csharp
using UdonSharp;

public static class GenericTools<T>
{
    public static T Identity(T value)
    {
        return value;
    }
}

public interface IValueSource<T>
{
    T Read();
}

public class ClosedGenericExamples : UdonSharpBehaviour
{
    public int Copy(IValueSource<int> source, int value)
    {
        int copied = GenericTools<int>.Identity(value);
        return source.Read() + copied;
    }
}
```

## Generic behaviours

Unity and Udon program assets require one concrete behaviour type.

**Rejected**

```csharp
using UdonSharp;

// Rejected even when the class has no UdonSharpProgramAsset.
public class Meter<T> : UdonSharpBehaviour { }
```

Make the behaviour concrete and move reusable algorithms into static generic
helpers.

**Replacement**

```csharp
using UdonSharp;

public static class GenericTools<T>
{
    public static T Identity(T value) { return value; }
}

public class IntMeter : UdonSharpBehaviour
{
    public int value;

    public int CopyValue()
    {
        return GenericTools<int>.Identity(value);
    }
}
```

## Generic heap objects

Generic reference-type instances have no supported Udon heap layout. The rule
applies to fields, locals, parameters, returns, allocations, and non-generic
classes derived from a constructed generic base.

**Rejected**

```csharp
using UdonSharp;

public class Box<T> { }
public class IntBox : Box<int> { }

public class GenericHeapExamples : UdonSharpBehaviour
{
    private Box<int> box;                       // Rejected type reference
    private IntBox inheritedGenericBox;          // Rejected through generic base

    public Box<int> Create() { return new Box<int>(); } // Rejected allocation
}
```

Use arrays, parallel arrays, concrete behaviours, or SDK value types such as
`VRCUrl` instead.

**Replacement**

```csharp
using UdonSharp;
using VRC.SDKBase;

public class ArrayBackedItems : UdonSharpBehaviour
{
    public int[] itemIds;
    public string[] itemNames;
    public VRCUrl[] itemUrls;
}
```

Static generic helpers and closed generic interfaces are not heap objects and
remain valid specialization inputs.

```csharp
int result = GenericTools<int>.Identity(value);      // Static generic helpers
IValueSource<int> source = intSource;                // Closed generic interfaces
```

## Collections, JSON, bytes, and bits

Exact `List<T>` and `Dictionary<TKey,TValue>` types are compiler-lowered to
VRChat `DataList` and `DataDictionary` values. The runnable
[`ListTypesExample.cs`](ListTypesExample.cs) demonstrates initializers,
indexers, mutation, `System.Text.Json`-style round trips, a Manual-mode synced
list, UTF-8 bytes, `BitConverter`, `Buffer.BlockCopy`, and bitwise operators.

In 0.3.x, editor proxy serialization uses the same lowered storage for fields,
nested collections, and arrays of collections. Null and empty values stay
distinct, and primitive or enum elements keep their exact `DataToken` type.

```csharp
List<int> values = new List<int> { 1, 2, 3 };
Dictionary<string, int> scores = new Dictionary<string, int>
{
    { "alpha", 10 },
};

scores["count"] = values.Count;
string json = JsonSerializer.Serialize(scores);
Dictionary<string, int> copy =
    JsonSerializer.Deserialize<Dictionary<string, int>>(json);
```

The compiler only recognizes the exact BCL collection definitions. Interfaces,
derived collections, arbitrary enumerable sources, custom comparers, and user
types that merely share the names `List` or `Dictionary` are not lowered.

```csharp
public class Scores : List<int> { }

IList<int> interfaceValues;       // Rejected: collection interface
Scores derivedValues;             // Rejected: derived collection
```

Collection fields cannot use Unity Inspector serialization. Keep local fields
private, or add `[NonSerialized]` to a public field. A synchronized collection
must use `[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]` and
`[UdonSynced, NonSerialized]`; ownership transfer and `RequestSerialization()`
remain explicit.

## Unsupported interface members

U# interfaces are build-time contracts for instance methods and ordinary
instance properties. The following members are rejected:

**Rejected**

```csharp
using System;
using UdonSharp;

public interface IInvalidContract
{
    const int Version = 1;                 // Field or constant
    event Action Changed;                  // Event
    int this[int index] { get; }           // Indexer
    static int StaticValue { get { return 0; } } // Static property
    static void Reset() { }                // Static method
    T Convert<T>(T value);                 // Generic method
    int DefaultValue() { return 1; }       // Default interface method
    class NestedType { }                   // Nested type
}

public class InvalidContractExample : UdonSharpBehaviour, IInvalidContract
{
    public event Action Changed;
    public int this[int index] { get { return index; } }
    public T Convert<T>(T value) { return value; }
}
```

Use normal instance methods and properties with concrete parameter types.

**Replacement**

```csharp
using UdonSharp;

public interface IValueSource<T>
{
    T Value { get; }
    T Read();
}

public class IntValueSource : UdonSharpBehaviour, IValueSource<int>
{
    public int Value { get; private set; }
    public int Read() { return Value; }
}
```

A behaviour may implement multiple supported interfaces, including closed
generic interfaces.

## Multiple concrete bases

C# and Roslyn reject multiple concrete base classes before Udon lowering.

**Rejected**

```csharp
public class FirstBase { }
public class SecondBase { }

// Rejected with CS1721.
public class Combined : FirstBase, SecondBase { }
```

Use one concrete base plus interfaces, or use composition.

**Replacement**

```csharp
using UdonSharp;

// HelperBehaviour.cs
public class HelperBehaviour : UdonSharpBehaviour
{
    public int ReadValue() { return 0; }
    public void ResetValue() { }
}
```

```csharp
using UdonSharp;

// Combined.cs
public abstract class FirstBase : UdonSharpBehaviour { }
public interface IReadable { int Read(); }
public interface IResettable { void ResetValue(); }

public class Combined : FirstBase, IReadable, IResettable
{
    public HelperBehaviour helper; // Composition

    public int Read()
    {
        return helper.ReadValue();
    }

    public void ResetValue()
    {
        helper.ResetValue();
    }
}
```

## Task<T> compatibility note

`Task<T>` is exempt from the generic heap-type diagnostic because async lowering
uses it as a compiler-only handle. It is not a normal heap object and cannot be
constructed directly.

The following is future-lowering pseudocode, not a runnable example yet:

```csharp
Task<int> pending = GetValueAsync(); // Local handle produced by lowered async code
new Task<int>(SomeFunction);         // Rejected generic heap-object construction
```

This exemption preserves the async lowering contract; it does not make CLR
threads, `Task.Run`, arbitrary task construction, or runtime task objects
available in Udon. The snippet documents the reserved representation; it becomes
runnable only when the corresponding async lowering and producer method are
available.
