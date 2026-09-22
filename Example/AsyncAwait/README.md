# Async/await UdonSharp example

This folder contains real U# examples for the build-time async lowering:

- `AsyncYieldDelayExample` uses `Task.Yield()` and `Task.Delay(int)`;
- `AsyncStringDownloadExample` awaits `VRCAsync.LoadStringAsync(url, out result)`
  while keeping the traditional `OnStringLoadSuccess`/`OnStringLoadError`
  callbacks;
- `AsyncImageDownloadExample` awaits `VRCAsync.LoadImageAsync` while keeping
  the traditional image callbacks;
- `AsyncVideoLoadExample` and `AsyncVideoEndExample` cover video readiness,
  errors, and playback completion;
- `AsyncGpuReadbackExample` covers `VRCAsyncGPUReadback`;
- `AsyncSerializationExample` keeps both serialization callbacks;
- `AsyncAvailableProductsExample`, `AsyncPurchasesExample`, and
  `AsyncProductOwnersExample` cover Creator Economy list operations.

Add an example to a GameObject with its generated Udon program, assign its URL
and (for images) target material, enter the world, and interact with it.

Currently supported in this slice:

- parameterless, instance, non-generic, non-network-callable `async void` methods;
- straight-line `await Task.Yield();` statements;
- straight-line `await Task.Delay(milliseconds);` statements where milliseconds
  is a positive compile-time constant;
- one VRChat SDK await per behaviour, including string/image downloads, video,
  GPU readback, manual serialization, and Creator Economy list operations.

For SDK awaits, the traditional callback body runs first and receives the SDK
result. For the string downloader's `out` overload, the compiler then copies
that SDK result into the supplied instance behaviour field before running the
generated await continuation. The original `LoadStringAsync(url)` overload
remains available. Array elements and async locals are rejected until their
storage locations can be safely hoisted. Assigning a `Task<T>` result directly
to an async local is not supported yet.

Do not start another string request with the same URL on the behaviour while its
await is pending. String completion is correlated by URL; image completion is
correlated by the returned `IVRCImageDownload` request identity. Each async
method is single-flight: another call while its continuation is pending is
ignored.

Video callbacks do not identify their source player, so do not start a manual
video operation on the same behaviour while a video await is pending. Keep the
video player and awaiting behaviour on the same GameObject. Economy list
operations have no SDK error callback and can therefore remain pending.

Locals, parameters, nested awaits, explicit returns, direct `Task<T>` result
assignment and multiple simultaneous SDK awaits still produce build diagnostics
until their later frame-pool lowering slices are implemented.
