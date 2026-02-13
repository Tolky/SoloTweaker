using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace SoloTweaker.Patches;

/// <summary>
/// Safety net: when BuffSystem_Spawn_Server processes newly spawned buffs,
/// re-populate stat buffer on our carrier buff if the spawn pipeline cleared it.
/// Primary population happens in BuffService.ApplyBuff().
/// </summary>
[HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
internal static class BuffSpawnPatch
{
    static readonly PrefabGUID CarrierBuff = new(740689171);

    static void Postfix(BuffSystem_Spawn_Server __instance)
    {
        var queries = __instance.EntityQueries;
        if (queries == null || queries.Length == 0) return;

        // The first query is typically the newly spawned buff entities
        var entities = queries[0].ToEntityArray(Allocator.Temp);
        try
        {
            var em = __instance.EntityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!em.HasComponent<PrefabGUID>(entity)) continue;

                var prefab = em.GetComponentData<PrefabGUID>(entity);
                if (prefab != CarrierBuff) continue;

                // Re-configure and re-populate in case spawn pipeline reset anything
                BuffService.ConfigurePermanentBuff(entity);
                BuffService.PopulateStatBuffer(entity);
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
