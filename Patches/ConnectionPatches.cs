using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace SoloTweaker.Patches;

/// <summary>
/// Replaces MonoBehaviour Update loop. Detects connection events via
/// ServerBootstrapSystem.OnUpdate postfix and runs buff updates:
/// - Immediately on connect/disconnect events
/// - Throttled scan every 5s for timer expiry
/// </summary>
[HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUpdate))]
internal static class ConnectionPatches
{
    static EntityQuery? _connectQuery;
    static EntityQuery? _disconnectQuery;
    static float _nextScanTime;
    static float _nextErrorLogTime;

    static void Postfix(ServerBootstrapSystem __instance)
    {
        try
        {
            var em = __instance.EntityManager;
            bool hasEvent = false;

            if (_connectQuery == null)
                _connectQuery = em.CreateEntityQuery(ComponentType.ReadOnly<UserConnectedServerEvent>());
            if (_disconnectQuery == null)
                _disconnectQuery = em.CreateEntityQuery(ComponentType.ReadOnly<UserDisconnectedServerEvent>());

            if (!_connectQuery.Value.IsEmptyIgnoreFilter || !_disconnectQuery.Value.IsEmptyIgnoreFilter)
                hasEvent = true;

            float now = UnityEngine.Time.time;
            if (!hasEvent && now < _nextScanTime)
                return;

            _nextScanTime = now + 5f;
            SoloBuffLogic.UpdateSoloBuffs();
        }
        catch (Exception ex)
        {
            float now = UnityEngine.Time.time;
            if (now >= _nextErrorLogTime)
            {
                _nextErrorLogTime = now + 30f;
                Plugin.Instance?.Log.LogError($"[SoloTweaker] ConnectionPatches error: {ex}");
            }
        }
    }

    internal static void Reset()
    {
        _connectQuery = null;
        _disconnectQuery = null;
        _nextScanTime = 0;
    }
}
