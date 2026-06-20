using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;

namespace ScpAgent.Bot.Sensors
{
    public abstract class AgentSensorsBase : ISensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
        protected Player _player;
        protected int _agentId;
        public static readonly AgentObservation obsVacia = new AgentObservation { Done = true };
        protected const float RANGO_MAPA     = 500f;
        protected const int   AIM_CACHE_FRAMES = 5;

        // ── Estado de movimiento ────────────────────────────────────────────────
        protected Vector3 _lastPos;
        protected float   _lastYaw;
        protected float   _lastPitch;

        // ── Caché del raycast de apuntado ───────────────────────────────────────
        protected int    _aimCacheCounter  = AIM_CACHE_FRAMES;
        protected string _cachedAimTarget  = "None";
        protected float  _cachedAimDist    = 0f;
        protected string _cachedAimRoom    = "Unknown";
        protected string _cachedAimDoorName = "None";
        protected string _cachedHitName    = "None";
        protected float  _cachedHitX, _cachedHitY, _cachedHitZ;
        protected float  _cachedForwardX,  _cachedForwardZ;
        public List<DoorData> _cachedNearDoors { get; set; } = new List<DoorData>();
        public List<LiftData> _cachedNearLifts { get; set; } = new List<LiftData>();
        public List<RoomData> _cachedNearRooms { get; set; } = new List<RoomData>();
     

        protected readonly List<(Door d, float dist)> _doorsConDist = new List<(Door d, float dist)>(50);
        protected readonly List<Habitaciones> _roomsPriorizada = new List<Habitaciones>(120);



        // ── Buffers estáticos para raycasts (sin alloc) ────────────────────────
        protected readonly RaycastHit[] _raycastBuffer    = new RaycastHit[10];
        protected readonly RaycastHit[] _behindDoorBuffer = new RaycastHit[5];

        // ── Cache de datos de sala por agente ──────────────────────────────────
        public static Dictionary<int, AgentCacheData> agentCacheData = new Dictionary<int, AgentCacheData>();
        protected static readonly AgentCacheData _fallbackCacheData = new AgentCacheData 
        { 
            center = Vector3.zero,
            halfX = Vector3.one.x * 10f,
            halfY = Vector3.one.y * 10f,
            halfZ = Vector3.one.z * 10f,
        };

        protected readonly DoorData[]     _doorPool    = new DoorData[15];
        protected readonly LiftData[]     _liftPool    = new LiftData[3];
        protected readonly RoomData[]     _roomPool    = new RoomData[5];
        protected static readonly Comparison<Habitaciones> _roomComparison = 
    (a, b) => b.Prioridad.CompareTo(a.Prioridad) == 0 ? a.Distancia.CompareTo(b.Distancia) : b.Prioridad.CompareTo(a.Prioridad);


        // ── Listas globales cacheadas (se cargan UNA VEZ por ronda) ───────────
        public List<Door>   _cachedDoors;
        protected List<Lift>   _cachedLifts;
        public List<Room> _cachedRooms { get; set; } = new List<Room>();

        // ── Cache de collider name por puerta (evita GetComponentsInChildren) ──
        // Se llena la primera vez que se procesa cada puerta y se reutiliza
        public Dictionary<int, string> _doorColliderCache
            = new Dictionary<int, string>();

        protected static readonly IComparer<RaycastHit> _raycastComparer =
            Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
        protected static readonly Comparison<(Door d, float dist)> _doorComparison =
            (a, b) => a.dist.CompareTo(b.dist);
        
        

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public AgentSensorsBase(int agentId)
        {
            _agentId = agentId;
            //_RefrescarPosicionBase();
            //_player = player;
        }

        public abstract void Init();

        /// <summary>
        /// Actualiza la referencia al jugador tras un respawn sin recrear la instancia.
        /// Preserva las cachés de listas globales (puertas, lifts, keycards).
        /// </summary>
        public void VincularPlayer(Player freshPlayer)
        {
            _player = freshPlayer;

            // Actualizar posición base con la nueva posición de spawn
            if (_player != null)
            {
                _lastPos   = _player.Position;
                _lastYaw   = _player.CameraTransform.rotation.eulerAngles.y;
                _lastPitch = _player.CameraTransform.rotation.eulerAngles.x;
            }

            Log.Debug($"[AgentSensors] Player vinculado: {freshPlayer?.Nickname}");
        }

