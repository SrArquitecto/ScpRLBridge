using Exiled.API.Features;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using PlayerRoles;
using Mirror;
using MEC;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Simulation;
using ScpAgent.Components;
using Exiled.API.Features.Doors;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Enums;
using ScpAgent.Managers;


namespace ScpAgent.Bot
{
    /// <summary>
    /// Representa un agente de IA (bot ClassD) dentro del servidor SCP:SL.
    /// Encapsula el spawn, control físico, cámara, sensores y recompensas.
    /// </summary>
    public class ScpAgentBot : IAgentController
    {
        // ── Registro global de agentes activos ─────────────────────────────────
        public static readonly Dictionary<int, ScpAgentBot> AllAgents = new Dictionary<int, ScpAgentBot>();
        

        // ── Identidad ───────────────────────────────────────────────────────────
        public int AgentId { get; private set; }
        public Player ExiledPlayer { get; set; }

        // ── Estado de la acción ─────────────────────────────────────────────────
        private int _ultimaAccion = 12; // 12 = NOOP
        private float _lastActionTime;

        // ── Sensores ────────────────────────────────────────────────────────────
        private AgentSensors _sensores;

        // ── Recompensa y estado de episodio ─────────────────────────────────────
        public float PendingReward { get; set; } = 0f;
        public bool EpisodioTerminado { get; private set; } = false;

        // ── Referencia al GameObject (no cambia aunque el wrapper Player quede stale) ──
        private GameObject _botGameObject;

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

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public ScpAgentBot(string nickname, int id, RoleTypeId role = RoleTypeId.ClassD)
        {
            AgentId = id;
            int idFalso = -1000 - AgentId;

            // 1. Crear conexión virtual y clonar el prefab del jugador
            _fakeConn = new FakeConnection(idFalso);
            GameObject prefab = NetworkManager.singleton.playerPrefab;
            _botGameObject = UnityEngine.Object.Instantiate(prefab);

            // 2. Vincular con el servidor de red de Mirror
            ReferenceHub hub = _botGameObject.GetComponent<ReferenceHub>();
            NetworkServer.AddPlayerForConnection(_fakeConn, _botGameObject);
            hub.nicknameSync.Network_myNickSync = nickname;

            // 3. Obtener wrapper inicial de EXILED y asignar rol
            ExiledPlayer = Player.Get(_botGameObject);
            ExiledPlayer.Role.Set(role);

            // 4. CRÍTICO: refrescar wrapper tras Role.Set (el wrapper puede quedar stale)
            // Role.Set es asíncrono internamente — esperamos 0.1s antes de releer
            _initDelayHandle = Timing.CallDelayed(1f, () =>
            {
                var freshPlayer = Player.Get(_botGameObject);
                if (freshPlayer != null)
                {
                    ExiledPlayer = freshPlayer;
                    Log.Debug($"[ScpAgentBot] Bot {AgentId} ({nickname}) — wrapper refrescado. " +
                             $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive}");
                }
                else
                {
                    Log.Error($"[ScpAgentBot] Bot {AgentId} — no se pudo refrescar el wrapper de EXILED.");
                }

                // 5. Añadir CharacterController si falta (necesario para cc.Move)
                if (_botGameObject.GetComponent<CharacterController>() == null)
                    _botGameObject.AddComponent<CharacterController>();

                // 6. Inicializar reflection de FpcMouseLook (solo una vez)
                _InicializarMouseLook();

                // 7. Inicializar sensores con la referencia ya válida
                //_sensores.ResetSensor();
                _sensores = new AgentSensors(this, ExiledPlayer);
                _sensores.ActualizarJugador(ExiledPlayer);
                // 8. Registrar en el directorio global
                //AllAgents[ExiledPlayer.Id] = this;

                // 9. Suscribir eventos de recompensa
                SuscribirEventos();
            });
            _cc = _botGameObject.GetComponent<CharacterController>();
            if (_cc == null) _cc = _botGameObject.AddComponent<CharacterController>();
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
            DesuscribirEventos();
            _fakeConn.Disconnect();
            // 2. Borrar del registro estático global
            if (ExiledPlayer != null && AllAgents.ContainsKey(ExiledPlayer.Id))
            {
                //AllAgents.Remove(ExiledPlayer.Id);
            }

            // 3. Desconectar de Mirror (tu FakeConnection tiene la lógica perfecta para esto)
            _fakeConn?.Disconnect();

            // 4. Destruir el objeto físico en Unity
            if (_botGameObject != null)
            {
                UnityEngine.Object.Destroy(_botGameObject);
            }

            Log.Debug($"[ScpAgentBot] Agente {AgentId} destruido y memoria liberada.");
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
            if (ExiledPlayer == null || _sensores == null)
                return new AgentObservation { Done = true };

            return _sensores.GetCurrentState(
                velLin:   0f,
                velLat:   0f,
                velVer:   0f,
                fixedDelta: delTime,
                accionAnterior: _ultimaAccion,
                reward:   ConsumirRecompensa(),
                done:     EpisodioTerminado
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
// 1. Declarar este flag a nivel de clase en ScpAgentBot.cs
   

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
            _respawnHandle = Timing.RunCoroutine(_RutinaRespawn2());
    }

    private IEnumerator<float> _RutinaRespawn()
    {
        _isRespawning = true;
        try
        {
            if (_botGameObject == null) yield break;

            int idAntiguo = ExiledPlayer.Id;
            // 1. Cambiar a Espectador momentáneamente para limpiar el cuerpo anterior
            var current = Player.Get(_botGameObject);
            if (current != null)
                current.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.None);

            yield return Timing.WaitForSeconds(0.2f);

            // 2. Asignar ClassD de nuevo con flags de spawn completo
            var respawning = Player.Get(_botGameObject);
            if (respawning != null)
                respawning.Role.Set(RoleTypeId.ClassD, RoleSpawnFlags.All);

            yield return Timing.WaitForSeconds(0.1f);

            // 3. Refrescar el wrapper de EXILED
            var freshPlayer = Player.Get(_botGameObject);
            if (freshPlayer != null)
            {
                int idNuevo = freshPlayer.Id;
                if (idAntiguo != idNuevo)
                {
                    if (ScpAgent.Components.AgentSensors.agentCacheData.TryGetValue(idAntiguo, out var datosExistentes))
                    {
                        ScpAgent.Components.AgentSensors.agentCacheData[idNuevo] = datosExistentes;
                        ScpAgent.Components.AgentSensors.agentCacheData.Remove(idAntiguo);
                        Log.Debug($"[ScpAgentBot] Datos de sala migrados del ID viejo {idAntiguo} al nuevo {idNuevo}.");
                    }
                    //AgentManager.ActualizarRegistroId(idAntiguo, idNuevo, this);
                }
                ExiledPlayer = freshPlayer;
                
            }

            // 4. Asegurar CharacterController único
            if (_botGameObject.GetComponent<CharacterController>() == null)
                _botGameObject.AddComponent<CharacterController>();

            // 5. Re-inicializar MouseLook con el módulo del nuevo cuerpo
            _InicializarMouseLook();

            // 6. 🌟 CRÍTICO: Limpiar y recrear los sensores con la referencia fresca del jugador
            _sensores.ActualizarJugador(ExiledPlayer);

            // 7. Resetear estado del episodio
            PendingReward = 0f;
            EpisodioTerminado = false;
            _ultimaAccion = 12; // NOOP

            Log.Debug($"[ScpAgentBot] Bot {AgentId} respawneado con éxito. Sensores re-enlazados. " +
                    $"Role={ExiledPlayer?.Role.Type} IsAlive={ExiledPlayer?.IsAlive}");
        }
        finally
        {
            _isRespawning = false; // Garantiza que el flag se resetee pase lo que pase
        }
    }

