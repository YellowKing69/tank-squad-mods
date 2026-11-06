# Potato Mode 1.4.4 (PotatoInjector)

Lower graphics memory and GPU load for Tank Squad on weaker PCs: texture mips, render scale, shadows, terrain and
GPU Instancer vegetation settings, plus an optional shader warm-up. Runtime settings only - no game files are changed.
Built so you never see less than other players: draw distances and cover are left alone unless you opt in
(the OPT-IN vegetation options are marked as such).

## Install

1. First-time setup from the [main README](../README.md) (BepInEx + the `core` DLLs).
2. Copy `plugins/PotatoMode/PotatoMode.dll` to `Tank Squad\BepInEx\plugins\PotatoMode\`.
3. Start the game once: it writes `Tank Squad\BepInEx\config\potatomode.vramtweaks.cfg` with the defaults below.
   Tune it for your own GPU; the file is read live (`LiveReloadConfig`).

If you see flickering with the game's FSR / upscaler on, set `[Rendering] RenderScale = -1`.

## Settings (defaults)

Most settings default to -1 / Leave = the game's own value. The ones that change something out of the box are
marked **bold**.

**Textures**
| Setting | Default | |
|---|---|---|
| **MipmapLimit** | **1** | Drop this many top mip levels from every texture: 1 = ~75% less texture memory, 2 = ~94% (blurry). |
| MipStreaming | Leave | Unity texture mip streaming on / off. |
| MipStreamingBudgetMB | -1 | Texture memory budget when mip streaming is on (e.g. 512 on a 2 GB card). |
| AnisotropicFiltering | Leave | Off = slightly blurrier textures at glancing angles, small saving. |

**Rendering**
| Setting | Default | |
|---|---|---|
| **RenderScale** | **0.8** | Internal render resolution. Set -1 when the game's FSR / upscaler is on. |
| **MSAA** | **1** | Anti-aliasing samples: 1 = off, 2, 4, 8. Off saves a lot of memory. |
| HDR | Leave | HDR render targets use 2x the memory; off may dull bloom. |
| **RealtimeReflectionProbes** | **Off** | Realtime reflection cubemaps. |
| LodBias | -1 | Lower = low-detail models sooner (e.g. 0.5). |
| MaximumLODLevel | -1 | 1 = skip the highest-detail model of every LOD group. |
| SoftParticles | Leave | Off = cheaper smoke / dust with harder edges. |
| AdditionalLightsPerObject | -1 | Extra point / spot lights per object, 0-8. |
| BuildShaderCache | false | Prepare every loaded material's GPU programs after a map loads, so nothing hitches the first time it appears (ShaderCache* options tune it). |

**Shadows**
| Setting | Default | |
|---|---|---|
| **ShadowMapResolution** | **1024** | 256 - 4096 (a 4096 map is ~64 MB by itself). |
| **ShadowCascades** | **1** | 1, 2 or 4. Fewer = smaller and faster. |
| ShadowDistance | -1 | Max shadow distance in metres (e.g. 50). |
| MainLightShadows | Leave | Sun shadows - off is the biggest single saving. |
| AdditionalLightShadows | Leave | Shadows from muzzle flashes, lamps. |
| SoftShadows | Leave | Off = harder shadow edges, cheaper. |

**Terrain** (Unity terrain): DetailDensityScale, DetailDistance, TreeDistance, BillboardStart, MaxFullTrees, PixelError,
BasemapDistance - all -1 (leave).

**GPUI Vegetation** (the game's trees, bushes and grass are drawn by GPU Instancer)
| Setting | Default | |
|---|---|---|
| Enabled | true | Master switch for the whole section. false = hands off: anything the mod changed is put back, then the game's vegetation is left alone. |
| IncludeProfiles | (empty) | Only touch profiles whose name contains one of these. Empty = all. |
| ShadowCasting | -1 | 0 = no vegetation shadows - big saving, doesn't change what you can see through. |
| CustomShadowDistance, ShadowCulling | -1 | Vegetation shadow distance / culling. |
| LodBiasScale | -1 | Multiplies each profile's own LOD bias (e.g. 0.6). |
| MaximumLODLevel | -1 | 1 = never use LOD0, 2 = never LOD0/LOD1. |
| LODCrossFade | -1 | 0 = no dithered fade between LODs (visible pops). |
| HideTypes, HideAllDetails | (empty) / false | OPT-IN, low-end PCs only: don't draw some or all vegetation types at all. |
| DetailDistance, DetailUpdateDistance, DetailDensityReduction | -1 | GPU Instancer's own detail draw / rebuild distances and density reduction. -1 never touches the game's value (only puts back one the mod changed). |
| ListVegetationTypes | false | Log every vegetation type with its name (for HideTypes). Needs VerboseLog. |

**Display**: Width / Height / WindowMode / TargetFrameRate / VSync - all leave the game's values by default.

**General**: ReapplyIntervalSeconds (-1 = off: settings are applied at start-up and whenever the config is saved; set e.g. 2 if the game undoes them), LiveReloadConfig (true), TickHooks (leave as is).

**Debug**: `VerboseLog` (false) - writes what the plugin found and changed to the BepInEx log. Off = the plugin logs
nothing (warnings still show).

## Build

See [Building from source](../README.md#building-from-source); the project is `src/PotatoMode.csproj`.

## When it runs

- Textures, rendering, shadows, terrain and display settings: applied at start-up and whenever the config is saved.
  With `ReapplyIntervalSeconds` set (e.g. 2) they are also re-checked that often, in case the game resets them;
  only changed values are written. Default -1 = no re-checks.
- If the game keeps putting a setting back, Potato Mode stops re-applying that one after 3 tries (rewriting it each
  time causes a hitch) and says so once in the log. Set that setting to -1 / Leave.
- GPUI vegetation: after every scene change (1, 3, 6, 12, 25 and 45 s after it, as the map loads), and once after
  each config save. Nothing runs in between.