        /// <summary>
        /// Invalida las cachés de objetos del mapa (llamar tras Round.Restart).
        /// </summary>

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public abstract AgentObservation GetCurrentState(
            float fixedDelta, int accionAnterior, float reward, bool done);
        

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────
        protected void _CalcularVelocidades(Vector3 posActual, float deltaTime, float yaw,
            out float velLin, out float velLat, out float velVer)
        {
            if (deltaTime <= 0f)
            {
                velLin = 0f; velLat = 0f; velVer = 0f;
                return;
            }

            // 1. Calcular velocidad en el espacio del mundo (World Space)
            Vector3 delta = posActual - _lastPos;
            Vector3 worldVelocity = delta / deltaTime;

            // 2. PASO CLAVE: En lugar de usar _player.Transform (que está roto en el servidor),
            // creamos una rotación limpia usando el Yaw que ya funciona bien.
            Quaternion rotacionReal = Quaternion.Euler(0f, yaw, 0f);
            
            // Multiplicar por la inversa rota el vector del mundo al espacio local del bot
            Vector3 localVel = Quaternion.Inverse(rotacionReal) * worldVelocity;

            // 3. ASIGNACIÓN MATEMÁTICA REAL Y CORRECTA
            velLin = localVel.z;      // Adelante (+) o Atrás (-) -> ¡Ahora sí tendrá signo!
            velLat = localVel.x;      // Derecha (+) o Izquierda (-)
            velVer = worldVelocity.y; // Altura real del mundo (Y global). Si no sube/baja, será 0.0

            _lastPos = posActual;
        }
        // ───────────────────────────────────────────────────────────────────────
        // ELEMENTOS CERCANOS
        // ───────────────────────────────────────────────────────────────────────
        protected abstract void _CargarElementosCercanos(Vector3 pos,
            float halfX, float halfY, float halfZ,
            int playerTier, AgentObservation obs);

        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────
        protected void _ProcesarAimRaycast(AgentObservation obs)
        {
            _aimCacheCounter++;
            if (_aimCacheCounter < AIM_CACHE_FRAMES)
            {   
                //Log.Info($"CACHE AIMRAYCAST");
                _CopiarCacheAObs(obs);
                return;
            }
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            _aimCacheCounter = 0;
            var ray = new Ray(_player.CameraTransform.position, _player.CameraTransform.forward);
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastBuffer, 75f);


            

            // En _ProcesarAimRaycast:
            System.Array.Sort(_raycastBuffer, 0, hitCount, _raycastComparer);
            //System.Array.Sort(_raycastBuffer, 0, hitCount,
                //System.Collections.Generic.Comparer<RaycastHit>.Create(
                    //(x, y) => x.distance.CompareTo(y.distance)));

            Vector3 flat = new Vector3(ray.direction.x, 0, ray.direction.z).normalized;
            _cachedForwardX = flat.x;
            _cachedForwardZ = flat.z;

            RaycastHit validHit = default;
            bool hasHit = false;
            for (int i = 0; i < hitCount; i++)
            {
                var h = _raycastBuffer[i];
                if (h.collider.gameObject == _player.GameObject ||
                    h.collider.transform.root == _player.Transform.root) continue;
                validHit = h;
                hasHit   = true;
                break;
            }

            if (hasHit)
            {
                _cachedAimDist = validHit.distance;
                _cachedHitName = validHit.collider.name.ToLower();
                _cachedHitX    = validHit.point.x;
                _cachedHitY    = validHit.point.y;
                _cachedHitZ    = validHit.point.z;

                var door = validHit.collider.GetComponentInParent<
                    Interactables.Interobjects.DoorUtils.DoorVariant>();
                bool isDoor = door != null ||
                              _cachedHitName.Contains("door") ||
                              _cachedHitName.Contains("gate");

                if (isDoor)
                {
                    _cachedAimTarget = "Door";
                    if (door != null)
                    {
                        var exD = Door.Get(door);
                        if (exD != null) _cachedAimDoorName = exD.Name;
                    }
                }
                else if (validHit.collider.GetComponentInParent<
                    MapGeneration.Distributors.Locker>() != null)
                    _cachedAimTarget = "Locker";
                else if (validHit.collider.GetComponentInParent<
                    InventorySystem.Items.Pickups.ItemPickupBase>() != null)
                    _cachedAimTarget = "Pickup";
                else
                {
                    float y = ray.direction.y;
                    if      (y < -0.40f) _cachedAimTarget = "Floor";
                    else if (y >  0.40f) _cachedAimTarget = "Ceiling";
                    else                 _cachedAimTarget = "Wall";
                }

                var hitRoom = Room.Get(validHit.point);
                _cachedAimRoom = hitRoom != null ? hitRoom.Type.ToString() : "Unknown";
            }
            float elapsed = (UnityEngine.Time.realtimeSinceStartup - t0) * 1000f;
            if (elapsed > 2f)
                Log.Debug($"[Perf] AimRaycast tardó {elapsed:F1}ms hitCount={hitCount}");
            _CopiarCacheAObs(obs);
        }

        protected void _CopiarCacheAObs(AgentObservation obs)
        {
            obs.AimTarget   = _cachedAimTarget;
            obs.AimDistance = _cachedAimDist;
            obs.AimRoom     = _cachedAimRoom;
            obs.AimDoorName = _cachedAimDoorName;
            obs.HitName     = _cachedHitName;
            obs.HitX        = _cachedHitX;
            obs.HitY        = _cachedHitY;
            obs.HitZ        = _cachedHitZ;
            obs.ForwardX    = _cachedForwardX;
            obs.ForwardZ    = _cachedForwardZ;
        }

        protected abstract void _CopiarACache(AgentObservation obs);
        
        // ── Actualizar InvalidarCachesMapa para incluir lockers ───────────────────
 

        // ───────────────────────────────────────────────────────────────────────
        // HELPERS
        // ───────────────────────────────────────────────────────────────────────
        private void _RefrescarPosicionBase()
        {
            if (_player == null) return;
            ResetEstado();
            _lastPos   = _player.Position;
            _lastYaw   = _player.CameraTransform.rotation.eulerAngles.y;
            _lastPitch = _player.CameraTransform.rotation.eulerAngles.x;
            

        }
        
        public abstract void ResetEstado();
        

        public void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }

        protected abstract void ObtenerListaSalasPriorizadas(int tierTarjeta);
        
    }
}