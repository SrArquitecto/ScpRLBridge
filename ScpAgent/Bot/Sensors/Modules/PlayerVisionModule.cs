using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Data;
using InventorySystem.Items;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;

namespace ScpAgent.Bot.Sensors.Modules
{
    /// <summary>
    /// Detecta jugadores cercanos con FOV + raycast + memoria temporal.
    /// Expone NearPlayers en AgentObservation y permite consultar si un
    /// jugador concreto está en memoria (usado por DamageModule).
    /// </summary>
    public class PlayerVisionModule : ISensorPlayerModule
    {
        // ── Configuración ────────────────────────────────────────────────
        private const float RANGO_RADAR    = 30f;
        private const float TIEMPO_OLVIDO  = 45f;
        private const float FOV_MIN_DOT    = 0f;   // -1 = 360°, sin filtro FOV
        private const float DIST_SIN_RAY   = 1.0f;  // a <1m no hace falta raycast
 
        // ── Estado interno ───────────────────────────────────────────────
        private Player _player;
 
        //private readonly Dictionary<int, MemoriaJugador> _memoria
            //= new Dictionary<int, MemoriaJugador>();
        
        private readonly VisualMemory<ObjectMemory> _memoria = new VisualMemory<ObjectMemory>(TIEMPO_OLVIDO);
 
        private readonly List<int>   _idsAEliminar    = new List<int>(8);
        private readonly List<Actor> _listaTemp       = new List<Actor>(5);
 
        // ── Pool de salida (sin alloc) ───────────────────────────────────
        private readonly ActorData[] _pool = new ActorData[8];
        private readonly Actor[] _poolTemporal = new Actor[8];

        // Cachés estáticas compartidas entre todos los bots (enum→string nunca cambia)
        private static readonly Dictionary<RoleTypeId, string> _roleCache = new Dictionary<RoleTypeId, string>();
        private static readonly Dictionary<Team, string> _teamCache = new Dictionary<Team, string>();
 
        // ── Buffer de raycast (sin alloc) ────────────────────────────────
        private readonly RaycastHit[] _rayBuffer = new RaycastHit[5];
 
        private static readonly IComparer<RaycastHit> _rayComparer =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        // Variable estática para ordenar el array sin generar basura por delegados inline
        private static readonly IComparer<Actor> _actorDistanceComparer = 
            Comparer<Actor>.Create((a, b) => a.Distancia.CompareTo(b.Distancia));
 
        // ── Constructor ──────────────────────────────────────────────────
        public PlayerVisionModule()
        {
            for (int i = 0; i < _pool.Length; i++)
                _pool[i] = new ActorData();
            for (int i = 0; i < _poolTemporal.Length; i++)
                _poolTemporal[i] = new Actor();

        }
 
        // ── ISensorModule ────────────────────────────────────────────────
 
        public void VincularPlayer(Player player) => _player = player;

        public void Reset()
        {
            _memoria.Clear();
            _idsAEliminar.Clear();
            _listaTemp.Clear();

        }
 
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            obs.NearPlayers.Clear();
            ctx.EnemyPositions.Clear();

            // Obtener el Player actual desde el ReferenceHub para evitar
            // referencias stale (el _player cacheado puede apuntar a otro bot
            // después de un respawn o cambio de rol)
            Player miPlayerActual = null;
            if (_player != null && _player.ReferenceHub != null)
            {
                miPlayerActual = Player.Get(_player.ReferenceHub);
                if (miPlayerActual == null) miPlayerActual = _player;
            }
            else
            {
                miPlayerActual = _player;
            }

            if (miPlayerActual == null || !miPlayerActual.IsAlive)
            {
                //if (UnityEngine.Time.frameCount % 100 == 0)
                    //Log.Warn($"[PlayerVision] Agente {miPlayerActual?.Id ?? -1}: _player es null o no está vivo");
                return;
            }

            Vector3 pos      = miPlayerActual.Position;
            Transform camTransform = miPlayerActual.CameraTransform;
            Vector3 miMirada = camTransform != null ? camTransform.forward : miPlayerActual.Transform.forward;
            Vector3 misOjos  = camTransform != null ? camTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;

            int totalCount = 0;
            _memoria.MarcarTodosNoVistos();
            int TotalPlayers = ReferenceHub.AllHubs.Count - 1;

            // Log diagnóstico detallado cada 100 frames
            bool logThisFrame = UnityEngine.Time.frameCount % 100 == 0;
            int distFilterCount = 0;
            int fovFilterCount = 0;
            int raycastFilterCount = 0;

            // Usar OverlapSphere para detectar todos los colliders en el radio
            Collider[] collidersEnRadio = Physics.OverlapSphere(pos, RANGO_RADAR, ~0, QueryTriggerInteraction.Ignore);

