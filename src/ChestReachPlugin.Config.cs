using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ChestReach
{
    public partial class ChestReachPlugin
    {
        // ------------------------------------------------------------------
        // config
        // ------------------------------------------------------------------
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDebug;

        private ConfigEntry<bool> _cfgCraft;
        private ConfigEntry<float> _cfgCraftRange;

        private ConfigEntry<bool> _cfgBuild;
        private ConfigEntry<float> _cfgBuildRange;

        private ConfigEntry<bool> _cfgRefuel;
        private ConfigEntry<bool> _cfgLoad;
        private ConfigEntry<bool> _cfgCook;
        private ConfigEntry<float> _cfgStationRange;

        private ConfigEntry<bool> _cfgCarts;
        private ConfigEntry<bool> _cfgShips;
        private ConfigEntry<bool> _cfgDungeon;
        private ConfigEntry<bool> _cfgWardAll;
        private ConfigEntry<string> _cfgExcluded;

        private ConfigEntry<bool> _cfgRepairAll;
        private ConfigEntry<bool> _cfgAreaRepair;
        private ConfigEntry<KeyboardShortcut> _cfgAreaRepairKey;
        private ConfigEntry<float> _cfgAreaRepairRadius;

        private ConfigEntry<bool> _cfgShowTotal;
        private ConfigEntry<bool> _cfgShowTaken;
        private ConfigEntry<bool> _cfgTooltip;
        private ConfigEntry<Color> _cfgCoveredColor;

        private ConfigEntry<bool> _cfgSync;

        // Settings that change what a player may take and where: the server's apply to every
        // client with the mod while connected (ChestReachPlugin.Sync.cs). Keys and display are
        // each player's own.
        private readonly List<ConfigEntryBase> _synced = new List<ConfigEntryBase>();

        private const float MaxRange = 50f;

        private ConfigEntry<T> Synced<T>(ConfigEntry<T> e)
        {
            _synced.Add(e);
            return e;
        }

        private ConfigEntry<float> Range(string section, string what)
        {
            return Synced(Config.Bind(section, "RangeMeters", 20f,
                new ConfigDescription(what, new AcceptableValueRange<float>(1f, MaxRange))));
        }

        private void BindConfig()
        {
            _cfgEnabled = Synced(Config.Bind("01 General", "Enabled", true, "Master switch. Off: crafting, building, stations and repair are vanilla."));
            _cfgDebug = Config.Bind("01 General", "Debug", false, "Log every chest request and every take.");

            _cfgCraft = Synced(Config.Bind("02 Crafting", "Enabled", true, "Craft and upgrade with materials from the chests around the crafting station (around you when crafting by hand). Your inventory is used first."));
            _cfgCraftRange = Range("02 Crafting", "Chests up to this far from the crafting station count.");

            _cfgBuild = Synced(Config.Bind("03 Building", "Enabled", true, "Build with materials from the chests around you: hammer, hoe, cultivator. Your inventory is used first."));
            _cfgBuildRange = Range("03 Building", "Chests up to this far from you count.");

            _cfgRefuel = Synced(Config.Bind("04 Stations", "Refuel", true, "With nothing to put in in your inventory, take one fuel item from a chest: fires, torches and lamps, smelter and oven fuel, the shield generator, ballista ammo."));
            _cfgLoad = Synced(Config.Bind("04 Stations", "LoadOre", true, "The same for what smelters process: ore into the smelter and furnace, wood into the charcoal kiln, flax and barley into the spinning wheel and windmill, sap into the eitr refinery."));
            _cfgCook = Synced(Config.Bind("04 Stations", "Cook", true, "The same for food: cooking stations, the oven, and mead bases into the fermenter."));
            _cfgStationRange = Range("04 Stations", "Chests up to this far from the station count.");

            _cfgCarts = Synced(Config.Bind("05 Chests", "IncludeCarts", true, "Take from carts."));
            _cfgShips = Synced(Config.Bind("05 Chests", "IncludeShips", true, "Take from the storage of ships."));
            _cfgDungeon = Synced(Config.Bind("05 Chests", "IncludeWorldChests", true, "Take from chests the world placed (dungeons, villages), not only from those built by players."));
            _cfgWardAll = Synced(Config.Bind("05 Chests", "RequireWardAccess", false, "Require access to the ward for every chest. The game asks it only of the chests that check wards; those are always respected."));
            _cfgExcluded = Synced(Config.Bind("05 Chests", "ExcludedPrefabs", "", "Chest prefabs never taken from, comma separated. Example: piece_chest_private,piece_chest_blackmetal"));
            _cfgExcluded.SettingChanged += delegate { ReadExcluded(); };
            ReadExcluded();

            _cfgRepairAll = Synced(Config.Bind("06 Repair", "RepairAll", true, "One press of the repair button at a station repairs every item it can, not one."));
            _cfgAreaRepair = Synced(Config.Bind("06 Repair", "AreaRepair", true, "The key below repairs the damaged buildings around you, as the hammer would one by one: wards and the crafting station a piece needs are respected, nothing is spent."));
            _cfgAreaRepairKey = Config.Bind("06 Repair", "AreaRepairKey", new KeyboardShortcut(KeyCode.Semicolon), "Key for the area repair. Ignored while typing or in a menu.");
            _cfgAreaRepairRadius = Synced(Config.Bind("06 Repair", "AreaRepairRadius", 50f,
                new ConfigDescription("Radius of the area repair, meters.", new AcceptableValueRange<float>(1f, 100f))));

            _cfgShowTotal = Config.Bind("07 Display", "ShowAvailableAmount", true, "A material in a recipe or a piece reads 'available/needed': what you carry plus what is in the chests nearby, over what it takes. Off: only the amount needed, as in vanilla.");
            _cfgShowTaken = Config.Bind("07 Display", "ShowTakenMessage", true, "After a craft or a placement, a line in the top-left message log says what was taken from the chests.");
            _cfgTooltip = Config.Bind("07 Display", "ShowCountsInTooltip", true, "The tooltip of a material in a recipe or a piece tells how many you carry and how many are in the chests nearby.");
            _cfgCoveredColor = Config.Bind("07 Display", "CoveredColor", new Color(0.62f, 0.83f, 1f, 1f), "Colour of a material amount that your inventory alone does not cover but the chests nearby do.");

            _cfgSync = Config.Bind("08 Sync", "SyncConfig", true, "The server sends its settings of sections 01-06 (except the key) to every connecting client with the mod and again when they change; the client uses them while connected. Players without the mod are never affected. Off on the server: every client uses its own file. Has no effect on a client.");

            Config.SettingChanged += delegate { _syncDirty = true; };
        }

        private readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _serverExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void ReadExcluded()
        {
            ReadList(_cfgExcluded.Value, _excluded);
            _indexVersion++;
        }

        private static void ReadList(string text, HashSet<string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text.Split(',', ';'))
            {
                string s = raw.Trim();
                if (s.Length > 0) into.Add(s);
            }
        }

        // ------------------------------------------------------------------
        // values in effect: the server's while connected to a server with the mod, else ours
        // ------------------------------------------------------------------
        private T Eff<T>(ConfigEntry<T> e)
        {
            object v;
            if (_serverSynced && _serverValues.TryGetValue(e, out v) && v is T) return (T)v;
            return e.Value;
        }

        private HashSet<string> Excluded { get { return _serverSynced && _serverValues.ContainsKey(_cfgExcluded) ? _serverExcluded : _excluded; } }

        private float CraftRange { get { return Mathf.Clamp(Eff(_cfgCraftRange), 1f, MaxRange); } }
        private float BuildRange { get { return Mathf.Clamp(Eff(_cfgBuildRange), 1f, MaxRange); } }
        private float StationRange { get { return Mathf.Clamp(Eff(_cfgStationRange), 1f, MaxRange); } }

        private bool CraftOn { get { return Ready && Eff(_cfgCraft); } }
        private bool BuildOn { get { return Ready && Eff(_cfgBuild); } }
    }
}
