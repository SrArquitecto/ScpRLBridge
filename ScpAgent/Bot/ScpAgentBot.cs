using Exiled.API.Features;
using System;
using System.Reflection;
using UnityEngine;
using PlayerRoles;
using MEC;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Simulation;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Sensors;

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
        public int _agentId { get; set; }
        public Player _exiledPlayer { get; set; }
        public string _nickname { get; set; }
        public RoleTypeId _role { get; set; }
        public FakeConnection _fakeConn { get; set; }
        public GameObject _botGameObject { get; set; }
        public CharacterController _cc { get; set; }
        public bool _firstRespawn { get; set; } = true;


        // ── Estado de la acción ─────────────────────────────────────────────────
        private int _ultimaAccion = 12; // 12 = NOOP
        private float _lastActionTime;

        // ── Sensores ────────────────────────────────────────────────────────────
        public ISensors _sensores { get; set;}
        private AgentContext _ctx;

        // ── Recompensa y estado de episodio ─────────────────────────────────────
        public float PendingReward { get; set; } = 0f;
        public bool EpisodioTerminado { get; set; } = false;

        // ── Referencia al GameObject (no cambia aunque el wrapper Player quede stale) ──
       
        public IAgentRoleStrategyBase _strategy { get; set; }

        // ── Reflection cache para FpcMouseLook ─────────────────────────────────

        
        //private CoroutineHandle _initDelayHandle;
        //private CoroutineHandle _respawnHandle;
        private Vector3 _lastPos;


        private BotSpawner _spawner;
        private BotEvents _events;


        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public ScpAgentBot(string nickname, int id, RoleTypeId role = RoleTypeId.ClassD)
        {
            _agentId   = id;
            _nickname = nickname;
            //Strategy = strategy;
            //_fakeConn = fakeConn; // ← recibida desde AgentManager, no creada aquí
            _role = role;
            _ctx = new AgentContext(
                agentId:   _agentId,
                rol:       _role,
                getReward: () => PendingReward,
                addReward: r => PendingReward += r,
                endEpisode: () => EpisodioTerminado = true
            );
            //Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
            //Exiled.Events.Handlers.Player.Hurting             += OnHurt;
            _spawner = new BotSpawner(this);
            _events = new BotEvents(this);
            // 1. Clonar el prefab del jugador
        }

        public void Init(FakeConnection fakeConn)
        {   
            _fakeConn = fakeConn;
            _spawner.Init(fakeConn, _nickname, _role);
            _events.SuscribirEventos();
        }
        public void FinalizarInicio(Player freshPlayer)
        {
            _exiledPlayer =  freshPlayer;
            _ctx.ActualizarPlayer(freshPlayer);
            PendingReward      = 0f;
            EpisodioTerminado  = false;
            _ultimaAccion      = 12;
            
            try 
            {
                _strategy?.InicializarMovimiento(_botGameObject, _cc);
            }
            catch (Exception ex)
            {
                Log.Info($"[EXCEPTION] {ex}");
            }

        }
        public void EjecutarRespawn()
        {
            _spawner.EjecutarRespawn(_role);
        }
        public void SpawnearEnNuevaRonda()
        {
            _events.firstTime = true;
            _spawner.SpawnearEnNuevaRonda(_role);
        }
        public void SetStrategy(IAgentRoleStrategyBase strategy)
        {   
            _strategy?.OnUnbind();
            _strategy = strategy;
            _strategy.OnBind(_ctx);
            if (_strategy is IAgentRoleStrategyHuman humanStrategy)
            {
                _sensores?.VincularEstrategia(
                    tipo => humanStrategy.CalcularPrioridadItem(tipo)
                );
            }
            else
            {
                _sensores?.VincularEstrategia(
                    tipo => 0f
                    // Alternativa: Si creaste un método específico para limpiar:
                    // _sensores?.DesvincularEstrategia();
                );
                // La estrategia actual es un SCP u otro rol que no implementa la interfaz humana
                // Aquí puedes ignorar el ítem o darle prioridad 0
            }
            // Pasar solo los delegados al sensor, no la estrategia completa
            
        }
        public void SetDependencias(FakeConnection fakeConn, GameObject botGameObject, Player player, CharacterController cc, RoleTypeId role)
        {
            _fakeConn = fakeConn;
            _botGameObject = botGameObject;
            _exiledPlayer = player;
            _cc = cc;
            _role = role;
        }

        public void ResetearPosicionInicial(Vector3 posicionSpawn)
        {
            _lastPos = posicionSpawn;
        }

        public void ResetEstado()
        {
            if (_exiledPlayer != null)
                BaseSensors.agentCacheData.Remove(_exiledPlayer.Id);

            _firstRespawn = true;
            // ── Estado de acción ────────────────────────────────────────────
            _ultimaAccion   = 12; // NOOP
            _lastActionTime = 0f;

            // ── Estado de episodio ───────────────────────────────────────────
            PendingReward     = 0f;
            EpisodioTerminado = false;

            // ── Flags de control ─────────────────────────────────────────────
            _spawner._isRespawning = false;

            // ── Cancelar corrutinas pendientes si las hay ─────────────────────


            // ── MouseLook — no resetear los fields de reflection ─────────────
            // _fieldCurH etc. siguen siendo válidos si el GameObject no cambió
            // Se re-inicializan en _RutinaRespawn si es necesario

            Log.Debug($"[ScpAgentBot] Bot {_agentId} estado reseteado.");
        }

        public void Destruir()
        {
            _strategy?.OnUnbind();
            _strategy = null;

            _events?.DesuscribirEventos();
            _events = null;

            _spawner?.Destruir();
            _spawner = null;

            _fakeConn = null;
            _botGameObject = null;
            _cc = null;
            _exiledPlayer = null;
            _ctx = null;
            _sensores = null;

            Log.Debug($"[ScpAgentBot] Agente {_agentId} destruido y memoria liberada.");
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
            if (_exiledPlayer == null)
            {
                Log.Warn($"[Bot {_agentId}] GetObs: ExiledPlayer es NULL");
                return BaseSensors.obsVacia;
            }

            if (!_exiledPlayer.IsAlive)
            {
                Log.Warn($"[Bot {_agentId}] GetObs: IsAlive=False Role={_exiledPlayer.Role.Type}");
                return BaseSensors.obsVacia;
            }

            if (_exiledPlayer.GameObject == null)
            {
                Log.Warn($"[Bot {_agentId}] GetObs: GameObject es NULL");
                return BaseSensors.obsVacia;
            }

            if (_exiledPlayer.CameraTransform == null)
            {
                Log.Warn($"[Bot {_agentId}] GetObs: CameraTransform es NULL");
                return BaseSensors.obsVacia;
            }

            if (_sensores == null)
            {
                Log.Warn($"[Bot {_agentId}] GetObs: _sensores es NULL");
                return BaseSensors.obsVacia;
            }

            return _sensores.GetCurrentState(
                fixedDelta: delTime,
                accionAnterior: _ultimaAccion,
                reward:   ConsumirRecompensa(),
                done:     EpisodioTerminado,
                _role,
                ModuleUtils.GetBestKeycardTier(_exiledPlayer)
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

        // ───────────────────────────────────────────────────────────────────────
        // MOTOR FÍSICO
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Llamado por el BucleMaestroCentral en cada tick de simulación.
        /// </summary>
        public void ActualizarFisica(float deltaTime)
        {
            // Refrescar referencia si quedó stale (por ejemplo tras respawn)
            if (_exiledPlayer == null || !_exiledPlayer.IsAlive) return;
                _strategy?.ActualizarFisica(deltaTime, _exiledPlayer, _ultimaAccion, _botGameObject);
            
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

    }


}