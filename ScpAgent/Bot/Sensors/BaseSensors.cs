using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;
using PlayerRoles;
using Exiled.API.Enums;
using System.Linq;

namespace ScpAgent.Bot.Sensors
{
    public abstract class BaseSensors : ISensors
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
        protected const int UPDATE_FREQUENCY = 20;
        protected int _frameCounter = UPDATE_FREQUENCY;
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

        public List<ActorData> _cachedNearPlayers { get; set;} = new List<ActorData>();
        public List<Actor> _listaTemporalPlayers { get; set;} = new List<Actor>();
     

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
        protected readonly ActorData[]   _playerPool   = new ActorData[5];
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
        private readonly Dictionary<int, MemoriaJugador> _memoriaJugadores 
            = new Dictionary<int, MemoriaJugador>();
        protected const float TIEMPO_OLVIDO_OBJETOS = 45f;

        private readonly VisualMemory _memoriaPuertas  = new VisualMemory(TIEMPO_OLVIDO_OBJETOS);
        private readonly VisualMemory _memoriaLifts    = new VisualMemory(TIEMPO_OLVIDO_OBJETOS);
        private readonly VisualMemory _memoriaRooms    = new VisualMemory(TIEMPO_OLVIDO_OBJETOS);

        protected const float TIEMPO_OLVIDO = 5f;

        protected static readonly IComparer<RaycastHit> _raycastComparer =
            Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
        protected static readonly Comparison<(Door d, float dist)> _doorComparison =
            (a, b) => a.dist.CompareTo(b.dist);
        
        

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public BaseSensors(int agentId)
        {
            _agentId = agentId;
            //_RefrescarPosicionBase();
            //_player = player;
        }