        private IEnumerator<float> _RutinaRespawn2()
        {
            _isRespawning = true;

            // 1. El bloque try-finally GLOBAL sí permite usar 'yield' dentro de su cuerpo.
            try
            {
                if (_botGameObject == null) yield break;

                int idAntiguo = -1;

                // ── FASE 1: Limpieza e inicio de transición (Sin yield interno) ──
                try
                {
                    idAntiguo = (ExiledPlayer != null) ? ExiledPlayer.Id : -1;
                    PendingReward = 0f;
                    EpisodioTerminado = false;
                    _ultimaAccion = 12;

                    var current = Player.Get(_botGameObject);
                    if (current != null)
                    {
                        current.ClearInventory();
                        current.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.None);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ResetAgent - Fase 1] Error: {ex.Message}");
                }

                // El yield está FUERA de los bloques 'catch', pero DENTRO del 'finally' general. ¡Compila perfectamente!
                yield return Timing.WaitForSeconds(0.15f);

                // ── FASE 2: Respawn físico a ClassD (Sin yield interno) ──
                try
                {
                    var respawning = Player.Get(_botGameObject);
                    if (respawning != null)
                    {
                        respawning.Role.Set(RoleTypeId.ClassD, RoleSpawnFlags.All);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ResetAgent - Fase 2] Error: {ex.Message}");
                }

                yield return Timing.WaitForSeconds(0.1f);

                // ── FASE 3: Sincronización de Red, Sensores y Físicas (Sin yield interno) ──
                try
                {
                    var freshPlayer = Player.Get(_botGameObject);
                    if (freshPlayer != null)
                    {
                        int idNuevo = freshPlayer.Id;
                        
                        if (idAntiguo != -1 && idAntiguo != idNuevo)
                        {
                            if (ScpAgent.Components.AgentSensors.agentCacheData.TryGetValue(idAntiguo, out var datosExistentes))
                            {
                                ScpAgent.Components.AgentSensors.agentCacheData[idNuevo] = datosExistentes;
                                ScpAgent.Components.AgentSensors.agentCacheData.Remove(idAntiguo);
                            }

                            AgentManager.ActualizarRegistroId(idAntiguo, idNuevo, this);

                            if (AllAgents.ContainsKey(idAntiguo)) AllAgents.Remove(idAntiguo);
                            AllAgents[idNuevo] = this;

                            Log.Debug($"[ResetAgent] ID mutó de {idAntiguo} a {idNuevo}. Registros actualizados.");
                        }
                        else if (!AllAgents.ContainsKey(idNuevo))
                        {
                            AllAgents[idNuevo] = this;
                        }

                        ExiledPlayer = freshPlayer;
                    }

                    _cc = _botGameObject.GetComponent<CharacterController>();
                    if (_cc == null) _cc = _botGameObject.AddComponent<CharacterController>();
                    
                    _cc.enabled = false; 
                    _cc.enabled = true;

                    _InicializarMouseLook();
                    //_sensores.ResetSensor();
                    _sensores?.ActualizarJugador(ExiledPlayer);
                }
                catch (Exception ex)
                {
                    Log.Error($"[ResetAgent - Fase 3] Error: {ex.Message}");
                }

                Log.Debug($"[ResetAgent] Bot {AgentId} reiniciado con éxito.");
            }
            finally
            {
                // 2. Este bloque se ejecuta SIEMPRE: ya sea porque la corrutina terminó de forma natural
                // o porque fue interrumpida abruptamente por Timing.KillCoroutines().
                _isRespawning = false; 
            }
        }

