# Extended language examples

These scripts are real UdonSharp behaviours with matching program assets. Add a component to a GameObject and interact with it to run the example.

- `RefOutExample.cs` demonstrates local, field, one-dimensional array-element, `out var`, multiple-parameter, and recursive `ref`/`out` copy-back.
- `GenericHierarchyExample.cs` demonstrates a closed generic static helper, a closed generic interface diamond, multiple interface inheritance, and deep single concrete-class inheritance.
- `LinqClosureExample.cs` demonstrates an immediate array `Where`/`Select`/`ToArray` pipeline whose lambdas capture both a field and a local. It is lowered to loops at build time.
- `DynamicExample.cs` demonstrates a local `dynamic` value whose one concrete type is proven and substituted at build time.
- `SpanExample.cs` demonstrates an array-backed local `Span<int>` with indexing, `Length`, `Fill`, `ToArray`, and `Clear`, lowered to array/offset/length locals and loops.
- `ExceptionHandlingExample.cs` demonstrates typed and catch-all-compatible payload handling, helper-method propagation, `finally`, explicit `UdonException`, and guarded array, string, and integral divide-by-zero failures.
- `../AsyncAwait/AsyncYieldDelayExample.cs` demonstrates the currently implemented build-time lowering for `await Task.Yield()` and constant positive `Task.Delay(int)`.
- `../GenericRestrictions/` contains runnable supported replacements and commented rejected forms for open generics, generic behaviours, generic heap objects, `List<T>`, unsupported interface members, and multiple concrete bases.

SDK callback-await examples are added only when their lowering passes the UASM integration test; they are not represented here by fake CLR-only examples.
