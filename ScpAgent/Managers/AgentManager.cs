using System;
using System.Collections.Generic;
using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using MEC;
using ScpAgent.Bot;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Components;
using ScpAgent.Bot.Simulation;

namespace ScpAgent.Managers
{
    // ───────────────────────────────────────────────────────────────────────────
    // SLOT — contenedor permanente de un agente + sus sensores
    // Se crea UNA SOLA VEZ y se reutiliza entre rondas
    // ───────────────────────────────────────────────────────────────────────────
    public class AgentSlot
    {
        public readonly int              AgentId;
        public readonly IAgentController Bot;
        public readonly AgentSensors     Sensors;
        public readonly FakeConnection   FakeConnection;

        // true cuando ExiledPlayer es válido y el bot puede recibir acciones
        public bool IsReady { get; private set; }

        public AgentSlot(int agentId, IAgentController bot, AgentSensors sensors, FakeConnection fakeConn)
        {
            AgentId        = agentId;
            Bot            = bot;
            Sensors        = sensors;
            FakeConnection = fakeConn;
            IsReady        = false;
        }

        /// <summary>
        /// Llamado cuando el bot ha completado el spawn y ExiledPlayer es válido.
        /// Vincula los sensores al nuevo Player wrapper y marca el slot como listo.
        /// </summary>
        public void OnSpawnComplete(Player exiledPlayer)
        {
            Bot.SetPlayer(exiledPlayer);
            Bot.ResetearPosicionInicial(exiledPlayer.Position);
            Sensors.VincularPlayer(exiledPlayer);
            (Bot as ScpAgentBot)?.SetSensores(Sensors);
            IsReady = true;
        }

        /// <summary>
        /// Reset entre rondas — limpia el estado pero NO destruye nada.
        /// </summary>
        public void Reset()
        {
            IsReady = false;
            Bot.ResetEstado();
            Sensors.ResetEstado();
        }
    }

    // ───────────────────────────────────────────────────────────────────────────
    // AGENTMANAGER
    // Pool permanente: los slots se crean una vez al inicio y se reutilizan siempre
    // ───────────────────────────────────────────────────────────────────────────
    public class AgentManager
    {
        public static AgentManager Instance { get; private set; }
        private ScpRLPlugin _plugin;

        private AgentSlot[] _pool;
        private int         _numAgentes;

        private readonly object _lock = new object();

        public AgentManager(ScpRLPlugin plugin)
        {
            _plugin = plugin;
            Instance = this;
        }

        // ───────────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN (una sola vez en OnEnabled)
        // ───────────────────────────────────────────────────────────────────────

        public void Inicializar(int numAgentes)
        {
            _numAgentes = numAgentes;
            _pool       = new AgentSlot[_numAgentes];

            for (int i = 0; i < _numAgentes; i++)
                _pool[i] = _CrearSlot(i);

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

        public AgentSensors      GetSensors(int agentId)
            => _ValidarId(agentId) ? _pool[agentId]?.Sensors : null;

        public bool              EstaListo(int agentId)
            => _ValidarId(agentId) && _pool[agentId]?.IsReady == true;

        /// <summary>
        /// Itera sobre slots listos SIN crear colecciones nuevas.
        /// Usar en el BucleMaestro en vez de GetBotsListos().
        /// </summary>
        public void ForEachListo(Action<int, IAgentController, AgentSensors> action)
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

        private AgentSlot _CrearSlot(int agentId)
        {
            lock (_lock)
            {
                try
                {
                    int    idFalso  = -1000 - agentId;
                    string nickname = $"IA_Agent_{agentId}";

                    var fakeConn = new FakeConnection(idFalso);

                    // Bot sin ExiledPlayer válido todavía —
                    // OnBotSpawnComplete() lo vinculará cuando Role.Set complete
                    var bot = new ScpAgentBot(nickname, agentId, fakeConn, RoleTypeId.ClassD);

                    // Sensores vacíos — VincularPlayer() los activará
                    var sensors = new AgentSensors();

                    return new AgentSlot(agentId, bot, sensors, fakeConn);
                }
                catch (Exception ex)
                {
                    Log.Error($"[AgentManager] Error creando slot {agentId}: {ex.Message}");
                    return null;
                }
            }
        }

        private bool _ValidarId(int id)
            => _pool != null && id >= 0 && id < _pool.Length;
    }
}