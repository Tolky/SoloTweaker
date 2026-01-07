using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Unity.Entities;

namespace SoloTweaker
{
    /// <summary>
    /// SoloTweaker's own stat boost service.
    /// 
    /// Handles:
    ///   - Attack speed (abilities + primary + cast speed)
    ///   - Physical damage
    ///   - Spell damage
    ///   - Max HP (directly modifies Health.MaxHealth)
    ///   - Move speed (directly modifies Movement.Speed)
    ///
    /// All changes are tracked per character so we can undo them cleanly.
    /// No Kindred / BoostedPlayerService involved.
    /// </summary>
    internal static class SoloStatBoostService
    {
        private struct StatState
        {
            public float AttackSpeedMul;      // 1 = no change
            public float PhysicalDamageMul;   // 1 = no change
            public float SpellDamageMul;      // 1 = no change
            public float HealthMul;           // 1 = no change (1.10 = +10%)
            public float MoveSpeedMul;        // 1 = no change (1.10 = +10%)
            public float CritChanceMul;       // 1 = no change (1.10 = +10%)
            public float CritDamageMul;       // 1 = no change (1.10 = +10%)

            // Store original values for exact revert (prevents compounding errors)
            public float BaseAbilityAttackSpeed;
            public float BasePrimaryAttackSpeed;
            public float BaseAbilityCastSpeed;
            public float BaseMaxHealth;          // Store unbuffed max HP
            public float BaseMoveSpeed;
            public float BasePhysicalPower;      // Store unbuffed physical power
            public float BaseSpellPower;         // Store unbuffed spell power
            public float BasePhysicalCritChance; // Store unbuffed physical crit chance
            public float BasePhysicalCritDamage; // Store unbuffed physical crit damage
        }

        private static readonly Dictionary<Entity, StatState> _states = new();

