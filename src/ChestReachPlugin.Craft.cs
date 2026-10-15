using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Crafting and upgrading.
    //
    //   * Player.HaveRequirementItems (recipes) counts the chests around the station too, so
    //     the recipe list, the craft button and the requirement rows treat them as carried.
    //   * Pressing Craft asks the owners of the chests that will be needed; the answers come
    //     during the crafting animation (about a second and a half).
    //   * InventoryGui.DoCrafting, at the end of the animation: in one frame, the chests are
    //     checked to be ours and to really hold the deficit, and only then does the vanilla
    //     craft run. Its ConsumeResources takes from the inventory as usual and the deficit
    //     from those chests. If a chest is not ours yet, the craft waits for it (the bar stays
    //     full) up to a few seconds; if it cannot be covered, nothing is crafted and nothing
    //     is spent.
    public partial class ChestReachPlugin
    {
        // The chests a ConsumeResources may take from: set right before a craft or a placement
        // whose deficit they cover, cleared right after it.
        private sealed class ConsumeContext
        {
            public List<Box> Boxes;
        }

        private ConsumeContext _consume;

        private bool _craftHold;
        private float _craftHoldStart = -1f;
        private const float CraftWait = 4f;

        // ------------------------------------------------------------------
        // what a recipe or a piece needs, with the same filters as the game
        // ------------------------------------------------------------------
        // Player.HaveRequirementItems / ConsumeResources skip the upgrader resource at a normal
        // station and the normal ones at an upgrader.
        private static bool Counts(Piece.Requirement req, CraftingStation station)
        {
            if (req == null || req.m_resItem == null) return false;
            if (station != null && station.m_upgrader != req.m_upgraderResource) return false;
            if (station == null && req.m_upgraderResource) return false;
            return true;
        }

        private static Dictionary<string, int> Needs(Player p, Piece.Requirement[] reqs, int quality, int multiplier)
        {
            Dictionary<string, int> need = new Dictionary<string, int>();
            if (reqs == null) return need;
            CraftingStation station = p.GetCurrentCraftingStation();
            foreach (Piece.Requirement req in reqs)
            {
                if (!Counts(req, station)) continue;
                int amount = req.GetAmount(quality) * multiplier;
                if (amount <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int n;
                need.TryGetValue(name, out n);
                need[name] = n + amount;
            }
            return need;
        }

        private static Dictionary<string, int> PieceNeeds(Piece piece)
        {
            Dictionary<string, int> need = new Dictionary<string, int>();
            if (piece == null || piece.m_resources == null) return need;
            foreach (Piece.Requirement req in piece.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int n;
                need.TryGetValue(name, out n);
                need[name] = n + req.m_amount;
            }
            return need;
        }

        // What the inventory does not cover.
        private static Dictionary<string, int> Deficits(Player p, Dictionary<string, int> need)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            Inventory inv = p.GetInventory();
            foreach (KeyValuePair<string, int> kv in need)
            {
                int missing = kv.Value - inv.CountItems(kv.Key);
                if (missing > 0) d[kv.Key] = missing;
            }
            return d;
        }

        private static Vector3 CraftCenter(Player p)
        {
            CraftingStation st = p.GetCurrentCraftingStation();
            return st != null ? st.transform.position : p.transform.position;
        }

        private bool CraftFree(Player p)
        {
            return p.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
        }

        // The recipe being crafted right now, as DoCrafting will see it.
        private void CraftJob(InventoryGui gui, Player p, out Recipe recipe, out Dictionary<string, int> deficits)
        {
            recipe = _craftRecipe.GetValue(gui) as Recipe;
            deficits = null;
            if (recipe == null) return;
            ItemDrop.ItemData upgrade = _craftUpgradeItem.GetValue(gui) as ItemDrop.ItemData;
            int quality = upgrade == null ? 1 : upgrade.m_quality + 1;
            int multiplier = (bool)_multiCrafting.GetValue(gui) ? Math.Max(1, gui.m_multiCraftAmount) : 1;
            if (recipe.m_requireOnlyOneIngredient) return;
            deficits = Deficits(p, Needs(p, recipe.m_resources, quality, multiplier));
        }

        // ------------------------------------------------------------------
        // patches
        // ------------------------------------------------------------------
        private void AfterHaveRequirementItems(Player player, Recipe recipe, bool discover, int quality, int amount, ref bool result)
        {
            if (result || discover || recipe == null || recipe.m_requireOnlyOneIngredient) return;
            if (!IsLocal(player) || !CraftOn) return;
            Stock s = GetStock(CraftCenter(player), CraftRange);
            if (s.Boxes.Count == 0) return;
            Inventory inv = player.GetInventory();
            CraftingStation station = player.GetCurrentCraftingStation();
            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (!Counts(req, station)) continue;
                int need = req.GetAmount(quality) * amount;
                if (need <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                if (inv.CountItems(name) + s.Count(name) < need) return;
            }
            result = true;
        }

        private void AfterCraftPressed(InventoryGui gui)
        {
            Player p = Player.m_localPlayer;
            if (p == null || !CraftOn || CraftFree(p)) return;
            Recipe recipe;
            Dictionary<string, int> deficits;
            CraftJob(gui, p, out recipe, out deficits);
            if (deficits == null || deficits.Count == 0) return;
            Stock s = GetStock(CraftCenter(p), CraftRange);
            List<Box> plan = Plan(s, deficits, null);
            if (plan != null) AcquireAll(plan, null);
        }

        // True: let the vanilla craft run now (with the chests set for ConsumeResources).
        private bool BeforeDoCrafting(InventoryGui gui, Player p)
        {
            _craftHold = false;
            _consume = null;
            if (!IsLocal(p) || !CraftOn || CraftFree(p)) return true;
            Recipe recipe;
            Dictionary<string, int> deficits;
            CraftJob(gui, p, out recipe, out deficits);
            if (deficits == null || deficits.Count == 0)
            {
                _craftHoldStart = -1f;
                return true;
            }

            Stock s = GetStock(CraftCenter(p), CraftRange);
            HashSet<Box> failed = new HashSet<Box>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<Box> plan = Plan(s, deficits, failed);
                if (plan == null)
                {
                    // the vanilla check will refuse it too; say what is missing
                    _craftHoldStart = -1f;
                    Tell(failed.Count > 0 ? T("busy") : T("short", Shortfall(deficits, s)));
                    return failed.Count == 0;
                }
                Own o = AcquireAll(plan, failed);
                if (o == Own.Failed) continue;
                if (o == Own.Pending)
                {
                    if (_craftHoldStart < 0f) _craftHoldStart = Time.time;
                    if (Time.time - _craftHoldStart > CraftWait)
                    {
                        _craftHoldStart = -1f;
                        Tell(T("busy"));
                        return false;
                    }
                    _craftHold = true;
                    return false;
                }
                if (!LiveCovers(plan, deficits))
                {
                    // the copy we counted from was behind the chest: count again
                    s = BuildStock(CraftCenter(p), CraftRange);
                    continue;
                }
                _craftHoldStart = -1f;
                ConsumeContext ctx = new ConsumeContext();
                ctx.Boxes = plan;
                _consume = ctx;
                return true;
            }
            _craftHoldStart = -1f;
            Tell(T("busy"));
            return false;
        }

        // Take what the inventory lacks from the chests of the context; the vanilla method then
        // removes the rest from the inventory.
        private void BeforeConsume(Player p, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
        {
            ConsumeContext ctx = _consume;
            if (ctx == null || !IsLocal(p) || requirements == null) return;
            _consume = null;
            Inventory inv = p.GetInventory();
            CraftingStation station = p.GetCurrentCraftingStation();
            List<string> report = new List<string>();
            foreach (Piece.Requirement req in requirements)
            {
                if (!Counts(req, station)) continue;
                int need = req.GetAmount(qualityLevel) * multiplier;
                if (need <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int missing = need - inv.CountItems(name, itemQuality);
                if (missing <= 0) continue;
                int taken = Take(ctx.Boxes, name, missing);
                if (taken > 0) report.Add(ItemName(name) + " x" + taken);
                if (taken < missing) Logger.LogWarning("Took " + taken + " of " + missing + " " + name + " from the chests; the rest was not there any more.");
            }
            if (report.Count > 0 && _cfgShowTaken.Value)
                p.Message(MessageHud.MessageType.TopLeft, T("taken", string.Join(", ", report.ToArray())));
        }

        [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
        private static class Player_HaveRequirementItems_Patch
        {
            private static void Postfix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.AfterHaveRequirementItems(__instance, piece, discover, qualityLevel, amount, ref __result); }
                catch (Exception e) { p.Fail("Player.HaveRequirementItems", e); }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        private static class InventoryGui_OnCraftPressed_Patch
        {
            private static void Postfix(InventoryGui __instance)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.AfterCraftPressed(__instance); } catch (Exception e) { p.Fail("InventoryGui.OnCraftPressed", e); }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting_Patch
        {
            private static bool Prefix(InventoryGui __instance, Player player)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return true;
                try { return p.BeforeDoCrafting(__instance, player); }
                catch (Exception e)
                {
                    p.Fail("InventoryGui.DoCrafting", e);
                    p._consume = null;
                    p._craftHold = false;
                    return false;
                }
            }

            private static Exception Finalizer(Exception __exception)
            {
                ChestReachPlugin p = Instance;
                if (p != null) p._consume = null;
                return __exception;
            }
        }

        // While a craft waits for a chest, keep the timer past its end so that DoCrafting is
        // tried again next frame.
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        private static class InventoryGui_UpdateRecipe_Patch
        {
            private static void Postfix(InventoryGui __instance)
            {
                ChestReachPlugin p = Instance;
                if (p == null || !p._craftHold) return;
                try { _craftTimer.SetValue(__instance, 1000f); } catch (Exception e) { p.Fail("InventoryGui.UpdateRecipe", e); }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        private static class InventoryGui_Hide_Patch
        {
            private static void Postfix()
            {
                ChestReachPlugin p = Instance;
                if (p == null) return;
                p._craftHold = false;
                p._craftHoldStart = -1f;
            }
        }

        [HarmonyPatch(typeof(Player), "ConsumeResources")]
        private static class Player_ConsumeResources_Patch
        {
            private static void Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._consume == null) return;
                try { p.BeforeConsume(__instance, requirements, qualityLevel, itemQuality, multiplier); }
                catch (Exception e) { p.Fail("Player.ConsumeResources", e); }
            }
        }
    }
}
