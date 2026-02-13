using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace SoloTweaker;

internal static class BuffService
{
    // Buff_General_Build_Spawn_Buff_WeakStructure_Wall — lightweight carrier buff
    static readonly PrefabGUID CarrierBuff = new(740689171);

    public static bool ApplyBuff(Entity user, Entity character)
    {
        var world = SoloBuffLogic.GetServerWorld();
        if (world == null || !world.IsCreated) return false;

        var em = world.EntityManager;

        // Already has our buff
        if (BuffUtility.TryGetBuff(em, character, CarrierBuff, out _))
            return true;

        var des = world.GetExistingSystemManaged<DebugEventsSystem>();
        var buffEvent = new ApplyBuffDebugEvent { BuffPrefabGUID = CarrierBuff };
        var fromChar = new FromCharacter { User = user, Character = character };

        des.ApplyBuff(fromChar, buffEvent);

        if (!BuffUtility.TryGetBuff(em, character, CarrierBuff, out Entity buffEntity))
            return false;

        ConfigurePermanentBuff(buffEntity);
        PopulateStatBuffer(buffEntity);
        return true;
    }

    public static void RemoveBuff(Entity character)
    {
        var world = SoloBuffLogic.GetServerWorld();
        if (world == null || !world.IsCreated) return;

        var em = world.EntityManager;
        if (BuffUtility.TryGetBuff(em, character, CarrierBuff, out var buffEntity))
        {
            DestroyUtility.Destroy(em, buffEntity, DestroyDebugReason.TryRemoveBuff);
        }
    }

    public static bool HasBuff(Entity character)
    {
        var world = SoloBuffLogic.GetServerWorld();
        if (world == null || !world.IsCreated) return false;

        return BuffUtility.TryGetBuff(world.EntityManager, character, CarrierBuff, out _);
    }

    internal static void ConfigurePermanentBuff(Entity buffEntity)
    {
        if (buffEntity.Has<CreateGameplayEventsOnSpawn>())
            buffEntity.Remove<CreateGameplayEventsOnSpawn>();
        if (buffEntity.Has<GameplayEventListeners>())
            buffEntity.Remove<GameplayEventListeners>();
        if (buffEntity.Has<RemoveBuffOnGameplayEvent>())
            buffEntity.Remove<RemoveBuffOnGameplayEvent>();
        if (buffEntity.Has<RemoveBuffOnGameplayEventEntry>())
            buffEntity.Remove<RemoveBuffOnGameplayEventEntry>();

        buffEntity.Add<Buff_Persists_Through_Death>();

        if (buffEntity.Has<LifeTime>())
        {
            var lifetime = buffEntity.Read<LifeTime>();
            lifetime.Duration = -1;
            lifetime.EndAction = LifeTimeEndAction.None;
            buffEntity.Write(lifetime);
        }
    }

    internal static void PopulateStatBuffer(Entity buffEntity)
    {
        var em = SoloBuffLogic.GetServerWorld().EntityManager;
        var buf = em.AddBuffer<ModifyUnitStatBuff_DOTS>(buffEntity);
        buf.Clear();

        float atkSpd = Plugin.SoloAttackSpeedPercent.Value;
        float dmg    = Plugin.SoloDamagePercent.Value;
        float spell  = Plugin.SoloSpellDamagePercent.Value;
        float crit   = Plugin.SoloCritChancePercent.Value;
        float critD  = Plugin.SoloCritDamagePercent.Value;
        float hp     = Plugin.SoloHealthPercent.Value;
        float pLeech = Plugin.SoloPhysicalLeechPercent.Value;
        float sLeech = Plugin.SoloSpellLeechPercent.Value;
        float pRes   = Plugin.SoloPhysicalResistancePercent.Value;
        float sRes   = Plugin.SoloSpellResistancePercent.Value;
        float move   = Plugin.SoloMoveSpeedPercent.Value;
        float res    = Plugin.SoloResourceYieldPercent.Value;

        // Attack speed
        if (atkSpd != 0f)
        {
            AddMul(buf, UnitStatType.PrimaryAttackSpeed, 1f + atkSpd);
            AddMul(buf, UnitStatType.AbilityAttackSpeed, 1f + atkSpd);
        }

        // Damage
        if (dmg != 0f) AddMul(buf, UnitStatType.PhysicalPower, 1f + dmg);
        if (spell != 0f) AddMul(buf, UnitStatType.SpellPower, 1f + spell);

        // Crit
        if (crit != 0f)
        {
            AddMul(buf, UnitStatType.PhysicalCriticalStrikeChance, 1f + crit);
            AddMul(buf, UnitStatType.SpellCriticalStrikeChance, 1f + crit);
        }
        if (critD != 0f)
        {
            AddMul(buf, UnitStatType.PhysicalCriticalStrikeDamage, 1f + critD);
            AddMul(buf, UnitStatType.SpellCriticalStrikeDamage, 1f + critD);
        }

        // Health
        if (hp != 0f) AddMul(buf, UnitStatType.MaxHealth, 1f + hp);

        // Leech (additive, not multiplicative)
        if (pLeech != 0f) AddFlat(buf, UnitStatType.PhysicalLifeLeech, pLeech);
        if (sLeech != 0f) AddFlat(buf, UnitStatType.SpellLifeLeech, sLeech);

        // Damage reduction (additive: 0.15 = 15% less damage taken)
        if (pRes != 0f) AddFlat(buf, UnitStatType.PhysicalResistance, pRes);
        if (sRes != 0f) AddFlat(buf, UnitStatType.SpellResistance, sRes);

        // Movement
        if (move != 0f) AddMul(buf, UnitStatType.MovementSpeed, 1f + move);

        // Resource yield
        if (res != 0f) AddMul(buf, UnitStatType.ResourceYield, 1f + res);
    }

    static void AddMul(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value)
    {
        buf.Add(new ModifyUnitStatBuff_DOTS
        {
            StatType = stat,
            Value = value,
            ModificationType = ModificationType.Multiply,
            AttributeCapType = AttributeCapType.Uncapped,
            Modifier = 1,
            Id = ModificationId.NewId(0)
        });
    }

    static void AddFlat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value)
    {
        buf.Add(new ModifyUnitStatBuff_DOTS
        {
            StatType = stat,
            Value = value,
            ModificationType = ModificationType.Add,
            AttributeCapType = AttributeCapType.Uncapped,
            Modifier = 1,
            Id = ModificationId.NewId(0)
        });
    }
}
