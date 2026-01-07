using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace SoloTweaker
{
    internal static class SoloBuffLogic
    {

        private static float AttackSpeedBonus    => Plugin.SoloAttackSpeedPercent.Value;
        private static float PhysicalDamageBonus => Plugin.SoloDamagePercent.Value;
        private static float SpellDamageBonus    => Plugin.SoloSpellDamagePercent.Value;
        private static float HealthPercentBonus  => Plugin.SoloHealthPercent.Value;
        private static float MoveSpeedPercent    => Plugin.SoloMoveSpeedPercent.Value;
        private static float CritChanceBonus     => Plugin.SoloCritChancePercent.Value;
        private static float CritDamageBonus     => Plugin.SoloCritDamagePercent.Value;
        private static float YieldMultiplier     => Plugin.SoloYieldMultiplier.Value;
        private static bool  NoCooldown          => Plugin.SoloNoCooldown.Value;
        private static bool  SunInvuln           => Plugin.SoloSunInvulnerable.Value;
        private static int   ClanOfflineThresholdMinutes => Plugin.SoloClanOfflineThresholdMinutes.Value;



        private static World? _serverWorld;
        private static readonly HashSet<Entity> _buffedCharacters = new();
        private static readonly HashSet<Entity> _optOutUsers      = new();

        public static World GetServerWorld()
        {
            if (_serverWorld != null && _serverWorld.IsCreated)
                return _serverWorld;

            var worlds = World.All;
            for (int i = 0; i < worlds.Count; i++)
            {
                var w = worlds[i];
                if (w != null && w.IsCreated && w.Name == "Server")
                {
                    _serverWorld = w;
                    break;
                }
            }

            return _serverWorld!;
        }


        public static void UpdateSoloBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
                return;

            var em = serverWorld.EntityManager;

            var desc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<User>()
                }
            };

            var query = em.CreateEntityQuery(desc);
            var users = query.ToEntityArray(Allocator.TempJob);

            try
            {
                foreach (var userEntity in users)
                {
                    UpdateBuffForUser(em, userEntity);
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();
            }

     
            _buffedCharacters.RemoveWhere(e => !em.Exists(e));
            _optOutUsers.RemoveWhere(e => !em.Exists(e));
        }

        public static void UpdateBuffForUser(EntityManager em, Entity userEntity)
        {
            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                return;

            var user = em.GetComponentData<User>(userEntity);

            // If they've opted out, make sure they get no buffs
            if (IsUserOptedOut(em, userEntity))
            {
                var charEntity = user.LocalCharacter._Entity;
                if (charEntity != Entity.Null && em.Exists(charEntity))
                {
                    SoloStatBoostService.Clear(em, charEntity);
                    _buffedCharacters.Remove(charEntity);
                }
                return;
            }

            // Not connected means no buffs
            if (!user.IsConnected)
            {
                var charEntity = user.LocalCharacter._Entity;
                if (charEntity != Entity.Null && em.Exists(charEntity))
                {
                    SoloStatBoostService.Clear(em, charEntity);
                    _buffedCharacters.Remove(charEntity);
                }
                return;
            }

            var character = user.LocalCharacter._Entity;
            if (character == Entity.Null || !em.Exists(character))
                return;

    
            bool isSolo = IsUserSolo(em, userEntity, user, out var clanEntity, out var onlineCount);

            if (isSolo)
            {
                SoloStatBoostService.ApplyBuffs(
                    em,
                    character,
                    AttackSpeedBonus,      // 0.10 = +10% attack speed
                    PhysicalDamageBonus,   // 0.10 = +10% physical damage
                    SpellDamageBonus,      // 0.10 = +10% spell damage
                    HealthPercentBonus,    // 0.10 = +10% max HP
                    MoveSpeedPercent,      // 0.10 = +10% move speed
                    CritChanceBonus,       // 0.10 = +10% crit chance
                    CritDamageBonus        // 0.10 = +10% crit damage
                );

                _buffedCharacters.Add(character);
            }
            else if (_buffedCharacters.Contains(character))
            {
                SoloStatBoostService.Clear(em, character);
                _buffedCharacters.Remove(character);
            }

            _buffedCharacters.RemoveWhere(e => !em.Exists(e));
        }


        public static bool TryGetStatusForUser(EntityManager em, Entity userEntity, out bool isSolo, out bool hasSoloBuff, out string info)
        {
            isSolo = false;
            hasSoloBuff = false;
            info = string.Empty;

            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
            {
                info = "User entity missing User component.";
                return false;
            }

            var user = em.GetComponentData<User>(userEntity);
            var charEntity = user.LocalCharacter._Entity;

            if (charEntity == Entity.Null || !em.Exists(charEntity))
            {
                info = "No character entity resolved from User.LocalCharacter.";
                return false;
            }
            // Determine solo status and clan entity
            Entity clanEntity;
            int dummy;
            isSolo = IsUserSolo(em, userEntity, user, out clanEntity, out dummy);

            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
            {
                info = "You are not in a clan.";
            }
            else
            {
                GetClanMemberCounts(em, clanEntity, out var totalMembers, out var onlineMembers);
                info = $"Clan members: {totalMembers} total, {onlineMembers} online.";

                // Show timer / reason when in a clan
                var remaining = GetClanSoloCooldownRemaining(em, userEntity, user, clanEntity);

                if (!isSolo)
                {
                    // Not currently treated as solo
                    if (remaining == null)
                    {
                        // Another clan member is online
                        if (onlineMembers > 1)
                        {
                            info += "\nAnother clan member is currently online; solo buff in clan is unavailable.";
                        }
                    }
                    else if (remaining.Value > TimeSpan.Zero)
                    {
                        // Everyone else offline, but within the threshold window
                        var pretty = FormatTimeSpanShort(remaining.Value);
                        info += $"\nSolo buff in clan will become available in {pretty} (if no clanmates log in).";
                    }
                    else
                    {
                        // Already eligible (should normally mean isSolo == true, but safe guard)
                        info += "\nYou are currently eligible for the solo buff while in a clan.";
                    }
                }
                else
                {
                    // Already solo according to the rule
                    if (ClanOfflineThresholdMinutes > 0 && totalMembers > 1)
                    {
                        info += "\nYou are currently eligible for the solo buff while in a clan.";
                    }
                }
            }


            var optedOut = IsUserOptedOut(em, userEntity);

            hasSoloBuff = _buffedCharacters.Contains(charEntity) && SoloStatBoostService.HasBuff(charEntity);

            if (optedOut)
            {
                info += "\nYou have SoloTweaker buffs DISABLED via .solooff.";
            }
            else if (hasSoloBuff)
            {
                info += "\nSoloTweaker buff is ACTIVE.";
            }
            else
            {
                info += "\nSoloTweaker buff is NOT ACTIVE.";
            }

            return true;
        }

        private static bool IsUserSolo(EntityManager em, Entity userEntity, User user, out Entity clanEntity, out int onlineCount)
        {
            onlineCount = 0;

            var netClanEntity = user.ClanEntity;
            clanEntity = netClanEntity._Entity;

            // No clan at all -> SOLO
            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
            {
                return true;
            }


            var minutes = ClanOfflineThresholdMinutes;
            if (minutes < 0) minutes = 0; 
            TimeSpan offlineThreshold = TimeSpan.FromMinutes(minutes);

            DateTime nowUtc = DateTime.UtcNow;

            var queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<User>()
                }
            };

            var query = em.CreateEntityQuery(queryDesc);
            var users = query.ToEntityArray(Allocator.Temp);

            bool otherRecentOrOnline = false;

            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var uEntity = users[i];
                    if (!em.Exists(uEntity) || !em.HasComponent<User>(uEntity))
                        continue;

                    var u = em.GetComponentData<User>(uEntity);
                    var uClan = u.ClanEntity._Entity;

                    if (uClan != clanEntity)
                        continue;

                    // Count how many clan members are online (for potential future use / debug)
                    if (u.IsConnected)
                    {
                        onlineCount++;

                        // Any other clan member online right now -> not solo
                        if (uEntity != userEntity)
                        {
                            otherRecentOrOnline = true;
                        }
                    }
                    else
                    {
                        // For other clan members who are offline, check how long they've been offline
                        if (uEntity != userEntity)
                        {
                            DateTime lastOnlineUtc;
                            try
                            {
                                lastOnlineUtc = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                            }
                            catch
                            {
                                lastOnlineUtc = DateTime.MinValue;
                            }

                            var offlineDuration = nowUtc - lastOnlineUtc;

                            // If they were online less than 30 minutes ago, still treat as "not solo"
                            if (offlineDuration < offlineThreshold)
                            {
                                otherRecentOrOnline = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();

                query.Dispose();
            }

            // SOLO if there are no other clan members currently online or recently online
            return !otherRecentOrOnline;
        }



        private static void GetClanMemberCounts(EntityManager em, Entity clanEntity, out int totalMembers, out int onlineMembers)
        {
            totalMembers = 0;
            onlineMembers = 0;

            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
                return;

            var queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<User>()
                }
            };

            var query = em.CreateEntityQuery(queryDesc);
            var users = query.ToEntityArray(Allocator.Temp);

            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var userEnt = users[i];
                    if (!em.Exists(userEnt) || !em.HasComponent<User>(userEnt))
                        continue;

                    var u = em.GetComponentData<User>(userEnt);
                    var uClan = u.ClanEntity._Entity;

                    if (uClan == clanEntity)
                    {
                        totalMembers++;

                        if (u.IsConnected)
                            onlineMembers++;
                    }
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();

                query.Dispose();
            }
        }

        private static TimeSpan? GetClanSoloCooldownRemaining(
            EntityManager em,
            Entity selfUserEntity,
            User selfUser,
            Entity clanEntity)
        {
            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
                return null;

            var minutes = ClanOfflineThresholdMinutes;
            if (minutes <= 0)
                return TimeSpan.Zero; // no delay configured

            TimeSpan threshold = TimeSpan.FromMinutes(minutes);
            DateTime nowUtc = DateTime.UtcNow;

            var queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<User>()
                }
            };

            var query = em.CreateEntityQuery(queryDesc);
            var users = query.ToEntityArray(Allocator.Temp);

            bool otherOnline = false;
            TimeSpan maxRemaining = TimeSpan.Zero;
            bool hasRemaining = false;

            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var uEntity = users[i];
                    if (!em.Exists(uEntity) || !em.HasComponent<User>(uEntity))
                        continue;

                    var u = em.GetComponentData<User>(uEntity);
                    var uClan = u.ClanEntity._Entity;

                    if (uClan != clanEntity)
                        continue;

                    if (uEntity == selfUserEntity)
                        continue;

                    if (u.IsConnected)
                    {
                        // Someone else in clan is online -> no countdown, buff blocked
                        otherOnline = true;
                        break;
                    }
                    else
                    {
                        // Offline clanmate: see how long they've been offline
                        DateTime lastOnlineUtc;
                        try
                        {
                            lastOnlineUtc = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                        }
                        catch
                        {
                            lastOnlineUtc = DateTime.MinValue;
                        }

                        var offlineDuration = nowUtc - lastOnlineUtc;
                        var remaining = threshold - offlineDuration;

                        // Only care about those who have not yet satisfied the threshold
                        if (remaining > TimeSpan.Zero)
                        {
                            if (!hasRemaining || remaining > maxRemaining)
                            {
                                maxRemaining = remaining;
                                hasRemaining = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();

                query.Dispose();
            }

            if (otherOnline)
                return null;

            if (hasRemaining)
                return maxRemaining;

            return TimeSpan.Zero;
        }

        private static string FormatTimeSpanShort(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero)
                ts = TimeSpan.Zero;

            if (ts.TotalHours >= 1.0)
            {
                int hours = (int)ts.TotalHours;
                int minutes = ts.Minutes;
                return $"{hours}h {minutes}m";
            }

            if (ts.TotalMinutes >= 1.0)
            {
                int minutes = (int)ts.TotalMinutes;
                int seconds = ts.Seconds;
                return $"{minutes}m {seconds}s";
            }

            return $"{ts.Seconds}s";
        }


        internal static bool IsUserOptedOut(EntityManager em, Entity userEntity)
        {
            return _optOutUsers.Contains(userEntity);
        }

        internal static void SetUserOptOut(EntityManager em, Entity userEntity)
        {
            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                return;

            if (!_optOutUsers.Contains(userEntity))
                _optOutUsers.Add(userEntity);

            var user = em.GetComponentData<User>(userEntity);
            var charEntity = user.LocalCharacter._Entity;

            if (charEntity != Entity.Null && em.Exists(charEntity))
            {
            SoloStatBoostService.Clear(em, charEntity);
            _buffedCharacters.Remove(charEntity);
            }
        }

        internal static void SetUserOptIn(EntityManager em, Entity userEntity)
        {
            _optOutUsers.Remove(userEntity);
        }

        internal static void ClearAllBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
                return;

            var em = serverWorld.EntityManager;

            // Get all buffed characters and clear them
            var buffedChars = new System.Collections.Generic.List<Entity>(_buffedCharacters);
            foreach (var character in buffedChars)
            {
                if (em.Exists(character))
                {
                    SoloStatBoostService.Clear(em, character);
                }
            }

            _buffedCharacters.Clear();
        }

    }
}
