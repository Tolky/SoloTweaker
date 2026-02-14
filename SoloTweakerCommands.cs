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

            string msg =
                $"<color=#ffcc00>[SoloTweaker]</color> v{Plugin.PluginVersion}\n" +
                "Automatic stat buffs for solo players.\n" +
                $"Activation: <color=green>automatic</color> when you are the only online clan member ({thresholdStr}).\n" +
                "Commands: <color=white>.solo status</color> | <color=white>.solo e</color> | <color=white>.solo t</color>";

            if (ctx.Event.User.IsAdmin)
                msg += " | <color=white>.solo reload</color> | <color=white>.solo scan</color> | <color=white>.solo debug</color>";

            ctx.Reply(msg);
        }
    }

    [CommandGroup("solo")]
    internal static class SoloCommands
    {
        static readonly PrefabGUID CarrierBuff = new(1774716596);

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

            if (!SoloBuffLogic.TryGetStatusData(em, ctx.Event.SenderUserEntity, out var data))
            {
                ctx.Reply("[SoloTweaker] Could not resolve your status.");
                return;
            }

            string enabledStr = data.IsEnabled
                ? "<color=green>enabled</color>"
                : "<color=red>disabled</color>";
            string eligibleStr = data.IsEligible
                ? "<color=green>YES</color>"
                : "<color=red>NO</color>";
            string buffStr = data.IsBuffActive
                ? "<color=green>ACTIVE</color>"
                : "<color=red>INACTIVE</color>";

            string msg =
                "<color=#ffcc00>[SoloTweaker]</color> Status\n" +
                $"Clan : {data.ClanTotal} total, {data.ClanOnline} online\n" +
                $"SoloTweaker {enabledStr} | Eligible : {eligibleStr} | Buff : {buffStr}";

            if (data.TimerRemaining != null)
                msg += $"\nTimer before application of buff : {FormatMinutes(data.TimerRemaining.Value)}";

            ctx.Reply(msg);
        }

        [Command("eligible", "e", null, "Show why you are or aren't eligible for solo buffs.")]
        public static void Eligible(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;
            var reasons = SoloBuffLogic.GetEligibilityReasons(em, ctx.Event.SenderUserEntity);

            bool hasBlocking = false;
            foreach (var r in reasons)
                if (r.Contains("[BLOCKING]")) { hasBlocking = true; break; }

            string header = hasBlocking
                ? "<color=#ffcc00>[SoloTweaker]</color> Eligibility: <color=red>NOT ELIGIBLE</color>"
                : "<color=#ffcc00>[SoloTweaker]</color> Eligibility: <color=green>ELIGIBLE</color>";

            string msg = header;
            foreach (var r in reasons)
                msg += "\n" + r;

            ctx.Reply(msg);
        }

        [Command("toggle", "t", null, "Toggle solo buffs on/off.")]
        public static void Toggle(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
            {
                ctx.Reply("[SoloTweaker] Server world not ready yet.");
                return;
            }

            EntityManager em = serverWorld.EntityManager;

            bool nowDisabled = SoloBuffLogic.ToggleUserOptOut(em, ctx.Event.SenderUserEntity);

            if (nowDisabled)
            {
                ctx.Reply("[SoloTweaker] Solo buffs <color=red>DISABLED</color>. Use <color=white>.solo t</color> to re-enable.");
            }
            else
            {
                SoloBuffLogic.UpdateBuffForUser(em, ctx.Event.SenderUserEntity);
                ctx.Reply("[SoloTweaker] Solo buffs <color=green>ENABLED</color>. Buffs will apply automatically when you are solo.");
            }
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

        static string FormatMinutes(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "0s";
            if (ts.TotalHours >= 1.0) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1.0) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}
