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
using ScpAgent.Managers.Data;
using ScpAgent.Network;
using ScpAgent.Network.Event;

namespace ScpAgent.Managers
{
    public class AgentManager
    {
        public static AgentManager Instance { get; private set; }
        private ScpRLPlugin _plugin;

        private AgentSlot[] _pool = null;
        private int         _numAgentes;
        private bool        _firstSpawn = true;

        private readonly object _lock = new object();

        public AgentManager(ScpRLPlugin plugin)
        {
            _plugin = plugin;
            Instance = this;
            ControlServer.AgentHandshakeReceived += OnAgentHandshakeReceived;
        }

        public void InstanciarSlot(int agentId, string rol)
        {
            if (_pool == null)
            {
                _pool = new AgentSlot[agentId + 1];
                for (int i = 0; i < _pool.Length; i++)
                {
                    _pool[i] = new AgentSlot(i);
                }
            }

            if (agentId >= _pool.Length)
            {
                int viejoTamano = _pool.Length;
                int nuevoTamano = agentId + 1;

                System.Array.Resize(ref _pool, nuevoTamano);
                
                for (int i = viejoTamano; i < nuevoTamano; i++)
                {
                    _pool[i] = new AgentSlot(i);
                }
            }

            _pool[agentId].Initialize(rol);
        }

        public void SpawnAll()
        {
            if (_firstSpawn)
            {
                Inicializar();
                _firstSpawn = false;
            }
            else
            {
                Reinicializar();
            }
        }

        public void Inicializar()
        {
            _numAgentes = _pool.Length;
            Log.Debug($"[AgentManager] Pool permanente de {_numAgentes} agentes.");
            
            for (int i = 0; i < _pool.Length; i++)
            {
                Log.Info($"[AgentManager] Iniciando conexión agente {i}");
                _pool[i].IniciarConexion();
            }
            
            for (int i = 0; i < _pool.Length; i++)
            {
                Log.Info($"[AgentManager] Spawneando agente {i}");
                _pool[i].Bot.EjecutarRespawn();
            }
        }

        public void Reinicializar()
        {
            Log.Info($"[AgentManager] Reinicializando {_numAgentes} agentes para nueva ronda...");
            for (int i = 0; i < _numAgentes; i++)
            {
                _pool[i].Bot.SpawnearEnNuevaRonda();
            }
        }

        public int GetLength()
        {
            return _pool?.Length ?? 0;
        }

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

        public AgentSlot GetSlot(int agentId)
            => _ValidarId(agentId) ? _pool[agentId] : null;

        public IAgentController GetBot(int agentId)
            => _ValidarId(agentId) ? _pool[agentId]?.Bot : null;

        public ISensors GetSensors(int agentId)
            => _ValidarId(agentId) ? _pool[agentId]?.Sensors : null;

        public bool EstaListo(int agentId)
            => _ValidarId(agentId) && _pool[agentId]?.IsReady == true;

        public void ForEachListo(Action<int, IAgentController, ISensors> action)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                var slot = _pool[i];
                if (slot != null && slot.IsReady && slot.Bot != null)
                    action(i, slot.Bot, slot.Sensors);
            }
        }

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

        public void Destruir()
        {
            ControlServer.AgentHandshakeReceived -= OnAgentHandshakeReceived;
            if (_pool == null) return;

            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i]?.Destruir();
            }

            Array.Clear(_pool, 0, _pool.Length);
            Log.Debug("[AgentManager] Pool destruido.");
        }

        private bool _ValidarId(int id)
            => _pool != null && id >= 0 && id < _pool.Length;

        public void OnAgentHandshakeReceived(object sender, AgentHandshakeEventArgs eventArgs)
        {
            lock(_lock)
            {
                InstanciarSlot(eventArgs.AgentId, eventArgs.RoleType);
            }
        }
    }
}
