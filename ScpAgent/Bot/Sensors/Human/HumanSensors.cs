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
using PlayerRoles;

namespace ScpAgent.Bot.Sensors
{
    public class HumanSensors : BaseSensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
          

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
        public HumanSensors(int agentId) : base(agentId)
        {
        }

        public override void Init()
        {

            for (int i = 0; i < _keycardPool.Length; i++) _keycardPool[i] = new KeycardData();
            for (int i = 0; i < _lockerPool.Length;  i++) _lockerPool[i]  = new LockerData();
        }

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public override AgentObservation GetCurrentState(
            float fixedDelta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {   
            if (_player == null || !_player.IsAlive || _player.GameObject == null) 
                return obsVacia;

            // Si la cámara aún no se ha creado en el nuevo cuerpo, abortamos este frame
            if (_player.CameraTransform == null) 
                return obsVacia;
            bool hasKeycard  = false;
            playerTier  = 3;
            if (role == RoleTypeId.Scientist || role == RoleTypeId.ClassD)
            {
                 hasKeycard = _player.Items.Any(i => _IsKeycard(i.Type));
                 playerTier = GetBestKeycardTier(_player);
            }

            AgentObservation observation = base.GetCurrentState(fixedDelta, accionAnterior, reward, done, role, playerTier);


            Vector3 pos         = _player.Position;
            AgentCacheData data = GetData();
            
            observation.HasKeycard = hasKeycard;
            observation.KeycardTier = playerTier;

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
        
            _cachedNearKeycards.Clear();
            _cachedNearDoors.Clear();
            _cachedNearLifts.Clear();
            _cachedNearLockers.Clear();
            _cachedNearRooms.Clear();
            //_doorColliderCache.Clear();
            _frameCounter = 0;
        
            try { _CargarKeycards(pos, halfX, halfY, halfZ); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en KEYCARDS: {ex.Message}"); }

            try { _CargarLockers(pos, halfX, halfY, halfZ); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en LOCKERS: {ex.Message}"); }
        
            float elapsed = (UnityEngine.Time.realtimeSinceStartup - t0) * 1000f;
            //Log.Info($"Elapsed: {elapsed}");
            if (elapsed > 2f)
                Log.Debug($"[Perf] _CargarElementosCercanos tardó {elapsed:F1}ms " +
                        $"(Habitaciones={_cachedNearRooms?.Count} keys={_cachedKeys?.Count})");
        
            _CopiarACache(obs);
        }

        private void _CargarKeycards(Vector3 pos, float halfX, float halfY, float halfZ)
        {
            if (_cachedKeys == null) _cachedKeys = new List<Pickup>(Pickup.List);
            else { _cachedKeys.Clear(); _cachedKeys.AddRange(Pickup.List); }
        
            var tarjetasPrioritarias = _cachedKeys
            .Where(pk => pk != null && _IsKeycard(pk.Type) && pk.Transform != null && pk.IsSpawned)
            .Select(pk => new 
            { 
                Tipo = pk.Type,
                Pickup = pk, 
                DistanciaReal = Vector3.Distance(pk.Transform.position, pos)/20,
                RelX = (pk.Position.x - pos.x) / 20f,
                RelY = (pk.Position.y - pos.y) / 20f,
                RelZ = (pk.Position.z - pos.z) / 20f,
                RealRelX = (pk.Position.x - pos.x),
                RealRelY = (pk.Position.y - pos.y),
                RealRelZ = (pk.Position.z - pos.z),
                Tier = GetBestKeycardTier(pk.Type) // Evaluamos el peso/importancia de la tarjeta
            })
            .Where(x => x.DistanciaReal <= 20f)       // Filtro de rango de radar (20 metros)
            .OrderByDescending(x => x.Tier)          // 1º Criterio: Mayor nivel primero (O5 > Scientist > Janitor)
            .ThenBy(x => x.DistanciaReal)            // 2º Criterio: En caso de igual Tier, la más cercana primero
            .Take(5)                                 // Nos quedamos estrictamente con las 5 mejores
            .ToList();


            int keycardCount = 0;
            foreach (var pk in tarjetasPrioritarias)
            {
                if (pk == null) continue;
        
                // Reutilizar objeto del pool en vez de new KeycardData
                var kd = _keycardPool[keycardCount];
                kd.Type     = pk.Tipo.ToString();
                kd.Distance = pk.DistanciaReal;
                kd.RelX = pk.RelX;
                kd.RelY = pk.RelY;
                kd.RelZ = pk.RelZ;
                kd.RealRelX = pk.RealRelX;
                kd.RealRelY = pk.RealRelY;
                kd.RealRelZ = pk.RealRelZ;
                _cachedNearKeycards.Add(kd);
                keycardCount++;
            }
        }

        
        private void _CargarLockers(Vector3 pos, float halfX, float halfY, float halfZ)
        {
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
                lkd.RelX = (l.Position.x - pos.x) / 25f;
                lkd.RelY = (l.Position.y - pos.y) / 25f;
                lkd.RelZ = (l.Position.z - pos.z) / 25f;
                lkd.RealRelX  = l.Position.x - pos.x;
                lkd.RealRelY  = l.Position.y - pos.y;
                lkd.RealRelZ  = l.Position.z - pos.z;
                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────
        

        protected override void _CopiarACache(AgentObservation obs)
        {
            obs.NearKeycards.Clear();
            obs.NearLockers.Clear();
            obs.NearKeycards.AddRange(_cachedNearKeycards);
            obs.NearLockers.AddRange(_cachedNearLockers);
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
        private int GetBestKeycardTier(ItemType p)
        {
            int t = 0;
            switch (p)
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
                    t = 1;
                    break;
            }
                
            return t;
        }     // implementa tu lógica

        private bool IsKeycardTypeName(string itemTypeName) => itemTypeName?.IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;
   // implementa tu lógica
        protected override void ObtenerListaSalasPriorizadas(int tierTarjeta)
        {
            //List<Habitaciones> listaPriorizada = new List<Habitaciones>();

            foreach (Room sala in _cachedRooms)
            {
                // Ignoramos salas desconocidas o zonas muertas
                if (sala.Type == RoomType.Unknown) continue;
                if (sala == null || sala.GameObject == null) continue;
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
    }
}