    public void Respawn()
    {
            
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
                DesuscribirEventos();
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

        public void SuscribirEventos()
        {
            Exiled.Events.Handlers.Player.Escaping            += OnEscaping;
            Exiled.Events.Handlers.Player.Dying               += OnDying;
            Exiled.Events.Handlers.Player.PickingUpItem       += OnPickup;
            Exiled.Events.Handlers.Player.InteractingDoor     += OnInteractDoor;
            Exiled.Events.Handlers.Player.InteractingElevator += OnInteractElevator;
            Exiled.Events.Handlers.Player.InteractingLocker   += OnInteractingLocker;
            Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
        }

        public void DesuscribirEventos()
        {
            Exiled.Events.Handlers.Player.Escaping            -= OnEscaping;
            Exiled.Events.Handlers.Player.Dying               -= OnDying;
            Exiled.Events.Handlers.Player.PickingUpItem       -= OnPickup;
            Exiled.Events.Handlers.Player.InteractingDoor     -= OnInteractDoor;
            Exiled.Events.Handlers.Player.InteractingElevator -= OnInteractElevator;
            Exiled.Events.Handlers.Player.InteractingLocker   -= OnInteractingLocker;
            Exiled.Events.Handlers.Player.RoomChanged         -= OnRoomChanged;
        }

        private void OnEscaping(EscapingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            PendingReward += 200f;
            EpisodioTerminado = true;
            Log.Debug($"[ScpAgentBot] Agente {AgentId} escapó. +200 — episodio terminado.");
        }

        private void OnDying(DyingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            PendingReward -= 100f;
            EpisodioTerminado = true;
            Log.Debug($"[ScpAgentBot] Agente {AgentId} murió. -100 — episodio terminado.");
        }

        private void OnPickup(PickingUpItemEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float bonus = _GetKeycardBonus(ev.Pickup.Type);
            if (bonus > 0)
            {
                PendingReward += bonus;
                Log.Debug($"[ScpAgentBot] Agente {AgentId} recogió {ev.Pickup.Type}. +{bonus}");
            }
        }

        private void OnInteractDoor(InteractingDoorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float r = ev.IsAllowed ? 3f : -4f;
            PendingReward += r;
        }

        private void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            PendingReward += 8f;
            Log.Debug($"[ScpAgentBot] Agente {AgentId} abrió locker. +8");
        }

        private void OnInteractElevator(InteractingElevatorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            PendingReward += 15f;
            Log.Debug($"[ScpAgentBot] Agente {AgentId} usó ascensor. +15");
        }

        public void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.NewRoom == null || ev.NewRoom.Type == RoomType.Unknown) return;

            try
            {
                Bounds b = MapUtils.ObtenerBoundsTotal(ev.Player.CurrentRoom);
                int pid = ev.Player.Id;

                if (!AgentSensors.agentCacheData.ContainsKey(pid))
                    AgentSensors.agentCacheData[pid] = new AgentCacheData();

                AgentSensors.agentCacheData[pid].CurrentBounds = b;
                AgentSensors.agentCacheData[pid].IsDataReady   = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[ScpAgentBot] OnRoomChanged Agente {AgentId}: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // HELPERS (privados)
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifica que el evento corresponda a ESTE agente concreto (no a otro jugador).
        /// </summary>
        private bool _EsEsteAgente(Player p)
        {
            if (p == null || ExiledPlayer == null) return false;
            return p.Id == ExiledPlayer.Id;
        }

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

        // ───────────────────────────────────────────────────────────────────────
        // LOOKUP ESTÁTICO
        // ───────────────────────────────────────────────────────────────────────

        public static bool TryGetAgent(int playerId, out ScpAgentBot agent)
            => AllAgents.TryGetValue(playerId, out agent);

        public AgentSensors GetSensores()
        {
            return _sensores;
        }
    }


}