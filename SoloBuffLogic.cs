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
        private static int ClanOfflineThresholdMinutes => Plugin.SoloClanOfflineThresholdMinutes.Value;



        private static World? _serverWorld;
        private static EntityQuery? _userQuery;
        private static readonly HashSet<Entity> _buffedCharacters = new();
        private static readonly HashSet<Entity> _optOutUsers      = new();

        // Track when users disconnect to properly enforce the offline threshold
        private static readonly Dictionary<Entity, DateTime> _userDisconnectTimes = new();

        // Track when users leave their clan to prevent bypassing the timer
        private static readonly Dictionary<Entity, DateTime> _userClanLeaveTimes = new();
        private static readonly Dictionary<Entity, Entity> _userLastClan = new();

        // Track when someone left a specific clan (clan entity -> departure time)
        // This ensures remaining members don't get instant buffs
        private static readonly Dictionary<Entity, DateTime> _clanMemberDepartureTimes = new();

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

        private static EntityQuery GetUserQuery(EntityManager em)
        {
            if (_userQuery == null)
                _userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
            return _userQuery.Value;
        }

        public static void UpdateSoloBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
                return;

            var em = serverWorld.EntityManager;
            var users = GetUserQuery(em).ToEntityArray(Allocator.Temp);

            // Track disconnect times and clan changes - use indexed for loop to avoid enumerator disposal
            for (int i = 0; i < users.Length; i++)
            {
                var userEntity = users[i];
                if (em.Exists(userEntity) && em.HasComponent<User>(userEntity))
                {
                    var user = em.GetComponentData<User>(userEntity);
                    var currentClan = user.ClanEntity._Entity;

                    // Track disconnect times (actual network disconnects)
                    if (!user.IsConnected && !_userDisconnectTimes.ContainsKey(userEntity))
                    {
                        _userDisconnectTimes[userEntity] = DateTime.UtcNow;

                        // If they're in a clan, record that someone left/disconnected from this clan
                        if (currentClan != Entity.Null && em.Exists(currentClan))
                        {
                            _clanMemberDepartureTimes[currentClan] = DateTime.UtcNow;
                        }
                    }
                    else if (user.IsConnected && _userDisconnectTimes.ContainsKey(userEntity))
                    {
                        // Only clear disconnect time if they're NOT in the clan leave timer
                        // If they left a clan, keep the disconnect time until they join a new clan
                        if (!_userClanLeaveTimes.ContainsKey(userEntity))
                        {
                            _userDisconnectTimes.Remove(userEntity);
                        }
                    }

                    // Track clan changes (to prevent leave/rejoin exploit)
                    if (user.IsConnected)
                    {
                        // Check if they had a clan before
                        if (_userLastClan.TryGetValue(userEntity, out Entity lastClan))
                        {
                            // If they were in a clan but now aren't (left clan)
                            if (lastClan != Entity.Null && em.Exists(lastClan) &&
                                (currentClan == Entity.Null || !em.Exists(currentClan)))
                            {
                                // Record when they left their clan (for the person leaving)
                                _userClanLeaveTimes[userEntity] = DateTime.UtcNow;
                                _userDisconnectTimes[userEntity] = DateTime.UtcNow;

                                // Record that someone left this clan (for remaining members)
                                _clanMemberDepartureTimes[lastClan] = DateTime.UtcNow;
                            }
                            // If they joined a new clan or switched clans
                            else if (currentClan != Entity.Null && em.Exists(currentClan) && currentClan != lastClan)
                            {
                                // Clear the leave timer
                                _userClanLeaveTimes.Remove(userEntity);
                                // Also remove disconnect time since they're back in a clan
                                _userDisconnectTimes.Remove(userEntity);
                            }
                        }
                        // else: First time seeing this user - will be set below

                        // Always update their last known clan (handles both first time and updates)
                        _userLastClan[userEntity] = currentClan;
                    }
                }
            }

            // Update buffs for all users - use indexed for loop to avoid enumerator disposal
            for (int i = 0; i < users.Length; i++)
            {
                UpdateBuffForUser(em, users[i]);
            }

            // Dispose properly - MUST dispose Temp allocations or we get memory leaks
            users.Dispose();


            // Clean up buffed characters (manual loop to avoid IL2CPP RemoveWhere issues)
            var buffedCharsToRemove = new List<Entity>();
            foreach (var e in _buffedCharacters)
            {
                if (!em.Exists(e))
                    buffedCharsToRemove.Add(e);
            }
            foreach (var e in buffedCharsToRemove)
            {
                _buffedCharacters.Remove(e);
            }

            // Clean up opted out users (manual loop to avoid IL2CPP RemoveWhere issues)
            var optOutToRemove = new List<Entity>();
            foreach (var e in _optOutUsers)
            {
                if (!em.Exists(e))
                    optOutToRemove.Add(e);
            }
            foreach (var e in optOutToRemove)
            {
                _optOutUsers.Remove(e);
            }

            // Clean up tracking data for entities that no longer exist
            var keysToRemove = new List<Entity>();
            foreach (var kvp in _userDisconnectTimes)
            {
                if (!em.Exists(kvp.Key))
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove)
            {
                _userDisconnectTimes.Remove(key);
                _userClanLeaveTimes.Remove(key);
                _userLastClan.Remove(key);
            }
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
                    BuffService.RemoveBuff(charEntity);
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
                    BuffService.RemoveBuff(charEntity);
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
                BuffService.ApplyBuff(userEntity, character);

                _buffedCharacters.Add(character);
            }
            else if (_buffedCharacters.Contains(character))
            {
                BuffService.RemoveBuff(character);
                _buffedCharacters.Remove(character);
            }
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

                // Check if they recently left a clan
                if (_userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
                {
                    var minutes = ClanOfflineThresholdMinutes;
                    if (minutes < 0) minutes = 0;
                    TimeSpan threshold = TimeSpan.FromMinutes(minutes);

                    var timeSinceLeave = DateTime.UtcNow - leaveTime;
                    var remaining = threshold - timeSinceLeave;

                    if (remaining > TimeSpan.Zero)
                    {
                        var pretty = FormatTimeSpanShort(remaining);
                        info += $"\nYou recently left a clan. Solo buff will become available in {pretty}.";
                    }
                }
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

            hasSoloBuff = _buffedCharacters.Contains(charEntity) && BuffService.HasBuff(charEntity);

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

            // No clan at all -> check if they recently left a clan
            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
            {
                // If they recently left a clan, apply the same threshold
                if (_userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
                {
                    var thresholdMinutes = ClanOfflineThresholdMinutes;
                    if (thresholdMinutes < 0) thresholdMinutes = 0;
                    TimeSpan leaveThreshold = TimeSpan.FromMinutes(thresholdMinutes);

                    var timeSinceLeave = DateTime.UtcNow - leaveTime;

                    // If they left less than the threshold ago, NOT solo yet
                    if (timeSinceLeave < leaveThreshold)
                    {
                        return false;
                    }
                    else
                    {
                        // Threshold passed, clear the leave time and allow solo
                        _userClanLeaveTimes.Remove(userEntity);
                        return true;
                    }
                }

                // No clan and no recent leave - truly solo
                return true;
            }


            var minutes = ClanOfflineThresholdMinutes;
            if (minutes < 0) minutes = 0;
            TimeSpan offlineThreshold = TimeSpan.FromMinutes(minutes);

            DateTime nowUtc = DateTime.UtcNow;

            // Check if someone recently left this clan
            if (_clanMemberDepartureTimes.TryGetValue(clanEntity, out DateTime clanDepartureTime))
            {
                var timeSinceDeparture = nowUtc - clanDepartureTime;

                if (timeSinceDeparture < offlineThreshold)
                {
                    // Someone left less than the threshold ago - NOT solo yet
                    return false;
                }
                else
                {
                    // Threshold passed, clear the clan departure time
                    _clanMemberDepartureTimes.Remove(clanEntity);
                }
            }

            var users = GetUserQuery(em).ToEntityArray(Allocator.Temp);

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
                            // Use our tracked disconnect time if available
                            if (_userDisconnectTimes.TryGetValue(uEntity, out DateTime disconnectTime))
                            {
                                var offlineDuration = nowUtc - disconnectTime;

                                // If they disconnected less than the threshold ago, still treat as "not solo"
                                if (offlineDuration < offlineThreshold)
                                {
                                    otherRecentOrOnline = true;
                                }
                            }
                            else
                            {
                                // No tracked disconnect time - try to use TimeLastConnected as fallback
                                DateTime lastOnlineUtc;
                                try
                                {
                                    lastOnlineUtc = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                                }
                                catch
                                {
                                    lastOnlineUtc = DateTime.MinValue;
                                }

                                // If we have a valid TimeLastConnected, use it
                                if (lastOnlineUtc != DateTime.MinValue)
                                {
                                    var offlineDuration = nowUtc - lastOnlineUtc;

                                    if (offlineDuration < offlineThreshold)
                                    {
                                        otherRecentOrOnline = true;
                                    }
                                }
                                else
                                {
                                    // No valid disconnect time at all - treat as recently online to be safe
                                    otherRecentOrOnline = true;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                users.Dispose();
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

            var users = GetUserQuery(em).ToEntityArray(Allocator.Temp);

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
                users.Dispose();
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

            var users = GetUserQuery(em).ToEntityArray(Allocator.Temp);

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
                        DateTime disconnectTime;

                        // Use our tracked disconnect time if available
                        if (_userDisconnectTimes.TryGetValue(uEntity, out disconnectTime))
                        {
                            var offlineDuration = nowUtc - disconnectTime;
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
                        else
                        {
                            // Fallback to TimeLastConnected
                            DateTime lastOnlineUtc;
                            try
                            {
                                lastOnlineUtc = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                            }
                            catch
                            {
                                lastOnlineUtc = DateTime.MinValue;
                            }

                            if (lastOnlineUtc != DateTime.MinValue)
                            {
                                var offlineDuration = nowUtc - lastOnlineUtc;
                                var remaining = threshold - offlineDuration;

                                if (remaining > TimeSpan.Zero)
                                {
                                    if (!hasRemaining || remaining > maxRemaining)
                                    {
                                        maxRemaining = remaining;
                                        hasRemaining = true;
                                    }
                                }
                            }
                            else
                            {
                                // No valid time - assume they need to wait full threshold
                                if (!hasRemaining || threshold > maxRemaining)
                                {
                                    maxRemaining = threshold;
                                    hasRemaining = true;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                users.Dispose();
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
                BuffService.RemoveBuff(charEntity);
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
                    BuffService.RemoveBuff(character);
                }
            }

            _buffedCharacters.Clear();
        }

    }
}
