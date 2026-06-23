using UnityEngine;
using System;
using ScpAgent.Bot.Sensors.Modules;

namespace ScpAgent.Bot.Sensors.Data
{
    // Contexto compartido entre módulos — evita pasar 10 parámetros a cada método
    public class SensorContext
    {   
        public float        HalfX            { get; set; }
        public float        HalfY            { get; set; }
        public float        HalfZ            { get; set; }
        public Vector3      Center           { get; set; }
        public float        Delta            { get; set; }
        public float        Reward           { get; set; }
        public int          LastAction       { get; set; }
        public bool         Done             { get; set; }
    }
}