        public virtual void Init()
        {
            for (int i = 0; i < _doorPool.Length;    i++) _doorPool[i]    = new DoorData();
            for (int i = 0; i < _liftPool.Length;    i++) _liftPool[i]    = new LiftData();
            for (int i = 0; i < _playerPool.Length;    i++) _playerPool[i]    = new ActorData();
            for (int i = 0; i < _roomPool.Length;    i++) _roomPool[i]    = new RoomData();
        }

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
        public virtual AgentObservation GetCurrentState(
            float fixedDelta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {
            Vector3 pos         = _player.Position;
            Vector3 camRotation = _player.CameraTransform.rotation.eulerAngles;
            

            // ── Velocidades angulares ──────────────────────────────────────────
            float deltaYaw   = Mathf.DeltaAngle(_lastYaw,   camRotation.y);
            float deltaPitch = Mathf.DeltaAngle(_lastPitch,  camRotation.x);
            float angVelYaw   = deltaYaw   / fixedDelta;
            float angVelPitch = deltaPitch / fixedDelta;
            _lastYaw   = camRotation.y;
            _lastPitch = camRotation.x;

            // ── Velocidades lineales ───────────────────────────────────────────
            float vLin, vLat, vVer;
            _CalcularVelocidades(pos, fixedDelta, camRotation.y, out vLin, out vLat, out vVer);
            bool intentaMoverse = (accionAnterior == 0 || accionAnterior == 1 || accionAnterior == 2 || accionAnterior == 3 || accionAnterior == 4);
            
            // ── Posición relativa dentro de la sala ────────────────────────────
            float relX = 0f, relY = 0f, relZ = 0f;
            AgentCacheData data = GetData();
            Vector3 relativePos = pos - data.center;
            var faction = _player.Role.Team;

            if (data.halfX > 0) relX = Mathf.Clamp(relativePos.x / data.halfX, -1f, 1f);
            if (data.halfY > 0) relY = Mathf.Clamp(relativePos.y / data.halfY, -1f, 1f);
            if (data.halfZ > 0) relZ = Mathf.Clamp(relativePos.z / data.halfZ, -1f, 1f);

            var observation = new AgentObservation
            {
                Faction = faction,
                FactionId = (float)faction/8f,
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                RelX = relX,  RelY = relY,  RelZ = relZ,
                GPSX = Mathf.Clamp(pos.x / RANGO_MAPA, -1f, 1f),
                GPSY = Mathf.Clamp(pos.y / RANGO_MAPA, -1f, 1f),
                GPSZ = Mathf.Clamp(pos.z / RANGO_MAPA, -1f, 1f),
                Yaw   = camRotation.y,
                Pitch = camRotation.x,
                VerVel    = vVer,
                LatVel    = vLat,
                LinVel    = vLin,
                AngVelYaw   = angVelYaw,
                AngVelPitch = angVelPitch,
                Health      = _player.Health / _player.MaxHealth,
                Zone        = _player.CurrentRoom?.Zone.ToString() ?? "Unknown",
                Room        = _player.CurrentRoom?.Type.ToString() ?? "Unknown",
                HasKeycard  = false,
                KeycardTier = 0,
                LastAction  = accionAnterior,
                Reward      = reward,
                Done        = done
            };

            _ProcesarAimRaycast(observation);

            _CargarElementosBaseCercanos(observation, pos, data.halfX, data.halfY, data.halfZ, playerTier);

            _CargarPersonajesCercanos(observation, pos, 100);
            Log.Debug($"[Perf-HUMAN] Tras CargarElementosCercanos: obs.NearDoors={observation.NearDoors.Count}");
            return observation;
        }
        

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

        protected AgentCacheData GetData()
        {
            AgentCacheData data;
            if (_player == null || !agentCacheData.TryGetValue(_player.Id, out data) || !data.IsDataReady)
            {
                data = _fallbackCacheData;
            }
            return data;
        }
        // ───────────────────────────────────────────────────────────────────────
        // ELEMENTOS CERCANOS
        // ───────────────────────────────────────────────────────────────────────
        private void _CargarElementosBaseCercanos(AgentObservation observation, Vector3 pos, float halfX, float halfY, float halfZ, int playerTier)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACachePuertas(observation);
                _CopiarACacheAscensores(observation);
                _CopiarACacheHabitaciones(observation);
                _CopiarACacheJugadores(observation);
                return;
            }
            _frameCounter = 0;
            if (_frameCounter == 0) { // justo tras recargar
                Log.Debug($"[Perf] Puertas cargadas en _cachedNearDoors: {_cachedNearDoors.Count}");
                Log.Debug($"[Perf] Puertas cargadas en _cachedNearRooms: {_cachedNearRooms.Count}");
            }

            _cachedNearDoors.Clear();
            _cachedNearLifts.Clear();
            _cachedNearRooms.Clear();
            _doorsConDist.Clear();
            _doorColliderCache.Clear();

