# ChestReach

**English** · [Русский](README-RU.md)

A Valheim mod: craft, build, refuel and cook with the materials in the chests around you.
Your inventory is used first; what it lacks comes from the chests nearby. Client-side only:
players without the mod can join and play, the host and a dedicated server need nothing.

Version history is in `CHANGELOG.md`.

## What it does

- **Crafting and upgrading.** At a station, the chests within `RangeMeters` (20 m) of the
  station count as carried; crafting by hand uses the chests around you. The recipe list, the
  craft button and the requirement rows all see them; multi-craft too. Recipes that need only
  one of their ingredients stay vanilla (inventory only).
- **Building** with the hammer, hoe and cultivator: the chests within 20 m of you. Pieces whose
  materials are in the chests become available in the build menu.
- **Stations.** Used with nothing to put in, a station gets one item from a chest within 20 m
  of it: fuel for fires, torches, lamps, smelters and the oven, the shield generator's bones,
  the ballista's ammo (`Refuel`); ore, wood, flax, barley and sap for the smelter, blast
  furnace, charcoal kiln, spinning wheel, windmill and eitr refinery (`LoadOre`); food for
  the cooking stations and the oven, mead bases for the fermenter (`Cook`).
- **Repair all**: one press of the repair button at a station repairs every item it can.
- **Area repair**: `;` repairs the damaged buildings within 50 m, as the hammer would one by
  one — wards and the crafting station a piece needs (near the piece) are respected, nothing
  is spent.
- **Display**: a material amount that the inventory alone does not cover but the chests do is
  drawn in its own colour instead of blinking red; the tooltip says how many are carried and
  how many are in the chests nearby.

Which chests: placed chests, carts, ship storage and chests the world placed (dungeons,
villages). Not graves, not other players' private chests, not chests behind a ward you have
no access to (the chests that check wards in vanilla; `RequireWardAccess` makes every chest
check), not chests open in someone's hands, not the prefabs in `ExcludedPrefabs`.

## How it works

**Taking from a chest someone else owns.** Only the owner of a chest may change it — the
client that loaded it first, or the player who opened it last. ChestReach asks for it as
pressing E does: `Container.RPC_RequestOpen` to the owner. The owner's game — **vanilla or
modded** — checks that the chest is not open and not private, makes us the owner and answers
`RPC_OpenResponse(true)`. The mod catches that answer before it opens the chest window. When
the new owner arrives with the chest's ZDO, the inventory is reloaded from the world and the
items are removed; the game saves the chest to the world and to everybody as when you take
something by hand. A refusal ("in use") or no answer within 3 s leaves that chest alone for
5 s and the mod plans without it. A chest without an owner is claimed, as the game does.

**Crafting.** Pressing Craft asks for the chests that will be needed; the answers come during
the crafting animation (about 1.5 s). At its end (`InventoryGui.DoCrafting`), in one frame:
the chests are checked to be ours and to really hold the deficit, then the vanilla craft runs
and its `ConsumeResources` takes what the inventory lacks from those chests. If a chest is
not ours yet, the craft waits for it (the bar stays full) up to 4 s. If the chests cannot
cover it — someone took the last iron a second ago — nothing is crafted and nothing is
spent, with a message saying what is missing.

**Building.** While a piece is selected and the inventory does not cover it, the chests it
would take from are asked for ahead of time (each at most every 5 s, so two builders sharing
a chest do not pass it back and forth). `Player.TryPlacePiece`: if those chests are ours and
hold the deficit, the piece is placed and the vanilla `ConsumeResources` takes from them;
otherwise the piece is not placed, "press again" is shown and the next click places it. In
your own base as the host the chests are almost always yours already.

**Stations.** One item is moved from a chest into your inventory right before the station
looks for it there; the station's own code does every check and sends its own RPC. If it
turned the item down (full, no fire, no roof, other ammo loaded), the item goes back into the
chest it came from. A chest that is not ours yet is asked for, and the use is repeated by
itself when it arrives.

**Speed.** Every `Container` registers itself when it wakes up. A chest's item counts are
recounted only when its own inventory changes (`Inventory.m_onChanged`: the owner changed it,
or another client loaded its new contents from the world), and every such change bumps one
version number. A query ("what is within 20 m of this station") is answered from a small
cache that lives while the version stays the same, for at most a second — wards, chests
opened by others and moving carts and ships change without touching an inventory. Changes
elsewhere in the world (smelters ticking, creatures, other players) do not touch it. A
requirement row or a piece in the build menu is a dictionary lookup.

**Nothing outside the world.** No ledger, no files: what was taken is gone from the chest in
the world save, as if taken by hand. Remove the mod at any time.

## Compatibility

