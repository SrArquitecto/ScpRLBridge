using System;
using System.Collections.Generic;
using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using MEC;
using ScpAgent.Bot;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Sensors;
using ScpAgent.Bot.Simulation;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Strategies.Human;
using ScpAgent.Managers.Data;
using ScpAgent.Network;
using ScpAgent.Network.Event;

namespace ScpAgent.Managers
{
    // ───────────────────────────────────────────────────────────────────────────
    // SLOT — contenedor permanente de un agente + sus sensores
    // Se crea UNA SOLA VEZ y se reutiliza entre rondas
    // ───────────────────────────────────────────────────────────────────────────

    // ───────────────────────────────────────────────────────────────────────────
    // AGENTMANAGER
    // Pool permanente: los slots se crean una vez al inicio y se reutilizan siempre
    // ───────────────────────────────────────────────────────────────────────────
    public class AgentManager
    {
        public static AgentManager Instance { get; private set; }
        private ScpRLPlugin _plugin;

        private AgentSlot[] _pool = null;
        private int         _numAgentes;

        private readonly object _lock = new object();

        public AgentManager(ScpRLPlugin plugin)
        {
            _plugin = plugin;
            Instance = this;
            ControlServer.AgentHandshakeReceived += OnAgentHandshakeReceived;
        }

        // ───────────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN (una sola vez en OnEnabled)
        // ───────────────────────────────────────────────────────────────────────
        public void InstaciarSlot(int agentId, string rol)
        {   
            // 1. Si el pool es completamente nulo, lo inicializamos con el tamaño mínimo necesario
            if (_pool == null)
            {
                _pool = new AgentSlot[agentId + 1];
                for (int i = 0; i < _pool.Length; i++)
                {
                    _pool[i] = new AgentSlot(i); // Inicializamos el bot base sin rol aún
                }
            }

            // 2. Si el agente que llega supera el tamaño actual del array, lo expandimos
            if (agentId >= _pool.Length)
            {
                int viejoTamano = _pool.Length;
                int nuevoTamano = agentId + 1;

                // Agrandamos el array de forma segura
                System.Array.Resize(ref _pool, nuevoTamano);
                
                // 🚨 CRÍTICO: Inicializamos TODOS los nuevos huecos creados para evitar NullReference futuros
                for (int i = viejoTamano; i < nuevoTamano; i++)
                {
                    _pool[i] = new AgentSlot(i);
                }
            }

            // 3. Ahora que estamos 100% seguros de que el slot existe y no es nulo, le metemos el rol
            _pool[agentId].Instanciar(agentId, rol);
        }
        public void Inicializar()
        {
            //_numAgentes = numAgentes;
            //_pool       = new AgentSlot[_numAgentes];
            for (int i = 0; i < _pool.Length; i++) 
            {
                Log.Info($"INICIANDO AGENTE {i}");
                _IniciarSlot(i);
            }
            Log.Debug($"[AgentManager] Pool permanente de {_numAgentes} agentes creado.");
        }

        public void Spawnear()
        {
            for (int i = 0; i < _numAgentes; i++)
            {
                _pool[i].Bot.EjecutarRespawn();
            }
        }
        
        public void Reinicializar()
        {
            for (int i = 0; i < _numAgentes; i++)
            {
                _pool[i].Bot.SpawnearEnNuevaRonda();
            }
        }
        
        public int GetLength()
        {
            return _pool.Length;
        }
        // ───────────────────────────────────────────────────────────────────────
        // ENTRE RONDAS — resetear, no recrear
        // ───────────────────────────────────────────────────────────────────────

