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

    /// <summary>
    /// Re-populate stat buffer on an existing buff without destroying it.
    /// Used by config reload to update values instantly.
    /// </summary>
    public static bool RefreshBuff(Entity character)
    {
        var world = SoloBuffLogic.GetServerWorld();
        if (world == null || !world.IsCreated) return false;

        if (!BuffUtility.TryGetBuff(world.EntityManager, character, CarrierBuff, out Entity buffEntity))
            return false;

        PopulateStatBuffer(buffEntity);
        return true;
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

        // Attack speed
        AddStat(buf, UnitStatType.PrimaryAttackSpeed,  Plugin.SoloAttackSpeedPercent.Value, Plugin.SoloAttackSpeedType.Value);
        AddStat(buf, UnitStatType.AbilityAttackSpeed,  Plugin.SoloAttackSpeedPercent.Value, Plugin.SoloAttackSpeedType.Value);

        // Damage
        AddStat(buf, UnitStatType.PhysicalPower, Plugin.SoloDamagePercent.Value,      Plugin.SoloDamageType.Value);
        AddStat(buf, UnitStatType.SpellPower,    Plugin.SoloSpellDamagePercent.Value,  Plugin.SoloSpellDamageType.Value);

        // Crit
        AddStat(buf, UnitStatType.PhysicalCriticalStrikeChance, Plugin.SoloCritChancePercent.Value, Plugin.SoloCritChanceType.Value);
        AddStat(buf, UnitStatType.SpellCriticalStrikeChance,    Plugin.SoloCritChancePercent.Value, Plugin.SoloCritChanceType.Value);
        AddStat(buf, UnitStatType.PhysicalCriticalStrikeDamage, Plugin.SoloCritDamagePercent.Value, Plugin.SoloCritDamageType.Value);
        AddStat(buf, UnitStatType.SpellCriticalStrikeDamage,    Plugin.SoloCritDamagePercent.Value, Plugin.SoloCritDamageType.Value);

        // Health
        AddStat(buf, UnitStatType.MaxHealth, Plugin.SoloHealthPercent.Value, Plugin.SoloHealthType.Value);

        // Leech
        AddStat(buf, UnitStatType.PhysicalLifeLeech, Plugin.SoloPhysicalLeechPercent.Value, Plugin.SoloPhysicalLeechType.Value);
        AddStat(buf, UnitStatType.SpellLifeLeech,    Plugin.SoloSpellLeechPercent.Value,    Plugin.SoloSpellLeechType.Value);

        // Damage reduction
        AddStat(buf, UnitStatType.PhysicalResistance, Plugin.SoloPhysicalResistancePercent.Value, Plugin.SoloPhysicalResistanceType.Value);
        AddStat(buf, UnitStatType.SpellResistance,    Plugin.SoloSpellResistancePercent.Value,    Plugin.SoloSpellResistanceType.Value);

        // Movement
        AddStat(buf, UnitStatType.MovementSpeed, Plugin.SoloMoveSpeedPercent.Value, Plugin.SoloMoveSpeedType.Value);

        // Resource yield
        AddStat(buf, UnitStatType.ResourceYield, Plugin.SoloResourceYieldPercent.Value, Plugin.SoloResourceYieldType.Value);
    }

    static void AddStat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value, int type)
    {
        if (value == 0f) return;

        if (type == 1)
            AddFlat(buf, stat, value);
        else
            AddMul(buf, stat, 1f + value);
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
