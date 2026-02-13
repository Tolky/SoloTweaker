using System;
using HookDOTS.API.Attributes;
using ProjectM;
using ProjectM.Gameplay.Clan;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace SoloTweaker.Patches;

/// <summary>
/// HookDOTS event-driven patches replacing Harmony + polling.
/// Two hooks:
///   1. ServerBootstrapSystem — connect/disconnect events + smart timer
///   2. ClanSystem_Server — clan join/leave/kick via ChangedTeamEvent
/// </summary>
public static class ConnectionEventPatches
{
    static EntityQuery _connectQuery;
    static EntityQuery _disconnectQuery;
    static float _nextTimerExpiry = float.MaxValue;
    static float _nextErrorLogTime;

    [EcsSystemUpdatePostfix(typeof(ServerBootstrapSystem))]
    public static void OnServerBootstrap()
    {
        try
        {
            var world = SoloBuffLogic.GetServerWorld();
            if (world == null || !world.IsCreated) return;

            // Deferred refresh from config reload (buffs cleared last tick, now safe to reapply)
            if (SoloBuffLogic.ConsumePendingRefresh())
            {
                SoloBuffLogic.UpdateSoloBuffs();
                ScheduleNextExpiry();
                return;
            }

            var em = world.EntityManager;

            if (_connectQuery == default)
                _connectQuery = em.CreateEntityQuery(ComponentType.ReadOnly<UserConnectedServerEvent>());
            if (_disconnectQuery == default)
                _disconnectQuery = em.CreateEntityQuery(ComponentType.ReadOnly<UserDisconnectedServerEvent>());

            // Immediate reaction to connect/disconnect
            if (!_connectQuery.IsEmptyIgnoreFilter || !_disconnectQuery.IsEmptyIgnoreFilter)
            {
                SoloBuffLogic.UpdateSoloBuffs();
                ScheduleNextExpiry();
                return;
            }

            // Precise timer: fire exactly when the next offline threshold expires
            if (_nextTimerExpiry < float.MaxValue)
            {
                float now = UnityEngine.Time.time;
                if (now >= _nextTimerExpiry)
                {
                    SoloBuffLogic.UpdateSoloBuffs();
                    ScheduleNextExpiry();
                }
            }
        }
        catch (Exception ex)
        {
            float now = UnityEngine.Time.time;
            if (now >= _nextErrorLogTime)
            {
                _nextErrorLogTime = now + 30f;
                Plugin.Instance?.Log.LogError($"[SoloTweaker] ConnectionEventPatches error: {ex}");
            }
        }
    }

    /// <summary>
    /// Compute the exact game-time when the next timer expires.
    /// Called after every UpdateSoloBuffs to schedule the next check.
    /// </summary>
    internal static void ScheduleNextExpiry()
    {
        if (!SoloBuffLogic.HasActiveTimers)
        {
            _nextTimerExpiry = float.MaxValue;
            return;
        }

        float secsLeft = SoloBuffLogic.GetSecondsUntilNextExpiry();
        if (secsLeft >= float.MaxValue)
        {
            _nextTimerExpiry = float.MaxValue;
            return;
        }

        _nextTimerExpiry = UnityEngine.Time.time + secsLeft;
    }

    internal static void Reset()
    {
        _connectQuery = default;
        _disconnectQuery = default;
        _nextTimerExpiry = float.MaxValue;
    }
}

public static class ClanEventPatches
{
    static EntityQuery _changedTeamQuery;
    static float _nextErrorLogTime;

    [EcsSystemUpdatePostfix(typeof(ClanSystem_Server))]
    public static void OnClanChange()
    {
        try
        {
            var world = SoloBuffLogic.GetServerWorld();
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            if (_changedTeamQuery == default)
                _changedTeamQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ChangedTeamEvent>());

            if (!_changedTeamQuery.IsEmptyIgnoreFilter)
            {
                SoloBuffLogic.UpdateSoloBuffs();
                ConnectionEventPatches.ScheduleNextExpiry();
            }
        }
        catch (Exception ex)
        {
            float now = UnityEngine.Time.time;
            if (now >= _nextErrorLogTime)
            {
                _nextErrorLogTime = now + 30f;
                Plugin.Instance?.Log.LogError($"[SoloTweaker] ClanEventPatches error: {ex}");
            }
        }
    }

    internal static void Reset()
    {
        _changedTeamQuery = default;
    }
}
