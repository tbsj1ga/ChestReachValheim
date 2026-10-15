# ChestReach

**Craft, build, refuel and cook with what is in the chests around you** — no more carrying
materials back and forth. Your inventory is used first; whatever it lacks comes from the
chests nearby.

- **Crafting and upgrading** at any station with the chests around it (by hand: around you).
  Recipes light up when the chests have enough; multi-craft works too.
- **Building** with the hammer, hoe and cultivator from the chests around you.
- **Stations**: press E with nothing to put in, and one item comes from a chest — fuel for
  fires, torches, lamps, smelters and the oven; ore, wood, flax, barley and sap for the
  smelter, furnace, kiln, spinning wheel, windmill and refinery; food for cooking stations
  and the oven; mead bases for the fermenter; bones for the shield generator, ammo for the
  ballista.
- **Repair all**: one press of the repair button repairs every item.
- **Area repair**: `;` repairs the damaged buildings around you (wards and stations respected).
- Material amounts covered by the chests show in their own colour; the tooltip tells how
  many you carry and how many are in the chests nearby.

## No lag near big storage

The chests in the loaded area are indexed once; a chest's item counts are recounted only
when that chest changes. A recipe row or a piece in the build menu is a table lookup — the
crafting and build menus stay smooth next to a hundred chests.

## Players without the mod can join

**Client-side only.** ChestReach takes from a chest the way the game opens one: it asks the
chest's owner with the game's own request, and the owner's game — **with or without the
mod** — hands it over. Nothing new travels over the network, nobody is ever refused for not
having the mod, the host and a dedicated server need nothing. A chest someone has open is
left alone. Nothing is stored outside the world: remove the mod at any time.

If the host has the mod, its settings (ranges, switches) apply to everyone with the mod.

## Which chests

Placed chests, **carts**, **ship storage** and **dungeon chests**, within 20 m by default
(of the station, of you when building). Not: graves, other players' private chests, chests
behind a ward you have no access to, chests open in someone's hands. Every one of these is
a setting.

## Settings

`BepInEx/config/j1ga.chestreach.cfg`, or in-game with ConfigurationManager (F1): each
feature on/off, the ranges for crafting, building and stations (up to 50 m), carts, ships,
dungeon chests, ward access for every chest, excluded chest prefabs, repair, the area repair
key and radius, the colour and the tooltip. Console: `chestreach status`.

## Compatibility

Built against Valheim 1.0.20, BepInEx 5.4.23.5 (BepInExPack_Valheim 5.4.2351). Do not run it
together with another craft-from-containers mod (RunicCrafting, AzuCraftyBoxes,
CraftFromContainers and the like) — they do the same job.

## Bugs and feedback

[GitHub issues](https://github.com/tbsj1ga/ChestReachValheim/issues) - please include
`BepInEx/LogOutput.log` and the output of `chestreach status` (F5).

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

Source, full documentation and the changelog: https://github.com/tbsj1ga/ChestReachValheim

*Developed with the help of an AI assistant (Claude by Anthropic); the design decisions,
verification against the game code and in-game testing are the author's.*
