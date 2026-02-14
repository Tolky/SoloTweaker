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
        private static readonly Dictionary<Entity, DateTime> _clanMemberDepartureTimes = new();

        // Track which users have been notified about a pending timer (avoid spam)
        private static readonly HashSet<Entity> _timerNotified = new();

        /// <summary>
        /// True when offline-threshold timers are pending (disconnect or clan leave).
        /// Used by EventPatches to skip periodic scans when no timers are active.
        /// </summary>
        internal static bool HasActiveTimers =>
            _userDisconnectTimes.Count > 0 ||
            _userClanLeaveTimes.Count > 0 ||
            _clanMemberDepartureTimes.Count > 0;

        /// <summary>
        /// Returns seconds until the next offline-threshold timer expires.
        /// Used by EventPatches to schedule the exact next scan instead of polling.
        /// Returns float.MaxValue if no timers are active.
        /// </summary>
        internal static float GetSecondsUntilNextExpiry()
        {
            var minutes = ClanOfflineThresholdMinutes;
            if (minutes <= 0) return 0f;

            TimeSpan threshold = TimeSpan.FromMinutes(minutes);
            DateTime nowUtc = DateTime.UtcNow;
            TimeSpan minRemaining = TimeSpan.MaxValue;

            foreach (var kvp in _userDisconnectTimes)
            {
                var remaining = threshold - (nowUtc - kvp.Value);
                if (remaining < minRemaining) minRemaining = remaining;
            }
            foreach (var kvp in _userClanLeaveTimes)
            {
                var remaining = threshold - (nowUtc - kvp.Value);
                if (remaining < minRemaining) minRemaining = remaining;
            }
            foreach (var kvp in _clanMemberDepartureTimes)
            {
                var remaining = threshold - (nowUtc - kvp.Value);
                if (remaining < minRemaining) minRemaining = remaining;
            }

            if (minRemaining == TimeSpan.MaxValue) return float.MaxValue;
            if (minRemaining <= TimeSpan.Zero) return 0f;
            return (float)minRemaining.TotalSeconds;
        }

        // Reusable per-tick snapshot: clan -> members
        struct UserSnapshot
        {
            public Entity UserEntity;
            public User User;
            public Entity ClanEntity;
        }

        static readonly List<UserSnapshot> _snapshotAll = new();
        static readonly Dictionary<Entity, List<UserSnapshot>> _clanMap = new();

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

        /// <summary>
        /// Build a single snapshot of all users and group by clan. O(n) single pass.
        /// </summary>
        static void BuildSnapshot(EntityManager em)
        {
            _snapshotAll.Clear();
            _clanMap.Clear();

            var users = GetUserQuery(em).ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var userEntity = users[i];
                    if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                        continue;

                    var user = em.GetComponentData<User>(userEntity);
                    var clan = user.ClanEntity._Entity;
                    if (clan != Entity.Null && !em.Exists(clan))
                        clan = Entity.Null;

                    var snap = new UserSnapshot
                    {
                        UserEntity = userEntity,
                        User = user,
                        ClanEntity = clan
                    };

                    _snapshotAll.Add(snap);

                    if (clan != Entity.Null)
                    {
                        if (!_clanMap.TryGetValue(clan, out var list))
                        {
                            list = new List<UserSnapshot>();
                            _clanMap[clan] = list;
                        }
                        list.Add(snap);
                    }
                }
            }
            finally
            {
                users.Dispose();
            }
        }

        public static void UpdateSoloBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
                return;

            var em = serverWorld.EntityManager;

            // Single O(n) pass to build clan map
            BuildSnapshot(em);

            // Track disconnect times and clan changes
            for (int i = 0; i < _snapshotAll.Count; i++)
            {
                var snap = _snapshotAll[i];
                TrackUserState(em, snap.UserEntity, snap.User, snap.ClanEntity);
            }

            // Update buffs for all users using pre-built clan map
            for (int i = 0; i < _snapshotAll.Count; i++)
            {
                var snap = _snapshotAll[i];
                UpdateBuffForUser(em, snap.UserEntity, snap.User, snap.ClanEntity);
            }

            // Clean up stale entities
            CleanupStaleEntities(em);
        }

        static void TrackUserState(EntityManager em, Entity userEntity, User user, Entity currentClan)
        {
            // Track disconnect times
            if (!user.IsConnected && !_userDisconnectTimes.ContainsKey(userEntity))
            {
                // Use TimeLastConnected for players already offline (e.g. after server reboot)
                // instead of DateTime.UtcNow which would reset the timer
                DateTime disconnectTime;
                try { disconnectTime = DateTime.FromBinary(user.TimeLastConnected).ToUniversalTime(); }
                catch { disconnectTime = DateTime.UtcNow; }

                if (disconnectTime == DateTime.MinValue || disconnectTime > DateTime.UtcNow)
                    disconnectTime = DateTime.UtcNow;

                _userDisconnectTimes[userEntity] = disconnectTime;
            }
            else if (user.IsConnected && _userDisconnectTimes.ContainsKey(userEntity))
            {
                if (!_userClanLeaveTimes.ContainsKey(userEntity))
                    _userDisconnectTimes.Remove(userEntity);
            }

            // Track clan changes (kick/leave/join)
            if (user.IsConnected)
            {
                if (_userLastClan.TryGetValue(userEntity, out Entity lastClan))
                {
                    if (lastClan != Entity.Null && em.Exists(lastClan) && currentClan == Entity.Null)
                    {
                        // Only apply cooldown if user wasn't already solo (had no buff)
                        var charEntity = user.LocalCharacter._Entity;
                        bool wasSolo = charEntity != Entity.Null && _buffedCharacters.Contains(charEntity);

                        if (!wasSolo)
                        {
                            _userClanLeaveTimes[userEntity] = DateTime.UtcNow;
                        }

                        // Remaining clan members still get departure cooldown
                        _clanMemberDepartureTimes[lastClan] = DateTime.UtcNow;
                    }
                    else if (currentClan != Entity.Null && currentClan != lastClan)
                    {
                        _userClanLeaveTimes.Remove(userEntity);
                        _userDisconnectTimes.Remove(userEntity);
                    }
                }

                _userLastClan[userEntity] = currentClan;
            }
        }

        static void UpdateBuffForUser(EntityManager em, Entity userEntity, User user, Entity clanEntity)
        {
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

            bool isSolo = IsUserSolo(userEntity, user, clanEntity);
            bool wasBuff = _buffedCharacters.Contains(character) || BuffService.HasBuff(character);

            if (isSolo)
            {
                BuffService.ApplyBuff(userEntity, character);
                _buffedCharacters.Add(character);
                _timerNotified.Remove(userEntity);
                if (!wasBuff)
                    NotifyPlayer(em, userEntity, "<color=green>[SoloTweaker] Solo buffs applied.</color>");
            }
            else if (wasBuff)
            {
                BuffService.RemoveBuff(character);
                _buffedCharacters.Remove(character);
                NotifyPlayer(em, userEntity, "<color=red>[SoloTweaker] Solo buffs removed.</color>");

                // If there's a cooldown timer, notify about it
                NotifyTimerIfNeeded(em, userEntity, clanEntity);
            }
            else
            {
                // Not solo, no buff — check if we should notify about a pending timer
                NotifyTimerIfNeeded(em, userEntity, clanEntity);
            }
        }

        // Public entry point for commands that need to update a single user
        public static void UpdateBuffForUser(EntityManager em, Entity userEntity)
        {
            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                return;

            var user = em.GetComponentData<User>(userEntity);
            var clan = user.ClanEntity._Entity;
            if (clan != Entity.Null && !em.Exists(clan))
                clan = Entity.Null;

            // Rebuild snapshot so IsUserSolo has clan data
            BuildSnapshot(em);
            UpdateBuffForUser(em, userEntity, user, clan);
        }

        /// <summary>
        /// Diagnostic: returns a list of reasons why the user is NOT eligible.
        /// Empty list = eligible.
        /// </summary>
        internal static List<string> GetEligibilityReasons(EntityManager em, Entity userEntity)
        {
            var reasons = new List<string>();

            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
            {
                reasons.Add("User entity invalid.");
                return reasons;
            }

            var user = em.GetComponentData<User>(userEntity);
            var clanEntity = user.ClanEntity._Entity;
            if (clanEntity != Entity.Null && !em.Exists(clanEntity))
                clanEntity = Entity.Null;

            if (_snapshotAll.Count == 0)
                BuildSnapshot(em);

            var minutes = ClanOfflineThresholdMinutes;
            if (minutes < 0) minutes = 0;
            TimeSpan offlineThreshold = TimeSpan.FromMinutes(minutes);
            DateTime nowUtc = DateTime.UtcNow;

            if (clanEntity == Entity.Null)
            {
                if (_userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
                {
                    var elapsed = nowUtc - leaveTime;
                    if (elapsed < offlineThreshold)
                    {
                        var remaining = offlineThreshold - elapsed;
                        reasons.Add($"<color=red>[BLOCKING]</color> Recently left clan — cooldown {FormatTimeSpanShort(remaining)} remaining.");
                    }
                }

                if (reasons.Count == 0)
                    reasons.Add("No clan — you are eligible.");
                return reasons;
            }

            // Clan departure timer
            if (_clanMemberDepartureTimes.TryGetValue(clanEntity, out DateTime clanDepartureTime))
            {
                var elapsed = nowUtc - clanDepartureTime;
                if (elapsed < offlineThreshold)
                {
                    var remaining = offlineThreshold - elapsed;
                    reasons.Add($"<color=red>[BLOCKING]</color> A member recently left this clan — cooldown {FormatTimeSpanShort(remaining)} remaining.");
                }
            }

            if (!_clanMap.TryGetValue(clanEntity, out var clanMembers))
            {
                reasons.Add("Clan map empty (no members found in snapshot).");
                return reasons;
            }

            reasons.Add($"Clan has {clanMembers.Count} member(s) in snapshot.");

            for (int i = 0; i < clanMembers.Count; i++)
            {
                var member = clanMembers[i];
                if (member.UserEntity == userEntity)
                    continue;

                string memberName = member.User.CharacterName.ToString();

                if (member.User.IsConnected)
                {
                    reasons.Add($"<color=red>[BLOCKING]</color> {memberName} is ONLINE.");
                    continue;
                }

                if (_userDisconnectTimes.TryGetValue(member.UserEntity, out DateTime disconnectTime))
                {
                    var elapsed = nowUtc - disconnectTime;
                    if (elapsed < offlineThreshold)
                    {
                        var remaining = offlineThreshold - elapsed;
                        reasons.Add($"<color=red>[BLOCKING]</color> {memberName} disconnected {FormatTimeSpanShort(elapsed)} ago — {FormatTimeSpanShort(remaining)} left.");
                    }
                    else
                    {
                        reasons.Add($"<color=green>[OK]</color> {memberName} offline {FormatTimeSpanShort(elapsed)} (threshold passed).");
                    }
                }
                else
                {
                    DateTime lastOnlineUtc;
                    try { lastOnlineUtc = DateTime.FromBinary(member.User.TimeLastConnected).ToUniversalTime(); }
                    catch { lastOnlineUtc = DateTime.MinValue; }

                    if (lastOnlineUtc == DateTime.MinValue)
                    {
                        reasons.Add($"<color=red>[BLOCKING]</color> {memberName} has unknown last connect time — treated as recent.");
                    }
                    else
                    {
                        var elapsed = nowUtc - lastOnlineUtc;
                        if (elapsed < offlineThreshold)
                        {
                            var remaining = offlineThreshold - elapsed;
                            reasons.Add($"<color=red>[BLOCKING]</color> {memberName} last seen {FormatTimeSpanShort(elapsed)} ago — {FormatTimeSpanShort(remaining)} left.");
                        }
                        else
                        {
                            reasons.Add($"<color=green>[OK]</color> {memberName} last seen {FormatTimeSpanShort(elapsed)} ago (threshold passed).");
                        }
                    }
                }
            }

            return reasons;
        }

        /// <summary>
        /// Determine if user qualifies as solo. Uses pre-built _clanMap — O(clan_size) not O(n).
        /// </summary>
        static bool IsUserSolo(Entity userEntity, User user, Entity clanEntity)
        {
            // No clan -> check if they recently left
            if (clanEntity == Entity.Null)
            {
                if (_userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
                {
                    var thresholdMinutes = ClanOfflineThresholdMinutes;
                    if (thresholdMinutes < 0) thresholdMinutes = 0;
                    var timeSinceLeave = DateTime.UtcNow - leaveTime;

                    if (timeSinceLeave < TimeSpan.FromMinutes(thresholdMinutes))
                        return false;

                    _userClanLeaveTimes.Remove(userEntity);
                }
                return true;
            }

            var minutes = ClanOfflineThresholdMinutes;
            if (minutes < 0) minutes = 0;
            TimeSpan offlineThreshold = TimeSpan.FromMinutes(minutes);
            DateTime nowUtc = DateTime.UtcNow;

            // Check if someone recently left this clan
            if (_clanMemberDepartureTimes.TryGetValue(clanEntity, out DateTime clanDepartureTime))
            {
                if (nowUtc - clanDepartureTime < offlineThreshold)
                    return false;
                _clanMemberDepartureTimes.Remove(clanEntity);
            }

            // Use pre-built clan map instead of re-querying
            if (!_clanMap.TryGetValue(clanEntity, out var clanMembers))
                return true; // no clan members found (shouldn't happen)

            for (int i = 0; i < clanMembers.Count; i++)
            {
                var member = clanMembers[i];
                if (member.UserEntity == userEntity)
                    continue;

                if (member.User.IsConnected)
                    return false; // another clan member is online

                // Check offline duration
                if (_userDisconnectTimes.TryGetValue(member.UserEntity, out DateTime disconnectTime))
                {
                    if (nowUtc - disconnectTime < offlineThreshold)
                        return false;
                }
                else
                {
                    DateTime lastOnlineUtc;
                    try { lastOnlineUtc = DateTime.FromBinary(member.User.TimeLastConnected).ToUniversalTime(); }
                    catch { lastOnlineUtc = DateTime.MinValue; }

                    if (lastOnlineUtc == DateTime.MinValue)
                        return false; // unknown = treat as recently online

                    if (nowUtc - lastOnlineUtc < offlineThreshold)
                        return false;
                }
            }

            return true;
        }

        internal struct StatusData
        {
            public int ClanTotal;
            public int ClanOnline;
            public bool IsEnabled;
            public bool IsEligible;
            public bool IsBuffActive;
            public TimeSpan? TimerRemaining;
        }

        internal static bool TryGetStatusData(EntityManager em, Entity userEntity, out StatusData data)
        {
            data = default;

            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                return false;

            var user = em.GetComponentData<User>(userEntity);
            var charEntity = user.LocalCharacter._Entity;
            if (charEntity == Entity.Null || !em.Exists(charEntity))
                return false;

            var clanEntity = user.ClanEntity._Entity;
            if (clanEntity != Entity.Null && !em.Exists(clanEntity))
                clanEntity = Entity.Null;

            if (_snapshotAll.Count == 0)
                BuildSnapshot(em);

            if (clanEntity != Entity.Null)
                GetClanMemberCounts(clanEntity, out data.ClanTotal, out data.ClanOnline);
            else
            {
                data.ClanTotal = 1;
                data.ClanOnline = user.IsConnected ? 1 : 0;
            }

            data.IsEnabled = !IsUserOptedOut(em, userEntity);
            data.IsEligible = IsUserSolo(userEntity, user, clanEntity);
            data.IsBuffActive = _buffedCharacters.Contains(charEntity) && BuffService.HasBuff(charEntity);

            // Timer: only relevant when enabled but not yet eligible
            if (data.IsEnabled && !data.IsEligible)
            {
                var remaining = clanEntity != Entity.Null
                    ? GetClanSoloCooldownRemaining(userEntity, clanEntity)
                    : null;

                if (remaining == null && _userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
                {
                    var mins = ClanOfflineThresholdMinutes;
                    if (mins > 0)
                    {
                        var left = TimeSpan.FromMinutes(mins) - (DateTime.UtcNow - leaveTime);
                        if (left > TimeSpan.Zero)
                            remaining = left;
                    }
                }

                if (remaining != null && remaining.Value > TimeSpan.Zero)
                    data.TimerRemaining = remaining;
            }

            return true;
        }

        /// <summary>
        /// Get clan member counts from pre-built snapshot. O(clan_size).
        /// </summary>
        static void GetClanMemberCounts(Entity clanEntity, out int totalMembers, out int onlineMembers)
        {
            totalMembers = 0;
            onlineMembers = 0;

            if (clanEntity == Entity.Null || !_clanMap.TryGetValue(clanEntity, out var members))
                return;

            totalMembers = members.Count;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].User.IsConnected)
                    onlineMembers++;
            }
        }

        /// <summary>
        /// Get remaining cooldown from pre-built snapshot. O(clan_size).
        /// </summary>
        static TimeSpan? GetClanSoloCooldownRemaining(Entity selfUserEntity, Entity clanEntity)
        {
            if (clanEntity == Entity.Null || !_clanMap.TryGetValue(clanEntity, out var members))
                return null;

            var minutes = ClanOfflineThresholdMinutes;
            if (minutes <= 0)
                return TimeSpan.Zero;

            TimeSpan threshold = TimeSpan.FromMinutes(minutes);
            DateTime nowUtc = DateTime.UtcNow;
            TimeSpan maxRemaining = TimeSpan.Zero;
            bool hasRemaining = false;

            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member.UserEntity == selfUserEntity)
                    continue;

                if (member.User.IsConnected)
                    return null; // someone else online

                if (_userDisconnectTimes.TryGetValue(member.UserEntity, out DateTime disconnectTime))
                {
                    var remaining = threshold - (nowUtc - disconnectTime);
                    if (remaining > TimeSpan.Zero && (!hasRemaining || remaining > maxRemaining))
                    {
                        maxRemaining = remaining;
                        hasRemaining = true;
                    }
                }
                else
                {
                    DateTime lastOnlineUtc;
                    try { lastOnlineUtc = DateTime.FromBinary(member.User.TimeLastConnected).ToUniversalTime(); }
                    catch { lastOnlineUtc = DateTime.MinValue; }

                    if (lastOnlineUtc != DateTime.MinValue)
                    {
                        var remaining = threshold - (nowUtc - lastOnlineUtc);
                        if (remaining > TimeSpan.Zero && (!hasRemaining || remaining > maxRemaining))
                        {
                            maxRemaining = remaining;
                            hasRemaining = true;
                        }
                    }
                    else
                    {
                        if (!hasRemaining || threshold > maxRemaining)
                        {
                            maxRemaining = threshold;
                            hasRemaining = true;
                        }
                    }
                }
            }

            return hasRemaining ? maxRemaining : TimeSpan.Zero;
        }

        static string FormatTimeSpanShort(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1.0) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1.0) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
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

            if (charEntity != Entity.Null && em.Exists(charEntity) && BuffService.HasBuff(charEntity))
            {
                BuffService.RemoveBuff(charEntity);
                _buffedCharacters.Remove(charEntity);
                NotifyPlayer(em, userEntity, "<color=red>[SoloTweaker] Solo buffs removed.</color>");
            }
        }

        internal static void SetUserOptIn(EntityManager em, Entity userEntity)
        {
            _optOutUsers.Remove(userEntity);
        }

        /// <summary>
        /// Toggle opt-out state. Returns true if now opted OUT (disabled).
        /// </summary>
        internal static bool ToggleUserOptOut(EntityManager em, Entity userEntity)
        {
            if (_optOutUsers.Contains(userEntity))
            {
                SetUserOptIn(em, userEntity);
                return false;
            }
            else
            {
                SetUserOptOut(em, userEntity);
                return true;
            }
        }

        internal static void ClearAllBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated)
                return;

            var em = serverWorld.EntityManager;

            var buffedChars = new List<Entity>(_buffedCharacters);
            foreach (var character in buffedChars)
            {
                if (em.Exists(character))
                    BuffService.RemoveBuff(character);
            }

            _buffedCharacters.Clear();
        }

        /// <summary>
        /// Clear all buffs and schedule reapply on next tick.
        /// Two-frame approach avoids timing issue where destroyed buff
        /// entity still exists when ApplyBuff checks.
        /// </summary>
        private static bool _pendingRefresh;

        internal static void RequestRefresh()
        {
            ClearAllBuffs();
            _pendingRefresh = true;
        }

        internal static bool ConsumePendingRefresh()
        {
            if (!_pendingRefresh) return false;
            _pendingRefresh = false;
            return true;
        }

        internal static void Reset()
        {
            _serverWorld = null;
            _userQuery = null;
            _buffedCharacters.Clear();
            _optOutUsers.Clear();
            _userDisconnectTimes.Clear();
            _userClanLeaveTimes.Clear();
            _userLastClan.Clear();
            _clanMemberDepartureTimes.Clear();
            _timerNotified.Clear();
            _snapshotAll.Clear();
            _clanMap.Clear();
            _pendingRefresh = false;
        }

        static void NotifyTimerIfNeeded(EntityManager em, Entity userEntity, Entity clanEntity)
        {
            if (_timerNotified.Contains(userEntity))
                return;

            var remaining = clanEntity != Entity.Null
                ? GetClanSoloCooldownRemaining(userEntity, clanEntity)
                : null;

            // Also check clan-leave timer for players with no clan
            if (remaining == null && _userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
            {
                var mins = ClanOfflineThresholdMinutes;
                if (mins > 0)
                {
                    var left = TimeSpan.FromMinutes(mins) - (DateTime.UtcNow - leaveTime);
                    if (left > TimeSpan.Zero)
                        remaining = left;
                }
            }

            if (remaining != null && remaining.Value > TimeSpan.Zero)
            {
                _timerNotified.Add(userEntity);
                NotifyPlayer(em, userEntity,
                    $"<color=yellow>[SoloTweaker] Solo buffs available in {FormatTimeSpanShort(remaining.Value)}.</color>");
            }
        }

        static void NotifyPlayer(EntityManager em, Entity userEntity, string message)
        {
            try
            {
                if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                    return;
                var user = em.GetComponentData<User>(userEntity);
                if (!user.IsConnected)
                    return;
                var msg = new Unity.Collections.FixedString512Bytes(message);
                ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
            }
            catch { }
        }

        static readonly List<Entity> _staleKeys = new();

        static void CleanupStaleEntities(EntityManager em)
        {
            _staleKeys.Clear();
            foreach (var e in _buffedCharacters)
                if (!em.Exists(e)) _staleKeys.Add(e);
            foreach (var e in _staleKeys)
                _buffedCharacters.Remove(e);

            _staleKeys.Clear();
            foreach (var e in _optOutUsers)
                if (!em.Exists(e)) _staleKeys.Add(e);
            foreach (var e in _staleKeys)
                _optOutUsers.Remove(e);

            _staleKeys.Clear();
            foreach (var e in _timerNotified)
                if (!em.Exists(e)) _staleKeys.Add(e);
            foreach (var e in _staleKeys)
                _timerNotified.Remove(e);

            _staleKeys.Clear();
            foreach (var kvp in _userDisconnectTimes)
                if (!em.Exists(kvp.Key)) _staleKeys.Add(kvp.Key);
            foreach (var key in _staleKeys)
            {
                _userDisconnectTimes.Remove(key);
                _userClanLeaveTimes.Remove(key);
                _userLastClan.Remove(key);
            }

            _staleKeys.Clear();
            foreach (var kvp in _clanMemberDepartureTimes)
                if (!em.Exists(kvp.Key)) _staleKeys.Add(kvp.Key);
            foreach (var key in _staleKeys)
                _clanMemberDepartureTimes.Remove(key);
        }
    }
}