            if (logThisFrame)
            {
                string miNick = miPlayerActual.Nickname ?? "null";
                string miHubId = miPlayerActual.ReferenceHub != null ? miPlayerActual.ReferenceHub.netId.ToString() : "null";
                string miGoName = miPlayerActual.GameObject != null ? miPlayerActual.GameObject.name : "null";
                //Log.Info($"[PlayerVisionDebug] Agente #{miPlayerActual.Id} nick={miNick} hubId={miHubId} go={miGoName}");
            }

            int hubsEncontrados = 0;
            int referenciaHubNull = 0;
            int playerNull = 0;
            int mismoBot = 0;
            int noAlive = 0;
            int spectator = 0;
            int mismoBotNull = 0;
            int dupHubs = 0; // Colliders del mismo bot ya procesados

            // HashSet para evitar procesar el mismo bot múltiples veces
            // (cada bot tiene ~19 colliders: cabeza, cuerpo, brazos, etc.)
            HashSet<uint> hubsYaProcesados = new HashSet<uint>();

            foreach (var col in collidersEnRadio)
            {
                if (col == null) continue;

                ReferenceHub hitHub = col.GetComponentInParent<ReferenceHub>();
                if (hitHub == null) { referenciaHubNull++; continue; }
                hubsEncontrados++;

                // Evitar procesar el mismo jugador múltiples veces (cada jugador tiene ~19 colliders)
                if (!hubsYaProcesados.Add(hitHub.netId))
                {
                    dupHubs++;
                    continue;
                }

                Player target = Player.Get(hitHub);
                if (target == null) { playerNull++; continue; }

                // Comparar por netId (uint, único por hub) en vez de referencia
                bool esMismoBot = (miPlayerActual.ReferenceHub != null &&
                                   hitHub.netId == miPlayerActual.ReferenceHub.netId);

                if (esMismoBot) { mismoBot++; continue; }
                if (!target.IsAlive) { noAlive++; continue; }

                var role = target.Role;
                if (role == null) continue;
                if (role.Type == RoleTypeId.Spectator || role.Type == RoleTypeId.None) { spectator++; continue; }

                if (target.GameObject == null || target.Transform == null) continue;

                if (totalCount >= _poolTemporal.Length) break;
                distFilterCount++;

                Vector3 targetEyes = target.CameraTransform != null
                    ? target.CameraTransform.position
                    : target.Position + Vector3.up;
                Vector3 dirHaciaEl = (targetEyes - misOjos).normalized;
                float dot = Vector3.Dot(miMirada, dirHaciaEl);
                if (dot < FOV_MIN_DOT) continue;
                fovFilterCount++;

                float dist = Vector3.Distance(target.Position, pos);

                // Si el target está MUY cerca (< 5m), asumir que hay línea de visión
                // (están en la misma habitación)
                if (dist < 5f)
                {
                    // No hacer raycast, asumir visible
                }
                else if (!_TieneLineaDeVision(misOjos, dirHaciaEl, dist, target.GameObject))
                {
                    continue;
                }
                raycastFilterCount++;

                _memoria.ObtenerORegistrar(target.Id, target.Position, ahora, target);

                // CRÍTICO: Actor es un struct — hay que escribir directamente
                // sobre el array. "var item = _poolTemporal[i]" copia el struct
                // y las asignaciones se pierden al salir del scope.
                _poolTemporal[totalCount].Player            = target;
                _poolTemporal[totalCount].Distancia         = dist;
                _poolTemporal[totalCount].EsRecordado       = false;
                _poolTemporal[totalCount].PosicionRecordada = Vector3.zero;
                _poolTemporal[totalCount].Antiguedad        = 0f;
                totalCount++;
            }
            /*
            if (logThisFrame)
            {
                Log.Info($"[PlayerVision] Agente {miPlayerActual.Id}: " +
                    $"overlap={collidersEnRadio.Length} hubsOk={hubsEncontrados} " +
                    $"hubNull={referenciaHubNull} dupHubs={dupHubs} playerNull={playerNull} " +
                    $"mismoBot={mismoBot} (null={mismoBotNull}) " +
                    $"noAlive={noAlive} spectator={spectator} " +
                    $"dist={distFilterCount} fov={fovFilterCount} ray={raycastFilterCount} " +
                    $"total={totalCount}");
            }
            */
            //_idsAEliminar.Clear();
            foreach (var kv in _memoria.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue;

                float antiguedad = ahora - kv.Value.UltimoTimestamp;
                if (antiguedad > TIEMPO_OLVIDO)
                {
                    //_idsAEliminar.Add(kv.Key);
                    continue;
                }

                var playerObj = Player.Get(kv.Key);
                if (playerObj == null || !playerObj.IsAlive || playerObj.GameObject == null)
                {
                    //_idsAEliminar.Add(kv.Key);
                    continue;
                }

                float distRecordada = Vector3.Distance(kv.Value.UltimaPosicion, pos);
                if (distRecordada > RANGO_RADAR * 1.5f) continue;

                if (totalCount >= _poolTemporal.Length) break;

                // CRÍTICO: Actor es un struct — escribir directamente sobre el array
                _poolTemporal[totalCount].Player            = playerObj;
                _poolTemporal[totalCount].Distancia         = distRecordada;
                _poolTemporal[totalCount].EsRecordado       = true;
                _poolTemporal[totalCount].PosicionRecordada = kv.Value.UltimaPosicion;
                _poolTemporal[totalCount].Antiguedad        = antiguedad;
                totalCount++;
            }
            // IMPORTANTE: Eliminar jugadores recordados que ya no están vivos
            // (esto es lo que PurgarOlvidados no hace)
            // Eliminar manualmente las entradas que ya no son válidas
            List<int> keysAEliminar = null;
            foreach (var kv in _memoria.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue;
                var playerObj = Player.Get(kv.Key);
                if (playerObj == null || !playerObj.IsAlive || playerObj.GameObject == null)
                {
                    if (keysAEliminar == null) keysAEliminar = new List<int>();
                    keysAEliminar.Add(kv.Key);
                }
            }
            if (keysAEliminar != null)
            {
                foreach (var key in keysAEliminar)
                    _memoria.Remove(key);
            }

