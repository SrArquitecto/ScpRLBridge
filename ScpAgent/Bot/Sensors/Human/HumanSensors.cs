using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Lockers;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Data;
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
        private readonly List<(Locker Locker, float DistMetros)> _lockersVisiblesConDist = new List<(Locker, float)>(5);
        private readonly List<(ObjectMemoryLocker Memoria, float DistMetros)> _lockersRecordadosConDist = new List<(ObjectMemoryLocker, float)>(5);

        private List<KeycardData> _cachedNearKeycards { get; set; } = new List<KeycardData>();
        private List<LockerData> _cachedNearLockers { get; set; } = new List<LockerData>();
        private List<ItemData> _cachedNearItems { get; set;} = new List<ItemData>();
        //private List<Pickup> _keycardsVisiblesConDist = new List<Pickup>();

// Cachés para el procesamiento de Keycards (Colócalas junto a _liftsConDist)
        private readonly List<(Pickup Pickup, float DistMetros, int Tier)> _keycardsVisiblesConDist = new List<(Pickup, float, int)>();
        private readonly List<(ObjectMemoryKeycard Memoria, float DistMetros, int Tier)> _keycardsRecordadasConDist = new List<(ObjectMemoryKeycard, float, int)>();
        
        private List<Locker> _cachedLockers;
        private List<Pickup> _cachedItems;
        
        


        private readonly KeycardData[]  _keycardPool = new KeycardData[5];
        private readonly ItemData[] _itemPool = new ItemData[10];
        private readonly LockerData[]   _lockerPool  = new LockerData[5];


        // ── Listas globales cacheadas (se cargan UNA VEZ por ronda) ───────────
        private List<Pickup> _cachedKeys;
  

        private readonly VisualMemory<ObjectMemoryKeycard> _memoriaKeycards = new VisualMemory<ObjectMemoryKeycard>(TIEMPO_OLVIDO_OBJETOS);
        private readonly VisualMemory<ObjectMemoryLocker> _memoriaLockers  = new VisualMemory<ObjectMemoryLocker>(TIEMPO_OLVIDO_OBJETOS);
        private readonly VisualMemory<ObjectMemoryKeycard> _memoriaItems = new VisualMemory<ObjectMemoryKeycard>(TIEMPO_OLVIDO_OBJETOS);

        // ── Cache de collider name por puerta (evita GetComponentsInChildren) ──
        // Se llena la primera vez que se procesa cada puerta y se reutiliza

        //refencias a las funciones de las estrategias:
        private readonly InventoryItemData[] _inventoryPool = new InventoryItemData[8];

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
            for (int i = 0; i < _itemPool.Length;  i++) _itemPool[i]  = new ItemData();
            for (int i = 0; i < _inventoryPool.Length;  i++) _inventoryPool[i]  = new InventoryItemData();
        }

        public void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            _fnPrioridad = fnPrioridad;
            _fnCategoria = fnCategoria;
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
            playerTier  = 0;
            
            
            hasKeycard = _player.Items.Any(i => _IsKeycard(i.Type));
            playerTier = GetBestKeycardTier(_player);
            

            AgentObservation observation = base.GetCurrentState(fixedDelta, accionAnterior, reward, done, role, playerTier);


            Vector3 pos         = _player.Position;
            AgentCacheData data = GetData();
            
            observation.HasKeycard = hasKeycard;
            observation.KeycardTier = playerTier;

            _CargarElementosCercanos(pos, data.halfX, data.halfY, data.halfZ, playerTier, observation);
            _ProcesarAimRaycast(observation);
            _CargarInventario(observation);

            
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
        
            try { _CargarItems(pos); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en KEYCARDS: {ex.Message}"); }

            try { _CargarLockers(pos); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en LOCKERS: {ex.Message}"); }
        
            float elapsed = (UnityEngine.Time.realtimeSinceStartup - t0) * 1000f;
            //Log.Info($"Elapsed: {elapsed}");
            
        
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

        
        private void _CargarLockers(Vector3 pos)
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

        private void _CargarItems(Vector3 pos)
        {
            if (_cachedItems == null)
                _cachedItems = new List<Pickup>(Pickup.List);
            else
            {
                _cachedItems.Clear();
                _cachedItems.AddRange(Pickup.List);
            }
        
            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
        
            _memoriaItems.MarcarTodosNoVistos();

            // ── 1. Filtrar por rango + visibilidad real ───────────────────────────
            var itemsConDist = new List<(Pickup p, float dist)>(20); // considera cachear esto como campo si se llama a menudo
            foreach (var pk in _cachedItems)
            {
                if (pk == null || !pk.IsSpawned || pk.Transform == null) continue;
        
                float dist = Vector3.Distance(pk.Transform.position, pos);
                if (dist >= 25f) continue;
        
                if (!_EsVisible(misOjos, miMirada, pk.Position, dist, pk.GameObject)) continue;
        
                int itemId = pk.GameObject.GetInstanceID();
                var mem = _memoriaItems.ObtenerORegistrar(itemId, pk.Position, ahora, pk);
                mem.Tipo = pk.Type;
                mem.Tier = GetKeycardTier(pk.Type);

                itemsConDist.Add((pk, dist));
            }
        
            // Ordenar por prioridad del rol activo, no solo por distancia
            itemsConDist.Sort((a, b) =>
            {
                float prioA = _fnPrioridad?.Invoke(a.p.Type) ?? 10f;
                float prioB = _fnPrioridad?.Invoke(b.p.Type) ?? 10f;
                // Prioridad descendente; a igual prioridad, más cercano primero
                int cmp = prioB.CompareTo(prioA);
                return cmp != 0 ? cmp : a.dist.CompareTo(b.dist);
            });
        
            // ── 2. Volcar items VISTOS AHORA al pool ──────────────────────────────
            int itemCount = 0;
            foreach (var (pk, dist) in itemsConDist)
            {
                if (itemCount >= 10) break;
        
                var id = _itemPool[itemCount];
                id.Type      = pk.Type.ToString();
                id.Category  = _fnCategoria?.Invoke(pk.Type) ?? "Other";
                id.Prioridad = _fnPrioridad?.Invoke(pk.Type) ?? 10f;
                id.Distance  = dist / 25f;
                id.RelX      = (pk.Position.x - pos.x) / 25f;
                id.RelY      = (pk.Position.y - pos.y) / 25f;
                id.RelZ      = (pk.Position.z - pos.z) / 25f;
                id.RealRelX  = pk.Position.x - pos.x;
                id.RealRelY  = pk.Position.y - pos.y;
                id.RealRelZ  = pk.Position.z - pos.z;
                id.EsRecordado = false;
                id.Antiguedad   = 0f;
                _cachedNearItems.Add(id);
                itemCount++;
            }
        
            // ── 3. Volcar items RECORDADOS ─────────────────────────────────────────
            foreach (var kv in _memoriaItems.Entradas)
            {
                if (itemCount >= 10) break;
                if (kv.Value.VistoEsteCiclo) continue;
        
                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 25f * 1.2f) continue;
        
                var tipoRecordado = mem.Tipo;
        
                var id = _itemPool[itemCount];
                id.Type      = tipoRecordado.ToString();
                id.Tier = GetKeycardTier(tipoRecordado);
                id.Category  = _fnCategoria?.Invoke(tipoRecordado) ?? "Other";
                id.Prioridad = _fnPrioridad?.Invoke(tipoRecordado) ?? 10f;
                id.Distance  = dist / 25f;
                id.RelX      = (mem.UltimaPosicion.x - pos.x) / 25f;
                id.RelY      = (mem.UltimaPosicion.y - pos.y) / 25f;
                id.RelZ      = (mem.UltimaPosicion.z - pos.z) / 25f;
                id.RealRelX  = mem.UltimaPosicion.x - pos.x;
                id.RealRelY  = mem.UltimaPosicion.y - pos.y;
                id.RealRelZ  = mem.UltimaPosicion.z - pos.z;
                id.EsRecordado = true;
                id.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO_OBJETOS;
                _cachedNearItems.Add(id);
                itemCount++;
            }
        
            _memoriaItems.PurgarOlvidados(ahora);
        }

        private void _CargarInventario(AgentObservation obs)
        {
            obs.Inventory.Clear();
            if (_player == null || !_player.IsAlive) return;

            var itemEquipado = _player.CurrentItem;
            int slotIndex = 0;

            foreach (var item in _player.Items)
            {
                if (slotIndex >= 8) break;
                if (item == null) continue;

                var inv = _inventoryPool[slotIndex];
                inv.Type       = item.Type.ToString();
                inv.Category   = _fnCategoria?.Invoke(item.Type) ?? "Other";
                inv.Tier       = GetKeycardTier(item.Type);
                inv.IsEquipped = itemEquipado != null && item.Serial == itemEquipado.Serial;
                inv.Ammo       = 0;

                // Balas en cargador solo del arma equipada
                try
                {
                    if (inv.IsEquipped)
                    {
                        var firearmsItem = item as Exiled.API.Features.Items.Firearm;
                        if (firearmsItem != null)
                            inv.Ammo = firearmsItem.MagazineAmmo;
                    }
                }
                catch { }

                obs.Inventory.Add(inv);
                slotIndex++;
            }

            obs.InventorySlots = 8 - slotIndex;

            // Munición en reserva — sistema separado del inventario
            try
            {
                obs.Ammo9x19    = _player.GetAmmo(AmmoType.Nato9);
                obs.Ammo12gauge = _player.GetAmmo(AmmoType.Ammo12Gauge);
                obs.Ammo556x45  = _player.GetAmmo(AmmoType.Nato556);
                obs.Ammo762x39  = _player.GetAmmo(AmmoType.Nato762);
                obs.Ammo44cal   = _player.GetAmmo(AmmoType.Ammo44Cal);
            }
            catch { }
        }

        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────
        

        protected override void _CopiarACache(AgentObservation obs)
        {
            obs.NearItems.Clear();
            obs.NearLockers.Clear();
            obs.NearItems.AddRange(_cachedNearItems);
            obs.NearLockers.AddRange(_cachedNearLockers);
        }
        
        public override void ResetEstado()
        {
            base.ResetEstado();
            // ── Estado de movimiento ─────────────────────────────────────────
 
            // ── Listas de entorno cercano ────────────────────────────────────
            
            _cachedNearItems.Clear();
            _memoriaItems.Clear();
            _cachedNearLockers.Clear();
            _memoriaLockers.Clear();



            // ── Caches de mapa (se recargan en el primer tick de la nueva ronda)
            _cachedItems   = null;
            
            _cachedLockers = null;
            

            // ── Contador de frames ───────────────────────────────────────────
            //_frameCounter = 0;

            Log.Debug($"[AgentSensors] Sensores reseteados para nueva ronda.");
        }

        public override void Destruir()
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
                        else t = 0;
                        break;
                }
                if (t > tier) tier = t;
            }
            return tier;
        }     // implementa tu lógica
        public static int GetKeycardTier(ItemType tipo)
        {
            switch (tipo)
            {
                case ItemType.KeycardJanitor:             return 1;
                case ItemType.KeycardScientist:
                case ItemType.KeycardResearchCoordinator:
                case ItemType.KeycardChaosInsurgency:     return 2;
                case ItemType.KeycardGuard:
                case ItemType.KeycardMTFPrivate:          return 3;
                case ItemType.KeycardZoneManager:
                case ItemType.KeycardMTFOperative:
                case ItemType.KeycardFacilityManager:     return 4;
                case ItemType.KeycardMTFCaptain:
                case ItemType.KeycardO5:                  return 5;
                default:                                   return 0; // no es keycard
            }
        }
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
        } 
        public string CategorizarItem(ItemType tipo)
        {
            string s = tipo.ToString();
            if (s.StartsWith("Gun"))              return "Weapon";
            if (s.StartsWith("Ammo"))             return "Ammo";
            if (s.StartsWith("Armor"))            return "Armor";
            if (s.Contains("Keycard"))            return "Keycard";
            if (s == "Medkit" || s == "Painkillers" || s == "Adrenaline") return "Medical";
            if (s.StartsWith("Grenade") || s == "SCP018") return "Tactical";
            return "Other";
        }
    // implementa tu lógica

        private bool IsKeycardTypeName(string itemTypeName) => itemTypeName?.IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;
   // implementa tu lógica
        
    }
}