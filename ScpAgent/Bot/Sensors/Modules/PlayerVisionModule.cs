
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
        private const float TIEMPO_OLVIDO  = 5f;
        private const float FOV_MIN_DOT    = 0.5f;  // ~60° semi-FOV
        private const float DIST_SIN_RAY   = 1.0f;  // a <1m no hace falta raycast
 
        // ── Estado interno ───────────────────────────────────────────────
        private Player _player;
 
        //private readonly Dictionary<int, MemoriaJugador> _memoria
            //= new Dictionary<int, MemoriaJugador>();
        
        private readonly VisualMemory<ObjectMemory> _memoria = new VisualMemory<ObjectMemory>(TIEMPO_OLVIDO);
 
        private readonly List<int>   _idsAEliminar    = new List<int>(8);
        private readonly List<Actor> _listaTemp       = new List<Actor>(5);
 
        // ── Pool de salida (sin alloc) ───────────────────────────────────
        private readonly ActorData[] _pool = new ActorData[5];
        private readonly Actor[] _poolTemporal = new Actor[5];
        private readonly Dictionary<RoleTypeId, string> _roleCache = new Dictionary<RoleTypeId, string>();
        private readonly Dictionary<Team, string> _teamCache = new Dictionary<Team, string>();
 
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

            if (_player == null || !_player.IsAlive) return;

            Vector3 pos      = _player.Position;
            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;

            // Contador unificado para el pool temporal (reemplaza a _listaTemp.Count)
            int totalCount = 0;

            // ── 1. Marcar todos como no vistos este ciclo ─────────────────
            _memoria.MarcarTodosNoVistos();
            //Log.Info($"{}");
            // ── 2. Detección directa: FOV + raycast (Bucle 'for' anti-GC) ──
            foreach (var hub in ReferenceHub.AllHubs)
            {   
                // Evitamos el desborde del pool temporal
                if (totalCount >= _poolTemporal.Length) break;
                
                if (hub == null) continue;

                Player target = Player.Get(hub);
                if (target == null || target == _player)      continue;
                if (!target.IsAlive || target.IsDead)         continue;
                
                // Blindaje estricto de Rol (Evita NullReference en cambios de estado de juego)
                var role = target.Role;
                if (role == null)                             continue;
                if (role.Type == RoleTypeId.Spectator ||
                    role.Type == RoleTypeId.None)             continue;
                    
                if (target.GameObject == null ||
                    target.Transform  == null)                 continue;

                float dist = Vector3.Distance(target.Position, pos);
                if (dist > RANGO_RADAR) continue;

                // Filtro FOV
                Vector3 dirHaciaEl = (target.Position - pos).normalized;
                if (Vector3.Dot(miMirada, dirHaciaEl) < FOV_MIN_DOT) continue;

                // Raycast de línea de visión
                if (!_TieneLineaDeVision(misOjos, dirHaciaEl, dist, target.GameObject))
                    continue;

                _memoria.ObtenerORegistrar(target.Id, target.Position, ahora, target);

                // OPTIMIZACIÓN: Escribir directamente sobre el pool pre-existente sin allocs
                var item = _poolTemporal[totalCount];
                item.Player            = target;
                item.Distancia         = dist;
                item.EsRecordado       = false;
                item.PosicionRecordada = Vector3.zero;
                item.Antiguedad        = 0f;
                totalCount++;
            }
            // ── 3. Añadir jugadores recordados ────────────────────────────
            _idsAEliminar.Clear();
            foreach (var kv in _memoria.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue;

                float antiguedad = ahora - kv.Value.UltimoTimestamp;
                if (antiguedad > TIEMPO_OLVIDO)
                {
                    _idsAEliminar.Add(kv.Key);
                    continue;
                }

                var playerObj = Player.Get(kv.Key);
                if (playerObj == null || !playerObj.IsAlive || playerObj.GameObject == null)
                {
                    _idsAEliminar.Add(kv.Key);
                    continue;
                }

                float distRecordada = Vector3.Distance(kv.Value.UltimaPosicion, pos);
                if (distRecordada > RANGO_RADAR * 1.5f) continue;

                if (totalCount >= _poolTemporal.Length) break;

                // OPTIMIZACIÓN: Escribir directamente sobre el pool pre-existente
                var item = _poolTemporal[totalCount];
                item.Player            = playerObj;
                item.Distancia         = distRecordada;
                item.EsRecordado       = true;
                item.PosicionRecordada = kv.Value.UltimaPosicion;
                item.Antiguedad        = antiguedad;
                totalCount++;
            }

            _memoria.PurgarOlvidados(ahora);

            // ── 4. Ordenar por distancia y volcar al pool ─────────────────
            // Ordena únicamente el segmento del array que contiene datos reales usando el Comparer estático
            if (totalCount > 1)
            {
                Array.Sort(_poolTemporal, 0, totalCount, _actorDistanceComparer);
            }

            int maxPlayers = Math.Min(totalCount, _pool.Length);

            for (int i = 0; i < maxPlayers; i++)
            {
                var item   = _poolTemporal[i];
                var target = item.Player;
                
                // Seguridad extra en caso de recolección de red intermedia
                if (target == null || target.Role == null) continue; 
                
                var pd     = _pool[i]; // INDEXACIÓN CORREGIDA: Usa 'i' para evitar desfases con el bucle

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

                pd.Distance = item.Distancia / RANGO_RADAR;
                pd.RelX     = relPos.x / RANGO_RADAR;
                pd.RelY     = relPos.y / RANGO_RADAR;
                pd.RelZ     = relPos.z / RANGO_RADAR;

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

                obs.NearPlayers.Add(pd);
            }
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
 
        private bool _TieneLineaDeVision(Vector3 origen, Vector3 dir,
            float distancia, GameObject objetivoGO)
        {
            // Muy cerca — asumimos visible sin raycast
            if (distancia < DIST_SIN_RAY) return true;
 
            int hitCount = Physics.RaycastNonAlloc(origen, dir, _rayBuffer,
                distancia + 0.5f, ~0, QueryTriggerInteraction.Ignore);
 
            if (hitCount == 0) return false;
 
            Array.Sort(_rayBuffer, 0, hitCount, _rayComparer);
 
            for (int i = 0; i < hitCount; i++)
            {
                var h = _rayBuffer[i];
                if (h.collider == null) continue;
 
                // Ignorar el propio cuerpo del bot
                if (_player != null &&
                    (h.collider.gameObject == _player.GameObject ||
                     h.collider.transform.root == _player.Transform.root))
                    continue;
 
                // El primer obstáculo real — ¿es el objetivo?
                return h.collider.gameObject.GetInstanceID() == objetivoGO.GetInstanceID()
                    || h.collider.transform.IsChildOf(objetivoGO.transform);
            }
 
            return false;
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
