# Generic and hierarchy build-time restrictions

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
| `List<T>` | [`ListTypesExample.cs`](ListTypesExample.cs) |
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

## List<T>

`List<T>` is rejected directly, inside another generic type, and through a
derived class.

**Rejected**

```csharp
using System.Collections.Generic;
using UdonSharp;

public class Wrapper<T> { }
public class Scores : List<int> { }

public class ListExamples : UdonSharpBehaviour
{
    private List<int> values;
    private Wrapper<List<int>> wrappedValues;
    private Scores derivedValues; // References the derived List<T> type.
}
```

Use a fixed array and track the used length explicitly.

**Replacement**

```csharp
using UdonSharp;

public class ArrayBackedValues : UdonSharpBehaviour
{
    public int[] values = new int[32];
    public int valueCount;

    public bool TryAdd(int value)
    {
        if (valueCount >= values.Length)
            return false;

        values[valueCount++] = value;
        return true;
    }
}
```

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
