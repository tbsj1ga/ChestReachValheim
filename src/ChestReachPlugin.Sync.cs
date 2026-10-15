using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // The server's settings on every client with the mod, so that everybody plays by the
    // host's ranges and switches. One routed RPC, sent to a peer a moment after its login and
    // to everybody when a setting changes on the server; the client keeps the values beside
    // its own file and uses them while connected. A server without the mod sends nothing and
    // the client's own file applies. Nobody is ever refused for not having the mod.
    public partial class ChestReachPlugin
    {
        private const string RpcName = "j1ga.chestreach.config";
        private const int PacketVersion = 1;

        private bool _serverSynced;
        private readonly Dictionary<ConfigEntryBase, object> _serverValues = new Dictionary<ConfigEntryBase, object>();

        // set by any config change; handled once per frame in Update
        private bool _syncDirty;

        private void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(RpcName, new Action<long, ZPackage>(OnConfigPacket));
        }

        private void FlushSync()
        {
            _syncDirty = false;
            _indexVersion++;
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || !_cfgSync.Value || ZRoutedRpc.instance == null) return;
            if (znet.GetPeers().Count == 0) return;
            SendConfig(ZRoutedRpc.Everybody);
            Debug("settings sent to " + znet.GetPeers().Count + " peers");
        }

        // ------------------------------------------------------------------
        // server
        // ------------------------------------------------------------------
        private ZPackage BuildPacket()
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(PacketVersion);
            pkg.Write(_synced.Count);
            foreach (ConfigEntryBase e in _synced)
            {
                pkg.Write(e.Definition.Section + "/" + e.Definition.Key);
                pkg.Write(e.GetSerializedValue());
            }
            return pkg;
        }

        private void SendConfig(long target)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcName, new object[] { BuildPacket() });
        }

        private void OnPeerInfo(ZNet znet, ZRpc rpc)
        {
            if (!znet.IsServer() || !_cfgSync.Value || rpc == null) return;
            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (peer == null || peer.m_rpc != rpc) continue;
                if (peer.m_uid != 0L) StartCoroutine(SendConfigLater(peer.m_uid));
                return;
            }
        }

        private IEnumerator SendConfigLater(long uid)
        {
            yield return new WaitForSeconds(1f);
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || ZRoutedRpc.instance == null || !_cfgSync.Value) yield break;
            ZNetPeer peer = znet.GetPeer(uid);
            if (peer == null) yield break;
            try
            {
                SendConfig(uid);
                Debug("settings sent to " + peer.m_playerName);
            }
            catch (Exception e)
            {
                Fail("send config", e);
            }
        }

        // ------------------------------------------------------------------
        // client
        // ------------------------------------------------------------------
        private void OnConfigPacket(long sender, ZPackage pkg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || znet.IsServer() || pkg == null) return;
                ZNetPeer server = znet.GetServerPeer();
                if (server == null || sender != server.m_uid)
                {
                    Logger.LogWarning("Settings packet from peer " + sender + ", which is not the server; ignored.");
                    return;
                }
                int version = pkg.ReadInt();
                if (version != PacketVersion)
                {
                    Logger.LogWarning("The server runs a " + Name + " with packet version " + version + ", this one understands " + PacketVersion + "; using the local settings.");
                    return;
                }
                Dictionary<string, ConfigEntryBase> byName = new Dictionary<string, ConfigEntryBase>();
                foreach (ConfigEntryBase e in _synced) byName[e.Definition.Section + "/" + e.Definition.Key] = e;

                int n = pkg.ReadInt();
                if (n < 0 || n > 1000) return;
                Dictionary<ConfigEntryBase, object> values = new Dictionary<ConfigEntryBase, object>();
                for (int i = 0; i < n; i++)
                {
                    string name = pkg.ReadString();
                    string text = pkg.ReadString();
                    ConfigEntryBase entry;
                    if (!byName.TryGetValue(name, out entry)) continue;
                    try { values[entry] = TomlTypeConverter.ConvertToValue(text, entry.SettingType); }
                    catch (Exception) { Logger.LogWarning("Server setting " + name + " = '" + text + "' could not be read; the local value applies."); }
                }

                _serverValues.Clear();
                foreach (KeyValuePair<ConfigEntryBase, object> kv in values) _serverValues[kv.Key] = kv.Value;
                object excluded;
                ReadList(_serverValues.TryGetValue(_cfgExcluded, out excluded) ? excluded as string : "", _serverExcluded);
                _serverSynced = true;
                _indexVersion++;
                Logger.LogInfo("Settings from the server: " + StatusValues());
            }
            catch (Exception e)
            {
                Fail("config packet", e);
            }
        }

        private void ClearServerValues()
        {
            _serverSynced = false;
            _serverValues.Clear();
            _serverExcluded.Clear();
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_Patch
        {
            private static void Postfix()
            {
                ChestReachPlugin p = Instance;
                if (p == null) return;
                try { p.RegisterRpc(); } catch (Exception e) { p.Fail("ZNet.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZNet_RPC_PeerInfo_Patch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.OnPeerInfo(__instance, rpc); } catch (Exception e) { p.Fail("ZNet.RPC_PeerInfo", e); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class ZNet_OnDestroy_Patch
        {
            private static void Postfix()
            {
                ChestReachPlugin p = Instance;
                if (p == null) return;
                try
                {
                    p.StopAllCoroutines();
                    p.ResetWorld();
                }
                catch (Exception e) { p.Fail("ZNet.OnDestroy", e); }
            }
        }
    }
}
