using Exiled.API.Features;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using PlayerRoles;
using Mirror;
using MEC;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Simulation;
using Exiled.API.Features.Doors;
using ScpAgent.Managers;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Sensors;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Enums;

namespace ScpAgent.Bot
{

    public class AgentContext
    {
        public Player         Player      { get; private set; }
        public int            AgentId     { get; private set; }
        public RoleTypeId     Rol         { get; private set; }
        public Func<float>    GetReward   { get; private set; } // leer recompensa acumulada
        public Action<float>  AddReward   { get; private set; } // añadir recompensa
        public Action         EndEpisode  { get; private set; } // marcar episodio terminado

        public AgentContext(int agentId, RoleTypeId rol,
            Func<float> getReward, Action<float> addReward, Action endEpisode)
        {
            AgentId    = agentId;
            Rol        = rol;
            GetReward  = getReward;
            AddReward  = addReward;
            EndEpisode = endEpisode;
        }

        // ScpAgentBot actualiza Player tras cada respawn sin recrear el contexto
        public void ActualizarPlayer(Player p) => Player = p;
    }


    /// <summary>
    /// Representa un agente de IA (bot ClassD) dentro del servidor SCP:SL.
    /// Encapsula el spawn, control físico, cámara, sensores y recompensas.
    /// </summary>
    public class ScpAgentBot : IAgentController
    {
        // ── Registro global de agentes activos ─────────────────────────────────
        //public static readonly Dictionary<int, ScpAgentBot> AllAgents = new Dictionary<int, ScpAgentBot>();

        // ── Identidad ───────────────────────────────────────────────────────────
        public int AgentId { get; private set; }
        public Player ExiledPlayer { get; set; }
        public IAgentRoleStrategyBase Strategy;
        public RoleTypeId rol;
        public string Nickname;

        // ── Estado de la acción ─────────────────────────────────────────────────
        private int _ultimaAccion = 12; // 12 = NOOP
        private float _lastActionTime;

        // ── Sensores ────────────────────────────────────────────────────────────
        private ISensors _sensores;
        private AgentContext _ctx;

        // ── Recompensa y estado de episodio ─────────────────────────────────────
        public float PendingReward { get; set; } = 0f;
        public bool EpisodioTerminado { get; set; } = false;

        // ── Referencia al GameObject (no cambia aunque el wrapper Player quede stale) ──
        private GameObject _botGameObject;
        private  IAgentRoleStrategyBase _strategy;

        // ── Reflection cache para FpcMouseLook ─────────────────────────────────
        private FieldInfo _fieldCurH;
        private FieldInfo _fieldCurV;
        private FieldInfo _fieldSyncH;
        private FieldInfo _fieldSyncV;
        private FakeConnection _fakeConn;
        private object _mouseLookInstance;
        public bool _isRespawning { get; private set; } = false;
        private CharacterController _cc;
        private CoroutineHandle _initDelayHandle;
        private CoroutineHandle _respawnHandle;
        private Vector3 _lastPos;
        public int contadorSuscripciones {get; set;} = 0;

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public ScpAgentBot(string nickname, int id, RoleTypeId role = RoleTypeId.ClassD)
        {
            AgentId   = id;
            Nickname = nickname;
            //Strategy = strategy;
            //_fakeConn = fakeConn; // ← recibida desde AgentManager, no creada aquí
            rol = role;
            Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             += OnHurt;
            
            // 1. Clonar el prefab del jugador
        }

