using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using PlayerRoles;

namespace ScpAgent.Bot.Data
{
    public class AgentObservation
    {
        // Estado básico y GPS
        public Team Faction { get; set; }
        public float FactionId { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public float GPSX { get; set; }
        public float GPSY { get; set; }
        public float GPSZ { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }

        // Físicas y Tiempos
        public float VerVel { get; set; }
        public float LatVel { get; set; }
        public float LinVel { get; set; }
        public float AngVelYaw { get; set; }
        public float AngVelPitch { get; set; }
        public float TimeLastAction { get; set; }
        public int CanInteract { get; set; }
        public int LastAction { get; set; }
        public float Health { get; set; }
        public string Zone { get; set; }
        public string Room { get; set; }
        public bool HasKeycard { get; set; }
        public int KeycardTier { get; set; }

        // Listas de Entorno Cercano
        public List<KeycardData> NearKeycards { get; set; } = new List<KeycardData>();
        public List<DoorData> NearDoors { get; set; } = new List<DoorData>();
        public List<LiftData> NearLifts { get; set; } = new List<LiftData>();
        public List<LockerData> NearLockers { get; set; } = new List<LockerData>();
        public List<RoomData> NearRooms { get; set; } = new List<RoomData>();
        public List<ActorData> NearPlayers { get; set; } = new List<ActorData>();

        public int TotalRooms => NearRooms.Count;

        // Datos del Raycast de apuntado (Aim)
        public string AimTarget { get; set; }
        public string AimRoom { get; set; }
        public string HitName { get; set; }
        public string AimDoorName { get; set; }
        public float AimDistance { get; set; }
        public float HitX { get; set; }
        public float HitY { get; set; }
        public float HitZ { get; set; }
        public float ForwardX { get; set; }
        public float ForwardZ { get; set; }

        // Estado del entrenamiento de RL
        public float Reward { get; set; }
        public bool Done { get; set; }
    }

    // Sub-estructuras para el entorno
    public class KeycardData
    {
        public string Type { get; set; }
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public float RealRelX { get; set; }
        public float RealRelY { get; set; }
        public float RealRelZ { get; set; }
        public float Distance { get; set; }
    }

    public class DoorData
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string ColliderName { get; set; }
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public float RealRelX { get; set; }
        public float RealRelY { get; set; }
        public float RealRelZ { get; set; }
        public float Distance { get; set; }
        public int RequiredTier { get; set; }
        public bool CanOpen { get; set; }
        public bool IsOpen { get; set; }
    }

    public class LiftData
    {
        public string Type { get; set; }
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public float RealRelX { get; set; }
        public float RealRelY { get; set; }
        public float RealRelZ { get; set; }
        public float Distance { get; set; }
        public bool IsMoving { get; set; }
        public bool CanUse { get; set; }
        public int CurrentLevel { get; set; }
    }

    public class LockerData
    {
        public string Type { get; set; }
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public bool IsOpen { get; set; }
        public float RealRelX { get; set; }
        public float RealRelY { get; set; }
        public float RealRelZ { get; set; }
        public float Distance { get; set; }
        public bool HasIsOpen { get; set; }
    }

    public class RoomData
    {
        public string Nombre { get; set; }
        public int Id { get; set;}
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float NormX { get; set; }
        public float NormY { get; set; }
        public float NormZ { get; set; }
        public float UbiX { get; set; }
        public float UbiY { get; set; }
        public float UbiZ { get; set; }
        public float Prioridad { get; set; }
        public float Dist { get; set; }
        public float DistNorm { get; set; }
    }
    public class Habitaciones
    {
        public string NombreHabitacion { get; set; }
        public int IdHabitacion { get; set;}
        public Vector3 PosicionReal { get; set; }
        public float PosicionNormX { get; set; }
        public float PosicionNormY { get; set; }
        public float PosicionNormZ { get; set; }
        public float PosicionUbiX { get; set; }
        public float PosicionUbiY { get; set; }
        public float PosicionUbiZ { get; set; }
        public float Prioridad { get; set; }
        public float Distancia { get; set; }
        public float DistanciaNormalizada { get; set; }// Opcional, pero muy útil para tu PPO
    }


    public class ActorData
    {
        public string Role { get; set; }     // Ej: "ClassD", "Scp173", "Scientist"
        public int FactionId { get; set; }
        public float Hostilidad { get; set; }
        public string Team { get; set; }     // Ej: "SCP", "Foundation", "Chaos"
        public float RelX { get; set; }
        public float RelY { get; set; }
        public float RelZ { get; set; }
        public float Distance { get; set; }
        public float Health { get; set; } // Útil para decidir si atacar o huir
        public float MiradaHaciaMi { get; set; }
        public bool  EsRecordado { get; set; }
        public float Antiguedad  { get; set; }
    }

    public struct Actor
    {
            public Player  Player;
            public float   Distancia;
            public bool    EsRecordado;
            public Vector3 PosicionRecordada;
            public float   Antiguedad;
    }

    public class MemoriaJugador
    {
        public Vector3 UltimaPosicion;
        public float   UltimoTimestamp;
        public bool    VistoEsteFrame;
    }
}
