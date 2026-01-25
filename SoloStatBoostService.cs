using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Unity.Entities;

namespace SoloTweaker
{
    /// <summary>
    /// SoloTweaker's stat boost service.
    ///
    /// Handles:
    ///   - Attack speed (abilities + primary + cast speed)
    ///   - Physical damage
    ///   - Spell damage
    ///   - Max HP
    ///   - Move speed
    ///   - Crit chance/damage
    ///   - Life leech
    ///   - Resource yield
    ///
    /// All changes are tracked per character so we can undo them cleanly.
    /// </summary>
    internal static class SoloStatBoostService
    {
        private struct StatState
        {
            // Store the multipliers being applied
            public float AttackSpeedMul;
            public float PhysicalDamageMul;
            public float SpellDamageMul;
            public float HealthMul;
            public float MoveSpeedMul;
            public float CritChanceMul;
            public float CritDamageMul;
            public float PhysicalLeechAdd;  // Leech is added, not multiplied
            public float SpellLeechAdd;
            public float ResourceYieldMul;
        }

        private static readonly Dictionary<Entity, StatState> _states = new();

        /// <summary>
        /// Apply solo buffs for this character.
        /// All bonus parameters: 0.10f = +10%.
        ///
        /// This method reads the CURRENT stats from the game and applies multipliers.
        /// It's safe to call repeatedly - stats will be re-read and re-applied fresh each time.
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
            float critDamageBonus,
            float physicalLeechBonus,
            float spellLeechBonus,
            float resourceYieldBonus)
        {
            if (!em.Exists(character))
                return;

            // If we already have buffs applied, clear them first
            // This ensures we're working with clean base values
            if (_states.ContainsKey(character))
            {
                ClearInternal(em, character);
            }

            // Convert to multipliers
            float attackSpeedMul   = Math.Abs(attackSpeedBonus)      > 0.0001f ? 1f + attackSpeedBonus      : 1f;
            float physDamageMul    = Math.Abs(physicalDamageBonus)   > 0.0001f ? 1f + physicalDamageBonus   : 1f;
            float spellDamageMul   = Math.Abs(spellDamageBonus)      > 0.0001f ? 1f + spellDamageBonus      : 1f;
            float healthMul        = Math.Abs(healthBonusPercent)    > 0.0001f ? 1f + healthBonusPercent    : 1f;
            float moveSpeedMul     = Math.Abs(moveSpeedBonusPercent) > 0.0001f ? 1f + moveSpeedBonusPercent : 1f;
            float critChanceMul    = Math.Abs(critChanceBonus)       > 0.0001f ? 1f + critChanceBonus       : 1f;
            float critDamageMul    = Math.Abs(critDamageBonus)       > 0.0001f ? 1f + critDamageBonus       : 1f;
            float resourceYieldMul = Math.Abs(resourceYieldBonus)    > 0.0001f ? 1f + resourceYieldBonus    : 1f;

            // Clamp leech bonuses (these are added, not multiplied)
            float clampedPhysLeechBonus = Math.Max(0f, Math.Min(0.5f, physicalLeechBonus));
            float clampedSpellLeechBonus = Math.Max(0f, Math.Min(0.5f, spellLeechBonus));

            // Clamp multipliers to sane values
            attackSpeedMul = Math.Max(0.1f, Math.Min(5f, attackSpeedMul));
            physDamageMul = Math.Max(0.1f, Math.Min(5f, physDamageMul));
            spellDamageMul = Math.Max(0.1f, Math.Min(20f, spellDamageMul));
            healthMul = Math.Max(0.1f, Math.Min(10f, healthMul));
            moveSpeedMul = Math.Max(0.1f, Math.Min(3f, moveSpeedMul));
            critChanceMul = Math.Max(0.1f, Math.Min(10f, critChanceMul));
            critDamageMul = Math.Max(0.1f, Math.Min(10f, critDamageMul));
            resourceYieldMul = Math.Max(0.1f, Math.Min(10f, resourceYieldMul));

            bool anyChanged = false;

            // ---- Attack speed ----
            if (attackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);
                    bar.AbilityAttackSpeed._Value *= attackSpeedMul;
                    bar.PrimaryAttackSpeed._Value *= attackSpeedMul;
                    em.SetComponentData(character, bar);
                    anyChanged = true;
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);
                    move.AbilityCastSpeedMultiplier *= attackSpeedMul;
                    em.SetComponentData(character, move);
                    anyChanged = true;
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
                        stats.PhysicalPower._Value *= physDamageMul;
                        anyChanged = true;
                    }

                    if (spellDamageMul != 1f)
                    {
                        stats.SpellPower._Value *= spellDamageMul;
                        anyChanged = true;
                    }

                    em.SetComponentData(character, stats);
                }
            }

            // ---- Spell damage bonus (VampireSpecificAttributes.BonusSpellPower) ----
            if (spellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);
                attrs.BonusSpellPower._Value *= spellDamageMul;
                em.SetComponentData(character, attrs);
                anyChanged = true;
            }

            // ---- Health ----
            if (healthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                // Calculate current health as a percentage of max
                float healthPercentage = health.MaxHealth._Value > 0 ? health.Value / health.MaxHealth._Value : 1f;

                // Apply multiplier to max health
                health.MaxHealth._Value *= healthMul;

                // Set current health to same percentage of new max
                health.Value = health.MaxHealth._Value * healthPercentage;

                em.SetComponentData(character, health);
                anyChanged = true;
            }

            // ---- Movement Speed ----
            if (moveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);
                move.Speed._Value *= moveSpeedMul;
                em.SetComponentData(character, move);
                anyChanged = true;
            }

            // ---- Crit Chance and Damage ----
            if ((critChanceMul != 1f || critDamageMul != 1f) && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                if (critChanceMul != 1f)
                {
                    attrs.PhysicalCriticalStrikeChance._Value *= critChanceMul;
                    attrs.SpellCriticalStrikeChance._Value *= critChanceMul;
                    anyChanged = true;
                }

                if (critDamageMul != 1f)
                {
                    attrs.PhysicalCriticalStrikeDamage._Value *= critDamageMul;
                    attrs.SpellCriticalStrikeDamage._Value *= critDamageMul;
                    anyChanged = true;
                }

                em.SetComponentData(character, attrs);
            }

            // ---- Physical and Spell Leech (Lifesteal) ----
            // Note: Leech starts at 0, so we ADD the bonus instead of multiplying
            if ((clampedPhysLeechBonus > 0f || clampedSpellLeechBonus > 0f) && em.HasComponent<LifeLeech>(character))
            {
                var leech = em.GetComponentData<LifeLeech>(character);

                if (clampedPhysLeechBonus > 0f)
                {
                    leech.PhysicalLifeLeechFactor._Value += clampedPhysLeechBonus;
                    leech.PrimaryLeechFactor._Value += clampedPhysLeechBonus;
                    anyChanged = true;
                }

                if (clampedSpellLeechBonus > 0f)
                {
                    leech.SpellLifeLeechFactor._Value += clampedSpellLeechBonus;
                    anyChanged = true;
                }

                em.SetComponentData(character, leech);
            }

            // ---- Resource Yield ----
            if (resourceYieldMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);
                attrs.ResourceYieldModifier._Value *= resourceYieldMul;
                em.SetComponentData(character, attrs);
                anyChanged = true;
            }

            // If nothing changed, don't store state
            if (!anyChanged)
                return;

            // Store the multipliers we applied (for reverting)
            _states[character] = new StatState
            {
                AttackSpeedMul     = attackSpeedMul,
                PhysicalDamageMul  = physDamageMul,
                SpellDamageMul     = spellDamageMul,
                HealthMul          = healthMul,
                MoveSpeedMul       = moveSpeedMul,
                CritChanceMul      = critChanceMul,
                CritDamageMul      = critDamageMul,
                PhysicalLeechAdd   = clampedPhysLeechBonus,
                SpellLeechAdd      = clampedSpellLeechBonus,
                ResourceYieldMul   = resourceYieldMul
            };
        }

        /// <summary>
        /// Clear all SoloTweaker buffs for this character.
        /// </summary>
        public static void Clear(EntityManager em, Entity character)
        {
            ClearInternal(em, character);
        }

        /// <summary>
        /// Internal clear that reverses the applied multipliers.
        /// </summary>
        private static void ClearInternal(EntityManager em, Entity character)
        {
            if (!_states.TryGetValue(character, out var state))
                return;

            _states.Remove(character);

            if (!em.Exists(character))
                return;

            // Reverse attack speed
            if (state.AttackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);
                    bar.AbilityAttackSpeed._Value /= state.AttackSpeedMul;
                    bar.PrimaryAttackSpeed._Value /= state.AttackSpeedMul;
                    em.SetComponentData(character, bar);
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);
                    move.AbilityCastSpeedMultiplier /= state.AttackSpeedMul;
                    em.SetComponentData(character, move);
                }
            }

            // Reverse physical + spell damage
            if (state.PhysicalDamageMul != 1f || state.SpellDamageMul != 1f)
            {
                if (em.HasComponent<UnitStats>(character))
                {
                    var stats = em.GetComponentData<UnitStats>(character);

                    if (state.PhysicalDamageMul != 1f)
                        stats.PhysicalPower._Value /= state.PhysicalDamageMul;

                    if (state.SpellDamageMul != 1f)
                        stats.SpellPower._Value /= state.SpellDamageMul;

                    em.SetComponentData(character, stats);
                }

                if (state.SpellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
                {
                    var attrs = em.GetComponentData<VampireSpecificAttributes>(character);
                    attrs.BonusSpellPower._Value /= state.SpellDamageMul;
                    em.SetComponentData(character, attrs);
                }
            }

            // Reverse health
            if (state.HealthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                float healthPercentage = health.MaxHealth._Value > 0 ? health.Value / health.MaxHealth._Value : 1f;
                health.MaxHealth._Value /= state.HealthMul;
                health.Value = health.MaxHealth._Value * healthPercentage;

                em.SetComponentData(character, health);
            }

            // Reverse movement speed
            if (state.MoveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);
                move.Speed._Value /= state.MoveSpeedMul;
                em.SetComponentData(character, move);
            }

            // Reverse crit
            if ((state.CritChanceMul != 1f || state.CritDamageMul != 1f) && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                if (state.CritChanceMul != 1f)
                {
                    attrs.PhysicalCriticalStrikeChance._Value /= state.CritChanceMul;
                    attrs.SpellCriticalStrikeChance._Value /= state.CritChanceMul;
                }

                if (state.CritDamageMul != 1f)
                {
                    attrs.PhysicalCriticalStrikeDamage._Value /= state.CritDamageMul;
                    attrs.SpellCriticalStrikeDamage._Value /= state.CritDamageMul;
                }

                em.SetComponentData(character, attrs);
            }

            // Reverse leech (subtract what we added)
            if ((state.PhysicalLeechAdd > 0f || state.SpellLeechAdd > 0f) && em.HasComponent<LifeLeech>(character))
            {
                var leech = em.GetComponentData<LifeLeech>(character);

                if (state.PhysicalLeechAdd > 0f)
                {
                    leech.PhysicalLifeLeechFactor._Value -= state.PhysicalLeechAdd;
                    leech.PrimaryLeechFactor._Value -= state.PhysicalLeechAdd;
                }

                if (state.SpellLeechAdd > 0f)
                    leech.SpellLifeLeechFactor._Value -= state.SpellLeechAdd;

                em.SetComponentData(character, leech);
            }

            // Reverse resource yield
            if (state.ResourceYieldMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);
                attrs.ResourceYieldModifier._Value /= state.ResourceYieldMul;
                em.SetComponentData(character, attrs);
            }
        }

        public static bool HasBuff(Entity character)
        {
            return _states.ContainsKey(character);
        }
    }
}