        public void Init(FakeConnection fakeConn)
        {
            _fakeConn = fakeConn;
            GameObject prefab = NetworkManager.singleton.playerPrefab;
            _botGameObject = UnityEngine.Object.Instantiate(prefab);

            // 2. Vincular con el servidor de red de Mirror
            ReferenceHub hub = _botGameObject.GetComponent<ReferenceHub>();
            NetworkServer.AddPlayerForConnection(_fakeConn, _botGameObject);
            hub.nicknameSync.Network_myNickSync = Nickname;

            // 3. Obtener wrapper inicial y asignar rol
            ExiledPlayer = Player.Get(_botGameObject);
            ExiledPlayer.Role.Set(rol);

            // 4. Añadir CharacterController inmediatamente (no esperar al delay)
            _cc = _botGameObject.GetComponent<CharacterController>();
            if (_cc == null) _cc = _botGameObject.AddComponent<CharacterController>();

            // Crear contexto con lambdas que apuntan a los campos del bot
            _ctx = new AgentContext(
                agentId:   AgentId,
                rol:       rol,
                getReward: () => PendingReward,
                addReward: r => PendingReward += r,
                endEpisode: () => EpisodioTerminado = true
            );
            // 5. CRÍTICO: refrescar wrapper tras Role.Set
            _initDelayHandle = Timing.CallDelayed(1f, () =>
            {
                var freshPlayer = Player.Get(_botGameObject);
                if (freshPlayer != null)
                {
                    SetPlayer(freshPlayer);
                    Log.Debug($"[ScpAgentBot] Bot {AgentId} ({Nickname}) — wrapper refrescado. " +
                            $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive}");
                }
                else
                {
                    Log.Error($"[ScpAgentBot] Bot {AgentId} — no se pudo refrescar el wrapper.");
                    return;
                }

                // 6. Inicializar reflection de FpcMouseLook
                _InicializarMouseLook();

                // 7. Suscribir eventos de recompensa
                
                addBoundsToCache(ExiledPlayer);

                // 8. Notificar al AgentManager — activa el slot y vincula sensores
                // AgentSensors.VincularPlayer() se llama desde AgentSlot.OnSpawnComplete()
                AgentManager.Instance?.OnBotSpawnComplete(AgentId, ExiledPlayer);
                
            });
        }
        public void SetStrategy(IAgentRoleStrategyBase strategy)
        {   
            _strategy?.OnUnbind();
            _strategy = strategy;
            _strategy.OnBind(_ctx);
            if (_strategy is IAgentRoleStrategyHuman humanStrategy)
            {
                _sensores?.VincularEstrategia(
                tipo => humanStrategy.CalcularPrioridadItem(tipo),
                tipo => humanStrategy.CategorizarItem(tipo)
                );
            }
            else
            {
                _sensores?.VincularEstrategia(
                    tipo => 0f, 
                    tipo => "Ninguno"
                    // Alternativa: Si creaste un método específico para limpiar:
                    // _sensores?.DesvincularEstrategia();
                );
                // La estrategia actual es un SCP u otro rol que no implementa la interfaz humana
                // Aquí puedes ignorar el ítem o darle prioridad 0
            }
            // Pasar solo los delegados al sensor, no la estrategia completa
            
        }


        public void SetPlayer(Player exiledPlayer)
        {
            ExiledPlayer = exiledPlayer;
            _ctx.ActualizarPlayer(exiledPlayer);
        }

        public void SetSensores(ISensors sensores)
        {
            _sensores = sensores;
        }

        public ISensors GetSensors()
        {
            return _sensores;
        }


        public void ResetearPosicionInicial(Vector3 posicionSpawn)
        {
            _lastPos = posicionSpawn;
        }
        public void ResetEstado()
        {   
            BaseSensors.agentCacheData.Clear();
            // ── Estado de acción ────────────────────────────────────────────
            _ultimaAccion   = 12; // NOOP
            _lastActionTime = 0f;

            // ── Estado de episodio ───────────────────────────────────────────
            PendingReward     = 0f;
            EpisodioTerminado = false;

            // ── Flags de control ─────────────────────────────────────────────
            _isRespawning = false;

            // ── Cancelar corrutinas pendientes si las hay ─────────────────────
            if (_respawnHandle.IsRunning)
                Timing.KillCoroutines(_respawnHandle);

            if (_initDelayHandle.IsRunning)
                Timing.KillCoroutines(_initDelayHandle);

            // ── MouseLook — no resetear los fields de reflection ─────────────
            // _fieldCurH etc. siguen siendo válidos si el GameObject no cambió
            // Se re-inicializan en _RutinaRespawn si es necesario

            Log.Debug($"[ScpAgentBot] Bot {AgentId} estado reseteado.");
        }

        public void Destruir()
        {
            // 1. Apagar los eventos para que no consuman CPU
            if (_respawnHandle.IsValid)
            {
                Timing.KillCoroutines(_respawnHandle);
            }
            Timing.KillCoroutines(_initDelayHandle);
            _LimpiarComponentesPrevios();

            _sensores = null;
            // 4. Destruir el objeto físico en Unity
            if (_botGameObject != null)
            {
                UnityEngine.Object.Destroy(_botGameObject);
            }
            Exiled.Events.Handlers.Player.RoomChanged         -= OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             -= OnHurt;
            Log.Debug($"[ScpAgentBot] Agente {AgentId} destruido y memoria liberada.");
            Exiled.Events.Handlers.Player.RoomChanged -= OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting     -= OnHurt;
        }

