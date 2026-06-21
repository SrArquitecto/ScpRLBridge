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
using ScpAgent.Bot.Sensors.Memory;
using ScpAgent.Bot.Sensors.Memory.Data;

namespace ScpAgent.Bot.Sensors
{
    public class HumanSensors : BaseSensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
          

        // ── Caché del raycast de apuntado ───────────────────────────────────────

// Cachés para el procesamiento de Lockers
        private readonly List<(Locker Locker, float DistMetros)> _lockersVisiblesConDist = new List<(Locker, float)>(16);
        private readonly List<(ObjectMemoryLocker Memoria, float DistMetros)> _lockersRecordadosConDist = new List<(ObjectMemoryLocker, float)>(16);

        private List<KeycardData> _cachedNearKeycards { get; set; } = new List<KeycardData>();
        //private List<Pickup> _keycardsVisiblesConDist = new List<Pickup>();

// Cachés para el procesamiento de Keycards (Colócalas junto a _liftsConDist)
        private readonly List<(Pickup Pickup, float DistMetros, int Tier)> _keycardsVisiblesConDist = new List<(Pickup, float, int)>(16);
        private readonly List<(ObjectMemoryKeycard Memoria, float DistMetros, int Tier)> _keycardsRecordadasConDist = new List<(ObjectMemoryKeycard, float, int)>(16);

        private List<Locker> _cachedLockers;

        private List<LockerData> _cachedNearLockers { get; set; } = new List<LockerData>();
        


        private readonly KeycardData[]  _keycardPool = new KeycardData[5];

        private readonly LockerData[]   _lockerPool  = new LockerData[5];


        // ── Listas globales cacheadas (se cargan UNA VEZ por ronda) ───────────
        private List<Pickup> _cachedKeys;
  

        private readonly VisualMemory<ObjectMemoryKeycard> _memoriaKeycards = new VisualMemory<ObjectMemoryKeycard>(TIEMPO_OLVIDO_OBJETOS);
        private readonly VisualMemory<ObjectMemoryLocker> _memoriaLockers  = new VisualMemory<ObjectMemoryLocker>(TIEMPO_OLVIDO_OBJETOS);

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
            base.Init();
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

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
            
            // 1. Resetear el estado de vision de la memoria
            _memoriaKeycards.MarcarTodosNoVistos();

            _keycardsVisiblesConDist.Clear();

            // 2. Filtrar y Registrar Tarjetas Visibles Actualmente
            foreach (var pk in _cachedKeys)
            {
                if (pk == null || !_IsKeycard(pk.Type) || pk.Transform == null || !pk.IsSpawned) 
                    continue;

                try
                {
                    if (pk.GameObject == null) continue;

                    float distMetros = Vector3.Distance(pk.Transform.position, pos);
                    
                    // Filtro de rango de radar normalizado (20f * 20f = 400 metros max según tu escala)
                    if (distMetros / 20f > 20f) continue; 

                    // Filtro de oclusión física (Raycast + FOV)
                    if (!_EsVisible(misOjos, miMirada, pk.Position, distMetros, pk.GameObject)) continue;

                    // ¡Es visible! Lo registramos o actualizamos en la memoria
                    int id = pk.GameObject.GetInstanceID();
                    var mem = _memoriaKeycards.ObtenerORegistrar(id, pk.Position, ahora, pk);
                    mem.Tipo = pk.Type;
                    mem.Tier = GetBestKeycardTier(pk.Type);

                    _keycardsVisiblesConDist.Add((pk, distMetros, mem.Tier));
                }
                catch { continue; }
            }

            // Ordenar visibles: 1º Mayor Tier, 2º Más cercana
            _keycardsVisiblesConDist.Sort((a, b) => {
                int tierCompare = b.Tier.CompareTo(a.Tier); // Descendente
                if (tierCompare != 0) return tierCompare;
                return a.DistMetros.CompareTo(b.DistMetros); // Ascendente
            });

            int keycardCount = 0;

