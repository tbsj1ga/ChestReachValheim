using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Repair.
    //
    //   * Repair all: the repair button at a station repairs one item per press in vanilla;
    //     here one press runs the vanilla RepairOneItem until nothing repairable is left.
    //   * Area repair: a key repairs the damaged buildings around you as the hammer would,
    //     one by one: WearNTear.Repair sends the piece's owner its vanilla RPC_Repair. Wards
    //     and the crafting station a piece needs (near the piece) are respected, like the
    //     hammer does near you; nothing is spent. Nearest first, a few per frame, each with
    //     the piece's own placement effect and sound, as the hammer plays it - a wave going
    //     out from you shows what was repaired and where.
    public partial class ChestReachPlugin
    {
        private bool _repairingAll;
        private readonly Queue<WearNTear> _repairQueue = new Queue<WearNTear>();
        private int _repairDone;
        private int _repairNoStation;
        private const int RepairsPerFrame = 3;

        // True: run the vanilla RepairOneItem (nothing repaired here).
        private bool BeforeRepairOne(InventoryGui gui)
        {
            if (_repairingAll || !Ready || !Eff(_cfgRepairAll)) return true;
            int n = 0;
            _repairingAll = true;
            try
            {
                while (n < 200 && (bool)_haveRepairable.Invoke(gui, null))
                {
                    _repairOne.Invoke(gui, null);
                    n++;
                }
            }
            finally
            {
                _repairingAll = false;
            }
            if (n == 0) return true;
            if (n > 1) Tell(T("repaired", n));
            return false;
        }

        private static bool Typing()
        {
            return (Chat.instance != null && Chat.instance.HasFocus()) || global::Console.IsVisible() || TextInput.IsVisible()
                || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.InTextInput() || StoreGui.IsVisible()
                || (Hud.instance != null && Hud.instance.m_buildUi != null && Hud.instance.m_buildUi.SearchFieldFocused);
        }

        private void UpdateAreaRepair()
        {
            if (_repairQueue.Count > 0)
            {
                for (int i = 0; i < RepairsPerFrame && _repairQueue.Count > 0; i++)
                {
                    WearNTear w = _repairQueue.Dequeue();
                    if (w == null || !w.Repair()) continue;
                    _repairDone++;
                    Piece piece = w.GetComponent<Piece>();
                    if (piece != null && piece.m_placeEffect != null)
                        piece.m_placeEffect.Create(w.transform.position, w.transform.rotation);
                }
                if (_repairQueue.Count == 0) ReportAreaRepair();
                return;
            }
            if (!Ready || !Eff(_cfgAreaRepair) || Typing()) return;
            if (!_cfgAreaRepairKey.Value.IsDown()) return;
            StartAreaRepair();
        }

        private void StartAreaRepair()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            float radius = Mathf.Clamp(Eff(_cfgAreaRepairRadius), 1f, 100f);
            float r2 = radius * radius;
            Vector3 me = p.transform.position;
            bool noWorkbench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            _repairDone = 0;
            _repairNoStation = 0;
            List<KeyValuePair<float, WearNTear>> found = new List<KeyValuePair<float, WearNTear>>();
            foreach (WearNTear w in WearNTear.GetAllInstances())
            {
                if (w == null) continue;
                Vector3 pos = w.transform.position;
                if ((pos - me).sqrMagnitude > r2) continue;
                Piece piece = w.GetComponent<Piece>();
                if (piece == null || !piece.IsPlacedByPlayer()) continue;
                ZNetView view = w.GetComponent<ZNetView>();
                if (view == null || view.GetZDO() == null) continue;
                if (view.GetZDO().GetFloat(ZDOVars.s_health, w.m_health) >= w.m_health) continue;
                if (!PrivateArea.CheckAccess(pos, 0f, false, false)) continue;
                if (piece.m_craftingStation != null && !noWorkbench && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, pos) == null)
                {
                    _repairNoStation++;
                    continue;
                }
                found.Add(new KeyValuePair<float, WearNTear>((pos - me).sqrMagnitude, w));
            }
            found.Sort(delegate(KeyValuePair<float, WearNTear> a, KeyValuePair<float, WearNTear> z) { return a.Key.CompareTo(z.Key); });
            foreach (KeyValuePair<float, WearNTear> kv in found) _repairQueue.Enqueue(kv.Value);
            if (_repairQueue.Count == 0) ReportAreaRepair();
        }

        private void ReportAreaRepair()
        {
            string text = _repairDone > 0 ? T("area", _repairDone) : T("area_none");
            if (_repairNoStation > 0) text += T("area_station", _repairNoStation);
            Tell(text);
        }

        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        private static class InventoryGui_RepairOneItem_Patch
        {
            private static bool Prefix(InventoryGui __instance)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return true;
                try { return p.BeforeRepairOne(__instance); }
                catch (Exception e)
                {
                    p.Fail("InventoryGui.RepairOneItem", e);
                    p._repairingAll = false;
                    return true;
                }
            }
        }
    }
}
