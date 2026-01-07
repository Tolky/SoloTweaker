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

        internal static ConfigEntry<float> SoloAttackSpeedPercent = null!;
        internal static ConfigEntry<float> SoloDamagePercent      = null!;
        internal static ConfigEntry<float> SoloHealthPercent;
        internal static ConfigEntry<float> SoloMoveSpeedPercent;
        internal static ConfigEntry<int>   SoloHealthBonus        = null!;
        internal static ConfigEntry<float> SoloMoveSpeedBonus     = null!;
        internal static ConfigEntry<float> SoloYieldMultiplier    = null!;
        internal static ConfigEntry<bool>  SoloNoCooldown         = null!;
        internal static ConfigEntry<bool>  SoloSunInvulnerable    = null!;
        internal static ConfigEntry<int>   SoloClanOfflineThresholdMinutes = null!;
        internal static ConfigEntry<float> SoloSpellDamagePercent;


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
            SoloAttackSpeedPercent = Config.Bind(
                "Solo Buffs",
                "AttackSpeedPercent",
                0.10f,
                "Attack speed bonus when solo (0.10 = +10%).");

            SoloDamagePercent = Config.Bind(
                "Solo Buffs",
                "DamagePercent",
                0.10f,
                "Damage bonus when solo (0.10 = +10% to physical and spell power).");

            SoloSpellDamagePercent = Config.Bind<float>(
                "Solo Buffs",
                "SpellDamagePercent",
                0.10f,
                "Spell damage bonus when solo (0.10 = +10% to spell damage only)."
            );

            SoloHealthPercent = Config.Bind<float>(
                "Solo Buffs",
                "HealthPercent",
                0.10f,
                "Max health bonus when solo (0.10 = +10% max HP). 0 = no health buff."
            );

            SoloMoveSpeedPercent = Config.Bind<float>(
                "Solo Buffs",
                "MoveSpeedPercent",
                0.10f,
                "Move speed bonus when solo (0.10 = +10% move speed). 0 = no move speed buff."
            );

            SoloHealthBonus = Config.Bind(
                "Solo Buffs",
                "HealthBonus",
                0,
                "Extra health when solo. 0 = no health buff. (currently unused by buff service)");

            SoloMoveSpeedBonus = Config.Bind(
                "Solo Buffs",
                "MoveSpeedBonus",
                0f,
                "Move speed bonus when solo (0.10 = +10%). 0 = no speed buff. (currently unused)");

            SoloYieldMultiplier = Config.Bind(
                "Solo Buffs",
                "YieldMultiplier",
                1f,
                "Resource yield multiplier when solo. 1.0 = no change, 1.5 = +50%. (currently unused)");

            SoloNoCooldown = Config.Bind(
                "Solo Buffs",
                "NoCooldown",
                false,
                "If true, solo players get no cooldowns. (currently unused)");

            SoloSunInvulnerable = Config.Bind(
                "Solo Buffs",
                "SunInvulnerable",
                false,
                "If true, solo players are sun-invulnerable. (currently unused)");

            SoloClanOfflineThresholdMinutes = Config.Bind(
                "Solo Buffs",
                "ClanOfflineThresholdMinutes",
                30,
                "Minutes other clan members must have been offline before you are treated as solo while in a clan. 0 = no delay.");
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
