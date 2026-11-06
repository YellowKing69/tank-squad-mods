# Potato VFX 1.4.3

Lighter visual effects for Tank Squad: cleaner tracers, fewer smoke clouds, cheaper rocket trails.
Harmony-only (no class injection). Settings are read live.

## Install

1. First-time setup from the [main README](../README.md) (BepInEx + the `core` DLLs).
2. Copy `plugins/PotatoVFX/PotatoVFX.dll` to `Tank Squad\BepInEx\plugins\PotatoVFX\`.
3. Optional: copy `config/potatovfx.tanksquad.cfg` to `Tank Squad\BepInEx\config\` (the settings below).

## What it does

**Shell tracers** `[Shells]`
| Setting | Here | Game | |
|---|---|---|---|
| RemoveSmoke | true | off | Removes the smoke trail behind tank shells (kept off even if the game switches it back on mid-flight). |
| SmokeMatch | smoke | | Effect parts with this in their name count as smoke. |
| GlowScale | 0.5 | 1 | Size of the shell glow. |
| GlowIntensity | 1 | 1 | Below 1 = fainter, above 1 = brighter. |

**Your tank firing** `[Shells]`
| Setting | Here | Game | |
|---|---|---|---|
| FrontSmoke | false | on | The smoke cloud in front of your barrel. |
| SideSmoke | true | on | The smoke fanning out left and right of the barrel (the flash always stays). |

**Machine-gun / rifle tracers** `[MGTracers]` (DP-27, MG34 ...)
| Setting | Here | Game | |
|---|---|---|---|
| RemoveTail | true | off | Removes the streak behind the bullet; the glowing round stays. |
| MaxCaliber | 30 | | Rounds up to this calibre (mm) count as MG / rifle ammo. |
| GlowScale / GlowIntensity | -1 | 1 | MG tracer glow; -1 = same as the shell settings. |
| TracerMatch, AmmoMatch, TailParts, TweakParts, TailSettings, TailMatch | | | Which effect parts count as the tail - normally leave as is. |

**Rockets and off-site artillery** `[OffsiteSmoke]`
| Setting | Here | Game | |
|---|---|---|---|
| RocketSmoke | false | on | Smoke behind rockets / off-site artillery (off = better performance). |
| Trails | true | on | Show their trails at all (off = best performance). |
| Match | rocket,nebelwerfer,katyusha,calliope | | Trail names that count as rockets. |

**Debug** `[Debug]`: `VerboseLog` (false) - writes diagnostic lines to the BepInEx log. Off = the mod logs nothing.

## Build

See [Building from source](../README.md#building-from-source); the project is `src/PotatoVFX.csproj`.
