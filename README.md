# tank-squad-mods

Tank Squad QOL adjustments, and GPU and engine parameter exposure for performance tweaking.
BepInEx 6 (IL2CPP) plugins, with their source.

## Mods

| Mod | What it does |
|---|---|
| [ControlTweaksLite](ControlTweaksLite/) | Turret aim speed per zoom, gunner-sight turret inertia, commander zoom speed and steadied binoculars - with the game's own sights |
| [PotatoMode](PotatoMode/) | Lower graphics memory / GPU load: textures, render scale, shadows, terrain, vegetation (formerly PotatoInjector) |
| [QuickCrew](QuickCrew/) | Shorter holds, faster hatch / visor / binocular transitions, seat and squad-attack hotkeys, AI gunner control |
| [PotatoVFX](PotatoVFX/) | Lighter shell and MG tracers, barrel smoke and rocket-trail toggles |

## First-time setup (once per game install)

1. Get BepInEx 6 bleeding edge: https://builds.bepinex.dev/projects/bepinex_be
   (used here: `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`).
2. Unzip it into your Tank Squad folder (next to `TankSquad.exe`).
3. Start the game once so BepInEx sets itself up, then close it.
4. Copy the three DLLs from [`core/`](core/) into `Tank Squad\BepInEx\core\` (overwrite; back up the old ones if you like).
5. In `Tank Squad\BepInEx\config\BepInEx.cfg` set:
   ```
   [Logging]
   UnityLogListening = false
   ```

## Installing a mod

Each mod folder is laid out like the game folder:

- `plugins/<Mod>/<Mod>.dll` → `Tank Squad\BepInEx\plugins\<Mod>\`
- `config/<mod>.tanksquad.cfg` → `Tank Squad\BepInEx\config\` (optional: the mod writes its own defaults if missing)

Settings are read live: save the .cfg while the game runs and the change applies within a second.

## Building from source

Each mod has a `src/` folder with its `.cs` and `.csproj`. Building needs the .NET 8 SDK and your own game's
`BepInEx` folder (its `core` and `interop` DLLs are the references - they are not in this repo):

```
dotnet build <Mod>/src -c Release -p:Ref="D:\Steam\steamapps\common\Tank Squad\BepInEx"
dotnet run --project tools/retarget -c Release -p:Ref="D:\Steam\steamapps\common\Tank Squad\BepInEx" -- <Mod>/src/bin/Release/net8.0/<Mod>.dll <Mod>.dll
```

The second step (`tools/retarget`) rewrites the net8.0 build so the game's .NET 6 runtime loads it.

## Licence

The mods in this repo are MIT licensed - see [LICENSE](LICENSE). The DLLs in `core/` keep their own licences
(see [core/README.md](core/README.md)).
