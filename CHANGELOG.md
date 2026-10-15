# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `ChestReachPlugin.Version` in `src/ChestReachPlugin.cs`.

## 0.1.0

- First release. Crafting and upgrading with the chests around the station, building with the
  chests around you, stations (fuel, ore, food, mead bases, shield fuel, ballista ammo) with one
  item from a chest; repair all; area repair on `;`.
- Client-side: a chest is taken from after its owner hands it over through the vanilla
  open request, which a player without the mod answers too. Nothing new over the network,
  nothing stored outside the world. The host's settings apply to everyone with the mod.
- Chests are indexed once and recounted only when they change: no lag in the crafting and
  build menus near large storage.
