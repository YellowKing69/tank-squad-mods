# Control Tweaks Lite 1.0.0

Aiming and camera tweaks for Tank Squad that keep the game's own gunner sights: its four zoom levels
(25, 15, 10, 5), its reticles and its range tables are not changed. Harmony-only (no class injection).
Settings are read live.

## Install

1. First-time setup from the [main README](../README.md) (BepInEx + the `core` DLLs).
2. Copy `plugins/ControlTweaksLite/ControlTweaksLite.dll` to `Tank Squad\BepInEx\plugins\ControlTweaksLite\`.
3. Start the game once: it writes `Tank Squad\BepInEx\config\controltweakslite.tanksquad.cfg` with the defaults below.

## What it does

**Turret aim speed per zoom** `[Aiming]`
| Setting | Default | |
|---|---|---|
| ZoomScaling | fov | fov = the same on-screen aim speed at every zoom step; game = the game's own curve. |
| ZoomCurve | 1.9 | Gunner sight and third person: 1 = same on-screen speed at every zoom, above 1 = slower when zoomed in. |
| MouseSmoothing | 1 | Smoothing of normal mouse aiming, in seconds (gunner sight and third person). 0 = off (game). |

**Gunner-sight turret inertia** `[GunnerAim]` - manual aiming in the gunner sight: the mouse moves a target and
the turret follows it, spinning up to its real traverse speed and braking onto the target.
| Setting | Default | |
|---|---|---|
| TurretInertia | true | Switch it on / off (off = the game's instant turning). |
| TraverseSpeed | 1 | Top speed as a multiple of the tank's real traverse speed. |
| AlsoElevation | true | Give gun elevation the same inertia. |
| SpinUpSeconds | 2 | Time to reach top speed (and to brake) at the narrowest zoom. |
| SpinUpFovWide | 15 | At this zoom and wider the turret turns instantly. |
| SpinUpFovNarrow | 5 | Zoom with the full SpinUpSeconds (the game's narrowest). |
| SpinUpCurve | 1.7 | How the spin-up grows between the two (1 = straight line). |

With the defaults the spin-up is: FOV 25 and 15 - instant, FOV 10 - about 0.6 s, FOV 5 - 2 s.

**Commander views** `[Cupola]`, `[Binoculars]`
| Setting | Default | |
|---|---|---|
| Cupola / ZoomCurve | 1.3 | Commander mouse speed per zoom (external cupola camera): above 1 = slower when zoomed in, so long-range spotting is not jumpy. -1 = game. |
| Cupola / HeadOut | true | Also use it with the head out of the hatch (low / full peek, with or without binoculars). |
| Cupola / VisionPorts | true | Also use it looking out through a vision port / the cupola glass. |
| Binoculars / Stabilize | 1 | Head out of the hatch: how much of the tank's pitch and roll is taken out of the view. 0 = bolted to the tank (game), 1 = level. |
| Binoculars / Smoothing | 0 | Seconds the view takes to follow the tilt that is left. 0 = instant. |

**Other** `[Shake]`, `[ItemHotkeys]`, `[OrderLine]`, `[HUD]`
| Setting | Default | |
|---|---|---|
| Shake / FireShake | 0.5 | Camera shake when guns fire. 0 = none. Game: 1. |
| ItemHotkeys / KeysEnabled | true | On foot: Ctrl+1, 2, 3, ... pick the items below. |
| ItemHotkeys / Items | Wrench,Assembly,Canister,AmmoCrate | The items, in order. |
| ItemHotkeys / Modifier | ctrl | ctrl, alt or shift. |
| ItemHotkeys / MouseWheel | true | The mouse wheel cycles through the items. |
| ItemHotkeys / InvertWheel | false | Reverse the wheel direction. |
| OrderLine / Width | 0.5 | Width of the order line from your units to the cursor. Game: 1. |
| OrderLine / ValidColor, InvalidColor | (empty) | Its colours as hex RRGGBB or RRGGBBAA. Empty = the game's green / red. |
| HUD / AimPointer | false | The small white aim dot in the middle of the screen (commander mode). Game: on. |

**Debug** `[Debug]`: `VerboseLog` (false) - writes diagnostic lines to the BepInEx log. Off = the mod logs nothing
except warnings and errors.

## Build

See [Building from source](../README.md#building-from-source); the project is `src/ControlTweaksLite.csproj`.
