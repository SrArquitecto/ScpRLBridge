using UnityEngine;
using System;
using ScpAgent.Bot.Sensors.Modules;

namespace ScpAgent.Bot.Sensors.Data
{
    // Contexto compartido entre módulos — evita pasar 10 parámetros a cada método
    public class SensorContext
    {   
        public float RANGO_MAPA         { get; set; }
        public Vector3 Pos              { get; set; }
        public Vector3 CamRotation      { get; set; }
        public float LastYaw            { get; set; }
        public float LastPitch          { get; set; }

        public Vector3 LastPos  { get; set; }   
        public Vector3 MisOjos          { get; set; }
        public Vector3 MiMirada         { get; set; }
        public float   HalfX            { get; set; }
        public float   HalfY            { get; set; }
        public float   HalfZ            { get; set; }
        public Vector3   Center         { get; set; }
        public int     PlayerTier       { get; set; }
        public float   DeltaTime        { get; set; }
        public float   TimeNow          { get; set; }
        public float Delta              { get; set; }
        public float Reward             { get; set; }
        public int LastAction           { get; set; }
        public bool    Done             { get; set; }

        // Delegados de estrategia — opcionales, solo módulos que los necesiten
        public Func<ItemType, float>  FnPrioridad  { get; set; }
        public Func<ItemType, string> FnCategoria  { get; set; }

        // Referencia a memoria de jugadores — compartida entre módulos
        // (DamageModule necesita saber si el atacante está en memoria de PlayerVisionModule)
        public PlayerVisionModule PlayerVision { get; set; }
    }
}