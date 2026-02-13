using System;
using BepInEx.Logging;
using UnityEngine;

namespace SoloTweaker
{
    public class SoloTweakerBehaviour : MonoBehaviour
    {
        private float _nextUpdateTime;
        private float _nextErrorLogTime;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Time.time < _nextUpdateTime)
                return;
            _nextUpdateTime = Time.time + 1f;

            try
            {
                SoloBuffLogic.UpdateSoloBuffs();
            }
            catch (Exception ex)
            {
                if (Time.time >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = Time.time + 30f;
                    Plugin.Instance?.Log.LogError($"[SoloTweaker] UpdateSoloBuffs error: {ex}");
                }
            }
        }
    }
}
