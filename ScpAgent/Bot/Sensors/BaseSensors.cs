using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;
using PlayerRoles;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules;


namespace ScpAgent.Bot.Sensors
{

    public abstract class BaseSensors : ISensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
        protected Player _player;
        protected int _agentId;
        public static readonly AgentObservation obsVacia = new AgentObservation { Done = true };
        protected const float RANGO_MAPA     = 500f;
        protected readonly ISensorRoomModule    _room    = new RoomModule();
        

        // ── Estado de movimiento ────────────────────────────────────────────────
        protected Vector3 _lastPos;
        protected float   _lastYaw;
        protected float   _lastPitch;

        // ── Caché del raycast de apuntado ───────────────────────────────────────
        protected const int UPDATE_FREQUENCY = 20;
        protected int _frameCounter = UPDATE_FREQUENCY;
        

        
        private List<LiftData> _cachedNearLifts { get; set; } = new List<LiftData>();
        
        
        public List<ActorData> _cachedNearPlayers { get; set;} = new List<ActorData>();
        public List<Actor> _listaTemporalPlayers { get; set;} = new List<Actor>();
     

        
        protected readonly List<(Lift d, float dist)> _liftsConDist = new List<(Lift d, float dist)>(50);
        protected readonly List<Habitaciones> _roomsPriorizada = new List<Habitaciones>(120);



        // ── Buffers estáticos para raycasts (sin alloc) ────────────────────────
 
        protected readonly RaycastHit[] _behindDoorBuffer = new RaycastHit[5];
        protected readonly RaycastHit[] _visibilidadBuffer = new RaycastHit[15];

        // ── Cache de datos de sala por agente ──────────────────────────────────
        public static Dictionary<int, AgentCacheData> agentCacheData = new Dictionary<int, AgentCacheData>();
        protected static readonly AgentCacheData _fallbackCacheData = new AgentCacheData 
        { 
            center = Vector3.zero,
            halfX = Vector3.one.x * 10f,
            halfY = Vector3.one.y * 10f,
            halfZ = Vector3.one.z * 10f,
        };

        protected readonly LiftData[]     _liftPool    = new LiftData[3];
       
        protected readonly ActorData[]   _playerPool   = new ActorData[5];
        
        


        // ── Listas globales cacheadas (se cargan UNA VEZ por ronda) ───────────
        
        protected List<Lift>   _cachedLifts;
        

        // ── Cache de collider name por puerta (evita GetComponentsInChildren) ──
        // Se llena la primera vez que se procesa cada puerta y se reutiliza
        
        private readonly Dictionary<int, MemoriaJugador> _memoriaJugadores 
            = new Dictionary<int, MemoriaJugador>();
        

        
        private readonly VisualMemory <ObjectMemoryLift> _memoriaLifts    = new VisualMemory<ObjectMemoryLift>(TIEMPO_OLVIDO_OBJETOS);

        //private readonly VisualMemory _memoriaRooms    = new VisualMemory(TIEMPO_OLVIDO_OBJETOS);

        protected const float TIEMPO_OLVIDO = 5f;

        

        protected static readonly Comparison<(Lift d, float dist)> _liftComparison =
            (a, b) => a.dist.CompareTo(b.dist);
        

        
        

        private float   _pendingDamage     = 0f;
        private string  _pendingDamageType = "Unknown";
        private Vector3 _pendingDamageDir  = Vector3.zero; // dirección hacia el atacante
        private bool    _attackerInMemory  = false;
        private const float DAMAGE_DECAY = 0.5f; // segundos que persiste la info de daño
        private float   _lastDamageTime = -999f;
       
        //ESTRATEGIAS:
        protected Func<ItemType, float> _fnPrioridad;
        protected Func<ItemType, string> _fnCategoria;

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
            
            for (int i = 0; i < _liftPool.Length;    i++) _liftPool[i]    = new LiftData();
            for (int i = 0; i < _playerPool.Length;    i++) _playerPool[i]    = new ActorData();
            
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

