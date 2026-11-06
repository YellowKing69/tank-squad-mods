# core

DLLs that go into `Tank Squad\BepInEx\core\` (overwrite the ones BepInEx installed). Needed once per game install.

| DLL | Version | Why |
|---|---|---|
| `Il2CppInterop.Runtime.dll` | 1.5.3, patched | Lets BepInEx find `Class::Init` in Tank Squad's Unity 2022.3 (IL2CPP metadata v31) game code; without it the plugins fail to start. |
| `Il2CppInterop.Common.dll` | 1.5.3 | Matches the patched runtime above. |
| `TerraFX.Interop.Windows.dll` | | Windows API bindings some of the mods use. |

## About the patched Il2CppInterop.Runtime.dll

- Built from Il2CppInterop: https://github.com/BepInEx/Il2CppInterop (version 1.5.3).
- The only change is in `Il2CppInterop.Runtime/Injection/InjectorHelpers.cs`: an extra signature
  (`MemoryUtils.SignatureDefinition`) so `Class::Init` is found on Unity 2022.3 / metadata v31 games.
  The patched file is in [`Il2CppInterop-patch/InjectorHelpers.cs`](Il2CppInterop-patch/InjectorHelpers.cs):
  to rebuild the DLL, put it over the same file in an Il2CppInterop 1.5.3 checkout and build `Il2CppInterop.Runtime`.
  It is source only - the game and the mods don't use it.
- The problem and that solution are described in BepInEx issue #1122:
  https://github.com/BepInEx/BepInEx/issues/1122
  (also set `UnityLogListening = false` in `BepInEx.cfg`, as in the main README).

## Licences

Il2CppInterop is LGPL-3.0 (https://github.com/BepInEx/Il2CppInterop); TerraFX.Interop.Windows is MIT
(https://github.com/terrafx/terrafx.interop.windows).
