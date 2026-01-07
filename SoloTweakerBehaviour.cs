using System;
using UnityEngine;

namespace SoloTweaker
{
    public class SoloTweakerBehaviour : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // Run every frame for instant buff updates on equipment changes
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
