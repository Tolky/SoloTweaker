using System;
using UnityEngine;

namespace SoloTweaker
{
    public class SoloTweakerBehaviour : MonoBehaviour
    {
        private const float TICK_INTERVAL = 10f;
        private float _timer;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            if (_timer < TICK_INTERVAL)
                return;

            _timer = 0f;

            try
            {
                SoloBuffLogic.UpdateSoloBuffs();
            }
            catch
            {
                
            }
        }
    }
}
