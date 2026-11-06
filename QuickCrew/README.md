# Quick Crew 2.2.1

Crew and turret quality-of-life for Tank Squad: shorter "hold to..." circles, faster hatch, visor and binocular
transitions, seat hotkeys, squad attack orders, and control over the AI gunner in your own tank.
Harmony-only (no class injection). Settings are read live.

## Install

1. First-time setup from the [main README](../README.md) (BepInEx + the `core` DLLs).
2. Copy `plugins/QuickCrew/QuickCrew.dll` to `Tank Squad\BepInEx\plugins\QuickCrew\`.
3. Start the game once: it writes `Tank Squad\BepInEx\config\quickcrew.tanksquad.cfg` with the defaults below.

Quick Crew includes the instant-visor tweaks that used to be a separate Instant Visors plugin. If you have that
older plugin, delete its folder from `BepInEx\plugins\` so the two don't both adjust the visors.

## Keys (defaults)

| Keys | Does |
|---|---|
| Ctrl+1 .. Ctrl+5 | Switch seat instantly: Commander, Gunner, Driver, Radio operator, Loader |
| Ctrl+G or Ctrl+9 | Toggle the AI gunner in your own tank |
| Ctrl+` | Toggle multi-crew mode instantly |
| Ctrl+V | Go inside the tank (interior camera, same as I) |
| Alt+1 / 2 / 3 | Look at an enemy: order squad 1 / 2 / 3 to attack it |
| Alt+4 | Order every squad to attack it |

## Settings (defaults)

**Holds** `[Holds]`
| Setting | Default | Game | |
|---|---|---|---|
| HoldTimeMultiplier | 0 | 1 | Hold time for every hold-to-use action. 0 = shortest (MinHoldTime). |
| MinHoldTime | 0.05 | | Shortest hold, in seconds. |
| ControlModeSwitchTime | 0 | 5 | Hold for the multi-crew toggle, in seconds. -1 = game value. |
| VehicleSwitchTime | -1 | | Hold for switching to another vehicle. -1 = game value. |
| SlotChangeFadeMultiplier | 0 | 1 | Seat-change fade length. 0 = shortest. |

**Hatch and peeking** `[Peeking]`
| Setting | Default | Game | |
|---|---|---|---|
| PeekWaitSpeed | 20 | 1 | Speed-up for hatch climbing (binoculars, seat changes, hatch open / close). |
| PeekFadeMultiplier | 0 | 1 | Hatch-climb fade length. 0 = shortest. |
| PeekSpeed, CameraBlendSpeed, ClimbAnimationSpeed | 1 | 1 | Experimental, no visible effect. |
| MaxClimbBoostSeconds | 2 | | Safety limit for ClimbAnimationSpeed. |

**Binoculars** `[Binoculars]`
| Setting | Default | Game | |
|---|---|---|---|
| AimDelayMultiplier | 0 | 1 | Delay before the binoculars zoom in. 0 = none. |
| ZoomSpeedMultiplier | 1 | 1 | Binocular zoom speed. |
| ApplyToAllZoomItems | false | off | Also apply to scopes and sights. |

**Visors** `[Visors]` (vision ports, periscopes, viewfinders)
| Setting | Default | Game | |
|---|---|---|---|
| FastEnter | true | off | Fast visor entry. |
| HoldTime | 0 | | Visor hold time in seconds. 0 = single tap. |
| AnimationSpeed | 20 | 1 | Hand-reach speed. |
| HideLabel | true | off | Hide the visor "Press F" label (the outline stays). |
| IncludeGunnerSights | true | | Also apply to gunner sights. |

**Your AI gunner** `[Gunner]`
| Setting | Default | Game | |
|---|---|---|---|
| OwnGunnerAI | false | true | AI gunner in your tank. false = idle, the turret stays put while you command. |
| ToggleKey | g,digit9 | | Modifier + any of these toggles it. Empty = off. |
| FreezeTurretWhenOff | true | | Stop the turret tracking while the AI gunner is off (manual aiming still works). |
| IgnoreInfantry | true | off | Your AI gunner never targets infantry. |
| APOnly | true | off | Your AI gunner only loads AP-type ammo. |

**Allied vehicles** `[AlliedNoInfantry]`
| Setting | Default | |
|---|---|---|
| Enabled | true | Allied vehicles of the models below never pick infantry as a target (orders you give still count). |
| Models | SU_152 | Parts of the vehicle's name, comma separated. |

**Squad attack** `[SquadAttack]`
| Setting | Default | |
|---|---|---|
| Enabled | true | Look at an enemy and press a hotkey to order a squad to attack it. |
| Modifier | alt | ctrl, alt or shift. |
| SquadKeys | digit1,digit2,digit3 | Keys for squad 1, 2, 3, ... |
| AllSquadsKey | digit4 | Sends every squad. Empty = off. |
| MoveTowardTarget | false | false = fire from where they are; true = drive toward the target. |
| MaxRange | 3000 | How far you can point at a target, in metres. |

**Hotkeys** `[SeatHotkeys]`, `[ExtraHotkeys]`
| Setting | Default | |
|---|---|---|
| SeatHotkeys / Enabled | true | Ctrl+1..5 seat switches. |
| SeatHotkeys / Seats | Commander,Gunner,Driver,RadioOperator,Loader | Seat for each key; leave an entry empty to skip that key. |
| ExtraHotkeys / Modifier | ctrl | For the keys below and the gunner toggle. |
| ExtraHotkeys / MultiCrewToggle | backquote | Instant multi-crew toggle. Empty = off. |
| ExtraHotkeys / InteriorCamera | v | Go inside the tank. Empty = off. |

**Debug** `[Debug]`: `VerboseLog` (false) - writes diagnostic lines (including a timing log of the binocular
sequence) to the BepInEx log. Off = the mod logs nothing except warnings and errors.

## Build

See [Building from source](../README.md#building-from-source); the project is `src/QuickCrew.csproj`.
