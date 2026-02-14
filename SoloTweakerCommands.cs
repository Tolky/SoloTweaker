using System;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace SoloTweaker
{
    // Bare .solo command (outside the group)
    internal static class SoloInfoCommand
    {
        [Command("solo", description: "Show SoloTweaker mod info.")]
        public static void SoloInfo(ChatCommandContext ctx)
        {
            var threshold = Plugin.SoloClanOfflineThresholdMinutes.Value;
            string thresholdStr = threshold <= 0 ? "instant" : $"{threshold}min after last clanmate logs off";

            ctx.Reply(
                $"<color=#ffcc00>[SoloTweaker]</color> v{Plugin.PluginVersion}\n" +
                "Automatic stat buffs for solo players.\n" +
                $"Activation: <color=green>automatic</color> when you are the only online clan member ({thresholdStr}).\n" +
                "Commands: <color=white>.solo status</color> | <color=white>.solo on</color> | <color=white>.solo off</color> | <color=white>.solo reload</color> | <color=white>.solo debug</color>");
        }
    }

    [CommandGroup("solo")]
    internal static class SoloCommands
    {
        static readonly PrefabGUID CarrierBuff = new(740689171);

        [Command("status", description: "Show your current solo/buff status.")]
        public static void Status(ChatCommandContext ctx)
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
                ? "<color=green>ACTIVE</color>"
                : "<color=red>INACTIVE</color>";

            ctx.Reply($"[SoloTweaker] {soloStr} | Buff: {buffStr}\n{info}");
        }

        [Command("on", description: "Re-enable solo buffs after opting out.")]
        public static void On(ChatCommandContext ctx)
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

            ctx.Reply("[SoloTweaker] Solo buffs <color=green>ENABLED</color>. Buffs will apply automatically when you are solo.");
        }

        [Command("off", description: "Opt out of solo buffs.")]
        public static void Off(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

            SoloBuffLogic.SetUserOptOut(em, ctx.Event.SenderUserEntity);
            ctx.Reply("[SoloTweaker] Solo buffs <color=red>DISABLED</color>. Use <color=white>.solo on</color> to re-enable.");
        }

        [Command("reload", null, null, "Reload SoloTweaker config from disk.", null, true)]
        public static void Reload(ChatCommandContext ctx)
        {
            Plugin.ReloadConfig();
            ctx.Reply("[SoloTweaker] Config reloaded from disk.");
        }

        [Command("scan", null, null, "Force rescan all players for solo buffs.", null, true)]
        public static void Scan(ChatCommandContext ctx)
        {
            try
            {
                SoloBuffLogic.UpdateSoloBuffs();
                ctx.Reply("[SoloTweaker] Rescanned all players.");
            }
            catch (Exception ex)
            {
                ctx.Reply("[SoloTweaker] Scan failed: " + ex.Message);
            }
        }

        [Command("debug", null, null, "Show native buff entity and stat modifiers.", null, true)]
        public static void Debug(ChatCommandContext ctx)
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
