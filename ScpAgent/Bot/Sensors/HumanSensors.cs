using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot;
using Exiled.API.Features.Lockers;
using Exiled.API.Enums;
using RemoteAdmin.Communication;

namespace ScpAgent.Bot.Sensors
{
    public class HumanSensors : AgentSensorsBase
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────

        private int _frameCounter = UPDATE_FREQUENCY;
        private const int UPDATE_FREQUENCY = 20;
       

        // ── Caché del raycast de apuntado ───────────────────────────────────────



        private List<KeycardData> _cachedNearKeycards { get; set; } = new List<KeycardData>();


        private List<Locker> _cachedLockers;

        private List<LockerData> _cachedNearLockers { get; set; } = new List<LockerData>();
        


        private readonly KeycardData[]  _keycardPool = new KeycardData[5];

        private readonly LockerData[]   _lockerPool  = new LockerData[5];


        // ── Listas globales cacheadas (se cargan UNA VEZ por ronda) ───────────
        private List<Pickup> _cachedKeys;
  
        private List<Lift>   _cachedLifts;


        // ── Cache de collider name por puerta (evita GetComponentsInChildren) ──
        // Se llena la primera vez que se procesa cada puerta y se reutiliza

        

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public HumanSensors()
        {
            //_RefrescarPosicionBase();
            //_player = player;
            for (int i = 0; i < _keycardPool.Length; i++) _keycardPool[i] = new KeycardData();
            for (int i = 0; i < _lockerPool.Length;  i++) _lockerPool[i]  = new LockerData();

        }

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public override AgentObservation GetCurrentState(
            float fixedDelta, int accionAnterior, float reward, bool done)
        {
            if (_player == null || !_player.IsAlive || _player.GameObject == null) 
                return obsVacia;

            // Si la cámara aún no se ha creado en el nuevo cuerpo, abortamos este frame
            if (_player.CameraTransform == null) 
                return obsVacia;
            

            Vector3 pos         = _player.Position;
            Vector3 camRotation = _player.CameraTransform.rotation.eulerAngles;
            int     playerTier  = GetBestKeycardTier(_player);

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
            AgentCacheData data;
            if (_player == null || !agentCacheData.TryGetValue(_player.Id, out data) || !data.IsDataReady)
            {
                data = _fallbackCacheData;
            }

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
                Health      = _player.Health,
                Zone        = _player.CurrentRoom?.Zone.ToString() ?? "Unknown",
                Room        = _player.CurrentRoom?.Type.ToString() ?? "Unknown",
                HasKeycard  = _player.Items.Any(i => _IsKeycard(i.Type)),
                KeycardTier = playerTier,
                LastAction  = accionAnterior,
                Reward      = reward,
                Done        = done
            };
            
            _CargarElementosCercanos(pos, data.halfX, data.halfY, data.halfZ, playerTier, observation);
            _ProcesarAimRaycast(observation);

