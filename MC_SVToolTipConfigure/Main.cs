using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MC_SVToolTipConfigure
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.tooltipconfigure";
        public const string pluginName = "SV Tooltip Configure";
        public const string pluginVersion = "0.6.0";

        private static ConfigEntry<bool> cfgEnable;
        private static ConfigEntry<float> cfgDelay;
        private static ConfigEntry<bool> cfgKeyMode;
        private static ConfigEntry<KeyCodeSubset> cfgShowKey;
        private static Dictionary<Tooltip, CallProperties> delayedTTs = new Dictionary<Tooltip, CallProperties>();
        private static bool keyMode = false;

        private static ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("MC_SVTooltipConfigure");

        public void Awake()
        {
            BindConfig();
            Harmony.CreateAndPatchAll(typeof(Main));
        }

        private void BindConfig()
        {
            cfgEnable = Config.Bind("1. Enable/Disable",
                "1. Enable tooltips?",
                true,
                "Enable/disable tooltips");
            cfgDelay = Config.Bind("2. Delay Mode",
                "1. Additional tooltip Delay",
                1.0f,
                "Additional tooltip pop-up delay");
            cfgKeyMode = Config.Bind("3. Key Mode",
                "1. Enable key mode?",
                false,
                "Overrides delay mode.  Hold a key to allow tooltips to be shown.  This is not a toggle, hold a key and tooltips will behave as normal.  Releaseing the key will not dispose of any active tooltips.");
            keyMode = cfgKeyMode.Value;
            keyMode = true;
            cfgShowKey = Config.Bind("3. Key Mode",
                "2. Key bind",
                KeyCodeSubset.LeftAlt,
                "The key which allows tooltips to appear.");
        }

        public void Update()
        {
            if (!cfgEnable.Value)
                return;

            if (!cfgKeyMode.Value)
            {
                keyMode = false;
                UpdateDelayMode();
            }
            else if (keyMode == false)
            {
                delayedTTs.Clear();
                keyMode = true;
            }
        }

        private void UpdateDelayMode()
        {
            if (cfgDelay.Value < 0f)
                cfgDelay.Value = 0f;

            if (delayedTTs.Count == 0)
                return;

            Tooltip[] delayedTTKeys = new Tooltip[delayedTTs.Keys.Count];
            delayedTTs.Keys.CopyTo(delayedTTKeys, 0);

            for (int i = 0; i < delayedTTKeys.Length; i++)
            {
                Tooltip ttInst = delayedTTKeys[i];
                if (ttInst == null)
                    delayedTTs.Remove(ttInst);
                else if (delayedTTs.TryGetValue(ttInst, out CallProperties callProperties) &&
                    !callProperties.deleteFlag && callProperties.count >= 0 &&
                    (callProperties.count -= Time.deltaTime) <= 0)
                {
                    callProperties.count = -1; // Just in case it is exactly 0
                    callProperties.showMain = true;
                    ttInst.ShowItem(callProperties.pText, callProperties.pBigImage, callProperties.pScreenCenter);
                }
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowItem))]
        [HarmonyPrefix]
        private static bool TooltipShowItem_Pre(Tooltip __instance, string text, bool bigImage, bool screenCenter)
        {
            if (!cfgEnable.Value ||
                (keyMode && !Input.GetKey((KeyCode)cfgShowKey.Value)))
                return false;

            if (keyMode && Input.GetKey((KeyCode)cfgShowKey.Value))
                return true;

            if (!delayedTTs.TryGetValue(__instance, out CallProperties cp))
                // First capture of this tooltip instance
                delayedTTs.Add(__instance, new CallProperties(cfgDelay.Value, text, bigImage, screenCenter));
            else
            {
                // This TT instance has been set inactive and now re-called, so refresh all values
                if (cp.deleteFlag)
                    delayedTTs[__instance] = new CallProperties(cfgDelay.Value, text, bigImage, screenCenter);

                // Timer elapsed, show main tool tip and indicate extras to be shown
                else if (cp.showMain)
                    return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowItem))]
        [HarmonyPostfix]
        private static void TooltipShowItem_Post(Tooltip __instance)
        {
            // Show extras must be called after show item
            if (cfgEnable.Value && !keyMode &&
                delayedTTs.TryGetValue(__instance, out CallProperties callProperties))
                if (callProperties.showMain)
                    if (callProperties.hasExtras)
                        __instance.ShowExtras(callProperties.pText1, callProperties.pText2, callProperties.pLinebreak);
                    else
                        callProperties.showMain = false;
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowExtras), new System.Type[] { typeof(string), typeof(string), typeof(string) })]
        [HarmonyPrefix]
        private static bool TooltipShowExtras_Pre(Tooltip __instance, string text1, string text2, string lineBreak, Text ___extraText1, Text ___extraText2, Text ___mainText)
        {
            if (!cfgEnable.Value)
                return false;

            if (keyMode && !Input.GetKey((KeyCode)cfgShowKey.Value))
                return false;

            if (delayedTTs.TryGetValue(__instance, out CallProperties callProperties) &&
                !callProperties.showMain)
            {
                callProperties.hasExtras = true;
                callProperties.pText1 = text1;
                callProperties.pText2 = text2;
                callProperties.pLinebreak = lineBreak;
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowExtras), new System.Type[] { typeof(string), typeof(string), typeof(string) })]
        [HarmonyPostfix]
        private static void TooltipShowExtras_Post(Tooltip __instance)
        {
            if (!keyMode && delayedTTs.TryGetValue(__instance, out CallProperties callProperties))
                callProperties.showMain = false;
        }

        [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
        [HarmonyPrefix]
        private static void GOSetActive_Pre(GameObject __instance, bool value)
        {
            if (!value && cfgEnable.Value && !keyMode && delayedTTs.Count > 0)
            {
                Tooltip tooltip = __instance.GetComponent<Tooltip>();
                if (tooltip != null && delayedTTs.TryGetValue(tooltip, out CallProperties callProperties))
                    callProperties.deleteFlag = true;
            }
        }
    }

    internal class CallProperties
    {
        public float count;
        public string pText;
        public bool pBigImage;
        public bool pScreenCenter;
        public bool showMain;

        public bool hasExtras;
        public string pText1;
        public string pText2;
        public string pLinebreak;

        public bool deleteFlag;

        public CallProperties(float count, string text, bool bigImage, bool screenCenter)
        {
            this.count = count;
            this.pText = text;
            this.pBigImage = bigImage;
            this.pScreenCenter = screenCenter;
            this.showMain = false;
            this.hasExtras = false;
            this.deleteFlag = false;
        }
    }
}