        // ───────────────────────────────────────────────────────────────────────
        // IAgentController — INTERFAZ PÚBLICA
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// El ControlServer deposita aquí la acción recibida de Python.
        /// </summary>
        public void ReceiveAction(AgentAction action)
        {
            if (action == null) return;
            _ultimaAccion = action.ActionId;
            _lastActionTime = Time.time;
        }

        /// <summary>
        /// Devuelve el snapshot completo de sensores para enviar a Python.
        /// </summary>
        public AgentObservation GetObservation(float delTime)
        {
                if (ExiledPlayer == null)
                {
                    Log.Warn($"[Bot {AgentId}] GetObs: ExiledPlayer es NULL");
                    return BaseSensors.obsVacia;
                }

                if (!ExiledPlayer.IsAlive)
                {
                    Log.Warn($"[Bot {AgentId}] GetObs: IsAlive=False Role={ExiledPlayer.Role.Type}");
                    return BaseSensors.obsVacia;
                }

                if (ExiledPlayer.GameObject == null)
                {
                    Log.Warn($"[Bot {AgentId}] GetObs: GameObject es NULL");
                    return BaseSensors.obsVacia;
                }

                if (ExiledPlayer.CameraTransform == null)
                {
                    Log.Warn($"[Bot {AgentId}] GetObs: CameraTransform es NULL");
                    return BaseSensors.obsVacia;
                }

                if (_sensores == null)
                {
                    Log.Warn($"[Bot {AgentId}] GetObs: _sensores es NULL");
                    return BaseSensors.obsVacia;
                }

            return _sensores.GetCurrentState(
                fixedDelta: delTime,
                accionAnterior: _ultimaAccion,
                reward:   ConsumirRecompensa(),
                done:     EpisodioTerminado,
                rol,
                0
            );
        }

        /// <summary>
        /// Extrae y resetea la recompensa acumulada desde el último tick.
        /// </summary>
        public float ConsumirRecompensa()
        {
            float r = PendingReward;
            PendingReward = 0f;
            return r;
        }

        /// <summary>
        /// Reset básico del agente (usado por IAgentController).
        /// Para respawn completo usa EjecutarRespawn().
        /// </summary>
        public void ResetAgent()
        {
            EjecutarRespawn();
        }

