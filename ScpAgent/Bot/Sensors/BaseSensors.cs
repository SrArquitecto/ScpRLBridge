using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;
using PlayerRoles;

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

            if (data.halfX > 0) relX = Mathf.Clamp(relativePos.x / data.halfX, -1f, 1f);
            if (data.halfY > 0) relY = Mathf.Clamp(relativePos.y / data.halfY, -1f, 1f);
            if (data.halfZ > 0) relZ = Mathf.Clamp(relativePos.z / data.halfZ, -1f, 1f);

            var observation = new AgentObservation
            {
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
                Health      = _player.Health / 100,
                Zone        = _player.CurrentRoom?.Zone.ToString() ?? "Unknown",
                Room        = _player.CurrentRoom?.Type.ToString() ?? "Unknown",
                HasKeycard  = false,
                KeycardTier = 0,
                LastAction  = accionAnterior,
                Reward      = reward,
                Done        = done
            };

            _CargarElementosBase(pos, data.halfX, data.halfY, data.halfZ, observation, playerTier);

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
        protected void _CargarElementosBase(Vector3 pos, float halfX, float halfY, float halfZ, AgentObservation obs, int playerTier)
        {
            if (halfX < 0.01f || halfY < 0.01f || halfZ < 0.01f)
            {
                _CopiarACache(obs);
                return;
            }
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACache(obs);
                return;
            }
            float t0 = UnityEngine.Time.realtimeSinceStartup;
        
            _cachedNearDoors.Clear();
            _cachedNearLifts.Clear();
            _cachedNearRooms.Clear();
            //_doorColliderCache.Clear();
            _frameCounter = 0;
        

            try { _CargarPuertas(pos, halfX, halfY, halfZ, playerTier); }
            catch (Exception ex) { Log.Error($"[Sensors] AGENTE: {_agentId} ID PLAYER: {_player.Id} NULL en PUERTAS: {ex.Message}"); }

            try { _CargarAscensores(pos, halfX, halfY, halfZ); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ASCENSORES: {ex.Message}"); }

            try { _CargarRooms(playerTier); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ROOMS: {ex.Message}"); }



            
            float elapsed = (UnityEngine.Time.realtimeSinceStartup - t0) * 1000f;
            //Log.Info($"Elapsed: {elapsed}");

            _CopiarACacheBase(obs);
        }

        private void _CargarAscensores(Vector3 pos, float halfX, float halfY, float halfZ)
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
        private void _CargarRooms(int playerTier)
        {
            if (_cachedRooms == null)
                _cachedRooms = new List<Room>(Room.List);

            _roomsPriorizada.Clear();
            ObtenerListaSalasPriorizadas(playerTier);
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
        }

        private void _CargarPuertas(Vector3 pos, float halfX, float halfY, float halfZ, int playerTier)
        {
            if (_cachedDoors != null && _cachedDoors.Count > 0)
            {
                // Revisamos solo la primera puerta. Si su GameObject es nulo o fue destruido por Unity,
                // significa que TODAS las puertas de la lista son de la ronda anterior.
                var primeraPuerta = _cachedDoors[0];
                
                // Usamos try-catch rápido solo para la validación por si el wrapper de EXILED es muy estricto
                bool cacheCaducada = false;
                try 
                {
                    if (primeraPuerta == null || primeraPuerta.GameObject == null) cacheCaducada = true;
                } 
                catch 
                { 
                    cacheCaducada = true; 
                }

                if (cacheCaducada)
                {
                    // PURGAMOS TODA LA MEMORIA DE LA RONDA ANTERIOR
                    _cachedDoors.Clear();
                    _doorColliderCache.Clear(); 
                    //Log.Debug("[Sensors] Caché de puertas invalidada por cambio de ronda.");
                }
            }
            if (_cachedDoors == null || _cachedDoors.Count == 0)
            {
                _cachedDoors = new List<Door>(Door.List);
                //Log.Info($"[Perf] Puertas cargadas: {_cachedDoors.Count}");
            }
        
            // Reutilizar lista de tuplas cacheada
            _doorsConDist.Clear();
            foreach (var d in _cachedDoors)
            {   
                if (d == null || d.Transform == null || d.GameObject == null) continue;
                if (d == null) continue;
                float dist = Vector3.Distance(d.Transform.position, pos);
                if (dist < 50f) _doorsConDist.Add((d, dist));
            }
            //_doorsConDist.Sort((a, b) => a.dist.CompareTo(b.dist));
            _doorsConDist.Sort(_doorComparison);
        
            int doorCount = 0;
            foreach (var (d, dist) in _doorsConDist)
            {
                if (doorCount >= 15) break;
                if (d.GameObject == null) continue;

                int doorId = d.GameObject.GetInstanceID();
                // Collider name con caché por instanceID
                if (!_doorColliderCache.TryGetValue(doorId, out string colliderName))
                {
                    colliderName = "Unknown";
                    if (d.GameObject != null)
                    {
                        var colliders = d.GameObject.GetComponentsInChildren<Collider>(true);
                        var valid = System.Array.Find(colliders,
                            c => !c.isTrigger &&
                                !c.name.Contains("TouchScreenPanel") &&
                                !c.name.Contains("Frame"));
                        if (valid != null) colliderName = valid.name;
                    }
                    _doorColliderCache[doorId] = colliderName;
                }
        
                int reqTier = GetDoorRequiredTier(d);
        
                // Reutilizar objeto del pool en vez de new DoorData
                var dd = _doorPool[doorCount];
                dd.Type         = d.RequiredPermissions.ToString();
                dd.Name         = d.Name;
                dd.ColliderName = colliderName;
                dd.Distance     = dist / 50f;
                dd.RequiredTier = reqTier;
                dd.CanOpen      = playerTier >= reqTier;
                dd.IsOpen       = d.IsOpen;
                dd.RelX = (d.Position.x - pos.x) / 50f;
                dd.RelY = (d.Position.y - pos.y) / 50f;
                dd.RelZ = (d.Position.z - pos.z) / 50f;
                dd.RealRelX     = d.Position.x - pos.x;
                dd.RealRelY     = d.Position.y - pos.y;
                dd.RealRelZ     = d.Position.z - pos.z;
                _cachedNearDoors.Add(dd);
                doorCount++;
            }
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

        private void _CargarPersonajesCercanos(Vector3 pos, float rangoRadar = 30f)
        {
            _listaTemporalPlayers.Clear();
            _cachedNearPlayers.Clear();

            // Dirección hacia donde mira MI BOT (su cámara o su transform)
            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos   = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;

            // 1. Filtrar jugadores que el bot REALMENTE puede ver
            foreach (Player hub in Player.List)
            {
                // Filtros básicos de estado
                if (hub == null || hub == _player || !hub.IsAlive || hub.IsDead || hub.Role.Type == RoleTypeId.Spectator)
                    continue;

                float d = Vector3.Distance(hub.Position, pos);
                if (d > rangoRadar) continue;

                // --- REGLA 1: ¿ESTÁ EN NUESTRO CONO DE VISIÓN? (FOV) ---
                Vector3 dirHaciaEl = (hub.Position - pos).normalized;
                float miProductoEscalar = Vector3.Dot(miMirada, dirHaciaEl);

                // 0.5f equivale a un campo de visión (FOV) de 120 grados (60° a cada lado del centro)
                // Si quieres que sea más estricto (ojo de humano enfocado), sube a 0.707f (90 grados totales)
                if (miProductoEscalar < 0.5f) 
                    continue; // Está detrás o muy en la periferia. El bot no sabe que existe.

                // --- REGLA 2: ¿HAY LÍNEA DE VISIÓN LIMPIA? (RAYCAST) ---
                Vector3 susOjos = hub.CameraTransform != null ? hub.CameraTransform.position : hub.Position + Vector3.up;
                Vector3 dirRayo = susOjos - misOjos;

                if (Physics.Raycast(misOjos, dirRayo.normalized, out RaycastHit hit, d + 0.5f))
                {
                    // Si el rayo choca con una pared o puerta antes de llegar al jugador objetivo...
                    if (hit.collider.gameObject != hub.GameObject && hit.transform.root != hub.Transform.root)
                    {
                        continue; // Hay un obstáculo bloqueando la vista. El bot no sabe que existe.
                    }
                }

                // Si pasa ambos filtros, el bot es consciente de su existencia y lo añade para evaluar cercanía
                _listaTemporalPlayers.Add(new Actor { Player = hub, Distancia = d });
            }

            // 2. Ordenar los que SÍ vemos por cercanía (el peligro más cercano primero)
            _listaTemporalPlayers.Sort((a, b) => a.Distancia.CompareTo(b.Distancia));

            // 3. Volcar los datos en el Pool (Máximo 5)
            int count = 0;
            int maxPlayers = Mathf.Min(_listaTemporalPlayers.Count, 5);

            for (int i = 0; i < maxPlayers; i++)
            {
                var item = _listaTemporalPlayers[i];
                var target = item.Player;
                var pd = _playerPool[count];
                var role = target.Role.Type;

                pd.Role = role.ToString();
                pd.FactionId = (int)role;
                
                Vector3 relPos = target.Position - pos;
                pd.Distance = item.Distancia / rangoRadar;
                pd.RelX     = relPos.x / rangoRadar;
                pd.RelY     = relPos.y / rangoRadar;
                pd.RelZ     = relPos.z / rangoRadar;
                pd.HealthPercent = target.MaxHealth > 0 ? (target.Health / target.MaxHealth) : 0f;

                // Calculamos qué tan fijamente nos está mirando ÉL a NOSOTROS
                Vector3 ojosEnemigo = target.CameraTransform != null ? target.CameraTransform.position : target.Position + Vector3.up;
                Vector3 dirHaciaMi = (misOjos - ojosEnemigo).normalized;
                Vector3 miradaEnemigo = target.CameraTransform != null ? target.CameraTransform.forward : target.Transform.forward;
                
                pd.MiradaHaciaMi = Vector3.Dot(miradaEnemigo, dirHaciaMi);

                _cachedNearPlayers.Add(pd);
                count++;
            }
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

        protected void _CopiarACacheBase(AgentObservation obs)
        {
            obs.NearDoors.Clear();
            obs.NearLifts.Clear();
            obs.NearRooms.Clear();
        
            obs.NearDoors.AddRange(_cachedNearDoors);
            obs.NearLifts.AddRange(_cachedNearLifts);
            obs.NearRooms.AddRange(_cachedNearRooms);
        }
        
        // ── Actualizar InvalidarCachesMapa para incluir lockers ───────────────────
 

        // ───────────────────────────────────────────────────────────────────────
        // HELPERS
        // ───────────────────────────────────────────────────────────────────────
        
        public virtual void ResetEstado()
        {
            _cachedNearPlayers.Clear();
            _listaTemporalPlayers.Clear();
        }
        

        public void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }

        protected abstract void ObtenerListaSalasPriorizadas(int tierTarjeta);
        
    }
}