using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;

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

        HookDOTS.API.HookDOTS? _hookDots;

        // Combat Stats
        internal static ConfigEntry<float> SoloAttackSpeedPercent = null!;
        internal static ConfigEntry<int>   SoloAttackSpeedType    = null!;
        internal static ConfigEntry<float> SoloDamagePercent      = null!;
        internal static ConfigEntry<int>   SoloDamageType         = null!;
        internal static ConfigEntry<float> SoloSpellDamagePercent = null!;
        internal static ConfigEntry<int>   SoloSpellDamageType    = null!;
        internal static ConfigEntry<float> SoloCritChancePercent  = null!;
        internal static ConfigEntry<int>   SoloCritChanceType     = null!;
        internal static ConfigEntry<float> SoloCritDamagePercent  = null!;
        internal static ConfigEntry<int>   SoloCritDamageType     = null!;

        // Survivability
        internal static ConfigEntry<float> SoloHealthPercent              = null!;
        internal static ConfigEntry<int>   SoloHealthType                 = null!;
        internal static ConfigEntry<float> SoloPhysicalLeechPercent       = null!;
        internal static ConfigEntry<int>   SoloPhysicalLeechType          = null!;
        internal static ConfigEntry<float> SoloSpellLeechPercent          = null!;
        internal static ConfigEntry<int>   SoloSpellLeechType             = null!;
        internal static ConfigEntry<float> SoloPhysicalResistancePercent  = null!;
        internal static ConfigEntry<int>   SoloPhysicalResistanceType     = null!;
        internal static ConfigEntry<float> SoloSpellResistancePercent     = null!;
        internal static ConfigEntry<int>   SoloSpellResistanceType        = null!;

        // Mobility & Utility
        internal static ConfigEntry<float> SoloMoveSpeedPercent     = null!;
        internal static ConfigEntry<int>   SoloMoveSpeedType        = null!;
        internal static ConfigEntry<float> SoloResourceYieldPercent = null!;
        internal static ConfigEntry<int>   SoloResourceYieldType    = null!;

        // Clan Settings
        internal static ConfigEntry<int> SoloClanOfflineThresholdMinutes = null!;

        public override void Load()
        {
            Instance = this;
            BindConfig();

            CommandRegistry.RegisterAll();

            _hookDots = new HookDOTS.API.HookDOTS(PluginGuid, Log);
            _hookDots.RegisterAnnotatedHooks();

            Log.LogInfo($"[{PluginName}] {PluginVersion} loaded.");
        }

        public override bool Unload()
        {
            SoloBuffLogic.ClearAllBuffs();
            SoloBuffLogic.Reset();
            Patches.ConnectionEventPatches.Reset();
            Patches.ClanEventPatches.Reset();

            _hookDots?.Dispose();
            _hookDots = null;

            CommandRegistry.UnregisterAssembly(Assembly.GetExecutingAssembly());

            Instance = null;
            Log.LogInfo($"[{PluginName}] unloaded.");
            return true;
        }

        private void BindConfig()
        {
            const string typeDesc = "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).";

            // ===== COMBAT STATS =====
            SoloAttackSpeedPercent = Config.Bind(
                "1. Combat Stats", "AttackSpeedValue", 0.10f,
                "Attack speed bonus when solo. Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloAttackSpeedType = Config.Bind(
                "1. Combat Stats", "AttackSpeedType", 0, typeDesc);

            SoloDamagePercent = Config.Bind(
                "1. Combat Stats", "PhysicalDamageValue", 0.10f,
                "Physical damage bonus when solo. Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloDamageType = Config.Bind(
                "1. Combat Stats", "PhysicalDamageType", 0, typeDesc);

            SoloSpellDamagePercent = Config.Bind(
                "1. Combat Stats", "SpellDamageValue", 0.10f,
                "Spell damage bonus when solo. Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloSpellDamageType = Config.Bind(
                "1. Combat Stats", "SpellDamageType", 0, typeDesc);

            SoloCritChancePercent = Config.Bind(
                "1. Combat Stats", "CritChanceValue", 0.10f,
                "Crit chance bonus when solo (physical + spell). Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloCritChanceType = Config.Bind(
                "1. Combat Stats", "CritChanceType", 0, typeDesc);

            SoloCritDamagePercent = Config.Bind(
                "1. Combat Stats", "CritDamageValue", 0.10f,
                "Crit damage bonus when solo (physical + spell). Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloCritDamageType = Config.Bind(
                "1. Combat Stats", "CritDamageType", 0, typeDesc);

            // ===== SURVIVABILITY =====
            SoloHealthPercent = Config.Bind(
                "2. Survivability", "HealthValue", 0.10f,
                "Max health bonus when solo. Multiply: 0.10 = +10%. Add: 100 = +100 flat HP. Set to 0 to disable.");
            SoloHealthType = Config.Bind(
                "2. Survivability", "HealthType", 0, typeDesc);

            SoloPhysicalLeechPercent = Config.Bind(
                "2. Survivability", "PhysicalLeechValue", 0.10f,
                "Physical lifesteal when solo. Set to 0 to disable.");
            SoloPhysicalLeechType = Config.Bind(
                "2. Survivability", "PhysicalLeechType", 1, typeDesc);

            SoloSpellLeechPercent = Config.Bind(
                "2. Survivability", "SpellLeechValue", 0.10f,
                "Spell lifesteal when solo. Set to 0 to disable.");
            SoloSpellLeechType = Config.Bind(
                "2. Survivability", "SpellLeechType", 1, typeDesc);

            SoloPhysicalResistancePercent = Config.Bind(
                "2. Survivability", "PhysicalResistanceValue", 0.10f,
                "Physical damage reduction when solo. Set to 0 to disable.");
            SoloPhysicalResistanceType = Config.Bind(
                "2. Survivability", "PhysicalResistanceType", 1, typeDesc);

            SoloSpellResistancePercent = Config.Bind(
                "2. Survivability", "SpellResistanceValue", 0.10f,
                "Spell damage reduction when solo. Set to 0 to disable.");
            SoloSpellResistanceType = Config.Bind(
                "2. Survivability", "SpellResistanceType", 1, typeDesc);

            // ===== MOBILITY & UTILITY =====
            SoloMoveSpeedPercent = Config.Bind(
                "3. Mobility & Utility", "MoveSpeedValue", 0.10f,
                "Move speed bonus when solo. Multiply: 0.10 = +10%. Add: 0.50 = +0.50 flat. Set to 0 to disable.");
            SoloMoveSpeedType = Config.Bind(
                "3. Mobility & Utility", "MoveSpeedType", 0, typeDesc);

            SoloResourceYieldPercent = Config.Bind(
                "3. Mobility & Utility", "ResourceYieldValue", 0.10f,
                "Resource gathering bonus when solo. Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloResourceYieldType = Config.Bind(
                "3. Mobility & Utility", "ResourceYieldType", 0, typeDesc);

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
