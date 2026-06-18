using UnityEngine;

namespace ScpAgent.Bot.Data
{
    public class AgentCacheData
    {
        public bool IsDataReady { get; set; }
        //public Bounds CurrentBounds { get; set; }
        public Vector3 center { get; set; }
        public float halfX;
        public float halfY;
        public float halfZ;
        // Puedes añadir aquí más variables si tu caché guardaba más cosas de la sala
    }
}