using System;
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

            SoloBuffLogic.UpdateBuffForUser(em, ctx.Event.SenderUserEntity);

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
    }
}
