using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Getting a chest that somebody else owns.
    //
    // Only the owner of a chest may change it. The mod asks for it exactly as pressing E
    // does: Container.RPC_RequestOpen to the owner, whose game - with or without this mod -
    // checks that the chest is not open and not private and then makes us the owner
    // (ZDO.SetOwner) and answers RPC_OpenResponse(true). That answer is caught here before it
    // opens the chest window. The new owner arrives with the ZDO a moment later; once
    // View.IsOwner() is true the chest's inventory is reloaded from the world (it may be up to
    // a second behind) and the chest is ready. A refusal ("in use") or no answer in time
    // leaves the chest alone for a few seconds; callers then plan without it.
    public partial class ChestReachPlugin
    {
        internal enum Own { Ready, Pending, Failed }

        private sealed class Request
        {
            public Box B;
            public float Sent;
            public bool Granted;
            public bool Denied;
            public bool Manual;
        }

        private const float RequestTimeout = 3f;
        private const float RefusedPause = 5f;

        private readonly Dictionary<Container, Request> _requests = new Dictionary<Container, Request>();
        private readonly Dictionary<Box, float> _refusedUntil = new Dictionary<Box, float>();

        // Chests the player opened by hand a moment ago: their answer must open the window.
        private readonly Dictionary<Container, float> _manualAt = new Dictionary<Container, float>();

        private bool OpenedByHand(Container c)
        {
            float t;
            return _manualAt.TryGetValue(c, out t) && Time.time - t < 2f;
        }

        private Own Acquire(Box b)
        {
            if (b == null || !b.Alive) return Own.Failed;
            Request r;
            _requests.TryGetValue(b.C, out r);
            if (b.View.IsOwner())
            {
                // granted and arrived: make sure the inventory is the world's latest
                if (r != null)
                {
                    _requests.Remove(b.C);
                    Reload(b);
                }
                return Own.Ready;
            }
            if (r != null) return r.Denied ? Own.Failed : Own.Pending;
            if (OpenedByHand(b.C)) return Own.Pending;

            float until;
            if (_refusedUntil.TryGetValue(b, out until))
            {
                if (Time.time < until) return Own.Failed;
                _refusedUntil.Remove(b);
            }

            ZDO zdo = b.View.GetZDO();
            if (zdo == null) return Own.Failed;
            if (zdo.GetOwner() == 0L)
            {
                // nobody's: take it, as the game does for a chest without an owner
                b.View.ClaimOwnership();
                if (b.View.IsOwner())
                {
                    Reload(b);
                    return Own.Ready;
                }
            }

            r = new Request();
            r.B = b;
            r.Sent = Time.time;
            _requests[b.C] = r;
            b.View.InvokeRPC("RPC_RequestOpen", MyPlayerId());
            Debug("asked owner " + zdo.GetOwner() + " for " + b.Prefab + " at " + Pos(b.Pos));
            return Own.Pending;
        }

        private void Reload(Box b)
        {
            try { _containerLoad.Invoke(b.C, null); }
            catch (Exception e) { Fail("Container.Load", e); }
            b.Dirty = true;
            _indexVersion++;
        }

        // Once a frame: requests that were answered, arrived or timed out.
        private void UpdateOwnership()
        {
            if (_manualAt.Count > 32) _manualAt.Clear();
            if (_requests.Count == 0) return;
            List<Container> done = null;
            foreach (KeyValuePair<Container, Request> kv in _requests)
            {
                Request r = kv.Value;
                Box b = r.B;
                bool finished = false;
                if (!b.Alive) finished = true;
                else if (b.View.IsOwner())
                {
                    Reload(b);
                    Debug("got " + b.Prefab + " at " + Pos(b.Pos) + " after " + F(Time.time - r.Sent) + " s");
                    finished = true;
                }
                else if (r.Denied || Time.time - r.Sent > RequestTimeout)
                {
                    _refusedUntil[b] = Time.time + RefusedPause;
                    Debug((r.Denied ? "refused: " : "no answer: ") + b.Prefab + " at " + Pos(b.Pos));
                    finished = true;
                }
                if (finished)
                {
                    if (done == null) done = new List<Container>();
                    done.Add(kv.Key);
                }
            }
            if (done != null) foreach (Container c in done) _requests.Remove(c);
        }

        private int PendingRequests { get { return _requests.Count; } }

        private void ClearOwnership()
        {
            _requests.Clear();
            _refusedUntil.Clear();
            _manualAt.Clear();
        }

        // All chests of a plan: Ready when every one is ours, Failed as soon as one is
        // refused, else Pending. Asks for each one that is not ours yet.
        private Own AcquireAll(List<Box> plan, HashSet<Box> failed)
        {
            Own all = Own.Ready;
            foreach (Box b in plan)
            {
                Own o = Acquire(b);
                if (o == Own.Failed)
                {
                    if (failed != null) failed.Add(b);
                    all = Own.Failed;
                }
                else if (o == Own.Pending && all == Own.Ready) all = Own.Pending;
            }
            return all;
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        // The owner's answer to our request: keep the chest window shut. A request the player
        // made by hand meanwhile (pressing E on the same chest) gets its window as usual.
        [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
        private static class Container_RPC_OpenResponse_Patch
        {
            private static bool Prefix(Container __instance, bool granted)
            {
                ChestReachPlugin p = Instance;
                if (p == null) return true;
                try
                {
                    Request r;
                    if (!p._requests.TryGetValue(__instance, out r)) return true;
                    if (granted) r.Granted = true;
                    else r.Denied = true;
                    return r.Manual || p.OpenedByHand(__instance);
                }
                catch (Exception e)
                {
                    p.Fail("Container.RPC_OpenResponse", e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Container), "Interact")]
        private static class Container_Interact_Patch
        {
            private static void Prefix(Container __instance, bool hold)
            {
                ChestReachPlugin p = Instance;
                if (p == null || hold) return;
                p._manualAt[__instance] = Time.time;
                Request r;
                if (p._requests.TryGetValue(__instance, out r)) r.Manual = true;
            }
        }
    }
}
