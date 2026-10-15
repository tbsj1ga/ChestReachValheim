using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Stations: fires, smelters, cooking stations, the fermenter, the shield generator, the
    // ballista.
    //
    // Each of them, when used with nothing in hand, looks in the inventory for something it
    // accepts, removes one and tells its owner through its own RPC. If the inventory has none
    // of what it accepts, one item is moved from a chest around the station into the
    // inventory just before it looks, and its own code does the rest - every check (full,
    // no fire, no roof, wrong ammo) and the RPC stay vanilla. If the station turned the item
    // down, it goes back into the chest it came from. A chest that is not ours yet is asked
    // for, and the use is repeated by itself when it arrives.
    public partial class ChestReachPlugin
    {
        private sealed class Fetched
        {
            public Box B;
            public string Name;
            public int After;
        }

        private sealed class StationRetry
        {
            public Box B;
            public Action Run;
            public float Until;
        }

        private StationRetry _stationRetry;

        private bool StationsOn(ConfigEntry<bool> kind)
        {
            return Ready && Eff(kind);
        }

        // True: run the vanilla method (with one item fetched into the inventory, if any).
        private bool BeforeStation(Humanoid user, Component station, List<string> wanted, Action retry, out Fetched fetched)
        {
            fetched = null;
            if (!IsLocal(user) || station == null || wanted == null || wanted.Count == 0) return true;
            Inventory inv = user.GetInventory();
            if (inv == null) return true;
            foreach (string w in wanted)
                if (inv.HaveItem(w)) return true;

            Stock s = GetStock(station.transform.position, StationRange);
            if (s.Boxes.Count == 0) return true;
            string name = null;
            foreach (string w in wanted)
            {
                if (s.Count(w) > 0) { name = w; break; }
            }
            if (name == null) return true;

            Box box = null;
            for (int pass = 0; pass < 2 && box == null; pass++)
            {
                foreach (Box b in s.Boxes)
                {
                    if ((pass == 0) != b.Owned || b.Count(name) <= 0) continue;
                    box = b;
                    break;
                }
            }
            if (box == null) return true;

            Own o = Acquire(box);
            if (o == Own.Pending)
            {
                StationRetry r = new StationRetry();
                r.B = box;
                r.Run = retry;
                r.Until = Time.time + RequestTimeout + 0.5f;
                _stationRetry = r;
                return false;
            }
            if (o == Own.Failed)
            {
                Tell(T("busy"));
                return false;
            }

            ItemDrop.ItemData item = FirstUsable(box.Inv, name);
            if (item == null) return true;
            ItemDrop.ItemData one = item.Clone();
            one.m_stack = 1;
            if (!inv.AddItem(one))
            {
                Tell(T("invfull", ItemName(name)));
                return false;
            }
            box.Inv.RemoveItem(item, 1);
            fetched = new Fetched();
            fetched.B = box;
            fetched.Name = name;
            fetched.After = inv.CountItems(name);
            Debug("fetched 1 " + name + " from " + box.Prefab + " for " + PrefabName(station.gameObject));
            return true;
        }

        // The station did not take the fetched item: back where it came from.
        private void AfterStation(Humanoid user, Fetched f)
        {
            if (f == null || user == null) return;
            Inventory inv = user.GetInventory();
            if (inv == null || inv.CountItems(f.Name) < f.After) return;
            if (!f.B.Owned) return;
            ItemDrop.ItemData item = FirstUsable(inv, f.Name);
            if (item == null) return;
            ItemDrop.ItemData one = item.Clone();
            one.m_stack = 1;
            if (f.B.Inv.AddItem(one))
            {
                inv.RemoveItem(item, 1);
                Debug("returned 1 " + f.Name + " to " + f.B.Prefab);
            }
        }

        // An item the game would count: of the current world level or better.
        private static ItemDrop.ItemData FirstUsable(Inventory inv, string name)
        {
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item != null && item.m_shared.m_name == name && item.m_worldLevel >= Game.m_worldLevel && item.m_stack > 0) return item;
            }
            return null;
        }

        private void UpdateStationRetry()
        {
            StationRetry r = _stationRetry;
            if (r == null) return;
            Own o = Acquire(r.B);
            if (o == Own.Pending && Time.time < r.Until) return;
            _stationRetry = null;
            if (o == Own.Ready)
            {
                try { if (r.Run != null) r.Run(); }
                catch (Exception e) { Fail("station retry", e); }
            }
            else Tell(T("busy"));
        }

        private static List<string> Names(ItemDrop single)
        {
            List<string> l = new List<string>();
            if (single != null) l.Add(single.m_itemData.m_shared.m_name);
            return l;
        }

        private static List<string> Names(IEnumerable<ItemDrop> items)
        {
            List<string> l = new List<string>();
            if (items == null) return l;
            foreach (ItemDrop d in items)
                if (d != null && !l.Contains(d.m_itemData.m_shared.m_name)) l.Add(d.m_itemData.m_shared.m_name);
            return l;
        }

        // ------------------------------------------------------------------
        // per station: what it accepts and how to use it again
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Fireplace), "Interact")]
        private static class Fireplace_Interact_Patch
        {
            private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || hold || !p.StationsOn(p._cfgRefuel)) return true;
                try
                {
                    if (!__instance.m_canRefill || __instance.m_infiniteFuel || __instance.m_fuelItem == null) return true;
                    ZNetView view = __instance.GetComponent<ZNetView>();
                    if (view == null || view.GetZDO() == null) return true;
                    float fuel = view.GetZDO().GetFloat(ZDOVars.s_fuel);
                    if (__instance.m_canTurnOff && !alt && fuel > 0f) return true;
                    if (Mathf.CeilToInt(fuel) >= __instance.m_maxFuel) return true;
                    Fireplace fp = __instance;
                    Humanoid u = user;
                    bool a = alt;
                    if (p.BeforeStation(user, __instance, Names(__instance.m_fuelItem), delegate { fp.Interact(u, false, a); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("Fireplace.Interact", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("Fireplace.Interact", e); }
            }
        }

        [HarmonyPatch(typeof(Smelter), "OnAddFuel")]
        private static class Smelter_OnAddFuel_Patch
        {
            private static bool Prefix(Smelter __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || item != null || !p.StationsOn(p._cfgRefuel)) return true;
                try
                {
                    Smelter sm = __instance;
                    Switch s = sw;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, Names(__instance.m_fuelItem), delegate { _smelterAddFuel.Invoke(sm, new object[] { s, u, null }); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("Smelter.OnAddFuel", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("Smelter.OnAddFuel", e); }
            }
        }

        [HarmonyPatch(typeof(Smelter), "OnAddOre")]
        private static class Smelter_OnAddOre_Patch
        {
            private static bool Prefix(Smelter __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || item != null || !p.StationsOn(p._cfgLoad)) return true;
                try
                {
                    List<string> wanted = new List<string>();
                    foreach (Smelter.ItemConversion c in __instance.m_conversion)
                        if (c != null && c.m_from != null && !wanted.Contains(c.m_from.m_itemData.m_shared.m_name)) wanted.Add(c.m_from.m_itemData.m_shared.m_name);
                    Smelter sm = __instance;
                    Switch s = sw;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, wanted, delegate { _smelterAddOre.Invoke(sm, new object[] { s, u, null }); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("Smelter.OnAddOre", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("Smelter.OnAddOre", e); }
            }
        }

        [HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch")]
        private static class CookingStation_OnAddFuelSwitch_Patch
        {
            private static bool Prefix(CookingStation __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || item != null || !p.StationsOn(p._cfgRefuel) || !__instance.m_useFuel) return true;
                try
                {
                    CookingStation cs = __instance;
                    Switch s = sw;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, Names(__instance.m_fuelItem), delegate { _cookingAddFuel.Invoke(cs, new object[] { s, u, null }); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("CookingStation.OnAddFuelSwitch", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("CookingStation.OnAddFuelSwitch", e); }
            }
        }

        // Interact and the food switch of the oven both end up here.
        [HarmonyPatch(typeof(CookingStation), "OnInteract")]
        private static class CookingStation_OnInteract_Patch
        {
            private static bool Prefix(CookingStation __instance, Humanoid user, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || !p.StationsOn(p._cfgCook)) return true;
                try
                {
                    if ((bool)_cookingHaveDone.Invoke(__instance, null)) return true;
                    List<string> wanted = new List<string>();
                    foreach (CookingStation.ItemConversion c in __instance.m_conversion)
                        if (c != null && c.m_from != null && !wanted.Contains(c.m_from.m_itemData.m_shared.m_name)) wanted.Add(c.m_from.m_itemData.m_shared.m_name);
                    CookingStation cs = __instance;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, wanted, delegate { _cookingInteract.Invoke(cs, new object[] { u }); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("CookingStation.OnInteract", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("CookingStation.OnInteract", e); }
            }
        }

        [HarmonyPatch(typeof(Fermenter), "Interact")]
        private static class Fermenter_Interact_Patch
        {
            private static bool Prefix(Fermenter __instance, Humanoid user, bool hold, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || hold || !p.StationsOn(p._cfgCook)) return true;
                try
                {
                    // only an empty barrel takes something; Status.Empty is 0
                    if (Convert.ToInt32(_fermenterStatus.Invoke(__instance, null)) != 0) return true;
                    List<string> wanted = new List<string>();
                    foreach (Fermenter.ItemConversion c in __instance.m_conversion)
                        if (c != null && c.m_from != null && !wanted.Contains(c.m_from.m_itemData.m_shared.m_name)) wanted.Add(c.m_from.m_itemData.m_shared.m_name);
                    Fermenter f = __instance;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, wanted, delegate { f.Interact(u, false, false); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("Fermenter.Interact", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("Fermenter.Interact", e); }
            }
        }

        [HarmonyPatch(typeof(ShieldGenerator), "OnAddFuel")]
        private static class ShieldGenerator_OnAddFuel_Patch
        {
            private static bool Prefix(ShieldGenerator __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || item != null || !p.StationsOn(p._cfgRefuel)) return true;
                try
                {
                    ShieldGenerator sg = __instance;
                    Switch s = sw;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, Names(__instance.m_fuelItems), delegate { _shieldAddFuel.Invoke(sg, new object[] { s, u, null }); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("ShieldGenerator.OnAddFuel", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("ShieldGenerator.OnAddFuel", e); }
            }
        }

        [HarmonyPatch(typeof(Turret), "UseItem")]
        private static class Turret_UseItem_Patch
        {
            private static bool Prefix(Turret __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result, out Fetched __state)
            {
                __state = null;
                ChestReachPlugin p = Instance;
                if (p == null || item != null || !p.StationsOn(p._cfgRefuel)) return true;
                try
                {
                    // loaded with one kind of ammo: only that kind fits
                    List<string> wanted = new List<string>();
                    if (__instance.GetAmmo() > 0 && ZNetScene.instance != null)
                    {
                        GameObject prefab = ZNetScene.instance.GetPrefab(__instance.GetAmmoType());
                        ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                        if (drop != null) wanted.Add(drop.m_itemData.m_shared.m_name);
                    }
                    else
                    {
                        foreach (Turret.AmmoType a in __instance.m_allowedAmmo)
                            if (a.m_ammo != null && !wanted.Contains(a.m_ammo.m_itemData.m_shared.m_name)) wanted.Add(a.m_ammo.m_itemData.m_shared.m_name);
                    }
                    Turret t = __instance;
                    Humanoid u = user;
                    if (p.BeforeStation(user, __instance, wanted, delegate { t.UseItem(u, null); }, out __state)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("Turret.UseItem", e); return true; }
            }

            private static void Postfix(Humanoid user, Fetched __state)
            {
                ChestReachPlugin p = Instance;
                if (p == null || __state == null) return;
                try { p.AfterStation(user, __state); } catch (Exception e) { p.Fail("Turret.UseItem", e); }
            }
        }
    }
}
