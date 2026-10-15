using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ChestReach
{
    // Crafting, building and station use with materials from the chests around you.
    //
    // Everything goes through what the game already does, so nothing new travels over the
    // network and a player without the mod, the host and a dedicated server need nothing:
    //
    //   * To take from a chest that someone else owns, the mod sends that owner the vanilla
    //     "open" request (Container.RPC_RequestOpen) - the one pressing E sends. The owner's
    //     game, modded or not, hands the chest over exactly as for opening it, unless it is in
    //     use or private. The answer is caught before it opens the chest window; once the
    //     chest is ours, the items are removed from its inventory and the game saves the chest
    //     to the world and to everyone, as when you take something by hand.
    //   * Stations get their fuel, ore or food through their own vanilla code: one item is
    //     moved from a chest into your inventory just before the station looks for it, and
    //     moved back if the station turned it down.
    //
    // Nothing is stored outside the world: the mod can be removed at any time.
    //
    // Speed: the chests in the loaded area are indexed once, and the item counts of a chest
    // are recounted only when that chest's inventory changes (ChestReachPlugin.Index.cs). A
    // requirement row or a piece in the build menu is a dictionary lookup.
    [BepInPlugin(Guid, Name, Version)]
    public partial class ChestReachPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.chestreach";
        public const string Name = "ChestReach";
        public const string Version = "0.1.0";

        // Harmony patches are static; they reach the running plugin through this.
        public static ChestReachPlugin Instance;

        private Harmony _harmony;

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        // ------------------------------------------------------------------
        // lifecycle
        // ------------------------------------------------------------------
        private void Awake()
        {
            try
            {
                Instance = this;
                BindConfig();
                FindReflection();
                RegisterCommands();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(ChestReachPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Logger.LogError("Awake failed, mod is inert: " + e);
                _disabledByErrors = true;
            }
        }

        private void Update()
        {
            if (_disabledByErrors) return;
            try
            {
                if (_syncDirty) FlushSync();
                if (Player.m_localPlayer == null) return;
                UpdateOwnership();
                UpdateStationRetry();
                UpdateBuildPrefetch();
                UpdateAreaRepair();
            }
            catch (Exception e)
            {
                Fail("Update", e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                StopAllCoroutines();
                if (_harmony != null) _harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                Logger.LogWarning("OnDestroy: " + e.Message);
            }
            if (Instance == this) Instance = null;
        }

        // The world is gone: forget every chest, request and setting from the server.
        private void ResetWorld()
        {
            ClearIndex();
            ClearOwnership();
            ClearServerValues();
            _consume = null;
            _craftHold = false;
            _stationRetry = null;
            _repairQueue.Clear();
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        private bool Ready { get { return !_disabledByErrors && Eff(_cfgEnabled); } }

        private static bool IsLocal(Humanoid h)
        {
            return h != null && h == Player.m_localPlayer;
        }

        private static long MyPlayerId()
        {
            Game g = Game.instance;
            return g != null && g.GetPlayerProfile() != null ? g.GetPlayerProfile().GetPlayerID() : 0L;
        }

        private static string PrefabName(GameObject go)
        {
            if (go == null) return "";
            string n = go.name;
            int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        private static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void Debug(string text)
        {
            if (_cfgDebug.Value) Logger.LogInfo(text);
        }

        // A message in the middle of the screen, at most once a second for the same text.
        private string _lastMessage;
        private float _lastMessageTime;

        private void Tell(string text)
        {
            Player p = Player.m_localPlayer;
            if (p == null || string.IsNullOrEmpty(text)) return;
            if (text == _lastMessage && Time.time - _lastMessageTime < 1f) return;
            _lastMessage = text;
            _lastMessageTime = Time.time;
            p.Message(MessageHud.MessageType.Center, text);
        }

        // Errors inside patches must never take the game down with them: log each distinct
        // message once, and after too many of them switch the mod off entirely.
        private void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
            if (++_errorCount >= MaxErrors && !_disabledByErrors)
            {
                _disabledByErrors = true;
                _consume = null;
                Logger.LogError("Too many errors, " + Name + " is now inert until the game restarts.");
            }
        }

        // ------------------------------------------------------------------
        // texts: English, or Russian when the game is in Russian
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            { "fetching", new[] { "Taking materials from the chests nearby... press again", "Беру материалы из сундуков рядом... нажмите ещё раз" } },
            { "waiting", new[] { "Waiting for the chests nearby...", "Жду сундуки рядом..." } },
            { "busy", new[] { "A chest nearby is in use or did not answer", "Сундук рядом занят или не ответил" } },
            { "short", new[] { "Not enough in the chests nearby: {0}", "Не хватает в сундуках рядом: {0}" } },
            { "invfull", new[] { "No room in the inventory for {0}", "Нет места в инвентаре для {0}" } },
            { "repaired", new[] { "Items repaired: {0}", "Починено предметов: {0}" } },
            { "area", new[] { "Pieces repaired: {0}", "Починено построек: {0}" } },
            { "area_none", new[] { "Nothing to repair nearby", "Рядом нечего чинить" } },
            { "area_station", new[] { "; {0} need a crafting station nearby", "; {0} требуют станцию рядом" } },
            { "taken", new[] { "From the chests: {0}", "Из сундуков: {0}" } },
            { "tooltip", new[] { "In inventory: {0} · In chests nearby: {1}", "В инвентаре: {0} · В сундуках рядом: {1}" } },
        };

        private static bool Russian()
        {
            Localization loc = Localization.instance;
            return loc != null && loc.GetSelectedLanguage() == "Russian";
        }

        private static string T(string key, params object[] args)
        {
            string[] t;
            if (!Texts.TryGetValue(key, out t)) return key;
            string s = Russian() ? t[1] : t[0];
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }

        private static string ItemName(string sharedName)
        {
            Localization loc = Localization.instance;
            return loc != null ? loc.Localize(sharedName) : sharedName;
        }
    }
}
