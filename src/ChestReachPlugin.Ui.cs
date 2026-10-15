using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ChestReach
{
    // The requirement rows of a recipe (crafting panel) and of a piece (build menu):
    // InventoryGui.SetupRequirement. An amount the inventory alone does not cover but the
    // chests nearby do is drawn in its own colour instead of blinking red; the tooltip tells
    // how many are carried and how many are in the chests around.
    public partial class ChestReachPlugin
    {
        // Which panel is filling rows right now: the build menu (Hud.SetupPieceInfo) or the
        // crafting panel (InventoryGui.SetupRequirementList).
        private bool _rowsForBuild;

        private void AfterSetupRequirement(Transform root, Piece.Requirement req, Player player, int quality, int multiplier)
        {
            if (root == null || req == null || req.m_resItem == null || !IsLocal(player)) return;
            bool build = _rowsForBuild;
            if (build ? !BuildOn : !CraftOn) return;
            int need = req.GetAmount(quality) * multiplier;
            if (need <= 0) return;
            string name = req.m_resItem.m_itemData.m_shared.m_name;
            int carried = player.GetInventory().CountItems(name);
            Stock s = build ? GetStock(player.transform.position, BuildRange) : GetStock(CraftCenter(player), CraftRange);
            int nearby = s.Count(name);

            Transform amount = root.Find("res_amount");
            TMP_Text text = amount != null ? amount.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                if (_cfgShowTotal.Value)
                {
                    FitOnOneLine(text);
                    text.text = Short(carried + nearby) + "/" + need;
                }
                if (carried < need && carried + nearby >= need) text.color = _cfgCoveredColor.Value;
            }
            if (_cfgTooltip.Value && nearby > 0)
            {
                UITooltip tip = root.GetComponent<UITooltip>();
                if (tip != null) tip.m_text = (tip.m_text ?? "") + "\n" + T("tooltip", carried, nearby);
            }
        }

        // The amount field is made for "10": "4679/40" wrapped onto a second line that the
        // field cuts off. One line, and the font shrinks to fit (down to 60 %), as TMP does
        // by itself with auto-size; the vanilla size stays the largest.
        private static void FitOnOneLine(TMP_Text text)
        {
            if (text.enableAutoSizing) return;
            float size = text.fontSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Max(6f, size * 0.6f);
            text.enableAutoSizing = true;
        }

        // Numbers past 9999 are shortened, so that the font does not shrink too far.
        private static string Short(int n)
        {
            if (n < 10000) return n.ToString();
            if (n < 1000000) return (n / 1000) + "k";
            return (n / 1000000) + "M";
        }

        [HarmonyPatch(typeof(InventoryGui), "SetupRequirement")]
        private static class InventoryGui_SetupRequirement_Patch
        {
            private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool __result)
            {
                ChestReachPlugin p = Instance;
                if (p == null || p._disabledByErrors || !__result) return;
                try { p.AfterSetupRequirement(elementRoot, req, player, quality, craftMultiplier); }
                catch (Exception e) { p.Fail("InventoryGui.SetupRequirement", e); }
            }
        }

        [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
        private static class Hud_SetupPieceInfo_Patch
        {
            private static void Prefix()
            {
                ChestReachPlugin p = Instance;
                if (p != null) p._rowsForBuild = true;
            }

            private static Exception Finalizer(Exception __exception)
            {
                ChestReachPlugin p = Instance;
                if (p != null) p._rowsForBuild = false;
                return __exception;
            }
        }
    }
}