        /// <summary>
        /// Apply solo buffs for this character.
        /// attackSpeedBonus / physicalDamageBonus / spellDamageBonus / healthBonusPercent / moveSpeedBonusPercent / critChanceBonus / critDamageBonus:
        ///   0.10f = +10%.
        /// </summary>
        public static void ApplyBuffs(
            EntityManager em,
            Entity character,
            float attackSpeedBonus,
            float physicalDamageBonus,
            float spellDamageBonus,
            float healthBonusPercent,
            float moveSpeedBonusPercent,
            float critChanceBonus,
            float critDamageBonus)
        {
            if (!em.Exists(character))
                return;

            // Only clear buffs that are currently active (to avoid clearing already-unbuffed stats)
            if (_states.ContainsKey(character))
            {
                var oldState = _states[character];

                // Track which specific buffs need to be cleared
                bool clearAttackSpeed = true;
                bool clearPhysicalDamage = true;
                bool clearSpellDamage = true;
                bool clearHealth = true;
                bool clearMoveSpeed = true;

                // If we had an HP buff, check if it's still applied
                if (oldState.HealthMul != 1f && em.HasComponent<Health>(character))
                {
                    var currentHealth = em.GetComponentData<Health>(character);
                    float expectedBuffedHP = oldState.BaseMaxHealth * oldState.HealthMul;

                    // If current HP doesn't match expected buffed HP, equipment changed - don't clear HP
                    if (Math.Abs(currentHealth.MaxHealth._Value - expectedBuffedHP) > 1f)
                    {
                        clearHealth = false;
                    }
                }

                // If we had a movement speed buff, check if it's still applied
                if (oldState.MoveSpeedMul != 1f && em.HasComponent<Movement>(character))
                {
                    var currentMovement = em.GetComponentData<Movement>(character);
                    float expectedBuffedSpeed = oldState.BaseMoveSpeed * oldState.MoveSpeedMul;

                    // If current speed doesn't match expected buffed speed, equipment changed - don't clear move speed
                    if (Math.Abs(currentMovement.Speed._Value - expectedBuffedSpeed) > 0.01f)
                    {
                        clearMoveSpeed = false;
                    }
                }

                // Clear only the buffs that are still active
                ClearSelective(em, character, clearAttackSpeed, clearPhysicalDamage, clearSpellDamage, clearHealth, clearMoveSpeed);
            }

            // Convert to multipliers
            float attackSpeedMul = Math.Abs(attackSpeedBonus)      > 0.0001f ? 1f + attackSpeedBonus      : 1f;
            float physDamageMul  = Math.Abs(physicalDamageBonus)   > 0.0001f ? 1f + physicalDamageBonus   : 1f;
            float spellDamageMul = Math.Abs(spellDamageBonus)      > 0.0001f ? 1f + spellDamageBonus      : 1f;
            float healthMul      = Math.Abs(healthBonusPercent)    > 0.0001f ? 1f + healthBonusPercent    : 1f;
            float moveSpeedMul   = Math.Abs(moveSpeedBonusPercent) > 0.0001f ? 1f + moveSpeedBonusPercent : 1f;
            float critChanceMul  = Math.Abs(critChanceBonus)       > 0.0001f ? 1f + critChanceBonus       : 1f;
            float critDamageMul  = Math.Abs(critDamageBonus)       > 0.0001f ? 1f + critDamageBonus       : 1f;

            // Clamp to sane values so things don't go completely nuclear
            if (attackSpeedMul < 0.1f) attackSpeedMul = 0.1f;
            if (attackSpeedMul > 5f)   attackSpeedMul = 5f;

            if (physDamageMul < 0.1f) physDamageMul = 0.1f;
            if (physDamageMul > 5f)   physDamageMul = 5f;

            if (spellDamageMul < 0.1f) spellDamageMul = 0.1f;
            if (spellDamageMul > 20f)  spellDamageMul = 20f;

            if (healthMul < 0.1f) healthMul = 0.1f;
            if (healthMul > 10f)  healthMul = 10f;

            if (moveSpeedMul < 0.1f) moveSpeedMul = 0.1f;
            if (moveSpeedMul > 3f)   moveSpeedMul = 3f;

            if (critChanceMul < 0.1f) critChanceMul = 0.1f;
            if (critChanceMul > 5f)   critChanceMul = 5f;

            if (critDamageMul < 0.1f) critDamageMul = 0.1f;
            if (critDamageMul > 10f)  critDamageMul = 10f;

            bool physChanged   = false;
            bool spellChanged  = false;
            bool healthChanged = false;
            bool moveChanged   = false;
            bool critChanged   = false;

            // Track original values for exact revert
            float baseAbilityAttackSpeed = 0f;
            float basePrimaryAttackSpeed = 0f;
            float baseAbilityCastSpeed = 0f;
            float baseMaxHealth = 0f;
            float baseMoveSpeed = 0f;
            float basePhysicalPower = 0f;
            float baseSpellPower = 0f;
            float basePhysicalCritChance = 0f;
            float basePhysicalCritDamage = 0f;

            // ---- Attack speed ----
            if (attackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);

                    // Store original values
                    baseAbilityAttackSpeed = bar.AbilityAttackSpeed._Value;
                    basePrimaryAttackSpeed = bar.PrimaryAttackSpeed._Value;

                    // Apply multiplier
                    bar.AbilityAttackSpeed._Value *= attackSpeedMul;
                    bar.PrimaryAttackSpeed._Value *= attackSpeedMul;

                    em.SetComponentData(character, bar);
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);

                    // Store original value
                    baseAbilityCastSpeed = move.AbilityCastSpeedMultiplier;