            bool canInteract = (observation.AimTarget == "Door" ||
                                observation.AimTarget == "Locker" ||
                                observation.AimTarget == "Pickup")
                               && observation.AimDistance <= 2.4f;
            observation.CanInteract = canInteract ? 1 : 0;
            //if (_player.Nickname == "IA_Agent_0")
                //Log.Info($"PLAYER {_player.Nickname} ACTION: {accionAnterior} | POSICION: {pos} | AIMTARGET: {observation.AimTarget} | AIMDISTANCE: {observation.AimDistance} | VEL LINEAL: {vLin} | VEL LATERAL: {vLat} | VEL VERTICAL: {vLin} | VEL ANGULAR: {angVelYaw}");
            return observation;
        }

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────

        // ───────────────────────────────────────────────────────────────────────
        // ELEMENTOS CERCANOS
        // ───────────────────────────────────────────────────────────────────────
        protected override void _CargarElementosCercanos(Vector3 pos,
            float halfX, float halfY, float halfZ,
            int playerTier, AgentObservation obs)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACache(obs);
                return;
            }
            float t0 = UnityEngine.Time.realtimeSinceStartup;
        
            _cachedNearKeycards.Clear();
            _cachedNearDoors.Clear();
            _cachedNearLifts.Clear();
            _cachedNearLockers.Clear();
            _cachedNearRooms.Clear();
            //_doorColliderCache.Clear();
            _frameCounter = 0;
        
            // ── KEYCARDS ──────────────────────────────────────────────────────────
            if (_cachedKeys == null) _cachedKeys = new List<Pickup>(Pickup.List);
            else { _cachedKeys.Clear(); _cachedKeys.AddRange(Pickup.List); }
        
            int keycardCount = 0;
            foreach (var pk in _cachedKeys)
            {
                if (pk == null || !_IsKeycard(pk.Type) || keycardCount >= 5) continue;
                if (!pk.IsSpawned) continue;
                float d = Vector3.Distance(pk.Transform.position, pos);
                if (d > 20f) continue;
        
                // Reutilizar objeto del pool en vez de new KeycardData
                var kd = _keycardPool[keycardCount];
                kd.Type     = pk.Type.ToString();
                kd.Distance = d / 20f;
                kd.RelX     = Mathf.Clamp((pk.Position.x - pos.x) / halfX, -1f, 1f);
                kd.RelY     = Mathf.Clamp((pk.Position.y - pos.y) / halfY, -1f, 1f);
                kd.RelZ     = Mathf.Clamp((pk.Position.z - pos.z) / halfZ, -1f, 1f);
                kd.RealRelX = pk.Position.x - pos.x;
                kd.RealRelY = pk.Position.y - pos.y;
                kd.RealRelZ = pk.Position.z - pos.z;
                _cachedNearKeycards.Add(kd);
                keycardCount++;
            }
        
            // ── PUERTAS ───────────────────────────────────────────────────────────
            if (_cachedDoors == null)
            {
                _cachedDoors = new List<Door>(Door.List);
                //Log.Info($"[Perf] Puertas cargadas: {_cachedDoors.Count}");
            }
        
            // Reutilizar lista de tuplas cacheada
            _doorsConDist.Clear();
            foreach (var d in _cachedDoors)
            {
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
        
                // Collider name con caché por instanceID
                int doorId = d.GameObject.GetInstanceID();
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
                dd.RelX         = Mathf.Clamp((d.Position.x - pos.x) / halfX, -1f, 1f);
                dd.RelY         = Mathf.Clamp((d.Position.y - pos.y) / halfY, -1f, 1f);
                dd.RelZ         = Mathf.Clamp((d.Position.z - pos.z) / halfZ, -1f, 1f);
                dd.RealRelX     = d.Position.x - pos.x;
                dd.RealRelY     = d.Position.y - pos.y;
                dd.RealRelZ     = d.Position.z - pos.z;
                _cachedNearDoors.Add(dd);
                doorCount++;
            }
        
            // ── ASCENSORES ────────────────────────────────────────────────────────
            if (_cachedLifts == null) _cachedLifts = new List<Lift>(Lift.List);
            else { _cachedLifts.Clear(); _cachedLifts.AddRange(Lift.List); }
        
            int liftCount = 0;
            foreach (var l in _cachedLifts)
            {
                if (l == null || liftCount >= 3) break;
                float d = Vector3.Distance(l.Transform.position, pos);
                if (d > 50f) continue;
        
                // Reutilizar objeto del pool en vez de new LiftData
                var ld = _liftPool[liftCount];
                ld.Type         = l.Type.ToString();
                ld.Distance     = d / 50f;
                ld.IsMoving     = l.IsMoving;
                ld.CanUse       = !l.IsMoving;
                ld.CurrentLevel = l.CurrentLevel;
                ld.RelX         = Mathf.Clamp((l.Position.x - pos.x) / halfX, -1f, 1f);
                ld.RelY         = Mathf.Clamp((l.Position.y - pos.y) / halfY, -1f, 1f);
                ld.RelZ         = Mathf.Clamp((l.Position.z - pos.z) / halfZ, -1f, 1f);
                ld.RealRelX     = l.Position.x - pos.x;
                ld.RealRelY     = l.Position.y - pos.y;
                ld.RealRelZ     = l.Position.z - pos.z;
                _cachedNearLifts.Add(ld);
                liftCount++;
            }
        
            // ── LOCKERS ───────────────────────────────────────────────────────────
            if (_cachedLockers == null)
                _cachedLockers = new List<Locker>(Locker.List);
            else
            {
                _cachedLockers.Clear();
                _cachedLockers.AddRange(Locker.List);
            }

            int lockerCount = 0;
            foreach (var l in _cachedLockers)
            {
                if (l == null || lockerCount >= 5) break;
                float d = Vector3.Distance(l.Position, pos);
                if (d > 25f) continue;

                var lkd = _lockerPool[lockerCount];
                lkd.Type      = l.Type.ToString();
                lkd.Distance  = d / 25f;
                lkd.HasIsOpen = false; // Locker EXILED no tiene IsOpen global — es por chamber
                lkd.RelX      = Mathf.Clamp((l.Position.x - pos.x) / halfX, -1f, 1f);
                lkd.RelY      = Mathf.Clamp((l.Position.y - pos.y) / halfY, -1f, 1f);
                lkd.RelZ      = Mathf.Clamp((l.Position.z - pos.z) / halfZ, -1f, 1f);
                lkd.RealRelX  = l.Position.x - pos.x;
                lkd.RealRelY  = l.Position.y - pos.y;
                lkd.RealRelZ  = l.Position.z - pos.z;
                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }

            if (_cachedRooms == null || _cachedRooms.Count == 0)
                _cachedRooms = new List<Room>(Room.List);

            _roomsPriorizada.Clear();
            ObtenerListaSalasPriorizadas(playerTier);
            // ── SALAS PRIORIZADAS ─────────────────────────────────────────────────
            //var habitaciones = ObtenerListaSalasPriorizadas(_player, playerTier);
            int roomsCounter = 0;
            foreach (var h in _roomsPriorizada)
            {
                if (h == null || roomsCounter >= 5) break;
                var r = _roomPool[roomsCounter];
                r.Nombre    = h.NombreHabitacion;
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
            float elapsed = (UnityEngine.Time.realtimeSinceStartup - t0) * 1000f;
            //Log.Info($"Elapsed: {elapsed}");
            if (elapsed > 2f)
                Log.Debug($"[Perf] _CargarElementosCercanos tardó {elapsed:F1}ms " +
                        $"(Habitaciones={_cachedNearRooms?.Count} keys={_cachedKeys?.Count})");
        
            _CopiarACache(obs);
        }


        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────
        

        protected override void _CopiarACache(AgentObservation obs)
        {
            obs.NearDoors.Clear();
            obs.NearKeycards.Clear();
            obs.NearLifts.Clear();
            obs.NearLockers.Clear();
            obs.NearRooms.Clear();
        
            obs.NearDoors.AddRange(_cachedNearDoors);
            obs.NearKeycards.AddRange(_cachedNearKeycards);
            obs.NearLifts.AddRange(_cachedNearLifts);
            obs.NearLockers.AddRange(_cachedNearLockers);
            obs.NearRooms.AddRange(_cachedNearRooms);
        }
        
        public override void ResetEstado()
        {
            // ── Estado de movimiento ─────────────────────────────────────────
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
            // ── Listas de entorno cercano ────────────────────────────────────
            _cachedNearDoors.Clear();
            _cachedNearKeycards.Clear();
            _cachedNearLifts.Clear();
            _cachedNearLockers.Clear();
            _cachedNearRooms.Clear();
            _doorsConDist.Clear();
            _roomsPriorizada.Clear();

            // ── Caches de mapa (se recargan en el primer tick de la nueva ronda)
            _cachedKeys   = null;
            _cachedDoors  = null;
            _cachedLifts  = null;
            _cachedLockers = null;
            _cachedRooms = null;
            _doorColliderCache.Clear();

            // ── Contador de frames ───────────────────────────────────────────
            //_frameCounter = 0;

            Log.Debug($"[AgentSensors] Sensores reseteados para nueva ronda.");
        }

        public void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }

        private bool _IsKeycard(ItemType t) =>
            t.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        private int GetBestKeycardTier(Player p)
        {
            int tier = 0;
            foreach (var item in p.Items)
            {
                int t = 0;
                switch (item.Type)
                {
                    case ItemType.KeycardJanitor:             t = 1; break;
                    case ItemType.KeycardGuard:               t = 4; break;
                    case ItemType.KeycardScientist:           t = 2; break;
                    case ItemType.KeycardResearchCoordinator: t = 3; break;
                    case ItemType.KeycardChaosInsurgency:     t = 5; break;
                    case ItemType.KeycardMTFPrivate:          t = 5; break;
                    case ItemType.KeycardMTFOperative:        t = 6; break;
                    case ItemType.KeycardMTFCaptain:          t = 7; break;
                    case ItemType.KeycardZoneManager:         t = 8; break;
                    case ItemType.KeycardO5:                  t = 9; break;
                    default:
                        if (IsKeycardTypeName(item.Type.ToString())) t = 1;
                        break;
                }
                if (t > tier) tier = t;
            }
            return tier;
        }     // implementa tu lógica
        private bool IsKeycardTypeName(string itemTypeName) => itemTypeName?.IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;
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
        }    // implementa tu lógica
        protected override void ObtenerListaSalasPriorizadas(int tierTarjeta)
        {
            //List<Habitaciones> listaPriorizada = new List<Habitaciones>();

            foreach (Room sala in _cachedRooms)
            {
                // Ignoramos salas desconocidas o zonas muertas
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
    }
}