            // 3. Volcar Tarjetas Visibles al Pool
            foreach (var (pk, distMetros, tier) in _keycardsVisiblesConDist)
            {
                if (keycardCount >= _keycardPool.Length) break;

                var kd = _keycardPool[keycardCount];
                kd.Type        = pk.Type.ToString();
                kd.Distance    = distMetros / 20f;
                kd.RelX        = (pk.Position.x - pos.x) / 20f;
                kd.RelY        = (pk.Position.y - pos.y) / 20f;
                kd.RelZ        = (pk.Position.z - pos.z) / 20f;
                kd.RealRelX    = pk.Position.x - pos.x;
                kd.RealRelY    = pk.Position.y - pos.y;
                kd.RealRelZ    = pk.Position.z - pos.z;
                
                // Campos de control para la IA (Recuerda añadirlos a tu KeycardData si no existen)
                kd.EsRecordado = false;
                kd.Antiguedad  = 0f;

                _cachedNearKeycards.Add(kd);
                keycardCount++;
            }

            // 4. Procesar Tarjetas Recordadas (Las que quedan en memoria pero no se ven)
            _keycardsRecordadasConDist.Clear();
            foreach (var kv in _memoriaKeycards.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue; // Ya procesada arriba

                var mem = kv.Value;
                float distMetros = Vector3.Distance(mem.UltimaPosicion, pos);
                
                if (distMetros / 20f > 20f) continue; // Muy lejos del radar

                _keycardsRecordadasConDist.Add((mem, distMetros, mem.Tier));
            }

            // Ordenar también las recordadas bajo el mismo criterio táctico
            _keycardsRecordadasConDist.Sort((a, b) => {
                int tierCompare = b.Tier.CompareTo(a.Tier);
                if (tierCompare != 0) return tierCompare;
                return a.DistMetros.CompareTo(b.DistMetros);
            });

            // Volcar Recordadas al Pool hasta llenarlo (máximo 5 u el tamaño del pool)
            foreach (var (mem, distMetros, tier) in _keycardsRecordadasConDist)
            {
                if (keycardCount >= _keycardPool.Length) break;

                var kd = _keycardPool[keycardCount];
                var pkRef = mem.ReferenciaObjeto as Pickup;

                // Comprobación segura por si el objeto fue destruido/recogido del suelo mientras no mirábamos
                if (pkRef != null && pkRef.GameObject != null)
                {
                    kd.Type = pkRef.Type.ToString();
                }
                else
                {
                    kd.Type = mem.Tipo.ToString(); // Fallback con el último tipo guardado
                }

                kd.Distance    = distMetros / 20f;
                kd.RelX        = (mem.UltimaPosicion.x - pos.x) / 20f;
                kd.RelY        = (mem.UltimaPosicion.y - pos.y) / 20f;
                kd.RelZ        = (mem.UltimaPosicion.z - pos.z) / 20f;
                kd.RealRelX    = mem.UltimaPosicion.x - pos.x;
                kd.RealRelY    = mem.UltimaPosicion.y - pos.y;
                kd.RealRelZ    = mem.UltimaPosicion.z - pos.z;
                
                kd.EsRecordado = true;
                kd.Antiguedad  = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO_OBJETOS;

                _cachedNearKeycards.Add(kd);
                keycardCount++;
            }

