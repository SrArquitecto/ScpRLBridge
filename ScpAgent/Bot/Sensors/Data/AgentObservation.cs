using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using PlayerRoles;

namespace ScpAgent.Bot.Sensors.Data
{
    public class AgentObservation
    {
        // Estado básico y GPS
        public Team Faction { get; set; }
        public float FactionId { get; set; }
        public RoleTypeId Role { get; set; }
        public float RoleId { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float PosicionLocalX { get; set; }
        public float PosicionLocalY { get; set; }
        public float PosicionLocalZ { get; set; }
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
        public float RoundTimeRemaining { get; set; }
        public int CanInteract { get; set; }
        public int LastAction { get; set; }
        public float Health { get; set; }
        public bool AmIHurt { get; set; }
        public string Zone { get; set; }
        public string Room { get; set; }
        public int CurrentRoomTypeId { get; set; }
        public bool HasKeycard { get; set; }
        public int KeycardTier { get; set; }


        public List<InventoryItemData> Inventory { get; set; } = new List<InventoryItemData>(32);
        public int   InventorySlots  { get; set; } // slots libres (max 8)
            // Munición en reserva por tipo — separada del inventario
        public float CountKeycards { get; set; } 
        public float CountFirearms { get; set; } 
        public float CountMedicals { get; set; } 
        public float CountArmor { get; set; }
        public float CountGrenades { get; set; } 
        public float CountScpItems { get; set; } 
        public float CountOthers   { get; set; } 
        public int   Ammo9x19        { get; set; }
        public int   Ammo12gauge     { get; set; }
        public int   Ammo556x45      { get; set; }
        public int   Ammo762x39      { get; set; }
        public int   Ammo44cal       { get; set; }



        // Listas de Entorno Cercano
        public List<ItemData> NearItems { get; set; } = new List<ItemData>(32);
        public List<DoorData> NearDoors { get; set; } = new List<DoorData>(32);
        public List<LiftData> NearLifts { get; set; } = new List<LiftData>(32);
        public List<LockerData> NearLockers { get; set; } = new List<LockerData>(32);
        public List<RoomData> NearRooms { get; set; } = new List<RoomData>(32);
        public List<ActorData> NearPlayers { get; set; } = new List<ActorData>(32);
        public float CountEnemies { get; set; }
        public float CountFriends { get; set; }
        public float CountNeutrals { get; set; }
        public float ClosestEnemyDistance { get; set; }
        public int TotalRooms => NearRooms.Count;

        // Datos del Raycast de apuntado (Aim)
        public float AimTarget { get; set; }
        public string AimRoom { get; set; }
        public string HitName { get; set; }
        public string AimDoorName { get; set; }
        public float AimDistance { get; set; }
        public float HitX { get; set; }
        public float HitY { get; set; }
        public float HitZ { get; set; }
        public float ForwardX { get; set; }
        public float ForwardZ { get; set; }

        //"PELOS" PARA DETERMINAR SI COCHA CONTRA UN OBSTACULO
        public float[] WhiskerDist { get; set; } = new float[8]; // distancia normalizada 0-1
        public float[] WhiskerType { get; set; } = new float[8]; // tipo codificado

        public float  DamageReceived  { get; set; }  // normalizado 0-1 (/ MaxHealth)
        public float  TimeSinceLastDamage { get; set; }
        public string DamageType      { get; set; }  // "Firearm", "Explosion", "Scp", "Fall", "Unknown"
        public float  DamageDirX      { get; set; }  // vector normalizado hacia el atacante (X)
        public float  DamageDirZ      { get; set; }  // vector normalizado hacia el atacante (Z)
        public bool   AttackerInMemory { get; set; } // si el atacante está en _memoriaJugadores

        // Estado del entrenamiento de RL
        public List<GraphNodeData> GraphNodes   { get; set; } = new List<GraphNodeData>(16);                                                                                                                                                   
        public float[,]           GraphAdjacency { get; set; } = new float[16, 16];                                                                                                                                                            
        public float[]            GraphMask      { get; set; } = new float[16];
        public float Reward { get; set; }
        public bool Done { get; set; }

        public void Clear()
        {
            Inventory.Clear();
            NearItems.Clear();
            NearDoors.Clear();
            NearLifts.Clear();
            NearLockers.Clear();
            NearRooms.Clear();
            NearPlayers.Clear();
            GraphNodes.Clear();
        }
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
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
    }

    public class ItemData
    {
        public string Type      { get; set; }
        public string Category  { get; set; }
        public float  Prioridad { get; set; }
        public int    Tier      { get; set; } // ← nuevo: 0 si no es keycard, 1-5 si lo es
        public float  Distance  { get; set; }
        public float  RelX      { get; set; }
        public float  RelY      { get; set; }
        public float  RelZ      { get; set; }
        public float  RealRelX  { get; set; }
        public float  RealRelY  { get; set; }
        public float  RealRelZ  { get; set; }
        public bool   EsRecordado { get; set; }
        public float  Antiguedad  { get; set; }
    }

    public class InventoryItemData
    {
        public string Type       { get; set; }
        public string Category   { get; set; }
        public int    Tier       { get; set; }
        public bool   IsEquipped { get; set; }
        public int    Ammo       { get; set; } // balas EN el cargador del arma equipada (no reserva)
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
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
                     
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
        public bool IsLocked { get; set; }
        public bool IsClosed { get; set; } 
        public bool IsInElevator { get; set; }
        public bool CanUse { get; set; }
        public int CurrentLevel { get; set; }
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
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
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
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
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
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
        public bool EsRecordado {get; set;}
        public float Antiguedad {get; set;}
        public int RoomInstanceId { get; set; }
    }


    public class ActorData
    {
        public string Role { get; set; }     // Ej: "ClassD", "Scp173", "Scientist"
        public float FactionId { get; set; }
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

    public class GraphNodeData                                                                                                                                                                                                             
     {                                                                                                                                                                                                                                      
         public int   Id;                                                                                                                                                                                                                   
         public int   TypeId;                                                                                                                                                                                                               
         public float RelX, RelY, RelZ;                                                                                                                                                                                                     
         public float PosX, PosY, PosZ;                                                                                                                                                                                                     
         public float Prioridad;                                                                                                                                                                                                            
         public float Distancia;                                                                                                                                                                                                            
         public float DistNorm;                                                                                                                                                                                                             
         public int   VisitCount;                                                                                                                                                                                                           
         public float Antiguedad;                                                                                                                                                                                                           
         public float EsActual;                                                                                                                                                                                                             
         public float TieneEnemigo;                                                                                                                                                                                                         
         public float TieneLoot;                                                                                                                                                                                                            
         public float PuertaBloq;                                                                                                                                                                                                           
                                                                                                                                                                                                                                            
         public static GraphNodeData Pad() => new GraphNodeData                                                                                                                                                                             
         {                                                                                                                                                                                                                                  
             Id = -1, TypeId = 0,                                                                                                                                                                                                           
             RelX = 0f,
             RelY = 0f,
             RelZ = 0f,                                                                                                                                                                                                       
             PosX = 0f,
             PosY = 0f,
             PosZ = 0f,                                                                                                                                                                                                       
             Prioridad = 0f, Distancia = 9999f, DistNorm = 1f,                                                                                                                                                                              
             VisitCount = 0, Antiguedad = 0f,                                                                                                                                                                                               
             EsActual = 0f,
             TieneEnemigo = 0f,
             TieneLoot = 0f,
             PuertaBloq = 0f                                                                                                                                                                          
         };                                                                                                                                                                                                                                 
     }                                      

}
