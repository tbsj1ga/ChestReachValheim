using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Building with the hammer, hoe and cultivator.
    //
    //   * Player.HaveRequirements(Piece) counts the chests around the player too, so the
    //     build menu and the placement check treat them as carried.
    //   * Placing: Player.TryPlacePiece runs after that check and before the vanilla
    //     ConsumeResources. If the chests that cover the deficit are ours and really hold it,
    //     they are set for ConsumeResources; if not, the piece is not placed, the owners are
    //     asked, and the next click places it. While a piece is selected the chests it would
    //     need are asked for ahead of time, so the first click usually works.
    public partial class ChestReachPlugin
    {
        private float _nextPrefetch;
        private readonly Dictionary<Box, float> _prefetched = new Dictionary<Box, float>();

        private const float PrefetchInterval = 0.5f;
        private const float PrefetchRepeat = 5f;

        private bool BuildFree(Player p, Piece piece)
        {
            if (p.NoCostCheat()) return true;
            ZoneSystem zs = ZoneSystem.instance;
            return zs != null && zs.GetGlobalKey(piece.FreeBuildKey());
        }

        private void AfterHavePieceRequirements(Player player, Piece piece, Player.RequirementMode mode, ref bool result)
        {
            if (result || piece == null || mode == Player.RequirementMode.IsKnown) return;
            if (!IsLocal(player) || !BuildOn) return;

            // the vanilla reasons that are not about materials stay
            if (piece.m_craftingStation != null)
            {
                if (mode == Player.RequirementMode.CanAlmostBuild)
                {
                    Dictionary<string, int> known = _knownStations.GetValue(player) as Dictionary<string, int>;
                    if (known == null || !known.ContainsKey(piece.m_craftingStation.m_name)) return;
                }
                else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position)
                         && (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)))
                    return;
            }
            if (piece.m_dlc.Length > 0 && (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(piece.m_dlc))) return;

            Stock s = GetStock(player.transform.position, BuildRange);
            if (s.Boxes.Count == 0) return;
            Inventory inv = player.GetInventory();
            foreach (Piece.Requirement req in piece.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                if (mode == Player.RequirementMode.CanAlmostBuild)
                {
                    if (!inv.HaveItem(name) && s.Count(name) <= 0) return;
                }
                else if (inv.CountItems(name) + s.Count(name) < req.m_amount) return;
            }
            result = true;
        }

        // True: let the vanilla placement run (with the chests set for ConsumeResources).
        private bool BeforeTryPlacePiece(Player p, Piece piece)
        {
            _consume = null;
            if (!IsLocal(p) || !BuildOn || piece == null || BuildFree(p, piece)) return true;
            Dictionary<string, int> deficits = Deficits(p, PieceNeeds(piece));
            if (deficits.Count == 0) return true;

            Stock s = GetStock(p.transform.position, BuildRange);
            HashSet<Box> failed = new HashSet<Box>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<Box> plan = Plan(s, deficits, failed);
                if (plan == null)
                {
                    Tell(failed.Count > 0 ? T("busy") : T("short", Shortfall(deficits, s)));
                    return false;
                }
                Own o = AcquireAll(plan, failed);
                if (o == Own.Failed) continue;
                if (o == Own.Pending)
                {
                    Tell(T("fetching"));
                    return false;
                }
                if (!LiveCovers(plan, deficits))
                {
                    s = BuildStock(p.transform.position, BuildRange);
                    continue;
                }
                ConsumeContext ctx = new ConsumeContext();
                ctx.Boxes = plan;
                _consume = ctx;
                return true;
            }
            Tell(T("busy"));
            return false;
        }

        // While a piece is selected and the inventory does not cover it, ask for the chests it
        // would take from - each one at most every few seconds, so that two builders sharing
        // a chest do not pass it back and forth all the time.
        private void UpdateBuildPrefetch()
        {
            if (Time.time < _nextPrefetch) return;
            _nextPrefetch = Time.time + PrefetchInterval;
            Player p = Player.m_localPlayer;
            if (p == null || !BuildOn || !p.InPlaceMode() || p.InRepairMode()) return;
            Piece piece = p.GetSelectedPiece();
            if (piece == null || piece.m_repairPiece || piece.m_removePiece || BuildFree(p, piece)) return;
            Dictionary<string, int> deficits = Deficits(p, PieceNeeds(piece));
            if (deficits.Count == 0) return;
            Stock s = GetStock(p.transform.position, BuildRange);
            List<Box> plan = Plan(s, deficits, null);
            if (plan == null) return;
            if (_prefetched.Count > 64) _prefetched.Clear();
            foreach (Box b in plan)
            {
                if (b.Owned) continue;
                float last;
                if (_prefetched.TryGetValue(b, out last) && Time.time - last < PrefetchRepeat) continue;
                _prefetched[b] = Time.time;
                Acquire(b);
            }
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Player), "HaveRequirements", new Type[] { typeof(Piece), typeof(Player.RequirementMode) })]
        private static class Player_HaveRequirements_Piece_Patch
        {
            private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.AfterHavePieceRequirements(__instance, piece, mode, ref __result); }
                catch (Exception e) { p.Fail("Player.HaveRequirements(Piece)", e); }
            }
        }

        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static class Player_TryPlacePiece_Patch
        {
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return true;
                try
                {
                    if (p.BeforeTryPlacePiece(__instance, piece)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e)
                {
                    p.Fail("Player.TryPlacePiece", e);
                    p._consume = null;
                    __result = false;
                    return false;
                }
            }

            // Placement refused for a vanilla reason: nothing to consume.
            private static void Postfix(bool __result)
            {
                ChestReachPlugin p = Instance;
                if (p != null && !__result) p._consume = null;
            }
        }

        // TryPlacePiece and the ConsumeResources after it both run inside UpdatePlacement; the
        // context never outlives it.
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        private static class Player_UpdatePlacement_Patch
        {
            private static void Prefix()
            {
                ChestReachPlugin p = Instance;
                if (p != null) p._consume = null;
            }

            private static Exception Finalizer(Exception __exception)
            {
                ChestReachPlugin p = Instance;
                if (p != null) p._consume = null;
                return __exception;
            }
        }
    }
}