            _memoria.PurgarOlvidados(ahora);

            // ── 4. Ordenar por distancia y volcar al pool ─────────────────
            // Ordena únicamente el segmento del array que contiene datos reales usando el Comparer estático
            if (totalCount > 1)
            {
                Array.Sort(_poolTemporal, 0, totalCount, _actorDistanceComparer);
            }

            int maxPlayers = Math.Min(totalCount, _pool.Length);
            int countNeutrals = 0;
            int countFriends = 0;
            int countEnemies = 0;
            float closestEnemyDistance = 1f;
            // poolIdx separado: si _poolTemporal[i].Player es null y hacemos continue,
            // no dejamos huecos en _pool ni en obs.NearPlayers
            int poolIdx = 0;
            for (int i = 0; i < maxPlayers; i++)
            {
                var item   = _poolTemporal[i];
                var target = item.Player;
                
                // Seguridad extra en caso de recolección de red intermedia
                if (target == null || target.Role == null) continue; 
                
                var pd = _pool[poolIdx++]; // indice propio, no i

                // OPTIMIZACIÓN: Evitar asignaciones por .ToString() usando la caché del diccionario de la clase
                if (!_roleCache.TryGetValue(target.Role.Type, out string roleStr))
                {
                    roleStr = target.Role.Type.ToString();
                    _roleCache[target.Role.Type] = roleStr;
                }
                pd.Role = roleStr;

                if (!_teamCache.TryGetValue(target.Role.Team, out string teamStr))
                {
                    teamStr = target.Role.Team.ToString();
                    _teamCache[target.Role.Team] = teamStr;
                }
                pd.Team = teamStr;

                pd.FactionId   = (float)target.Role.Type;
                pd.EsRecordado = item.EsRecordado;
                pd.Antiguedad  = item.EsRecordado
                    ? item.Antiguedad / TIEMPO_OLVIDO
                    : 0f;

                Vector3 posRef = item.EsRecordado
                    ? item.PosicionRecordada
                    : target.Position;
                Vector3 relPos = posRef - pos;

                pd.Distance = item.Distancia / 50;
                pd.RelX     = relPos.x / 50;
                pd.RelY     = relPos.y / 50;
                pd.RelZ     = relPos.z / 50;

                pd.Hostilidad = _CalcularHostilidad(_player, target);
                

                if (!item.EsRecordado)
                {
                    pd.Health = target.MaxHealth > 0
                        ? target.Health / target.MaxHealth
                        : 0f;

                    Vector3 ojosEnemigo  = target.CameraTransform != null
                        ? target.CameraTransform.position
                        : target.Position + Vector3.up;
                    Vector3 dirHaciaMi   = (misOjos - ojosEnemigo).normalized;
                    Vector3 miradaEnemigo = target.CameraTransform != null
                        ? target.CameraTransform.forward
                        : target.Transform.forward;
                    pd.MiradaHaciaMi = Vector3.Dot(miradaEnemigo, dirHaciaMi);
                }
                else
                {
                    pd.Health        = -1f;
                    pd.MiradaHaciaMi =  0f;
                }

                if (pd.Hostilidad == 0f)
                    countNeutrals++;
                else if (pd.Hostilidad == 1f)
                {
                    countEnemies++;
                    if (item.Distancia/50f < closestEnemyDistance)
                        closestEnemyDistance = item.Distancia/50f;
                    Vector3 worldPos = item.EsRecordado ? item.PosicionRecordada : target.Position;
                    ctx.EnemyPositions.Add(worldPos);
                }
                else
                    countFriends++;

                obs.NearPlayers.Add(pd);
            }
            obs.CountFriends = (float)countFriends/(float)_pool.Length;
            obs.CountNeutrals = (float)countNeutrals/(float)_pool.Length;
            obs.CountEnemies = (float)countEnemies/(float)_pool.Length;
            obs.ClosestEnemyDistance = closestEnemyDistance;
            //if (UnityEngine.Time.frameCount % 100 == 0)
                //Log.Info($"[PlayerVisionEnd] Agente {miPlayerActual.Id} NearPlayers count={obs.NearPlayers.Count} CountEnemies={obs.CountEnemies}");
        }
        
 
        // ── API pública extra ────────────────────────────────────────────
 