            try { _CargarPuertas(observation, pos, halfX, halfY, halfZ, playerTier); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en PUERTAS: {ex.Message}"); }
            _CopiarACachePuertas(observation);
            Log.Debug($"[Perf-BASE] Tras CopiarACachePuertas: obs.NearDoors={observation.NearDoors.Count}");

            
            try { _CargarAscensores(observation, pos, halfX, halfY, halfZ); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ASCENSORES: {ex.Message}"); }
            _CopiarACacheAscensores(observation);

            
            try { _CargarRooms(observation, playerTier); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ROOMS: {ex.Message}"); }
            _CopiarACacheHabitaciones(observation);
        }
        private void _CargarAscensores(AgentObservation obs, Vector3 pos, float halfX, float halfY, float halfZ)
        {   

            if (_cachedLifts == null) _cachedLifts = new List<Lift>(Lift.List);
            else { _cachedLifts.Clear(); _cachedLifts.AddRange(Lift.List); }
        
            int liftCount = 0;
            foreach (var l in _cachedLifts)
            {
                if (l == null || liftCount >= 3) break;
                if (l.Transform == null) continue;
                float d = Vector3.Distance(l.Transform.position, pos);
                if (d > 50f) continue;
        
                // Reutilizar objeto del pool en vez de new LiftData
                var ld = _liftPool[liftCount];
                ld.Type         = l.Type.ToString();
                ld.Distance     = d / 50f;
                ld.IsMoving     = l.IsMoving;
                ld.CanUse       = !l.IsMoving;
                ld.CurrentLevel = l.CurrentLevel;
                ld.RelX = (l.Position.x - pos.x) / 50f;
                ld.RelY = (l.Position.y - pos.y) / 50f;
                ld.RelZ = (l.Position.z - pos.z) / 50f;
                ld.RealRelX     = l.Position.x - pos.x;
                ld.RealRelY     = l.Position.y - pos.y;
                ld.RealRelZ     = l.Position.z - pos.z;
                _cachedNearLifts.Add(ld);
                liftCount++;
            }
        }
        private void _CargarRooms(AgentObservation obs, int playerTier)
        {
            //var roomListSnapshot = Room.List.ToList();
            if (_cachedRooms == null || _cachedRooms.Count == 0)
                _cachedRooms = new List<Room>(Room.List);
                
            _roomsPriorizada.Clear();
            ObtenerListaSalasPriorizadas(playerTier);
            //Log.Info($"_roomsPriorizada: {_roomsPriorizada.Count}");
            // ── SALAS PRIORIZADAS ─────────────────────────────────────────────────
            //var habitaciones = ObtenerListaSalasPriorizadas(_player, playerTier);
            int roomsCounter = 0;
            foreach (var h in _roomsPriorizada)
            {
                if (h == null || roomsCounter >= 5) break;
                if (h.PosicionReal == null) continue;
                var r = _roomPool[roomsCounter];
                r.Nombre    = h.NombreHabitacion;
                r.Id        = h.IdHabitacion;
                r.PosX      = h.PosicionReal.x;
                r.PosY      = h.PosicionReal.y;
                r.PosZ      = h.PosicionReal.z;
                r.NormX     = h.PosicionNormX;
                r.NormY     = h.PosicionNormY;
                r.NormZ     = h.PosicionNormZ;
                r.UbiX      = h.PosicionUbiX;
                r.UbiY      = h.PosicionUbiY;
                r.UbiZ      = h.PosicionUbiZ;
                r.Prioridad = h.Prioridad;
                r.Dist      = h.Distancia;
                r.DistNorm  = h.DistanciaNormalizada;
                _cachedNearRooms.Add(r);
                roomsCounter++;
            }
            //Log.Info($"_cachedNearRooms: {_cachedNearRooms.Count}");
        }

        private void _CargarPuertas(AgentObservation obs, Vector3 pos, float halfX, float halfY, float halfZ, int playerTier)
        {
            if (_cachedDoors == null)
                _cachedDoors = new List<Door>(Door.List);
            else
            {
                _cachedDoors.Clear();
                _cachedDoors.AddRange(Door.List);
            }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;

            _memoriaPuertas.MarcarTodosNoVistos();

            // ── 1. Filtrar por rango y comprobar visibilidad real ────────────────
            _doorsConDist.Clear();
            foreach (var d in _cachedDoors)
            {
                if (d == null) continue;

                try
                {
                    if (d.GameObject == null || d.Transform == null) continue;

                    float dist = Vector3.Distance(d.Transform.position, pos);
                    if (dist >= 50f) continue;

                    // Filtro de visibilidad — FOV + raycast
                    if (!_EsVisible(misOjos, miMirada, d.Position, dist, d.GameObject)) continue;

                    // Visible ahora — registrar/actualizar memoria
                    int doorId = d.GameObject.GetInstanceID();
                    _memoriaPuertas.RegistrarVisto(doorId, d.Position, ahora, estadoBool: d.IsOpen);

                    _doorsConDist.Add((d, dist));
                }
                catch { continue; }
            }
            _doorsConDist.Sort(_doorComparison);

            // ── 2. Volcar puertas VISTAS AHORA al pool ────────────────────────────
            int doorCount = 0;
            foreach (var (d, dist) in _doorsConDist)
            {
                if (doorCount >= 15) break;

                try
                {
                    if (d.GameObject == null) continue;

                    int doorId = d.GameObject.GetInstanceID();
                    if (!_doorColliderCache.TryGetValue(doorId, out string colliderName))
                    {
                        colliderName = "Unknown";
                        var colliders = d.GameObject.GetComponentsInChildren<Collider>(true);
                        var valid = System.Array.Find(colliders,
                            c => !c.isTrigger && !c.name.Contains("TouchScreenPanel") && !c.name.Contains("Frame"));
                        if (valid != null) colliderName = valid.name;
                        _doorColliderCache[doorId] = colliderName;
                    }

                    int reqTier = GetDoorRequiredTier(d);

                    var dd = _doorPool[doorCount];
                    dd.Type         = d.RequiredPermissions.ToString();
                    dd.Name         = d.Name;
                    dd.ColliderName = colliderName;
                    dd.Distance     = dist / 50f;
                    dd.RequiredTier = reqTier;
                    dd.CanOpen      = playerTier >= reqTier;
                    dd.IsOpen       = d.IsOpen;
                    dd.RelX         = (d.Position.x - pos.x) / 50f;
                    dd.RelY         = (d.Position.y - pos.y) / 50f;
                    dd.RelZ         = (d.Position.z - pos.z) / 50f;
                    dd.RealRelX     = d.Position.x - pos.x;
                    dd.RealRelY     = d.Position.y - pos.y;
                    dd.RealRelZ     = d.Position.z - pos.z;
                    dd.EsRecordado  = false;
                    dd.Antiguedad   = 0f;
                    _cachedNearDoors.Add(dd);
                    doorCount++;
                }
                catch { continue; }
            }

            // ── 3. Volcar puertas RECORDADAS (no vistas ahora, dentro de memoria) ─
            foreach (var kv in _memoriaPuertas.Entradas)
            {
                if (doorCount >= 15) break;
                if (kv.Value.VistoEsteCiclo) continue; // ya procesada arriba

                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 50f * 1.2f) continue; // ya muy lejos, no relevante

                var dd = _doorPool[doorCount];
                dd.Type         = "Unknown"; // no tenemos el wrapper Door a mano, solo posición
                dd.Name         = "Recordada";
                dd.ColliderName = "Unknown";
                dd.Distance     = dist / 50f;
                dd.RequiredTier = 0;
                dd.CanOpen      = false;
                dd.IsOpen       = mem.EstadoBoolCache; // último estado conocido
                dd.RelX         = (mem.UltimaPosicion.x - pos.x) / 50f;
                dd.RelY         = (mem.UltimaPosicion.y - pos.y) / 50f;
                dd.RelZ         = (mem.UltimaPosicion.z - pos.z) / 50f;
                dd.RealRelX     = mem.UltimaPosicion.x - pos.x;
                dd.RealRelY     = mem.UltimaPosicion.y - pos.y;
                dd.RealRelZ     = mem.UltimaPosicion.z - pos.z;
                dd.EsRecordado  = true;
                dd.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO_OBJETOS; // normalizado 0-1
                _cachedNearDoors.Add(dd);
                doorCount++;
            }

            _memoriaPuertas.PurgarOlvidados(ahora);
        }
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

        private void _CargarPersonajesCercanos(AgentObservation obs, Vector3 pos, float rangoRadar = 30f)
        {
            _listaTemporalPlayers.Clear();
            _cachedNearPlayers.Clear();

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;

            // Marcar todos como "no vistos este frame" antes de re-evaluar
            foreach (var mem in _memoriaJugadores.Values)
                mem.VistoEsteFrame = false;
            //Log.Info($"Player.List: {Player.List.Count()} | ReferenceHub.AllHubs: {ReferenceHub.AllHubs.Count} | _player.IsAlive: {_player.IsAlive}");
            // ── 1. Detección directa (visión + raycast) ─────────────────────────
            
            
            foreach (var hub in ReferenceHub.AllHubs)
            {
                Player target = Player.Get(hub);
                // Filtrar hub de servidor/dummy — normalmente nickname vacío o rol None
                if (target == null) continue;
                if (target == _player) continue;
                if (!target.IsAlive || target.IsDead) continue;
                if (target.Role.Type == RoleTypeId.Spectator || target.Role.Type == RoleTypeId.None) continue;
                if (string.IsNullOrEmpty(target.Nickname)) continue; // ← filtra el hub fantasma
                if (target.GameObject == null || target.Transform == null) continue;

                float d = Vector3.Distance(target.Position, pos);
                if (d > rangoRadar) continue;

                Vector3 dirHaciaEl = (target.Position - pos).normalized;
                float dot = Vector3.Dot(miMirada, dirHaciaEl);
                if (Vector3.Dot(miMirada, dirHaciaEl) < 0.5f) continue;
                //if (d < 1f) // solo logear casos de muy corta distancia para no saturar
                    //Log.Info($"[FOV-Debug] Agente {_agentId} dist={d:F2} dot={dot:F2} miMirada={miMirada} dirHaciaEl={dirHaciaEl}");

                //if (dot < 0.5f) continue;
                Vector3 susOjos = target.CameraTransform != null ? target.CameraTransform.position : target.Position + Vector3.up;
                Vector3 dirRayo = susOjos - misOjos;

                int layerMaskSinJugadores = ~LayerMask.GetMask("Players"); // si existe esa layer
                if (d >= 1f)
                {
                    // Solo gastamos CPU en el Raycast si el objeto está a más de 1 metro
                    if (Physics.Raycast(misOjos, dirRayo.normalized, out RaycastHit hit, d + 0.5f,
                        layerMaskSinJugadores, QueryTriggerInteraction.Ignore))
                    {
                        // Si el rayo choca con algo que NO es el objeto objetivo (hub), está tapado por una pared
                        if (hit.collider.gameObject != target.GameObject && hit.transform.root != target.Transform.root)
                        {
                            continue; // Rompe la línea de visión, saltamos al siguiente objeto
                        }
                    }
                }

                // Visto correctamente — actualizar/crear memoria
                if (!_memoriaJugadores.TryGetValue(target.Id, out var mem))
                {
                    mem = new MemoriaJugador();
                    _memoriaJugadores[target.Id] = mem;
                }
                mem.UltimaPosicion   = target.Position;
                mem.UltimoTimestamp  = Time.time;
                mem.VistoEsteFrame   = true;

                _listaTemporalPlayers.Add(new Actor { Player = target, Distancia = d, EsRecordado = false });
            }

            // ── 2. Añadir jugadores RECORDADOS (no vistos ahora pero dentro del tiempo de olvido) ──
            var idsAEliminar = new List<int>();
            foreach (var kv in _memoriaJugadores)
            {
                if (kv.Value.VistoEsteFrame) continue; // ya procesado arriba

                float antiguedad = Time.time - kv.Value.UltimoTimestamp;
                if (antiguedad > TIEMPO_OLVIDO)
                {
                    idsAEliminar.Add(kv.Key); // demasiado viejo, olvidar
                    continue;
                }

                var playerObj = Player.Get(kv.Key);
                if (playerObj == null || !playerObj.IsAlive)
                {
                    idsAEliminar.Add(kv.Key);
                    continue;
                }

                float distRecordada = Vector3.Distance(kv.Value.UltimaPosicion, pos);
                if (distRecordada > rangoRadar * 1.5f) continue; // demasiado lejos, no relevante

                _listaTemporalPlayers.Add(new Actor {
                    Player = playerObj,
                    Distancia = distRecordada,
                    EsRecordado = true,
                    PosicionRecordada = kv.Value.UltimaPosicion,
                    Antiguedad = antiguedad
                });
            }

            foreach (var id in idsAEliminar)
                _memoriaJugadores.Remove(id);

            _cachedNearPlayers.Clear();
            // ── 3. Ordenar y volcar al pool (igual que antes, con campo extra) ────
            _listaTemporalPlayers.Sort((a, b) => a.Distancia.CompareTo(b.Distancia));

            int count = 0;
            int maxPlayers = Mathf.Min(_listaTemporalPlayers.Count, 5);

            for (int i = 0; i < maxPlayers; i++)
            {
                var item   = _listaTemporalPlayers[i];
                var target = item.Player;
                var pd     = _playerPool[count];
                var role   = target.Role.Type;

                pd.Role      = role.ToString();
                pd.FactionId = (int)role;
                pd.EsRecordado = item.EsRecordado;          // ← nuevo campo
                pd.Antiguedad  = item.EsRecordado ? item.Antiguedad / TIEMPO_OLVIDO : 0f; // normalizado 0-1

                Vector3 posReferencia = item.EsRecordado ? item.PosicionRecordada : target.Position;
                Vector3 relPos = posReferencia - pos;

                pd.Distance = item.Distancia / rangoRadar;
                pd.RelX     = relPos.x / rangoRadar;
                pd.RelY     = relPos.y / rangoRadar;
                pd.RelZ     = relPos.z / rangoRadar;

                // Hostilidad y mirada solo tienen sentido si lo vemos AHORA
                if (!item.EsRecordado)
                {
                    pd.Hostilidad = _calcularHostilidad(_player, target);
                    pd.Health     = target.MaxHealth > 0 ? (target.Health / target.MaxHealth) : 0f;

                    Vector3 ojosEnemigo  = target.CameraTransform != null ? target.CameraTransform.position : target.Position + Vector3.up;
                    Vector3 dirHaciaMi   = (misOjos - ojosEnemigo).normalized;
                    Vector3 miradaEnemigo = target.CameraTransform != null ? target.CameraTransform.forward : target.Transform.forward;
                    pd.MiradaHaciaMi = Vector3.Dot(miradaEnemigo, dirHaciaMi);
                }
                else
                {
                    // Sin información actual — valores neutros
                    pd.Hostilidad   = _calcularHostilidad(_player, target);
                    pd.Health       = -1f; // -1 indica "desconocido" para la red
                    pd.MiradaHaciaMi = 0f;
                }

                _cachedNearPlayers.Add(pd);
                count++;
            }
            //Log.Info($"Agent: {_agentId} _cachedNearPlayers: {_cachedNearPlayers.Count}");
            _CopiarACacheJugadores(obs);
            
        }
        private int GetDoorRequiredTier(Door d)
        {
            var perms = (int)d.RequiredPermissions;
            if (perms == 0)        return 0;
            if ((perms & 64)  != 0) return 7;
            if ((perms & 128) != 0) return 7;
            if ((perms & 16)  != 0) return 5;
            if ((perms & 32)  != 0) return 5;
            if ((perms & 4)   != 0) return 3;
            if ((perms & 8)   != 0) return 3;
            if ((perms & 2)   != 0) return 4;
            if ((perms & 256) != 0) return 6;
            return 1;
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

        private void _CopiarACachePuertas(AgentObservation obs)
        {
            obs.NearDoors.Clear();
            obs.NearDoors.AddRange(_cachedNearDoors);
        }
        private void _CopiarACacheAscensores(AgentObservation obs)
        {
            obs.NearLifts.Clear();
            obs.NearLifts.AddRange(_cachedNearLifts);
        }
        private void _CopiarACacheHabitaciones(AgentObservation obs)
        {
            obs.NearRooms.Clear();
            obs.NearRooms.AddRange(_cachedNearRooms);
        }
        private void _CopiarACacheJugadores(AgentObservation obs)
        {
            obs.NearPlayers.Clear();
            obs.NearPlayers.AddRange(_cachedNearPlayers);
        }

        
        // ── Actualizar InvalidarCachesMapa para incluir lockers ───────────────────
 

        // ───────────────────────────────────────────────────────────────────────
        // HELPERS
        // ───────────────────────────────────────────────────────────────────────
        
        public virtual void ResetEstado()
        {

            _lastPos   = Vector3.zero;
            _lastYaw   = 0f;
            _lastPitch = 0f;

            // ── Cache del raycast ────────────────────────────────────────────
            _aimCacheCounter   = 0;
            _cachedAimTarget   = "None";
            _cachedAimDist     = 0f;
            _cachedAimRoom     = "Unknown";
            _cachedAimDoorName = "None";
            _cachedHitName     = "None";
            _cachedHitX        = 0f;
            _cachedHitY        = 0f;
            _cachedHitZ        = 0f;
            _cachedForwardX    = 0f;
            _cachedForwardZ    = 0f;
            _frameCounter = UPDATE_FREQUENCY;
            _aimCacheCounter = AIM_CACHE_FRAMES;

            _cachedNearPlayers.Clear();
            _listaTemporalPlayers.Clear();
            _memoriaJugadores.Clear();
            _cachedNearDoors.Clear();
            _cachedNearRooms.Clear();
            _doorsConDist.Clear();
            _roomsPriorizada.Clear();
            _cachedNearLifts.Clear();
            _cachedDoors  = null;
            _cachedLifts  = null;
            _cachedRooms = null;
            _doorColliderCache.Clear();
        }
        

        public void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }

        private void ObtenerListaSalasPriorizadas(int tierTarjeta)
        {
            //List<Habitaciones> listaPriorizada = new List<Habitaciones>();
            if (_player == null || _player.Transform == null) return;

            foreach (Room sala in _cachedRooms)
            {
                // Ignoramos salas desconocidas o zonas muertas
                
                if (sala == null || sala.GameObject == null) continue;
                if (sala.Type == RoomType.Unknown) continue;
                float distancia = Vector3.Distance(_player.Position, sala.Position);
                if (distancia > 500f) continue;

                float prioridad = 0;

                // --- LÓGICA DINÁMICA DE PRIORIDADES ---
                switch (sala.Type)
                {
                    case RoomType.Lcz914:
                        // Si necesita mejorar la tarjeta, 914 es la prioridad máxima absoluta
                        prioridad = tierTarjeta is >= 1 and < 3 ? 80f : 0f;
                        break;

                    case RoomType.LczCheckpointB:
                    case RoomType.LczCheckpointA:
                        // Si ya tiene tarjeta buena para salir de LCZ, los checkpoints son vitales
                        prioridad = tierTarjeta >= 3 ? 100f : 0f;
                        break;
                    case RoomType.LczPlants:
                        prioridad = tierTarjeta <= 2 ? 40f : 5f;
                        break;
                    case RoomType.LczClassDSpawn:
                        prioridad = 0;
                        break;

                    case RoomType.LczArmory:
                        prioridad = tierTarjeta > 3 ? 60f : 0f;
                        // Zonas de armas/loot (prioridad media-alta para sobrevivir)
                        
                        break;
                    case RoomType.Lcz330:
                        prioridad = tierTarjeta == 2 ? 100f : 0f;
                        break;
                    case RoomType.Lcz173:
                    case RoomType.LczGlassBox:
                    case RoomType.LczCafe:
                        prioridad = tierTarjeta < 3 ? 100f : 0f;
                        break;
                    case RoomType.LczToilets:
                        prioridad = tierTarjeta < 1 ? 80f : 0f;
                        break;


                    default:
                        // Pasillos, curvas y salas estándar tienen prioridad baja (solo sirven para transitar)
                        prioridad = 5f;
                        break;
                }

                //float distancia = Vector3.Distance(_player.Position, sala.Position);
                Vector3 vectorObjetivo = sala.Position - _player.Transform.position;
                Vector3 dirNormalizada = vectorObjetivo.normalized;
                float distNormalizada = Mathf.Clamp01(vectorObjetivo.magnitude / RANGO_MAPA);
                float salaNormX = Mathf.Clamp(sala.Position.x / RANGO_MAPA, -1f, 1f);
                float salaNormY = Mathf.Clamp(sala.Position.y / RANGO_MAPA, -1f, 1f); // Altura (LCZ vs HCZ)
                float salaNormZ = Mathf.Clamp(sala.Position.z / RANGO_MAPA, -1f, 1f);

                _roomsPriorizada.Add(new Habitaciones
                {
                    NombreHabitacion = sala.Type.ToString(),
                    IdHabitacion = (int)sala.Type,
                    PosicionReal = sala.Position,
                    PosicionNormX = dirNormalizada.x,
                    PosicionNormY = dirNormalizada.y,
                    PosicionNormZ = dirNormalizada.z,
                    PosicionUbiX = salaNormX,
                    PosicionUbiY = salaNormY,
                    PosicionUbiZ = salaNormZ,
                    Prioridad = prioridad/200f,
                    Distancia = distancia,
                    DistanciaNormalizada = distNormalizada
                });

            }

            _roomsPriorizada.Sort(_roomComparison);
        }

        private float _calcularHostilidad(Player player, Player objetivo)
        {
            Team faccionPlayer = player.Role.Team; 
            Team faccionObjetivo = objetivo.Role.Team;

        if (faccionPlayer == faccionObjetivo)
            return -1.0f;

        switch (faccionPlayer)
        {
        case PlayerRoles.Team.FoundationForces: // --- TU BOT ES NTF O GUARDIA ---
            if (faccionObjetivo == PlayerRoles.Team.Scientists)       return -1.0f; // Aliado
            if (faccionObjetivo == PlayerRoles.Team.ClassD)           return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.ChaosInsurgency)  return 1.0f;  // Hostil
            if (faccionObjetivo == PlayerRoles.Team.SCPs)             return 1.0f;  // Hostil
            break;

        case PlayerRoles.Team.ChaosInsurgency: // --- TU BOT ES CHAOS ---
            if (faccionObjetivo == PlayerRoles.Team.ClassD)           return -1.0f; // Aliado
            if (faccionObjetivo == PlayerRoles.Team.Scientists)       return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.FoundationForces) return 1.0f;  // Hostil
            if (faccionObjetivo == PlayerRoles.Team.SCPs)             return 1.0f;  // Hostil
            break;

        case PlayerRoles.Team.Scientists: // --- TU BOT ES CIENTÍFICO ---
            if (faccionObjetivo == PlayerRoles.Team.FoundationForces) return -1.0f; // Aliado
            if (faccionObjetivo == PlayerRoles.Team.ChaosInsurgency)  return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.ClassD)           return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.SCPs)             return 1.0f;  // Hostil
            break;

        case PlayerRoles.Team.ClassD: // --- TU BOT ES CLASE D ---
            if (faccionObjetivo == PlayerRoles.Team.ChaosInsurgency)  return -1.0f; // Aliado
            if (faccionObjetivo == PlayerRoles.Team.FoundationForces) return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.Scientists)       return 0.0f;  // Neutral
            if (faccionObjetivo == PlayerRoles.Team.SCPs)             return 1.0f;  // Hostil
            break;
        
        case PlayerRoles.Team.SCPs: // --- TU BOT ES CLASE D ---
            if (faccionObjetivo == PlayerRoles.Team.SCPs)  return -1.0f; // Aliado
            else return 1.0f;
            //break;
        }

        return 0.0f;
        }

        protected bool _EsVisible(Vector3 misOjos, Vector3 miMirada, Vector3 posObjetivo,
            float distancia, GameObject objetivoGO, float fovMinDot = 0.45f)
        {
            // FOV — fuera del cono de visión
            Vector3 dirHaciaObjetivo = (posObjetivo - misOjos).normalized;
            if (Vector3.Dot(miMirada, dirHaciaObjetivo) < fovMinDot) return false;

            // Muy cerca — asumimos visible sin raycast (evita ruido a corta distancia)
            if (distancia < 1.5f) return true;

            // Raycast — algo bloquea la línea de visión
            if (Physics.Raycast(misOjos, dirHaciaObjetivo, out RaycastHit hit, distancia + 0.5f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (objetivoGO != null &&
                    hit.collider.gameObject != objetivoGO &&
                    hit.transform.root != objetivoGO.transform.root)
                {
                    return false; // obstáculo entre el bot y el objeto
                }
            }

            return true;
        }
        
    }
}