        public void ResetearTodos()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] != null)
                    _pool[i].Reset();
            }
            Log.Debug("[AgentManager] Todos los slots reseteados para nueva ronda.");
        }

        public void OnBotSpawnComplete(int agentId, Player exiledPlayer)
        {
            if (!_ValidarId(agentId)) return;

            if (exiledPlayer == null || !exiledPlayer.IsAlive)
            {
                Log.Warn($"[AgentManager] Bot {agentId} — OnSpawnComplete con player no vivo. Reintentando...");
                Timing.CallDelayed(0.5f, () => OnBotSpawnComplete(agentId, Player.Get(exiledPlayer?.GameObject)));
                return;
            }
            
            _pool[agentId].OnSpawnComplete(exiledPlayer);
            
            Log.Debug($"[AgentManager] Agente {agentId} ({exiledPlayer.Nickname}) listo. " +
                     $"({NumListos}/{_numAgentes})");
        }



        // ───────────────────────────────────────────────────────────────────────
        // CONSULTAS
        // ───────────────────────────────────────────────────────────────────────

        public AgentSlot         GetSlot(int agentId)
            => _ValidarId(agentId) ? _pool[agentId] : null;

        public IAgentController  GetBot(int agentId)
            => _ValidarId(agentId) ? _pool[agentId]?.Bot : null;

        public ISensors      GetSensors(int agentId)
            => _ValidarId(agentId) ? _pool[agentId]?.Sensors : null;

        public bool              EstaListo(int agentId)
            => _ValidarId(agentId) && _pool[agentId]?.IsReady == true;

        /// <summary>
        /// Itera sobre slots listos SIN crear colecciones nuevas.
        /// Usar en el BucleMaestro en vez de GetBotsListos().
        /// </summary>
        public void ForEachListo(Action<int, IAgentController, ISensors> action)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                var slot = _pool[i];
                if (slot != null && slot.IsReady && slot.Bot != null)
                    action(i, slot.Bot, slot.Sensors);
            }
        }

        /// <summary>
        /// Diccionario de bots listos.
        /// NO llamar cada frame — solo cuando cambia el conjunto de agentes activos.
        /// </summary>
        public Dictionary<int, IAgentController> GetBotsListos()
        {
            var result = new Dictionary<int, IAgentController>(_numAgentes);
            for (int i = 0; i < _pool.Length; i++)
            {
                var slot = _pool[i];
                if (slot != null && slot.IsReady && slot.Bot != null)
                    result[i] = slot.Bot;
            }
            return result;
        }

        public int NumAgentes => _numAgentes;
        public int NumListos
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pool.Length; i++)
                    if (_pool[i]?.IsReady == true) n++;
                return n;
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // APAGADO (solo en Plugin.OnDisabled)
        // ───────────────────────────────────────────────────────────────────────

        public void Destruir()
        {   
            ControlServer.AgentHandshakeReceived -= OnAgentHandshakeReceived;
            if (_pool == null) return;

            for (int i = 0; i < _pool.Length; i++)
            {
                try   { 
                    _pool[i]?.Bot?.Destruir(); 
                    _pool[i]?.Sensors.Destruir();
                    _pool[i]?.FakeConnection.Disconnect();
                }
                catch (Exception ex)
                { Log.Error($"[AgentManager] Error destruyendo agente {i}: {ex.Message}"); }
            }

            Array.Clear(_pool, 0, _pool.Length);
            Log.Debug("[AgentManager] Pool destruido.");
        }

        // ───────────────────────────────────────────────────────────────────────
        // CREACIÓN INTERNA (privada, solo en Inicializar)
        // ───────────────────────────────────────────────────────────────────────
        private IAgentRoleStrategyBase SetStrategy(RoleTypeId rol)
        {
            IAgentRoleStrategyBase strategy;
            if (rol == RoleTypeId.ClassD || rol == RoleTypeId.Scientist)
            {
                
                strategy = new SurvivorStrategy(rol);
            }
            else if (rol == RoleTypeId.FacilityGuard || rol == RoleTypeId.NtfCaptain || rol == RoleTypeId.NtfPrivate ||
            rol == RoleTypeId.NtfSergeant ||rol == RoleTypeId.NtfSpecialist || rol == RoleTypeId.ChaosMarauder || 
            rol == RoleTypeId.ChaosConscript || rol == RoleTypeId.ChaosRepressor || rol == RoleTypeId.ChaosRifleman)
            {
                
                strategy = new CombatStrategy(rol);
            }
            else 
                strategy = new SurvivorStrategy(rol);

            return strategy;
        }
        private void _IniciarSlot(int agentId)
        {
            lock (_lock)
            {
                try
                {
                    int idFalso  = -1000 - agentId;
                    _pool[agentId].FakeConnection = new FakeConnection(idFalso);
                    // Bot sin ExiledPlayer válido todavía —
                    // OnBotSpawnComplete() lo vinculará cuando Role.Set complete
                    _pool[agentId].Bot.Init(_pool[agentId].FakeConnection);
                    _pool[agentId].Sensors.Init();

                }
                catch (Exception ex)
                {
                    Log.Error($"[AgentManager] Error creando slot {agentId}: {ex.Message}");
                }
            }
        }

        private bool _ValidarId(int id)
            => _pool != null && id >= 0 && id < _pool.Length;


        public void OnAgentHandshakeReceived(object sender, AgentHandshakeEventArgs eventArgs)
        {
            lock(_lock)
            {
                InstaciarSlot(eventArgs.AgentId, eventArgs.RoleType);
            }
        }

    }

}