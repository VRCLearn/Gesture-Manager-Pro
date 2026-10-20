using System;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    [DefaultExecutionOrder(-9500)]
    public sealed class VrSimulationPump : MonoBehaviour
    {
        public Action Tick;
        private void Update() => Tick?.Invoke();
    }
}