        // ───────────────────────────────────────────────────────────────────────
        // MOTOR FÍSICO
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Llamado por el BucleMaestroCentral en cada tick de simulación.
        /// </summary>
        public void ActualizarFisica(float deltaTime)
        {
            // Refrescar referencia si quedó stale (por ejemplo tras respawn)
            if (ExiledPlayer == null || !ExiledPlayer.IsAlive)
            {
                var fresh = Player.Get(_botGameObject);
                if (fresh != null && fresh.IsAlive)
                {
                    ExiledPlayer = fresh;
                    //AllAgents[ExiledPlayer.Id] = this;
                }
                else return;
            }

            switch (_ultimaAccion)
            {
                case 0: case 1: case 2: case 3: case 4:
                    _MoverPersonaje(_ultimaAccion, deltaTime);
                    break;
                case 5: case 6:
                    _Accion();
                    break;
                case 7:
                    _EquiparTarjeta();
                    break;
                case 8:
                    _MoverCamara(-15f);
                    break;
                case 9:
                    _MoverCamara(15f);
                    break;
                // 10, 11, 12 = NOOP
                default:
                    break;
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // RESPAWN
        // ───────────────────────────────────────────────────────────────────────
        public void EjecutarRespawn()
        {
            if (_isRespawning) return; // Evita carreras de corrutinas concurrentes
            // 1. 🔥 CORTE QUIRÚRGICO: Si había un respawn en progreso, lo matamos inmediatamente
                if (_respawnHandle.IsValid)
                {
                    Timing.KillCoroutines(_respawnHandle);
                }

                // 2. Forzamos el flag a false por si la corrutina muerta lo dejó en true
                _isRespawning = false; 

                // 3. Iniciamos la nueva rutina y guardamos su handle de seguimiento
                _respawnHandle = Timing.RunCoroutine(_RutinaRespawn());
        }

        private IEnumerator<float> _RutinaRespawn()
        {
            _isRespawning = true;
            try
            {
                if (_botGameObject == null) yield break;

                int idAntiguo = ExiledPlayer?.Id ?? -1;

                // 1. Notificar al slot que el bot ya no está listo
                AgentManager.Instance?.GetSlot(AgentId)?.Reset();

                // 2. Cambiar a Espectador momentáneamente
                var current = Player.Get(_botGameObject);
                if (current != null)
                    current.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.None);

                yield return Timing.WaitForSeconds(0.2f);

                // 3. Asignar ClassD de nuevo
                var respawning = Player.Get(_botGameObject);
                if (respawning != null)
                    respawning.Role.Set(RoleTypeId.ClassD, RoleSpawnFlags.All);

                yield return Timing.WaitForSeconds(0.1f);

                // 4. Refrescar wrapper de EXILED
                var freshPlayer = Player.Get(_botGameObject);
                if (freshPlayer == null)
                {
                    Log.Error($"[ScpAgentBot] Bot {AgentId} — no se pudo refrescar wrapper tras respawn.");
                    yield break;
                }

                // 5. Migrar cache de sala si el ID cambió
                int idNuevo = freshPlayer.Id;
                if (idAntiguo != idNuevo && idAntiguo >= 0)
                destroyBoundsCache(idAntiguo, idNuevo);

                ExiledPlayer = freshPlayer;

                // 6. Asegurar CharacterController
                if (_botGameObject.GetComponent<CharacterController>() == null)
                    _botGameObject.AddComponent<CharacterController>();

                // 7. Re-inicializar MouseLook
                _InicializarMouseLook();

                // 8. Notificar al AgentManager — reactiva el slot y vincula sensores frescos
                // Esto llama AgentSlot.OnSpawnComplete() → Bot.SetPlayer() + Sensors.VincularPlayer()
                AgentManager.Instance?.OnBotSpawnComplete(AgentId, freshPlayer);

                // 9. Resetear estado del episodio
                PendingReward      = 0f;
                EpisodioTerminado  = false;
                _ultimaAccion      = 12;

                Log.Debug($"[ScpAgentBot] Bot {AgentId} respawneado. " +
                        $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive}");
            }
            finally
            {
                _isRespawning = false;
            }
        }

        /// <summary>
/// Llamado por RoundManager tras cada Round.Restart().
/// Recrea el GameObject y lo vincula de nuevo a Mirror — 
/// la FakeConnection se reutiliza.
/// </summary>
        public void SpawnearEnNuevaRonda(RoleTypeId role = RoleTypeId.ClassD)
        {   
            int idAntiguo = ExiledPlayer?.Id ?? -1;
            
            // 1. Destruir el GameObject anterior si existe
            if (_botGameObject != null)
            {
                UnityEngine.Object.Destroy(_botGameObject);
                _botGameObject = null;
            }

            // 2. Limpiar la conexión anterior de Mirror si sigue registrada
            if (NetworkServer.connections.ContainsKey(_fakeConn.connectionId))
                NetworkServer.connections.Remove(_fakeConn.connectionId);

            // 3. Clonar el prefab de nuevo
            GameObject prefab = NetworkManager.singleton.playerPrefab;
            _botGameObject = UnityEngine.Object.Instantiate(prefab);

            // 4. Vincular con Mirror usando la misma FakeConnection
            ReferenceHub hub = _botGameObject.GetComponent<ReferenceHub>();
            NetworkServer.AddPlayerForConnection(_fakeConn, _botGameObject);
            hub.nicknameSync.Network_myNickSync = $"IA_Agent_{AgentId}";

            // 5. Obtener wrapper inicial y asignar rol
            ExiledPlayer = Player.Get(_botGameObject);
            ExiledPlayer.Role.Set(role);

            // 6. Asegurar CharacterController
            _cc = _botGameObject.GetComponent<CharacterController>();
            if (_cc == null) _cc = _botGameObject.AddComponent<CharacterController>();

            // 7. Refrescar wrapper tras Role.Set
            Timing.CallDelayed(1f, () =>
            {
                var freshPlayer = Player.Get(_botGameObject);
                if (freshPlayer != null)
                {
                    int idNuevo = freshPlayer.Id;
                    destroyBoundsCache(idAntiguo, idNuevo);
                    SetPlayer(freshPlayer);
                    addBoundsToCache(ExiledPlayer);
                    _InicializarMouseLook();
                    AgentManager.Instance?.OnBotSpawnComplete(AgentId, freshPlayer);
                    Log.Debug($"[ScpAgentBot] Bot {AgentId} respawneado en nueva ronda. " +
                            $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive}");
                }
                else
                {
                    Log.Error($"[ScpAgentBot] Bot {AgentId} — fallo al refrescar wrapper en nueva ronda.");
                }
            });
        }

        private void _LimpiarComponentesPrevios()
        {
            try
            {
                // ── FRENTE 1: DESTRUIR MONOBEHAVIOURS PERSONALIZADOS ──────────────────
                // Si tu MouseLook (o scripts de movimiento/percepción) se añade como componente
                // al GameObject, búscalo y destrúyelo explícitamente.
                if (_botGameObject != null)
                {
                    // Cambia 'MouseLook' por el nombre exacto de tu script/componente de Unity
                    /*
                    var viejoMouseLook = _botGameObject.GetComponent<MouseLook>(); 
                    if (viejoMouseLook != null)
                    {
                        UnityEngine.Object.Destroy(viejoMouseLook);
                        Log.Debug($"[ScpAgentBot] Componente MouseLook antiguo destruido para Bot {AgentId}.");
                    }
                    */
                    // Si tienes más componentes añadidos por ti (ej. Sensores, Controladores), destrúyelos aquí:
                    // var viejoSensor = _botGameObject.GetComponent<AgentSensor>();
                    // if (viejoSensor != null) UnityEngine.Object.Destroy(viejoSensor);
                }

                // ── FRENTE 2: ROMPER EVENTOS DE C# (¡EL MAYOR CAUSANTE DE LEAKS!) ──────
                // Si tu bot se suscribe a eventos de EXILED o del servidor para escuchar cuándo
                // recibe daño, mata a alguien o toca una puerta, DEBES hacer el '-=' aquí.
                if (ExiledPlayer != null)
                {   
                    // Ejemplos comunes de EXILED (Descomenta y adapta a los eventos que uses):
                    // Exiled.Events.Handlers.Player.Hurting -= OnHurting;
                    // Exiled.Events.Handlers.Player.Dying -= OnDying;
                    
                    Log.Debug($"[ScpAgentBot] Eventos de EXILED desvinculados para Bot {AgentId}.");
                }

                // ── FRENTE 3: ANULAR REFERENCIAS Y CONFIGURACIONES DE C# ──────────────
                // Si usas clases puras de C# (que no heredan de MonoBehaviour) para IA,
                // matemáticas o inputs, limpia sus estructuras internas y lánzalas al Garbage Collector.
                /*
                if (_moduloCamaraCsharp != null)
                {
                    _moduloCamaraCsharp.LimpiarCache(); // Si tiene listas o diccionarios dentro
                    _moduloCamaraCsharp = null;         // Forzar recolección de basura
                }
                */

                // ── FRENTE 4: DETENER CORRUTINAS LOCALES DEL BOT ──────────────────────
                // Si este bot inicia corrutinas individuales para enviar datos a Python por ticks,
                // asegúrate de matarlas para que no sigan ejecutándose sobre el cuerpo viejo.
                // Timing.KillCoroutines($"BucleTick_Bot_{AgentId}");

            }
            catch (Exception ex)
            {
                Log.Warn($"[ScpAgentBot] Error al limpiar componentes previos del Bot {AgentId}: {ex.Message}");
            }
        }
        // ───────────────────────────────────────────────────────────────────────
        // MÉTODOS DE MOVIMIENTO (privados)
        // ───────────────────────────────────────────────────────────────────────

        private void _MoverPersonaje(int accion, float deltaTime)
        {
            // Usar CameraTransform (no ExiledPlayer.Rotation) para que el movimiento
            // siga la dirección de la cámara, no del cuerpo
            float yawRad = ExiledPlayer.CameraTransform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 adelante = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad)).normalized;
            Vector3 derecha   = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)).normalized;

            //CharacterController cc = _botGameObject.GetComponent<CharacterController>();
            if (_cc == null) return;

            Vector3 vel = Vector3.zero;
            switch (accion)
            {
                case 0: vel = adelante * 3.9f;    break; // W
                case 1: vel = -adelante * 3.9f;   break; // S
                case 2: vel = -derecha * 3.9f;    break; // A
                case 3: vel = derecha * 3.9f;     break; // D
                case 4: vel = adelante * 5.4052f; break; // Sprint
            }

            vel.y = _cc.isGrounded ? -0.5f : -9.81f;

            _cc.Move(vel * deltaTime);

            // Sincronizar posición lógica de EXILED con la posición física de Unity
            ExiledPlayer.Position = _botGameObject.transform.position;
        }


        private void _MoverCamara(float deltaYaw)
        {
            if (_mouseLookInstance == null || _fieldCurH == null) return;

            float currentH = (float)_fieldCurH.GetValue(_mouseLookInstance);
            float currentV = (float)_fieldCurV.GetValue(_mouseLookInstance);
            float newH = currentH + deltaYaw;

            _fieldCurH.SetValue(_mouseLookInstance, currentH + deltaYaw);
            _fieldCurV.SetValue(_mouseLookInstance, currentV);
            _fieldSyncH.SetValue(_mouseLookInstance, currentH + deltaYaw);
            _fieldSyncV.SetValue(_mouseLookInstance, currentV);
            float readback = (float)_fieldCurH.GetValue(_mouseLookInstance);
        Log.Debug($"[Bot {AgentId}] Yaw: {currentH:F1} → {newH:F1} | Readback: {readback:F1}");
        }

        private void _Accion()
        {
            int layerMask = ~(1 << 13);
            if (!Physics.Raycast(
                ExiledPlayer.CameraTransform.position,
                ExiledPlayer.CameraTransform.forward,
                out RaycastHit hit, 2.4f, layerMask)) return;

            var doorVariant = hit.collider.GetComponentInParent<
                Interactables.Interobjects.DoorUtils.DoorVariant>();
            if (doorVariant != null)
            {
                var exiledDoor = Door.Get(doorVariant);
                if (exiledDoor != null)
                    exiledDoor.IsOpen = !exiledDoor.IsOpen;
                else
                    doorVariant.NetworkTargetState = !doorVariant.TargetState;
            }
        }

        private void _EquiparTarjeta()
        {
            var keycard = ExiledPlayer.Items.FirstOrDefault(i => _IsKeycard(i.Type));
            if (keycard != null) ExiledPlayer.CurrentItem = keycard;
        }

        // ───────────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────────

        private void _InicializarMouseLook()
        {
            try
            {
                var movModule = _botGameObject.GetComponentInChildren<
                    PlayerRoles.FirstPersonControl.FirstPersonMovementModule>(true);

                if (movModule == null)
                {
                    Log.Warn($"[ScpAgentBot] Bot {AgentId} — FirstPersonMovementModule no encontrado.");
                    return;
                }

                _mouseLookInstance = movModule.MouseLook;

                var type = _mouseLookInstance.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                _fieldCurH  = type.GetField("_curHorizontal",  flags);
                _fieldCurV  = type.GetField("_curVertical",    flags);
                _fieldSyncH = type.GetField("_syncHorizontal", flags);
                _fieldSyncV = type.GetField("_syncVertical",   flags);

                if (_fieldCurH == null || _fieldCurV == null)
                    Log.Warn($"[ScpAgentBot] Bot {AgentId} — campos de MouseLook no encontrados.");
                else
                    Log.Debug($"[ScpAgentBot] Bot {AgentId} — MouseLook inicializado correctamente.");
            }
            catch (Exception ex)
            {
                Log.Error($"[ScpAgentBot] Bot {AgentId} — error inicializando MouseLook: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // EVENTOS DE RECOMPENSA
        // ───────────────────────────────────────────────────────────────────────



        
        // ───────────────────────────────────────────────────────────────────────
        // HELPERS (privados)
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifica que el evento corresponda a ESTE agente concreto (no a otro jugador).
        /// </summary>
        /// 

        private bool _IsKeycard(ItemType t) =>
            t.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        private float _GetKeycardBonus(ItemType type) => type switch
        {
            ItemType.KeycardJanitor             => 5f,
            ItemType.KeycardGuard               => 25f,
            ItemType.KeycardScientist           => 35f,
            ItemType.KeycardResearchCoordinator => 20f,
            ItemType.KeycardZoneManager         => 40f,
            ItemType.KeycardChaosInsurgency     => 10f,
            ItemType.KeycardMTFPrivate          => 20f,
            ItemType.KeycardMTFOperative        => 100f,
            ItemType.KeycardMTFCaptain          => 35f,
            ItemType.KeycardO5                  => 50f,
            _ => _IsKeycard(type) ? 10f : 0f
        };

        public void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.NewRoom == null || ev.NewRoom.Type == RoomType.Unknown) return;
            
            try
            {
                addBoundsToCache(ev.Player);
                //_sensores?.MarcarRoomDescubierta(ev.NewRoom);
                _strategy?.OnRoomChanged(ev.OldRoom, ev.NewRoom);
            }
            catch (Exception ex)
            {
                Log.Error($"[ScpAgentBot] OnRoomChanged Agente {AgentId}: {ex.Message}");
            }
        }

        private void OnHurt(Exiled.Events.EventArgs.Player.HurtingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.Amount <= 0f) return;
        
            // ── Tipo de daño ──────────────────────────────────────────────────────
            string tipoDaño = "Unknown";
            if (ev.DamageHandler != null)
            {
                string handlerName = ev.DamageHandler.GetType().Name;
                
                if (handlerName.Contains("Firearm") || handlerName.Contains("Bullet"))
                    tipoDaño = "Firearm";
                else if (handlerName.Contains("Explosion") || handlerName.Contains("Grenade"))
                    tipoDaño = "Explosion";
                else if (handlerName.Contains("Scp") || handlerName.Contains("Scpitem"))
                    tipoDaño = "Scp";
                else if (handlerName.Contains("Fall"))
                    tipoDaño = "Fall";
                else if (handlerName.Contains("Bleeding") || handlerName.Contains("Poison"))
                    tipoDaño = "Status";
                else
                    tipoDaño = "Unknown";
            }
        
            // ── Dirección hacia el atacante ───────────────────────────────────────
            Vector3 dirHaciaAtacante = Vector3.zero;
            bool atacanteEnMemoria   = false;
        
            if (ev.Attacker != null && ev.Attacker != ev.Player)
            {
                Vector3 vecHaciaAtacante = (ev.Attacker.Position - ev.Player.Position);
                vecHaciaAtacante.y = 0f; // solo plano horizontal
                if (vecHaciaAtacante.sqrMagnitude > 0.001f)
                    dirHaciaAtacante = vecHaciaAtacante.normalized;
        
                // Comprobar si el atacante está en la memoria visual del sensor
                // Necesitas acceso al sensor — opciones:
                // A) Pasar la comprobación al sensor directamente
                // B) Usar AgentContext para preguntar
                atacanteEnMemoria = _sensores?.TieneEnMemoriaJugadores(ev.Attacker.Id) ?? false;
            }
        
            // ── Pasar al sensor ───────────────────────────────────────────────────
            _sensores?.RegistrarDaño(ev.Amount, tipoDaño, dirHaciaAtacante, atacanteEnMemoria);
            _strategy?.OnDamageTaken(ev.Amount, tipoDaño);
            // ── Dar recompensa negativa por daño recibido (a la estrategia) ───────
            //_ctx?.AddReward(-ev.Amount * 0.5f); // penalización proporcional al daño
        }



        public void addBoundsToCache(Player player)
        {
            Bounds b = MapUtils.ObtenerBoundsTotal(player.CurrentRoom);
            int pid = player.Id;
            
            if (!BaseSensors.agentCacheData.ContainsKey(pid))
                BaseSensors.agentCacheData[pid] = new AgentCacheData();

            BaseSensors.agentCacheData[pid].center = b.center;
            BaseSensors.agentCacheData[pid].halfX = b.size.x / 2f;
            BaseSensors.agentCacheData[pid].halfY = b.size.y / 2f;
            BaseSensors.agentCacheData[pid].halfZ = b.size.z / 2f;
            BaseSensors.agentCacheData[pid].IsDataReady   = true;   

            _sensores?.MarcarRoomDescubierta(player.CurrentRoom);        
        }

        public void destroyBoundsCache(int idAntiguo, int idNuevo)
        {
            if (idAntiguo != idNuevo && idAntiguo >= 0)
            {
                if (BaseSensors.agentCacheData.TryGetValue(idAntiguo, out var datos))
                {
                    BaseSensors.agentCacheData[idNuevo] = datos;
                    BaseSensors.agentCacheData.Remove(idAntiguo);
                    Log.Debug($"[ScpAgentBot] Cache migrada ID {idAntiguo} → {idNuevo}.");
                }
            }
        }     

        private  bool _EsEsteAgente(Player player)
        {
            return ExiledPlayer == player;
        }

    }


}