        public void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            _fnPrioridad = fnPrioridad;
            _fnCategoria = fnCategoria;
        }

        /// <summary>
        /// Invalida las cachés de objetos del mapa (llamar tras Round.Restart).
        /// </summary>

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public virtual AgentObservation GetCurrentState(
            float delta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {

            Vector3 pos         = _player.Position;
            Vector3 camRotation = _player.CameraTransform.rotation.eulerAngles;

            

            // ── Velocidades lineales ───────────────────────────────────────────
        
            bool intentaMoverse = (accionAnterior == 0 || accionAnterior == 1 || accionAnterior == 2 || accionAnterior == 3 || accionAnterior == 4);
            
            // ── Posición relativa dentro de la sala ────────────────────────────
            
            AgentCacheData data = GetData();
            Vector3 relativePos = pos - data.center;
            

            _CargarDaño(observation);


            _CargarElementosBaseCercanos(observation, pos, data.halfX, data.halfY, data.halfZ, playerTier);

            _CargarPersonajesCercanos(observation, pos, 100);

            //Actualizamos ultimas posiciones de la camara y el personaje
            _lastYaw   = camRotation.y;
            _lastPitch = camRotation.x;
            _lastPos = pos;

            Log.Debug($"[Perf-HUMAN] Tras CargarElementosCercanos: obs.NearDoors={observation.NearDoors.Count}");
            return observation;
        }
        
        public void MarcarRoomDescubierta(Room sala)
        {
            _room.MarcarRoomDescubierta(sala);
        }

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────

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
                
                _CopiarACacheJugadores(observation);
                return;
            }
            _frameCounter = 0;
            if (_frameCounter == 0) { // justo tras recargar
                Log.Debug($"[Perf] Puertas cargadas en _cachedNearDoors: {_cachedNearDoors.Count}");
                Log.Debug($"[Perf] Puertas cargadas en _cachedNearRooms: {_cachedNearRooms.Count}");
            }

            
            _cachedNearLifts.Clear();
            
            

            

            
            try { _CargarAscensores(observation, pos, halfX, halfY, halfZ); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ASCENSORES: {ex.Message}"); }
            _CopiarACacheAscensores(observation);

            
            
        }
        private void _CargarAscensores(AgentObservation obs, Vector3 pos, float halfX, float halfY, float halfZ)
        {   

            if (_cachedLifts == null) _cachedLifts = new List<Lift>(Lift.List);
            else { _cachedLifts.Clear(); _cachedLifts.AddRange(Lift.List); }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
            
            _memoriaLifts.MarcarTodosNoVistos();

            _liftsConDist.Clear();
            foreach (var l in _cachedLifts)
            {
                if (l == null) continue;

                try
                {
                    if (l.GameObject == null || l.Transform == null) continue;

                    float dist = Vector3.Distance(l.Transform.position, pos);
                    if (dist >= 50f) continue;

                    // Filtro de visibilidad — FOV + raycast
                    if (!_EsVisible(misOjos, miMirada, l.Position, dist, l.GameObject)) continue;

                    // Visible ahora — registrar/actualizar memoria
                    int liftId = l.GameObject.GetInstanceID();
                    var memLifts = _memoriaLifts.ObtenerORegistrar(liftId, l.Position, ahora, l);
                    memLifts.AscensorCerrado = l.IsLocked; 
                    memLifts.AscensorOperativo = l.IsOperative; 
                    memLifts.AscensorMoviendose = l.IsMoving; 
                    memLifts.NivelActual = l.CurrentLevel; 
                    _liftsConDist.Add((l, dist));
                }
                catch { continue; }
            }
            _liftsConDist.Sort(_liftComparison);

            int liftCount = 0;
            foreach (var (l, dist) in _liftsConDist)
            {
                if (l == null || liftCount >= 3) break;
                if (l.Transform == null) continue;
                float d = Vector3.Distance(l.Transform.position, pos);
                if (d > 50f) continue;
        
                // Reutilizar objeto del pool en vez de new LiftData
                var ld = _liftPool[liftCount];
                ld.Type         = l.Type.ToString();
                ld.Distance     = d / 50f;
                ld.IsLocked     = l.IsLocked;
                ld.IsClosed     = l.IsOperative;
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

            foreach (var kv in _memoriaLifts.Entradas)
            {
                if (liftCount >= 3) break;
                if (kv.Value.VistoEsteCiclo) continue; // ya procesada arriba

                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 50f * 1.2f) continue; // ya muy lejos, no relevante


                var l = _liftPool[liftCount];
                var liftRef = mem.ReferenciaObjeto as Lift;

                if (liftRef != null && liftRef.GameObject != null)
                {
                    l.Type         = liftRef.Type.ToString();;; // no tenemos el wrapper Door a mano, solo posición
                }
                else
                {
                    
                }
                l.IsLocked     = mem.AscensorCerrado;
                l.IsClosed     = mem.AscensorOperativo;
                l.IsMoving     = mem.AscensorMoviendose;
                l.CanUse       = mem.PuedeUsarse;;                       
                l.CurrentLevel = mem.NivelActual;
                l.RelX = (mem.UltimaPosicion.x - pos.x) / 50f;
                l.RelY = (mem.UltimaPosicion.y - pos.y) / 50f;
                l.RelZ = (mem.UltimaPosicion.z - pos.z) / 50f;
                l.RealRelX     = mem.UltimaPosicion.x - pos.x;
                l.RealRelY     = mem.UltimaPosicion.y - pos.y;
                l.RealRelZ     = mem.UltimaPosicion.z - pos.z;
                l.EsRecordado  = true;
                l.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO_OBJETOS;
                _cachedNearLifts.Add(l);
                liftCount++;
            }

            _memoriaLifts.PurgarOlvidados(ahora);
        }
        
        
        protected abstract void _CargarElementosCercanos(Vector3 pos,
            float halfX, float halfY, float halfZ,
            int playerTier, AgentObservation obs);

        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────




        // ── Lectura en GetCurrentState ────────────────────────────────────────────
        private void _CargarDaño(AgentObservation obs)
        {
            float tiempoDesdeUltimoDaño = Time.time - _lastDamageTime;
        
            if (_pendingDamage > 0f && tiempoDesdeUltimoDaño < DAMAGE_DECAY)
            {
                float maxHealth = _player?.MaxHealth ?? 100f;
                obs.DamageReceived   = Mathf.Clamp01(_pendingDamage / maxHealth);
                obs.DamageType       = _pendingDamageType;
                obs.DamageDirX       = _pendingDamageDir.x;
                obs.DamageDirZ       = _pendingDamageDir.z;
                obs.AttackerInMemory = _attackerInMemory;
            }
            else
            {
                // Sin daño reciente — resetear a cero
                obs.DamageReceived   = 0f;
                obs.DamageType       = "None";
                obs.DamageDirX       = 0f;
                obs.DamageDirZ       = 0f;
                obs.AttackerInMemory = false;
                _pendingDamage       = 0f; // limpiar acumulado
            }
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
                pd.FactionId = (float)role / 8f;
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
        


        protected abstract void _CopiarACache(AgentObservation obs);


        private void _CopiarACacheAscensores(AgentObservation obs)
        {
            obs.NearLifts.Clear();
            obs.NearLifts.AddRange(_cachedNearLifts);
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
            _frameCounter = UPDATE_FREQUENCY;


            _pendingDamage     = 0f;
            _pendingDamageType = "Unknown";
            _pendingDamageDir  = Vector3.zero;
            _attackerInMemory  = false;
            _lastDamageTime    = -999f;


            _cachedNearPlayers.Clear();
            _listaTemporalPlayers.Clear();
            _memoriaJugadores.Clear();
            _memoriaPuertas.Clear();
            _memoriaLifts.Clear();
            _cachedNearDoors.Clear();
            
            _doorsConDist.Clear();
            
            _cachedNearLifts.Clear();
            _cachedDoors  = null;
            _cachedLifts  = null;
            
            _doorColliderCache.Clear();
        }
        

        public abstract void Destruir();
        //{
            //_player = null;
            //ResetEstado();    
        
        //}



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
            if (objetivoGO == null) return false;

            Vector3 dirHaciaObjetivo = (posObjetivo - misOjos).normalized;
            if (Vector3.Dot(miMirada, dirHaciaObjetivo) < fovMinDot) return false;

            if (distancia < 1.5f) return true;

            int hitCount = Physics.RaycastNonAlloc(misOjos, dirHaciaObjetivo, _visibilidadBuffer,
                distancia + 0.5f, ~0, QueryTriggerInteraction.Ignore);

            if (hitCount == 0) return false; // nada detectado, sospechoso — no visible por defecto

            System.Array.Sort(_visibilidadBuffer, 0, hitCount, _raycastComparer);

            // El primer hit (más cercano) que NO sea el propio jugador es lo que realmente bloquea/confirma
            for (int i = 0; i < hitCount; i++)
            {
                var h = _visibilidadBuffer[i];
                if (h.collider.gameObject == _player.GameObject) continue; // ignorar el propio cuerpo

                // ¿El primer obstáculo real ES el objetivo (o un hijo de él)?
                return h.collider.gameObject.GetInstanceID() == objetivoGO.GetInstanceID()
                    || h.collider.transform.IsChildOf(objetivoGO.transform);
            }

            return false;
        }

        public void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria)
        {
            _pendingDamage     += cantidad; // acumular si hay varios hits en el mismo tick
            _pendingDamageType  = tipo;
            _pendingDamageDir   = dirHaciaAtacante;
            _attackerInMemory   = atacanteEnMemoria;
            _lastDamageTime     = Time.time;
        }
        public bool TieneEnMemoriaJugadores(int playerId)
            => _memoriaJugadores.ContainsKey(playerId);

    }
}