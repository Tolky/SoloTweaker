using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using VampireCommandFramework;
//plugin
namespace SoloTweaker
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid    = "solotweaker";
        public const string PluginName    = "SoloTweaker";
        public const string PluginVersion = "2.2.0";

        internal static Plugin? Instance;

        // Combat Stats
        internal static ConfigEntry<float> SoloAttackSpeedPercent = null!;
        internal static ConfigEntry<float> SoloDamagePercent      = null!;
        internal static ConfigEntry<float> SoloSpellDamagePercent = null!;
        internal static ConfigEntry<float> SoloCritChancePercent  = null!;
        internal static ConfigEntry<float> SoloCritDamagePercent  = null!;

        // Survivability
        internal static ConfigEntry<float> SoloHealthPercent        = null!;
        internal static ConfigEntry<float> SoloPhysicalLeechPercent = null!;
        internal static ConfigEntry<float> SoloSpellLeechPercent    = null!;

        // Mobility & Utility
        internal static ConfigEntry<float> SoloMoveSpeedPercent     = null!;
        internal static ConfigEntry<float> SoloResourceYieldPercent = null!;

        // Clan Settings
        internal static ConfigEntry<int> SoloClanOfflineThresholdMinutes = null!;


        public override void Load()
        {
            Instance = this;
            BindConfig();

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<SoloTweakerBehaviour>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<SoloTweakerBehaviour>();
            }

            var go = new GameObject("SoloTweakerBehaviour");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<SoloTweakerBehaviour>();

            CommandRegistry.RegisterAll();

            bool enabled;
            var handler = new BepInExInfoLogInterpolatedStringHandler(11, 2, out enabled);
            if (enabled)
            {
                handler.AppendLiteral("[");
                handler.AppendFormatted(PluginName);
                handler.AppendLiteral("] ");
                handler.AppendFormatted(PluginVersion);
                handler.AppendLiteral(" loaded.");
            }
            Log.LogInfo(handler);

        }

        private void BindConfig()
        {
            // ===== COMBAT STATS =====
            SoloAttackSpeedPercent = Config.Bind(
                "1. Combat Stats",
                "AttackSpeedPercent",
                0.10f,
                "Attack speed bonus when solo (0.10 = +10% attack speed). Set to 0 to disable.");

            SoloDamagePercent = Config.Bind(
                "1. Combat Stats",
                "PhysicalDamagePercent",
                0.10f,
                "Physical damage bonus when solo (0.10 = +10% physical power). Set to 0 to disable.");

            SoloSpellDamagePercent = Config.Bind(
                "1. Combat Stats",
                "SpellDamagePercent",
                0.10f,
                "Spell damage bonus when solo (0.10 = +10% spell power). Set to 0 to disable.");

            SoloCritChancePercent = Config.Bind(
                "1. Combat Stats",
                "CritChancePercent",
                0.10f,
                "Critical strike chance multiplier when solo (0.10 = 10% more crit chance for both physical and spells, e.g., 50% base becomes 55%). Set to 0 to disable.");

            SoloCritDamagePercent = Config.Bind(
                "1. Combat Stats",
                "CritDamagePercent",
                0.10f,
                "Critical strike damage multiplier when solo (0.10 = 10% more crit damage for both physical and spells). Set to 0 to disable.");

            // ===== SURVIVABILITY =====
            SoloHealthPercent = Config.Bind(
                "2. Survivability",
                "HealthPercent",
                0.10f,
                "Max health bonus when solo (0.10 = +10% max HP). Set to 0 to disable.");

            SoloPhysicalLeechPercent = Config.Bind(
                "2. Survivability",
                "PhysicalLeechPercent",
                0.10f,
                "Physical lifesteal when solo (0.10 = 10% lifesteal on basic attacks and physical abilities). Set to 0 to disable.");

            SoloSpellLeechPercent = Config.Bind(
                "2. Survivability",
                "SpellLeechPercent",
                0.10f,
                "Spell lifesteal when solo (0.10 = 10% lifesteal on spells). Set to 0 to disable.");

            // ===== MOBILITY & UTILITY =====
            SoloMoveSpeedPercent = Config.Bind(
                "3. Mobility & Utility",
                "MoveSpeedPercent",
                0.10f,
                "Move speed bonus when solo (0.10 = +10% move speed). Set to 0 to disable.");

            SoloResourceYieldPercent = Config.Bind(
                "3. Mobility & Utility",
                "ResourceYieldPercent",
                0.10f,
                "Resource gathering bonus when solo (0.10 = +10% more resources from gathering). Set to 0 to disable.");

            // ===== CLAN SETTINGS =====
            SoloClanOfflineThresholdMinutes = Config.Bind(
                "4. Clan Settings",
                "ClanOfflineThresholdMinutes",
                30,
                "Minutes other clan members must be offline before you are treated as solo while in a clan. Set to 0 for instant solo status when clanmates go offline.");
        }

        internal static void ReloadConfig()
        {
            if (Instance == null)
                return;

            Instance.Config.Reload();
            Instance.Config.Save();
            Instance.Log.LogInfo("[SoloTweaker] Config reloaded from disk.");

            // Clear all existing buffs so they can be reapplied with new config values
            SoloBuffLogic.ClearAllBuffs();
        }
    }
}
