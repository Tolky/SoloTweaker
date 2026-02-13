using System;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Unity.Entities;
using VampireCommandFramework;

namespace SoloTweaker
{
    internal static class SoloTweakerCommands
    {
        [Command(
            "solo",
            description: "Shows your SoloTweaker solo/buff status and updates your buff.")]
        public static void SoloStatus(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

            // Don't force update - let the automatic update handle buffs
            // Just show status
            if (!SoloBuffLogic.TryGetStatusForUser(
                    em,
                    ctx.Event.SenderUserEntity,
                    out bool isSolo,
                    out bool hasSoloBuff,
                    out string info))
            {
                ctx.Reply("[SoloTweaker] Could not resolve your status.");
                return;
            }

            string soloStr = isSolo
                ? "<color=green>SOLO</color>"
                : "<color=red>NOT SOLO</color>";

            string buffStr = hasSoloBuff
                ? "<color=green>BUFF</color>"
                : "<color=red>NO BUFF</color>";

            string msg =
                $"[SoloTweaker] Status: {soloStr}, {buffStr}\n" +
                info;

            ctx.Reply(msg);
        }

        public static void SoloAll(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            try
            {
                SoloBuffLogic.UpdateSoloBuffs();
                ctx.Reply("[SoloTweaker] Updated solo buffs for all connected players.");
            }
            catch (Exception ex)
            {
                ctx.Reply("[SoloTweaker] Global rescan failed: " + ex.Message);
            }
        }

        [Command("soloreload", null, null, "Reload SoloTweaker config from disk.", null, true)]
        public static void ReloadConfig(ChatCommandContext ctx)
        {
            Plugin.ReloadConfig();
            ctx.Reply("[SoloTweaker] Config reloaded from disk.");
        }
        
        [Command(
            "soloon",
            description: "Enable SoloTweaker buffs for yourself (they will apply automatically when you are solo).")]
        public static void SoloOn(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

            SoloBuffLogic.SetUserOptIn(em, ctx.Event.SenderUserEntity);
            SoloBuffLogic.UpdateBuffForUser(em, ctx.Event.SenderUserEntity);

            ctx.Reply("[SoloTweaker] Your solo buffs are now <color=green>ENABLED</color> (when you are solo).");
        }

        [Command(
            "solooff",
            description: "Disable SoloTweaker buffs for yourself (they will not be applied even if you are solo).")]
        public static void SoloOff(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

            SoloBuffLogic.SetUserOptOut(em, ctx.Event.SenderUserEntity);
            ctx.Reply("[SoloTweaker] Your solo buffs are now <color=red>DISABLED</color>.");
        }

        [Command("solodebug", null, null, "Debug leech and resource yield values", null, true)]
        public static void SoloDebug(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;
            var user = em.GetComponentData<User>(ctx.Event.SenderUserEntity);
            var character = user.LocalCharacter._Entity;

            if (character == Entity.Null || !em.Exists(character))
            {
                ctx.Reply("[SoloTweaker] No character found.");
                return;
            }

            string msg = "[SoloTweaker] Debug Info:\n";

            // Check if character has buff
            bool hasBuff = BuffService.HasBuff(character);
            msg += $"Has SoloTweaker Buff: {hasBuff}\n\n";

            // Check if LifeLeech component exists
            if (em.HasComponent<LifeLeech>(character))
            {
                var leech = em.GetComponentData<LifeLeech>(character);
                msg += $"LifeLeech Component Found:\n";
                msg += $"  PhysicalLifeLeechFactor: {leech.PhysicalLifeLeechFactor._Value}\n";
                msg += $"  SpellLifeLeechFactor: {leech.SpellLifeLeechFactor._Value}\n";
                msg += $"  PrimaryLeechFactor: {leech.PrimaryLeechFactor._Value}\n";
            }
            else
            {
                msg += "LifeLeech Component: NOT FOUND\n";
            }

            // Check VampireSpecificAttributes (crit, resource yield, etc)
            if (em.HasComponent<ProjectM.Shared.VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<ProjectM.Shared.VampireSpecificAttributes>(character);
                msg += $"\nVampireSpecificAttributes:\n";
                msg += $"  PhysicalCriticalStrikeChance: {attrs.PhysicalCriticalStrikeChance._Value}\n";
                msg += $"  PhysicalCriticalStrikeDamage: {attrs.PhysicalCriticalStrikeDamage._Value}\n";
                msg += $"  SpellCriticalStrikeChance: {attrs.SpellCriticalStrikeChance._Value}\n";
                msg += $"  SpellCriticalStrikeDamage: {attrs.SpellCriticalStrikeDamage._Value}\n";
                msg += $"  ResourceYieldModifier: {attrs.ResourceYieldModifier._Value}\n";
            }
            else
            {
                msg += "VampireSpecificAttributes Component: NOT FOUND\n";
            }

            // Show config values and calculated multipliers
            msg += $"\nConfig Values:\n";
            msg += $"  CritChancePercent: {Plugin.SoloCritChancePercent.Value}\n";
            msg += $"  CritDamagePercent: {Plugin.SoloCritDamagePercent.Value}\n";
            msg += $"  PhysicalLeechPercent: {Plugin.SoloPhysicalLeechPercent.Value}\n";
            msg += $"  SpellLeechPercent: {Plugin.SoloSpellLeechPercent.Value}\n";
            msg += $"  ResourceYieldPercent: {Plugin.SoloResourceYieldPercent.Value}\n";

            // Show calculated multipliers
            float critChanceMul = System.Math.Abs(Plugin.SoloCritChancePercent.Value) > 0.0001f ? 1f + Plugin.SoloCritChancePercent.Value : 1f;
            msg += $"\nCalculated Multipliers:\n";
            msg += $"  CritChance Multiplier: {critChanceMul}x (config {Plugin.SoloCritChancePercent.Value} becomes 1 + {Plugin.SoloCritChancePercent.Value} = {critChanceMul})\n";

            float physLeechMul = System.Math.Abs(Plugin.SoloPhysicalLeechPercent.Value) > 0.0001f ? 1f + Plugin.SoloPhysicalLeechPercent.Value : 1f;
            float spellLeechMul = System.Math.Abs(Plugin.SoloSpellLeechPercent.Value) > 0.0001f ? 1f + Plugin.SoloSpellLeechPercent.Value : 1f;

            msg += $"\nCalculated Multipliers (before clamping):\n";
            msg += $"  PhysicalLeechMul: {physLeechMul}\n";
            msg += $"  SpellLeechMul: {spellLeechMul}\n";

            ctx.Reply(msg);
        }
    }
}
