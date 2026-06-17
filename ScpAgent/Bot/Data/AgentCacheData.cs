using UnityEngine;

namespace ScpAgent.Bot.Data
{
    public class AgentCacheData
    {
        public bool IsDataReady { get; set; }
        public Bounds CurrentBounds { get; set; }
        
        // Puedes añadir aquí más variables si tu caché guardaba más cosas de la sala
    }
}