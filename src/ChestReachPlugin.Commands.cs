using System;
using System.Text;
using UnityEngine;

namespace ChestReach
{
    public partial class ChestReachPlugin
    {
        // ------------------------------------------------------------------
        // console command: chestreach
        // ------------------------------------------------------------------
        private void RegisterCommands()
        {
            new Terminal.ConsoleCommand("chestreach",
                "ChestReach. 'chestreach status' shows the settings in effect and the chests around you",
                delegate(Terminal.ConsoleEventArgs args) { RunCommand(args); });
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }

        private void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "";
                if (sub == "status") { Say(args, StatusText()); return; }
                Say(args, "chestreach status");
            }
            catch (Exception e)
            {
                Say(args, "chestreach: " + e.Message);
                Fail("command", e);
            }
        }

        private static string OnOff(bool v) { return v ? "on" : "off"; }

        // The settings in effect, in one line.
        private string StatusValues()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("enabled ").Append(OnOff(Eff(_cfgEnabled)))
              .Append("; crafting ").Append(OnOff(Eff(_cfgCraft))).Append(" ").Append(F(CraftRange)).Append(" m")
              .Append("; building ").Append(OnOff(Eff(_cfgBuild))).Append(" ").Append(F(BuildRange)).Append(" m")
              .Append("; stations ").Append(F(StationRange)).Append(" m: refuel ").Append(OnOff(Eff(_cfgRefuel)))
              .Append(", ore ").Append(OnOff(Eff(_cfgLoad))).Append(", cook ").Append(OnOff(Eff(_cfgCook)))
              .Append("; carts ").Append(OnOff(Eff(_cfgCarts))).Append(", ships ").Append(OnOff(Eff(_cfgShips)))
              .Append(", world chests ").Append(OnOff(Eff(_cfgDungeon))).Append(", ward for all ").Append(OnOff(Eff(_cfgWardAll)))
              .Append("; excluded ").Append(Excluded.Count)
              .Append("; repair all ").Append(OnOff(Eff(_cfgRepairAll)))
              .Append(", area repair ").Append(OnOff(Eff(_cfgAreaRepair))).Append(" ").Append(F(Eff(_cfgAreaRepairRadius))).Append(" m.");
            return sb.ToString();
        }

        private string StatusText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Name).Append(" ").Append(Version).Append(_disabledByErrors ? " - DISABLED after errors, see the log" : "").Append('\n');
            sb.Append(_serverSynced ? "Settings from the server: " : "Local settings: ").Append(StatusValues()).Append('\n');
            Player p = Player.m_localPlayer;
            if (p != null)
            {
                Prune();
                Stock s = BuildStock(p.transform.position, BuildRange);
                int owned = 0;
                foreach (Box b in s.Boxes) if (b.Owned) owned++;
                sb.Append("Chests loaded: ").Append(_boxes.Count)
                  .Append("; within ").Append(F(BuildRange)).Append(" m of you and usable: ").Append(s.Boxes.Count)
                  .Append(" (yours now: ").Append(owned).Append("), ").Append(s.Total.Count).Append(" kinds of items")
                  .Append("; requests in flight: ").Append(PendingRequests).Append('.');
            }
            return sb.ToString();
        }
    }
}
