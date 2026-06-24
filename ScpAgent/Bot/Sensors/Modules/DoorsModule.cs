using Exiled.API.Features;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System.Collections.Generic;
using System;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class DoorsModule : ISensorModule
    {
        private Player _player;
        private const float RANGO_MAPA     = 500f;
        private const float TIEMPO_OLVIDO = 45f;
        private const int UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;
        protected readonly DoorData[]     _doorPool    = new DoorData[5];
        private List<Door> _cachedDoors;
        private Dictionary<int, string> _doorColliderCache = new Dictionary<int, string>();
        private List<DoorData> _cachedNearDoors { get; set; } = new List<DoorData>();  
        protected readonly List<(Door d, float dist)> _doorsConDist = new List<(Door d, float dist)>(50);
        private readonly VisualMemory <ObjectMemoryDoor> _memoriaPuertas  = new VisualMemory<ObjectMemoryDoor>(TIEMPO_OLVIDO);
        private static readonly Comparison<(Door d, float dist)> _doorComparison = (a, b) => a.dist.CompareTo(b.dist);

        private static readonly Dictionary<Interactables.Interobjects.DoorUtils.DoorPermissionFlags, string> _permCache = new Dictionary<Interactables.Interobjects.DoorUtils.DoorPermissionFlags, string>();
        public DoorsModule()
        {
            for (int i = 0; i < _doorPool.Length;    i++) 
                _doorPool[i]    = new DoorData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _memoriaPuertas.Clear();
            _doorColliderCache.Clear();
            _cachedNearDoors.Clear();
            _doorsConDist.Clear();
            _cachedDoors  = null;
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACachePuertas(obs);
                return;
            }
            _frameCounter = 0;
            _cachedNearDoors.Clear();
            _doorsConDist.Clear();
            //_doorColliderCache.Clear();
            obs.NearDoors.Clear();

            try { _CargarPuertas(_player.Position, ModuleUtils.GetBestKeycardTier(_player)); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en PUERTAS: {ex.Message}"); }
            _CopiarACachePuertas(obs);
            Log.Debug($"[Perf-BASE] Tras CopiarACachePuertas: obs.NearDoors={obs.NearDoors.Count}");
        }

        private void _CargarPuertas(Vector3 pos, int playerTier)
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
            var allDoors = Door.List;
            foreach (var d in _cachedDoors)
            {
                if (d == null) continue;

                try
                {
                    if (d.GameObject == null || d.Transform == null) continue;

                    float dist = Vector3.Distance(d.Transform.position, pos);
                    if (dist >= 50f) continue;

                    // Filtro de visibilidad — FOV + raycast
                    if (!ModuleUtils.EsVisible(_player, misOjos, miMirada, d.Position, dist, d.GameObject)) continue;

                    // Visible ahora — registrar/actualizar memoria
                    int reqTier = ModuleUtils.GetDoorRequiredTier(d);
                    int doorId = d.GameObject.GetInstanceID();
                    var mem = _memoriaPuertas.ObtenerORegistrar(doorId, d.Position, ahora, d);
                    mem.PermisoPuerta = reqTier;
                    mem.PuertaAbierta = d.IsOpen;
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
                    if (!_permCache.TryGetValue(d.RequiredPermissions, out string permStr))
                    {
                        permStr = d.RequiredPermissions.ToString();
                        _permCache[d.RequiredPermissions] = permStr;
                    }

                    int reqTier = ModuleUtils.GetDoorRequiredTier(d);

                    var dd = _doorPool[doorCount];
                    dd.Type         = permStr;
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
                if (dist >= 60f) continue; // ya muy lejos, no relevante


                var dd = _doorPool[doorCount];
                var doorRef = mem.ReferenciaObjeto as Door;

                
                _memoriaPuertas.PurgarOlvidados(ahora);

                if (doorRef != null && doorRef.GameObject != null)
                {
                    int doorId = doorRef.GameObject.GetInstanceID();
                    if (!_doorColliderCache.TryGetValue(doorId, out string colliderName))
                    {
                        colliderName = "Unknown";
                        var colliders = doorRef.GameObject.GetComponentsInChildren<Collider>(true);
                        var valid = System.Array.Find(colliders,
                            c => !c.isTrigger && !c.name.Contains("TouchScreenPanel") && !c.name.Contains("Frame"));
                        if (valid != null) colliderName = valid.name;
                        _doorColliderCache[doorId] = colliderName;
                    }
                    if (!_permCache.TryGetValue(doorRef.RequiredPermissions, out string permStr))
                    {
                        permStr = doorRef.RequiredPermissions.ToString();
                        _permCache[doorRef.RequiredPermissions] = permStr;
                    }
                    int reqTier = ModuleUtils.GetDoorRequiredTier(doorRef);
                    dd.Type         = permStr; // no tenemos el wrapper Door a mano, solo posición
                    dd.Name         = doorRef.Name;
                    dd.ColliderName = colliderName;
                    dd.CanOpen      = playerTier >= reqTier;
                    dd.RequiredTier = ModuleUtils.GetDoorRequiredTier(doorRef);
                }
                else
                {
                    // Puerta cuyo GameObject fue destruido (cambio de ronda) pero sigue en memoria
                    // Usamos los datos cacheados en la memoria misma
                    dd.Type         = "Unknown";
                    dd.Name         = "Recordada";
                    dd.ColliderName = "Unknown";
                    dd.CanOpen      = false; // no podemos saber sin el wrapper
                    dd.RequiredTier = mem.PermisoPuerta; // lo guardaste al registrar
                }
                
                dd.Distance     = dist / 50f;
                
                
                dd.IsOpen       = mem.PuertaAbierta; // último estado conocido
                dd.RelX         = (mem.UltimaPosicion.x - pos.x) / 50f;
                dd.RelY         = (mem.UltimaPosicion.y - pos.y) / 50f;
                dd.RelZ         = (mem.UltimaPosicion.z - pos.z) / 50f;
                dd.RealRelX     = mem.UltimaPosicion.x - pos.x;
                dd.RealRelY     = mem.UltimaPosicion.y - pos.y;
                dd.RealRelZ     = mem.UltimaPosicion.z - pos.z;
                dd.EsRecordado  = true;
                dd.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO; // normalizado 0-1
                _cachedNearDoors.Add(dd);
                doorCount++;
            }

        }

        private void _CopiarACachePuertas(AgentObservation obs)
        {
            obs.NearDoors.Clear();
            obs.NearDoors.AddRange(_cachedNearDoors);
        }

    }
}