        /// <summary>
        /// Consulta si un jugador por ID está actualmente en memoria.
        /// Usado por DamageModule para saber si el atacante fue visto recientemente.
        /// </summary>
        public bool TieneEnMemoria(int playerId) 
        {
            return _memoria.ContainsKey(playerId);
        }
        // ── Helpers privados ─────────────────────────────────────────────
 
        // Cache de layer mask — se inicializa una sola vez

        // Layers conocidos de SCP:SL (se inicializan una sola vez)
        private static int? _wallMask = null;
        private static int  _layerHead    = -1;
        private static int  _layerPlayer2 = -1;

        private bool _TieneLineaDeVision(Vector3 origen, Vector3 dir, float distancia, GameObject objetivoGO)
        {
            if (distancia < DIST_SIN_RAY) return true;
            if (objetivoGO == null) return false;

            // Inicializar una sola vez
            if (_wallMask == null)
            {
                _layerHead    = LayerMask.NameToLayer("Head");       // Collider cabeza jugador
                _layerPlayer2 = LayerMask.NameToLayer("Player");     // Collider cuerpo/físicas
                int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
                int hitboxLayer = LayerMask.NameToLayer("Hitbox");   // <- ¡AQUÍ ESTÁ LA CLAVE!
                int specLayer   = LayerMask.NameToLayer("Spectator"); // Por si acaso hay restos del collider
                
                // Mask: todos los layers EXCEPTO los relacionados con jugadores y triggers
                _wallMask = ~0
                    & ~(1 << _layerHead)
                    & ~(1 << _layerPlayer2)
                    & ~(1 << ignoreLayer)
                    & ~(1 << hitboxLayer)
                    & ~(1 << specLayer);
            }

            // Lanzar linecast ignorando colliders de jugadores.
            Vector3 destino = objetivoGO.transform.position + Vector3.up * 0.8f;
            bool bloqueado  = Physics.Linecast(origen, destino, _wallMask.Value, QueryTriggerInteraction.Ignore);
            
            return !bloqueado;
        }
        
        private static float _CalcularHostilidad(Player yo, Player objetivo)
        {
            if (yo == null || objetivo == null) return 0f;
 
            Team miEquipo      = yo.Role.Team;
            Team equipoObjetivo = objetivo.Role.Team;
 
            if (miEquipo == equipoObjetivo) return -1f; // aliado
 
            return miEquipo switch
            {
                Team.FoundationForces => equipoObjetivo switch
                {
                    Team.Scientists      => -1f,
                    Team.ClassD          =>  0f,
                    Team.ChaosInsurgency =>  1f,
                    Team.SCPs            =>  1f,
                    _                    =>  0f
                },
                Team.ChaosInsurgency => equipoObjetivo switch
                {
                    Team.ClassD          => -1f,
                    Team.Scientists      =>  0f,
                    Team.FoundationForces => 1f,
                    Team.SCPs            =>  1f,
                    _                    =>  0f
                },
                Team.Scientists => equipoObjetivo switch
                {
                    Team.FoundationForces => -1f,
                    Team.ClassD          =>  0f,
                    Team.ChaosInsurgency =>  0f,
                    Team.SCPs            =>  1f,
                    _                    =>  0f
                },
                Team.ClassD => equipoObjetivo switch
                {
                    Team.ChaosInsurgency => -1f,
                    Team.Scientists      =>  0f,
                    Team.FoundationForces => 0f,
                    Team.SCPs            =>  1f,
                    _                    =>  0f
                },
                Team.SCPs => 1f, // SCP hostil a todo lo que no sea SCP
                _          => 0f
            };
        }

    }
}