            // 5. Limpieza final de elementos obsoletos de esta categoría
            _memoriaKeycards.PurgarOlvidados(ahora);
        }

        
        private void _CargarLockers(Vector3 pos, float halfX, float halfY, float halfZ)
        {
            if (_cachedLockers == null) _cachedLockers = new List<Locker>(Locker.List);
            else { _cachedLockers.Clear(); _cachedLockers.AddRange(Locker.List); }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
            
            // 1. Resetear visibilidad en la memoria de lockers
            _memoriaLockers.MarcarTodosNoVistos();

            _lockersVisiblesConDist.Clear();

            // 2. Filtrar y registrar Lockers que el bot está VIENDO ahora mismo
            foreach (var l in _cachedLockers)
            {
                if (l == null || l.Transform == null || l.GameObject == null) continue;

                try
                {
                    float distMetros = Vector3.Distance(l.Position, pos);
                    if (distMetros > 25f) continue; // Rango máximo de radar para lockers

                    // Filtro de oclusión física (FOV + Raycast)
                    if (!_EsVisible(misOjos, miMirada, l.Position, distMetros, l.GameObject)) continue;

                    // ¡Es visible! Lo registramos o actualizamos en la memoria genérica
                    int lockerId = l.GameObject.GetInstanceID();
                    var mem = _memoriaLockers.ObtenerORegistrar(lockerId, l.Position, ahora, l);
                    //mem.ReferenciaObjeto = l;
                    mem.TipoLocker = l.Type.ToString();

                    _lockersVisiblesConDist.Add((l, distMetros));
                }
                catch { continue; }
            }

            // Ordenar los visibles por cercanía (el más cercano primero)
            _lockersVisiblesConDist.Sort((a, b) => a.DistMetros.CompareTo(b.DistMetros));

            int lockerCount = 0;

            // 3. Volcar los lockers VISIBLES actuales al pool
            foreach (var (l, distMetros) in _lockersVisiblesConDist)
            {
                if (lockerCount >= _lockerPool.Length) break;

                var lkd = _lockerPool[lockerCount];
                lkd.Type      = l.Type.ToString();
                lkd.Distance  = distMetros / 25f; // Normalizado a tu escala de 25m
                lkd.HasIsOpen = false;            // Mantener tu lógica de EXILED por chamber
                lkd.RelX      = (l.Position.x - pos.x) / 25f;
                lkd.RelY      = (l.Position.y - pos.y) / 25f;
                lkd.RelZ      = (l.Position.z - pos.z) / 25f;
                lkd.RealRelX  = l.Position.x - pos.x;
                lkd.RealRelY  = l.Position.y - pos.y;
                lkd.RealRelZ  = l.Position.z - pos.z;
                
                // Control de memoria para Python
                lkd.EsRecordado = false;
                lkd.Antiguedad  = 0f;

                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }

            // 4. Procesar los lockers RECORDADOS (los que están en memoria pero ya no se ven)
            _lockersRecordadosConDist.Clear();
            foreach (var kv in _memoriaLockers.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue; // Ya procesado arriba

                var mem = kv.Value;
                float distMetros = Vector3.Distance(mem.UltimaPosicion, pos);
                if (distMetros > 30f) continue; // Si se aleja demasiado (ej. 30m), deja de ser relevante en su radar inmediato

                _lockersRecordadosConDist.Add((mem, distMetros));
            }

            // Ordenar los recuerdos por cercanía
            _lockersRecordadosConDist.Sort((a, b) => a.DistMetros.CompareTo(b.DistMetros));

            // Volcar los recuerdos en los huecos sobrantes del pool
            foreach (var (mem, distMetros) in _lockersRecordadosConDist)
            {
                if (lockerCount >= _lockerPool.Length) break;

                var lkd = _lockerPool[lockerCount];
                var lockerRef = mem.ReferenciaObjeto as Locker;

                // Comprobación segura por si el mapa cambia o el objeto sufre modificaciones
                if (lockerRef != null && lockerRef.GameObject != null)
                {
                    lkd.Type = lockerRef.Type.ToString();
                }
                else
                {
                    lkd.Type = mem.TipoLocker; // Fallback al string guardado
                }

                lkd.Distance  = distMetros / 25f;
                lkd.HasIsOpen = false;
                lkd.RelX      = (mem.UltimaPosicion.x - pos.x) / 25f;
                lkd.RelY      = (mem.UltimaPosicion.y - pos.y) / 25f;
                lkd.RelZ      = (mem.UltimaPosicion.z - pos.z) / 25f;
                lkd.RealRelX  = mem.UltimaPosicion.x - pos.x;
                lkd.RealRelY  = mem.UltimaPosicion.y - pos.y;
                lkd.RealRelZ  = mem.UltimaPosicion.z - pos.z;
                
                lkd.EsRecordado = true;
                lkd.Antiguedad  = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO_OBJETOS;

                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }

            // 5. Purgar memorias obsoletas de lockers
            _memoriaLockers.PurgarOlvidados(ahora);
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
            base.ResetEstado();
            // ── Estado de movimiento ─────────────────────────────────────────
 
            // ── Listas de entorno cercano ────────────────────────────────────
            
            _cachedNearKeycards.Clear();
            
            _cachedNearLockers.Clear();
            



            // ── Caches de mapa (se recargan en el primer tick de la nueva ronda)
            _cachedKeys   = null;
            
            _cachedLockers = null;
            

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
        
    }
}