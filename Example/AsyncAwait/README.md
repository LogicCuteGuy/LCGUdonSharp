# Async/await UdonSharp example

`AsyncYieldDelayExample` demonstrates the first build-time async lowering slice.
Add it to a GameObject with its generated Udon program, enter the world, and
interact with the object. The log appears immediately, on the next frame, and
one second later.

Currently supported in this slice:

- parameterless, instance, non-generic, non-network-callable `async void` methods;
- straight-line `await Task.Yield();` statements;
- straight-line `await Task.Delay(milliseconds);` statements where milliseconds
  is a positive compile-time constant.

Each method is currently single-flight: another call while its continuation is
pending is ignored.

Locals, parameters, nested awaits, explicit returns, `Task<T>`, and VRChat SDK
operation adapters produce build diagnostics until their frame-hoisting and
callback lowering slices are implemented.
