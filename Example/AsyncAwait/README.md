# Async/await UdonSharp example

This folder contains real U# examples for the build-time async lowering:

- `AsyncYieldDelayExample` uses `Task.Yield()` and `Task.Delay(int)`;
- `AsyncStringDownloadExample` awaits `VRCAsync.LoadStringAsync` while keeping
  the traditional `OnStringLoadSuccess`/`OnStringLoadError` callbacks;
- `AsyncImageDownloadExample` awaits `VRCAsync.LoadImageAsync` while keeping
  the traditional image callbacks.

Add an example to a GameObject with its generated Udon program, assign its URL
and (for images) target material, enter the world, and interact with it.

Currently supported in this slice:

- parameterless, instance, non-generic, non-network-callable `async void` methods;
- straight-line `await Task.Yield();` statements;
- straight-line `await Task.Delay(milliseconds);` statements where milliseconds
  is a positive compile-time constant;
- one `VRCAsync.LoadStringAsync(...)` or `VRCAsync.LoadImageAsync(...)` await per
  behaviour.

For SDK awaits, the traditional callback body runs first and receives the SDK
result. The generated await continuation runs afterward. This initial slice
uses the callback to store result data in behaviour fields; assigning a
`Task<T>` result directly to an async local is not supported yet.

Do not start another string request with the same URL on the behaviour while its
await is pending. String completion is correlated by URL; image completion is
correlated by the returned `IVRCImageDownload` request identity. Each async
method is single-flight: another call while its continuation is pending is
ignored.

Locals, parameters, nested awaits, explicit returns, direct `Task<T>` result
assignment, video/GPU/serialization/economy adapters, and multiple simultaneous
SDK awaits still produce build diagnostics until their later lowering slices
are implemented.