                    // Apply multiplier
                    move.AbilityCastSpeedMultiplier *= attackSpeedMul;
                    em.SetComponentData(character, move);
                }
            }

            // ---- Physical + spell damage via UnitStats ----
            if (physDamageMul != 1f || spellDamageMul != 1f)
            {
                if (em.HasComponent<UnitStats>(character))
                {
                    var stats = em.GetComponentData<UnitStats>(character);

                    if (physDamageMul != 1f)
                    {
                        // Store original value before modifying
                        basePhysicalPower = stats.PhysicalPower._Value;
                        stats.PhysicalPower._Value *= physDamageMul;
                        physChanged = true;
                    }

                    if (spellDamageMul != 1f)
                    {
                        // Store original value before modifying
                        baseSpellPower = stats.SpellPower._Value;
                        stats.SpellPower._Value *= spellDamageMul;
                        spellChanged = true;
                    }

                    em.SetComponentData(character, stats);
                }

                // Per-category damage multipliers (shared by physical and spells).
                // We tie these to physical damage so spells don't get double-dipped too hard.
                if (physDamageMul != 1f && em.HasComponent<DamageCategoryStats>(character))
                {
                    var cats = em.GetComponentData<DamageCategoryStats>(character);

                    cats.DamageVsUndeads._Value       *= physDamageMul;
                    cats.DamageVsHumans._Value        *= physDamageMul;
                    cats.DamageVsDemons._Value        *= physDamageMul;
                    cats.DamageVsMechanical._Value    *= physDamageMul;
                    cats.DamageVsBeasts._Value        *= physDamageMul;
                    cats.DamageVsCastleObjects._Value *= physDamageMul;
                    cats.DamageVsVampires._Value      *= physDamageMul;
                    cats.DamageVsWood._Value          *= physDamageMul;
                    cats.DamageVsMineral._Value       *= physDamageMul;
                    cats.DamageVsVegetation._Value    *= physDamageMul;
                    cats.DamageVsLightArmor._Value    *= physDamageMul;
                    cats.DamageVsVBloods._Value       *= physDamageMul;
                    cats.DamageVsMagic._Value         *= physDamageMul;

                    em.SetComponentData(character, cats);
                    physChanged = true;
                }
            }

            // ---- Health ----
            if (healthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                // Store the unbuffed max health
                baseMaxHealth = health.MaxHealth._Value;

                // Calculate current health as a percentage of max
                float healthPercentage = health.MaxHealth._Value > 0 ? health.Value / health.MaxHealth._Value : 1f;

                // Apply multiplier to max health
                health.MaxHealth._Value *= healthMul;

                // Set current health to same percentage of new max
                health.Value = health.MaxHealth._Value * healthPercentage;

                em.SetComponentData(character, health);
                healthChanged = true;
            }

            // ---- Movement Speed ----
            if (moveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);

                // Save original speed for exact revert later
                baseMoveSpeed = move.Speed._Value;

                // Apply multiplier to speed
                move.Speed._Value *= moveSpeedMul;

                em.SetComponentData(character, move);
                moveChanged = true;
            }

            // ---- VampireSpecificAttributes: spell damage knobs ----
            if (spellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                attrs.BonusSpellPower._Value           *= spellDamageMul;
                attrs.SpellCriticalStrikeChance._Value *= spellDamageMul;
                attrs.SpellCriticalStrikeDamage._Value *= spellDamageMul;

                em.SetComponentData(character, attrs);
                spellChanged = true;
            }

            // ---- Physical Crit Chance and Damage ----
            if ((critChanceMul != 1f || critDamageMul != 1f) && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                if (critChanceMul != 1f)
                {
                    // Store original value before modifying
                    basePhysicalCritChance = attrs.PhysicalCriticalStrikeChance._Value;
                    attrs.PhysicalCriticalStrikeChance._Value *= critChanceMul;
                    critChanged = true;
                }

                if (critDamageMul != 1f)
                {
                    // Store original value before modifying
                    basePhysicalCritDamage = attrs.PhysicalCriticalStrikeDamage._Value;
                    attrs.PhysicalCriticalStrikeDamage._Value *= critDamageMul;
                    critChanged = true;
                }

                em.SetComponentData(character, attrs);
            }

            // If literally nothing changed, don't store state
            if (attackSpeedMul == 1f &&
                !physChanged &&
                !spellChanged &&
                !healthChanged &&
                !moveChanged &&
                !critChanged)
            {
                return;
            }

            _states[character] = new StatState
            {
                AttackSpeedMul         = attackSpeedMul,
                PhysicalDamageMul      = physDamageMul,
                SpellDamageMul         = spellDamageMul,
                HealthMul              = healthMul,
                MoveSpeedMul           = moveSpeedMul,
                CritChanceMul          = critChanceMul,
                CritDamageMul          = critDamageMul,
                BaseAbilityAttackSpeed = baseAbilityAttackSpeed,
                BasePrimaryAttackSpeed = basePrimaryAttackSpeed,
                BaseAbilityCastSpeed   = baseAbilityCastSpeed,
                BaseMaxHealth          = baseMaxHealth,
                BaseMoveSpeed          = baseMoveSpeed,
                BasePhysicalPower      = basePhysicalPower,
                BaseSpellPower         = baseSpellPower,
                BasePhysicalCritChance = basePhysicalCritChance,
                BasePhysicalCritDamage = basePhysicalCritDamage
            };
        }

        /// <summary>
        /// Clear all SoloTweaker buffs for this character.
        /// Safe to call even if equipment has changed since buffs were applied.
        /// </summary>
        public static void Clear(EntityManager em, Entity character)
        {
            if (!_states.TryGetValue(character, out var state))
                return;

            _states.Remove(character);

            if (!em.Exists(character))
                return;

            // Undo attack speed - restore exact original values
            if (state.AttackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);

                    // Restore exact original values
                    bar.AbilityAttackSpeed._Value = state.BaseAbilityAttackSpeed;
                    bar.PrimaryAttackSpeed._Value = state.BasePrimaryAttackSpeed;

                    em.SetComponentData(character, bar);
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);

                    // Restore exact original value
                    move.AbilityCastSpeedMultiplier = state.BaseAbilityCastSpeed;
                    em.SetComponentData(character, move);
                }
            }

            // Undo physical + spell damage - restore exact original values
            if (state.PhysicalDamageMul != 1f || state.SpellDamageMul != 1f)
            {
                if (em.HasComponent<UnitStats>(character))
                {
                    var stats = em.GetComponentData<UnitStats>(character);

                    if (state.PhysicalDamageMul != 1f)
                        stats.PhysicalPower._Value = state.BasePhysicalPower;

                    if (state.SpellDamageMul != 1f)
                        stats.SpellPower._Value = state.BaseSpellPower;

                    em.SetComponentData(character, stats);
                }

                if (state.PhysicalDamageMul != 1f && em.HasComponent<DamageCategoryStats>(character))
                {
                    var cats = em.GetComponentData<DamageCategoryStats>(character);

                    cats.DamageVsUndeads._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsHumans._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsDemons._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsMechanical._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsBeasts._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsCastleObjects._Value /= state.PhysicalDamageMul;
                    cats.DamageVsVampires._Value      /= state.PhysicalDamageMul;
                    cats.DamageVsWood._Value          /= state.PhysicalDamageMul;
                    cats.DamageVsMineral._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsVegetation._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsLightArmor._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsVBloods._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsMagic._Value         /= state.PhysicalDamageMul;

                    em.SetComponentData(character, cats);
                }

                if (state.SpellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
                {
                    var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                    attrs.BonusSpellPower._Value           /= state.SpellDamageMul;
                    attrs.SpellCriticalStrikeChance._Value /= state.SpellDamageMul;
                    attrs.SpellCriticalStrikeDamage._Value /= state.SpellDamageMul;

                    em.SetComponentData(character, attrs);
                }
            }

            // Undo health buff - restore exact unbuffed value
            if (state.HealthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                // Calculate current health percentage
                float healthPercentage = health.MaxHealth._Value > 0 ? health.Value / health.MaxHealth._Value : 1f;

                // Restore exact unbuffed max health
                health.MaxHealth._Value = state.BaseMaxHealth;

                // Restore current health to same percentage of unbuffed max
                health.Value = state.BaseMaxHealth * healthPercentage;

                em.SetComponentData(character, health);
            }

            // Undo movement speed buff
            if (state.MoveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);

                // Restore exact original speed
                move.Speed._Value = state.BaseMoveSpeed;

                em.SetComponentData(character, move);
            }

            // Undo physical crit buffs - restore exact original values
            if ((state.CritChanceMul != 1f || state.CritDamageMul != 1f) && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                if (state.CritChanceMul != 1f)
                    attrs.PhysicalCriticalStrikeChance._Value = state.BasePhysicalCritChance;

                if (state.CritDamageMul != 1f)
                    attrs.PhysicalCriticalStrikeDamage._Value = state.BasePhysicalCritDamage;

                em.SetComponentData(character, attrs);
            }
        }

        /// <summary>
        /// Selectively clear specific buffs for this character.
        /// Allows clearing only certain buffs while leaving others intact (useful when equipment changes affect some stats but not others).
        /// </summary>
        private static void ClearSelective(
            EntityManager em,
            Entity character,
            bool clearAttackSpeed,
            bool clearPhysicalDamage,
            bool clearSpellDamage,
            bool clearHealth,
            bool clearMoveSpeed,
            bool clearCrit = true)
        {
            if (!_states.TryGetValue(character, out var state))
                return;

            if (!em.Exists(character))
                return;

            // Only remove from state tracking if we're clearing everything
            bool clearingEverything = clearAttackSpeed && clearPhysicalDamage && clearSpellDamage && clearHealth && clearMoveSpeed && clearCrit;
            if (clearingEverything)
            {
                _states.Remove(character);
            }

            // Undo attack speed - restore exact original values
            if (clearAttackSpeed && state.AttackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);

                    // Restore exact original values
                    bar.AbilityAttackSpeed._Value = state.BaseAbilityAttackSpeed;
                    bar.PrimaryAttackSpeed._Value = state.BasePrimaryAttackSpeed;

                    em.SetComponentData(character, bar);
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);

                    // Restore exact original value
                    move.AbilityCastSpeedMultiplier = state.BaseAbilityCastSpeed;
                    em.SetComponentData(character, move);
                }
            }

            // Undo physical + spell damage - restore exact original values
            if ((clearPhysicalDamage && state.PhysicalDamageMul != 1f) || (clearSpellDamage && state.SpellDamageMul != 1f))
            {
                if (em.HasComponent<UnitStats>(character))
                {
                    var stats = em.GetComponentData<UnitStats>(character);

                    if (clearPhysicalDamage && state.PhysicalDamageMul != 1f)
                        stats.PhysicalPower._Value = state.BasePhysicalPower;

                    if (clearSpellDamage && state.SpellDamageMul != 1f)
                        stats.SpellPower._Value = state.BaseSpellPower;

                    em.SetComponentData(character, stats);
                }

                if (clearPhysicalDamage && state.PhysicalDamageMul != 1f && em.HasComponent<DamageCategoryStats>(character))
                {
                    var cats = em.GetComponentData<DamageCategoryStats>(character);

                    cats.DamageVsUndeads._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsHumans._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsDemons._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsMechanical._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsBeasts._Value        /= state.PhysicalDamageMul;
                    cats.DamageVsCastleObjects._Value /= state.PhysicalDamageMul;
                    cats.DamageVsVampires._Value      /= state.PhysicalDamageMul;
                    cats.DamageVsWood._Value          /= state.PhysicalDamageMul;
                    cats.DamageVsMineral._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsVegetation._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsLightArmor._Value    /= state.PhysicalDamageMul;
                    cats.DamageVsVBloods._Value       /= state.PhysicalDamageMul;
                    cats.DamageVsMagic._Value         /= state.PhysicalDamageMul;

                    em.SetComponentData(character, cats);
                }

                if (clearSpellDamage && state.SpellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
                {
                    var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                    attrs.BonusSpellPower._Value           /= state.SpellDamageMul;
                    attrs.SpellCriticalStrikeChance._Value /= state.SpellDamageMul;
                    attrs.SpellCriticalStrikeDamage._Value /= state.SpellDamageMul;

                    em.SetComponentData(character, attrs);
                }
            }

            // Undo health buff - restore exact unbuffed value
            if (clearHealth && state.HealthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                // Calculate current health percentage
                float healthPercentage = health.MaxHealth._Value > 0 ? health.Value / health.MaxHealth._Value : 1f;

                // Restore exact unbuffed max health
                health.MaxHealth._Value = state.BaseMaxHealth;

                // Restore current health to same percentage of unbuffed max
                health.Value = state.BaseMaxHealth * healthPercentage;

                em.SetComponentData(character, health);
            }

            // Undo movement speed buff
            if (clearMoveSpeed && state.MoveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);

                // Restore exact original speed
                move.Speed._Value = state.BaseMoveSpeed;

                em.SetComponentData(character, move);
            }

            // Undo physical crit buffs - restore exact original values
            if (clearCrit && (state.CritChanceMul != 1f || state.CritDamageMul != 1f) && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                if (state.CritChanceMul != 1f)
                    attrs.PhysicalCriticalStrikeChance._Value = state.BasePhysicalCritChance;

                if (state.CritDamageMul != 1f)
                    attrs.PhysicalCriticalStrikeDamage._Value = state.BasePhysicalCritDamage;

                em.SetComponentData(character, attrs);
            }
        }

        // --- Convenience helpers / back-compat ---

        public static void ApplyAttackSpeedBuff(EntityManager em, Entity character, float bonus)
        {
            ApplyBuffs(em, character, bonus, 0f, 0f, 0f, 0f, 0f, 0f);
        }

        public static void ClearAttackSpeedBuff(EntityManager em, Entity character)
        {
            Clear(em, character);
        }

        public static bool HasBuff(Entity character)
        {
            return _states.ContainsKey(character);
        }
    }
}