Built against **Valheim 1.0.20** (network version 41), **BepInEx 5.4.23.5**
(BepInExPack_Valheim 5.4.2351). Not together with another craft-from-containers mod
(RunicCrafting, AzuCraftyBoxes, CraftFromContainers and the like): they patch the same
places. Mods that change the inventory (ExtraSlots, quick stack mods) are fine — the mod only
adds what the inventory lacks before the vanilla `ConsumeResources`, which still takes the
rest from the inventory itself.

## Who needs it

| Who | What |
|---|---|
| A player who wants to craft and build from chests | installs it |
| Other players | nothing; with the mod they use it too, without it they play as usual |
| The host, a dedicated server | nothing; with the mod on the host, its settings apply to everyone with the mod |

## Bugs and feedback

https://github.com/tbsj1ga/ChestReachValheim/issues — please attach `BepInEx/LogOutput.log` and
the output of `chestreach status` (console, F5). `Debug = true` logs every request and take.

## Installation

Through r2modman / Thunderstore, or put `build/ChestReach.dll` into

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\ChestReach\
```

(or `build.ps1 -Install`).

## Settings

`BepInEx/config/j1ga.chestreach.cfg`, or in-game with ConfigurationManager (F1). Changes apply
at once. Sections 01-06 (except the key) come from the server while connected to a host with
the mod and `SyncConfig` on.

| Section | Key | Default | What |
|---|---|---|---|
| 01 General | `Enabled` | `true` | master switch |
| 01 General | `Debug` | `false` | log every chest request and take |
| 02 Crafting | `Enabled` | `true` | craft and upgrade from chests |
| 02 Crafting | `RangeMeters` | `20` | chests this far from the station (1..50) |
| 03 Building | `Enabled` | `true` | build from chests |
| 03 Building | `RangeMeters` | `20` | chests this far from you (1..50) |
| 04 Stations | `Refuel` | `true` | fuel for fires, lamps, smelters, the oven, the shield generator; ballista ammo |
| 04 Stations | `LoadOre` | `true` | ore, wood, flax, barley, sap into smelters, kiln, spinning wheel, windmill, refinery |
| 04 Stations | `Cook` | `true` | food for cooking stations and the oven, mead bases for the fermenter |
| 04 Stations | `RangeMeters` | `20` | chests this far from the station (1..50) |
| 05 Chests | `IncludeCarts` | `true` | take from carts |
| 05 Chests | `IncludeShips` | `true` | take from ship storage |
| 05 Chests | `IncludeWorldChests` | `true` | take from chests the world placed (dungeons, villages) |
| 05 Chests | `RequireWardAccess` | `false` | ward access for every chest, not only those that check wards in vanilla |
| 05 Chests | `ExcludedPrefabs` | empty | chest prefabs never taken from, comma separated |
| 06 Repair | `RepairAll` | `true` | one press of the repair button repairs every item |
| 06 Repair | `AreaRepair` | `true` | the key repairs the buildings around you |
| 06 Repair | `AreaRepairKey` | `;` | the key (each player's own) |
| 06 Repair | `AreaRepairRadius` | `50` | radius of the area repair (1..100) |
| 07 Display | `ShowCountsInTooltip` | `true` | carried and nearby counts in the material tooltip |
| 07 Display | `CoveredColor` | light blue | colour of an amount covered by the chests |
| 08 Sync | `SyncConfig` | `true` | on the server: send sections 01-06 to clients with the mod |

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Nexus     # ... zip, and the Nexus Mods kit
```

The compiler is `csc.exe` from the .NET Framework (C# 5: no `out var`, `?.`, `$""`,
`nameof`); references come straight from the game folder and the r2modman profile's
`BepInEx\core`; the paths are at the top of `build.ps1` and `check-refs.ps1`. After the
build, `check-refs.ps1` checks every type and member reference, every reflection target and
every Harmony patch target against the game.

## Repository

Branch `main` on GitHub: https://github.com/tbsj1ga/ChestReachValheim. Versioned: sources,
scripts, documentation, the Thunderstore template and `build\ChestReach.dll`. Not versioned:
the BepInEx config, zip packages, Nexus kits - see `.gitignore`.

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap — and a detailed map when you zoom in. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Raid-style boss fights: phases, adds, nests, shields, marks — built from vanilla parts. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |
| [![HudLayout](https://raw.githubusercontent.com/tbsj1ga/HudLayoutValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HudLayout/) | **[HudLayout](https://thunderstore.io/c/valheim/p/j1gA/HudLayout/)** — Move, resize and restyle your HUD with the mouse: bars, food, hotbar, minimap, even other mods' HUD. |
| [![HealthPulse](https://raw.githubusercontent.com/tbsj1ga/HealthPulseValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HealthPulse/) | **[HealthPulse](https://thunderstore.io/c/valheim/p/j1gA/HealthPulse/)** — Low health warning: the screen edges pulse red like a heartbeat, faster as your health drops. |

## AI assistance

This mod was developed with the help of an AI assistant (Claude by Anthropic).
The code and the documentation were written together with it and checked
against the game's IL; the design decisions, in-game testing and releases are
the author's.
