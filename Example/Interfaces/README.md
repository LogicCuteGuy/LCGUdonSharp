# UdonSharp Interface Example

This example demonstrates one interface with two independent `UdonSharpBehaviour`
implementations. `InterfaceExampleRunner` invokes both implementations through
`INumberOperation` and reads the interface property afterward.

## Create the example

1. Fix any unrelated C# compilation errors and let Unity finish compiling.
2. Select **Tools > UdonSharp Interface Example > Create Example**. The setup
   command creates any missing UdonSharp `.asset` files before adding components.
3. If you only want to generate the assets, use **Generate Program Assets** in
   the same menu.
4. Enter Play Mode and inspect the Console. The default input `10` produces:
   - Add result: `15`
   - Multiply result: `30`
   - Property results: `15, 30`

The runner also exposes `Interact()`, so the same example can be triggered by
interacting with the object after suitable interaction setup.

Unity cannot serialize an interface field directly. The runner therefore stores
the concrete components in serialized fields, then assigns them to interface-typed
local variables for runtime dispatch.
