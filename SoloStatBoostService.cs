using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Unity.Entities;

namespace SoloTweaker
{

    internal static class SoloStatBoostService
    {
        private struct StatState
        {
            public float AttackSpeedMul;      
            public float PhysicalDamageMul;   
            public float SpellDamageMul;      
            public float HealthMul;           

            public float MoveSpeedMul;        
            public float BaseMoveSpeed;       
        }

        private static readonly Dictionary<Entity, StatState> _states = new();

        // Apply solo buffs for this character.
        //  0.10f = +10%.
 
        public static void ApplyBuffs(
            EntityManager em,
            Entity character,
            float attackSpeedBonus,
            float physicalDamageBonus,
            float spellDamageBonus,
            float healthBonusPercent,
            float moveSpeedBonusPercent)
        {
            if (!em.Exists(character))
                return;

            // Remove any previous SoloTweaker buffs to avoid stacking / drift.
            Clear(em, character);

            // Convert to multipliers
            float attackSpeedMul = Math.Abs(attackSpeedBonus)      > 0.0001f ? 1f + attackSpeedBonus      : 1f;
            float physDamageMul  = Math.Abs(physicalDamageBonus)   > 0.0001f ? 1f + physicalDamageBonus   : 1f;
            float spellDamageMul = Math.Abs(spellDamageBonus)      > 0.0001f ? 1f + spellDamageBonus      : 1f;
            float healthMul      = Math.Abs(healthBonusPercent)    > 0.0001f ? 1f + healthBonusPercent    : 1f;
            float moveSpeedMul   = Math.Abs(moveSpeedBonusPercent) > 0.0001f ? 1f + moveSpeedBonusPercent : 1f;

            if (attackSpeedMul < 0.1f) attackSpeedMul = 0.1f;
            if (attackSpeedMul > 5f)   attackSpeedMul = 5f;

            if (physDamageMul < 0.1f) physDamageMul = 0.1f;
            if (physDamageMul > 5f)   physDamageMul = 5f;

            if (spellDamageMul < 0.1f) spellDamageMul = 0.1f;
            if (spellDamageMul > 20f)  spellDamageMul = 20f;

            if (healthMul < 0.1f) healthMul = 0.1f;
            if (healthMul > 10f)  healthMul = 10f;

            if (moveSpeedMul < 0.1f) moveSpeedMul = 0.1f;
            if (moveSpeedMul > 5f)   moveSpeedMul = 5f;

            bool physChanged   = false;
            bool spellChanged  = false;
            bool healthChanged = false;
            bool moveChanged   = false;

            StatState state = new StatState
            {
                AttackSpeedMul    = 1f,
                PhysicalDamageMul = 1f,
                SpellDamageMul    = 1f,
                HealthMul         = 1f,
                MoveSpeedMul      = 1f,
                BaseMoveSpeed     = 0f
            };

            if (attackSpeedMul != 1f)
            {
                if (em.HasComponent<AbilityBar_Shared>(character))
                {
                    var bar = em.GetComponentData<AbilityBar_Shared>(character);

                    bar.AbilityAttackSpeed._Value *= attackSpeedMul;
                    bar.PrimaryAttackSpeed._Value *= attackSpeedMul;

                    em.SetComponentData(character, bar);
                }

                if (em.HasComponent<Movement>(character))
                {
                    var move = em.GetComponentData<Movement>(character);
                    move.AbilityCastSpeedMultiplier *= attackSpeedMul;
                    em.SetComponentData(character, move);
                }

                state.AttackSpeedMul = attackSpeedMul;
            }

            if (physDamageMul != 1f || spellDamageMul != 1f)
            {
                if (em.HasComponent<UnitStats>(character))
                {
                    var stats = em.GetComponentData<UnitStats>(character);

                    if (physDamageMul != 1f)
                    {
                        stats.PhysicalPower._Value *= physDamageMul;
                        physChanged = true;
                        state.PhysicalDamageMul = physDamageMul;
                    }

                    if (spellDamageMul != 1f)
                    {
                        stats.SpellPower._Value *= spellDamageMul;
                        spellChanged = true;
                        state.SpellDamageMul = spellDamageMul;
                    }

                    em.SetComponentData(character, stats);
                }

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
                    state.PhysicalDamageMul = physDamageMul;
                }

                if (spellDamageMul != 1f && em.HasComponent<VampireSpecificAttributes>(character))
                {
                    var attrs = em.GetComponentData<VampireSpecificAttributes>(character);

                    attrs.BonusSpellPower._Value           *= spellDamageMul;
                    attrs.SpellCriticalStrikeChance._Value *= spellDamageMul;
                    attrs.SpellCriticalStrikeDamage._Value *= spellDamageMul;

                    em.SetComponentData(character, attrs);
                    spellChanged = true;
                    state.SpellDamageMul = spellDamageMul;
                }
            }

            if (healthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                health.MaxHealth._Value *= healthMul;
                health.Value            *= healthMul;

                em.SetComponentData(character, health);
                healthChanged    = true;
                state.HealthMul  = healthMul;
            }

            if (moveSpeedMul != 1f && em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);

                state.BaseMoveSpeed = move.Speed;
                move.Speed          = move.Speed * moveSpeedMul;

                em.SetComponentData(character, move);
                moveChanged        = true;
                state.MoveSpeedMul = moveSpeedMul;
            }

            if (state.AttackSpeedMul == 1f &&
                state.PhysicalDamageMul == 1f &&
                state.SpellDamageMul == 1f &&
                state.HealthMul == 1f &&
                !moveChanged)
            {
                return;
            }

            _states[character] = state;
        }

        public static void Clear(EntityManager em, Entity character)
        {
            if (!_states.TryGetValue(character, out var state))
                return;

            _states.Remove(character);

            if (!em.Exists(character))
                return;

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

            if (state.HealthMul != 1f && em.HasComponent<Health>(character))
            {
                var health = em.GetComponentData<Health>(character);

                health.MaxHealth._Value /= state.HealthMul;
                health.Value            /= state.HealthMul;

                em.SetComponentData(character, health);
            }

            if (state.MoveSpeedMul != 1f &&
                state.BaseMoveSpeed > 0f &&
                em.HasComponent<Movement>(character))
            {
                var move = em.GetComponentData<Movement>(character);

                move.Speed = state.BaseMoveSpeed;

                em.SetComponentData(character, move);
            }
        }

        public static void ApplyAttackSpeedBuff(EntityManager em, Entity character, float bonus)
        {
            ApplyBuffs(em, character, bonus, 0f, 0f, 0f, 0f);
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
