using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // The chests in the loaded area and what is in them.
    //
    // Every Container registers itself when it wakes up. Its item counts are recounted only
    // when its own inventory changes - the game calls the inventory's m_onChanged when the
    // owner changes it and when another client loads the new contents from the world - and
    // every such change bumps one version number. A query ("what is within 20 m of this
    // station") is answered from a small cache that lives while the version stays the same,
    // for at most a second (wards, chests opened by others and moving carts and ships change
    // without touching an inventory). Changes elsewhere in the world - smelters ticking,
    // creatures, other players - do not touch the cache at all.
    public partial class ChestReachPlugin
    {
        internal sealed class Box
        {
            public Container C;
            public Inventory Inv;
            public ZNetView View;
            public Piece Piece;
            public string Prefab;
            public bool Tomb, Cart, Ship, World;
            public bool Dirty = true;
            public int WorldLevel = int.MinValue;
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

            public bool Alive { get { return C != null && View != null && View.IsValid(); } }

            public bool Owned { get { return Alive && View.IsOwner(); } }

            public Vector3 Pos { get { return C.transform.position; } }

            // Items as the game counts them for crafting: by shared name, only those of the
            // current world level or better (Inventory.CountItems).
            public int Count(string name)
            {
                if (Dirty || WorldLevel != Game.m_worldLevel) Recount();
                int n;
                return Counts.TryGetValue(name, out n) ? n : 0;
            }

            public void Recount()
            {
                Counts.Clear();
                Dirty = false;
                WorldLevel = Game.m_worldLevel;
                if (Inv == null) return;
                foreach (ItemDrop.ItemData item in Inv.GetAllItems())
                {
                    if (item == null || item.m_shared == null || item.m_worldLevel < Game.m_worldLevel) continue;
                    int n;
                    Counts.TryGetValue(item.m_shared.m_name, out n);
                    Counts[item.m_shared.m_name] = n + item.m_stack;
                }
            }
        }

        internal sealed class Stock
        {
            public Vector3 Center;
            public float Range;
            public int Version;
            public float Time;
            public readonly List<Box> Boxes = new List<Box>();
            public readonly Dictionary<string, int> Total = new Dictionary<string, int>();

            public int Count(string name)
            {
                int n;
                return Total.TryGetValue(name, out n) ? n : 0;
            }
        }

        private readonly List<Box> _boxes = new List<Box>();
        private readonly Dictionary<Container, Box> _byContainer = new Dictionary<Container, Box>();
        private readonly List<Stock> _stocks = new List<Stock>();
        private int _indexVersion;

        private static FieldInfo _containerView;
        private static MethodInfo _containerLoad;
        private static FieldInfo _craftRecipe;
        private static FieldInfo _craftUpgradeItem;
        private static FieldInfo _multiCrafting;
        private static FieldInfo _craftTimer;
        private static FieldInfo _knownStations;
        private static MethodInfo _haveRepairable;
        private static MethodInfo _repairOne;
        private static MethodInfo _smelterAddOre;
        private static MethodInfo _smelterAddFuel;
        private static MethodInfo _cookingInteract;
        private static MethodInfo _cookingAddFuel;
        private static MethodInfo _cookingHaveDone;
        private static MethodInfo _fermenterStatus;
        private static MethodInfo _shieldAddFuel;

        // Private members of the game; check-refs.ps1 verifies every one of them against the
        // installed game after each build.
        private void FindReflection()
        {
            _containerView = AccessTools.Field(typeof(Container), "m_nview");
            _containerLoad = AccessTools.Method(typeof(Container), "Load");
            _craftRecipe = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
            _craftUpgradeItem = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
            _multiCrafting = AccessTools.Field(typeof(InventoryGui), "m_multiCrafting");
            _craftTimer = AccessTools.Field(typeof(InventoryGui), "m_craftTimer");
            _knownStations = AccessTools.Field(typeof(Player), "m_knownStations");
            _haveRepairable = AccessTools.Method(typeof(InventoryGui), "HaveRepairableItems");
            _repairOne = AccessTools.Method(typeof(InventoryGui), "RepairOneItem");
            _smelterAddOre = AccessTools.Method(typeof(Smelter), "OnAddOre");
            _smelterAddFuel = AccessTools.Method(typeof(Smelter), "OnAddFuel");
            _cookingInteract = AccessTools.Method(typeof(CookingStation), "OnInteract");
            _cookingAddFuel = AccessTools.Method(typeof(CookingStation), "OnAddFuelSwitch");
            _cookingHaveDone = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
            _fermenterStatus = AccessTools.Method(typeof(Fermenter), "GetStatus");
            _shieldAddFuel = AccessTools.Method(typeof(ShieldGenerator), "OnAddFuel");
            if (_containerView == null || _containerLoad == null)
                throw new MissingMemberException("Container.m_nview / Container.Load not found: the game has changed, " + Name + " cannot work.");
        }

        // ------------------------------------------------------------------
        // registry
        // ------------------------------------------------------------------
        private void Register(Container c)
        {
            if (c == null || _byContainer.ContainsKey(c)) return;
            Inventory inv = c.GetInventory();
            ZNetView view = _containerView.GetValue(c) as ZNetView;
            if (inv == null || view == null || view.GetZDO() == null) return;

            Box b = new Box();
            b.C = c;
            b.Inv = inv;
            b.View = view;
            b.Piece = c.GetComponent<Piece>();
            b.Prefab = PrefabName(view.gameObject);
            b.Tomb = c.GetComponent<TombStone>() != null;
            b.Cart = c.m_wagon != null || c.GetComponentInParent<Vagon>() != null;
            b.Ship = c.GetComponentInParent<Ship>() != null;
            b.World = !b.Cart && !b.Ship && (b.Piece == null || !b.Piece.IsPlacedByPlayer());
            inv.m_onChanged = (Action)Delegate.Combine(inv.m_onChanged, new Action(delegate
            {
                b.Dirty = true;
                _indexVersion++;
            }));
            _boxes.Add(b);
            _byContainer[c] = b;
            _indexVersion++;
        }

        private Box BoxOf(Container c)
        {
            Box b;
            return c != null && _byContainer.TryGetValue(c, out b) ? b : null;
        }

        private void Prune()
        {
            for (int i = _boxes.Count - 1; i >= 0; i--)
            {
                Box b = _boxes[i];
                if (b.Alive) continue;
                _boxes.RemoveAt(i);
                if (!ReferenceEquals(b.C, null)) _byContainer.Remove(b.C);
                _indexVersion++;
            }
            if (_byContainer.Count != _boxes.Count)
            {
                _byContainer.Clear();
                foreach (Box b in _boxes) _byContainer[b.C] = b;
            }
        }

        private void ClearIndex()
        {
            _boxes.Clear();
            _byContainer.Clear();
            _stocks.Clear();
            _indexVersion++;
        }

        // ------------------------------------------------------------------
        // which chests may be taken from
        // ------------------------------------------------------------------
        private bool Eligible(Box b, long me)
        {
            if (!b.Alive) return false;
            if (b.Tomb) return false;
            if (b.Cart && !Eff(_cfgCarts)) return false;
            if (b.Ship && !Eff(_cfgShips)) return false;
            if (b.World && !Eff(_cfgDungeon)) return false;
            if (Excluded.Contains(b.Prefab)) return false;

            // the same rules as Container.Interact / RPC_RequestOpen
            Container c = b.C;
            if (c.m_privacy == Container.PrivacySetting.Private && (b.Piece == null || b.Piece.GetCreator() != me)) return false;
            if (c.m_privacy == Container.PrivacySetting.Group) return false;
            if ((c.m_checkGuardStone || Eff(_cfgWardAll)) && !PrivateArea.CheckAccess(b.Pos, 0f, false, false)) return false;

            // open in somebody else's hands right now: its owner would refuse
            if (!b.View.IsOwner())
            {
                ZDO zdo = b.View.GetZDO();
                if (zdo == null || zdo.GetInt(ZDOVars.s_inUse) == 1) return false;
                if (c.m_wagon != null && c.m_wagon.InUse()) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // stock around a point
        // ------------------------------------------------------------------
        private Stock GetStock(Vector3 center, float range)
        {
            float now = Time.time;
            for (int i = 0; i < _stocks.Count; i++)
            {
                Stock s = _stocks[i];
                if (s.Version == _indexVersion && s.Range == range && now - s.Time < 1f && (s.Center - center).sqrMagnitude < 0.25f)
                    return s;
            }
            Stock fresh = BuildStock(center, range);
            if (_stocks.Count >= 8) _stocks.RemoveAt(0);
            _stocks.Add(fresh);
            return fresh;
        }

        private Stock BuildStock(Vector3 center, float range)
        {
            Prune();
            Stock s = new Stock();
            s.Center = center;
            s.Range = range;
            s.Version = _indexVersion;
            s.Time = Time.time;
            long me = MyPlayerId();
            float r2 = range * range;
            List<KeyValuePair<float, Box>> found = new List<KeyValuePair<float, Box>>();
            foreach (Box b in _boxes)
            {
                float d = (b.Pos - center).sqrMagnitude;
                if (d > r2 || !Eligible(b, me)) continue;
                found.Add(new KeyValuePair<float, Box>(d, b));
            }
            found.Sort(delegate(KeyValuePair<float, Box> a, KeyValuePair<float, Box> z) { return a.Key.CompareTo(z.Key); });
            foreach (KeyValuePair<float, Box> kv in found)
            {
                Box b = kv.Value;
                s.Boxes.Add(b);
                if (b.Dirty || b.WorldLevel != Game.m_worldLevel) b.Recount();
                foreach (KeyValuePair<string, int> c in b.Counts)
                {
                    int n;
                    s.Total.TryGetValue(c.Key, out n);
                    s.Total[c.Key] = n + c.Value;
                }
            }
            return s;
        }

        // ------------------------------------------------------------------
        // which chests to take from
        // ------------------------------------------------------------------
        // The chests that cover the deficits: the ones already ours first (no request needed),
        // then the nearest. Null when everything eligible together does not cover them.
        private static List<Box> Plan(Stock s, Dictionary<string, int> deficits, HashSet<Box> skip)
        {
            Dictionary<string, int> left = new Dictionary<string, int>(deficits);
            List<string> names = new List<string>(deficits.Keys);
            List<Box> plan = new List<Box>();
            int remaining = 0;
            foreach (int v in left.Values) remaining += Math.Max(0, v);
            if (remaining == 0) return plan;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Box b in s.Boxes)
                {
                    if (!b.Alive || (skip != null && skip.Contains(b))) continue;
                    if ((pass == 0) != b.Owned) continue;
                    bool used = false;
                    foreach (string name in names)
                    {
                        int want = left[name];
                        if (want <= 0) continue;
                        int have = b.Count(name);
                        if (have <= 0) continue;
                        int take = Math.Min(want, have);
                        left[name] = want - take;
                        remaining -= take;
                        used = true;
                    }
                    if (used) plan.Add(b);
                    if (remaining <= 0) return plan;
                }
            }
            return null;
        }

        // What the chests of a plan really hold now (we own them, so their inventories are
        // the truth), against the deficits.
        private static bool LiveCovers(List<Box> plan, Dictionary<string, int> deficits)
        {
            foreach (KeyValuePair<string, int> d in deficits)
            {
                if (d.Value <= 0) continue;
                int have = 0;
                foreach (Box b in plan)
                {
                    if (!b.Owned) continue;
                    have += b.Inv.CountItems(d.Key);
                    if (have >= d.Value) break;
                }
                if (have < d.Value) return false;
            }
            return true;
        }

        // Remove up to `amount` of an item from the chests (all must be ours). The game saves
        // each chest to the world as its inventory changes.
        private int Take(List<Box> boxes, string name, int amount)
        {
            int taken = 0;
            foreach (Box b in boxes)
            {
                if (amount - taken <= 0) break;
                if (!b.Owned) continue;
                int have = b.Inv.CountItems(name);
                if (have <= 0) continue;
                int n = Math.Min(have, amount - taken);
                b.Inv.RemoveItem(name, n);
                taken += n;
                Debug("took " + n + " " + name + " from " + b.Prefab + " at " + Pos(b.Pos));
            }
            return taken;
        }

        private static string Pos(Vector3 p)
        {
            return "(" + F(p.x) + ", " + F(p.y) + ", " + F(p.z) + ")";
        }

        private static string Shortfall(Dictionary<string, int> deficits, Stock s)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> d in deficits)
            {
                int missing = d.Value - s.Count(d.Key);
                if (missing > 0) parts.Add(ItemName(d.Key) + " x" + missing);
            }
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Container), "Awake")]
        private static class Container_Awake_Patch
        {
            private static void Postfix(Container __instance)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.Register(__instance); } catch (Exception e) { p.Fail("Container.Awake", e); }
            }
        }
    }
}
