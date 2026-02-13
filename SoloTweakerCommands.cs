using System;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace SoloTweaker
{
    internal static class SoloTweakerCommands
    {
        static readonly PrefabGUID CarrierBuff = new(740689171);

        [Command(
            "solo",
            description: "Shows your SoloTweaker solo/buff status.")]
        public static void SoloStatus(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

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

            ctx.Reply($"[SoloTweaker] Status: {soloStr}, {buffStr}\n{info}");
        }

        [Command("soloall", null, null, "Force rescan all players for solo buffs.", null, true)]
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
            description: "Enable SoloTweaker buffs for yourself.")]
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
            description: "Disable SoloTweaker buffs for yourself.")]
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

        [Command("solodebug", null, null, "Show native buff entity and stat modifiers.", null, true)]
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

            bool hasBuff = BuffUtility.TryGetBuff(em, character, CarrierBuff, out Entity buffEntity);
            msg += $"Native Buff: {(hasBuff ? "PRESENT" : "ABSENT")}";
            if (hasBuff) msg += $" (Entity: {buffEntity.Index}:{buffEntity.Version})";
            msg += "\n";

            // Show stat buffer contents
            if (hasBuff && em.HasComponent<ModifyUnitStatBuff_DOTS>(buffEntity))
            {
                var buf = em.GetBuffer<ModifyUnitStatBuff_DOTS>(buffEntity);
                msg += $"Stat Modifiers ({buf.Length}):\n";
                for (int i = 0; i < buf.Length; i++)
                {
                    var entry = buf[i];
                    string modType = entry.ModificationType == ModificationType.Multiply ? "Mul" : "Add";
                    msg += $"  {entry.StatType}: {entry.Value:F3} ({modType})\n";
                }
            }
            else if (hasBuff)
            {
                msg += "Stat Buffer: NOT FOUND on buff entity\n";
            }

            // Show config values
            msg += $"\nConfig:\n";
            msg += $"  AtkSpd: {Plugin.SoloAttackSpeedPercent.Value}  Dmg: {Plugin.SoloDamagePercent.Value}  Spell: {Plugin.SoloSpellDamagePercent.Value}\n";
            msg += $"  Crit: {Plugin.SoloCritChancePercent.Value}  CritDmg: {Plugin.SoloCritDamagePercent.Value}  HP: {Plugin.SoloHealthPercent.Value}\n";
            msg += $"  PLeech: {Plugin.SoloPhysicalLeechPercent.Value}  SLeech: {Plugin.SoloSpellLeechPercent.Value}\n";
            msg += $"  PRes: {Plugin.SoloPhysicalResistancePercent.Value}  SRes: {Plugin.SoloSpellResistancePercent.Value}\n";
            msg += $"  Move: {Plugin.SoloMoveSpeedPercent.Value}  ResYield: {Plugin.SoloResourceYieldPercent.Value}\n";

            ctx.Reply(msg);
        }
    }
}
