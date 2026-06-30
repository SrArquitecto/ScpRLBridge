using UnityEngine;
using System;
using System.Collections.Generic;
using ScpAgent.Bot.Sensors.Modules;

namespace ScpAgent.Bot.Sensors.Data
{
    // Contexto compartido entre módulos — evita pasar 10 parámetros a cada método
    public class SensorContext
    {   
        public int AgentId { get; set; }
        public float        HalfX            { get; set; }
        public float        HalfY            { get; set; }
        public float        HalfZ            { get; set; }
        public Vector3      Center           { get; set; }
        public float        Delta            { get; set; }
        public float        Reward           { get; set; }
        // Acción multi-discreto anterior (7 ejes): long,lat,yaw,pitch,inv,interact,jump
        public int[]        LastAction       { get; set; } = new int[] { 0, 0, 0, 0, 0, 0, 0 };
        public bool         Done             { get; set; }

        // Datos del jugador para evitar recomputación
        public Vector3      PlayerPosition   { get; set; }
        public int          PlayerTier       { get; set; }

        // Acumuladores para RoomsGraphModule — rompen dependencia de orden con obs
        public readonly List<Vector3> EnemyPositions       = new List<Vector3>(8);
        public readonly List<Vector3> LootPositions        = new List<Vector3>(16);
        public readonly List<Vector3> BlockedDoorPositions = new List<Vector3>(16);
    }
}