using System;
using System.Collections.Generic;
using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot;
using MEC;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Components;

namespace ScpAgent.Managers
{
    public class AgentSlot {
    public IAgentController Bot;
    public AgentSensors Sensor;
    public bool IsActive;

    public AgentSlot(IAgentController bot, AgentSensors sensor) {
        Bot = bot;
        Sensor = sensor;
        IsActive = false;
    }
}
    public class AgentManager
    {
        private readonly object _lock = new();

        private AgentSlot[] _botPool;
  
        private int _numAgentes = 0;

        public static AgentManager Instance { get; private set; }

        public AgentManager()
        {
            Instance = this;
        }

        public void Start(int numAgentes)
        {
            _numAgentes = numAgentes;
            _botPool = new AgentSlot[_numAgentes];
        }

        public bool Exists(int agentId)
        {
            // 1. Validamos que el ID esté dentro del rango del array
            if (agentId < 0 || agentId >= _botPool.Length)
                return false;

            // 2. Retornamos si el bot está activo (ya no necesitas lock, 
            // pues los arrays son estructuras de acceso directo seguras 
            // en este contexto)
            return _botPool[agentId].IsActive;
        }


        ///////////////////////////////////

        public IAgentController GetNextAvailableBot() 
        {
            for(int i = 0; i < _botPool.Length; i++) {
                if(!_botPool[i].IsActive) {
                    _botPool[i].IsActive = true;
                    return _botPool[i].Bot;
                }
            }
            return null; // Pool lleno
        }
        ///////////////////////////////////
        /// 
        public IAgentController Get(int agentId)
        {
            if (_botPool[agentId].Bot != null)
                return _botPool[agentId].Bot;
            else 
                return null;
        }
        public Dictionary<int, IAgentController> GetAllSnapshot()
        {
            lock (_lock)
            {
                return new Dictionary<int, IAgentController>(_agents);
            }
        }
        public Dictionary<int, IAgentController> GetAll()
        {
            return _agents;
        }
        public void CreateAll(IEnumerable<int> agentIds)
        {
            foreach (var id in agentIds)
            {
                Create(id);
            }
        }
        public void RecreateAll(IEnumerable<int> agentIds)
        {
            foreach (var id in agentIds)
            {
                Recreate(id);
            }
        }

        public IAgentController Create(int agentId)
        {
            lock (_lock)
            {
                if (_agents.ContainsKey(agentId))
                {
                    Log.Warn($"[AgentManager] El agente {agentId} ya existe.");
                    return _agents[agentId];
                }

                string nickname = $"IA_Agent_{agentId}";
                IAgentController bot = new ScpAgentBot(nickname, agentId, RoleTypeId.ClassD);
                _agents[agentId] = bot;

                Log.Debug($"[AgentManager] Agente {agentId} creado.");
                return bot;
            }
        }
        public void Recreate(int agentId) // Llámalo en el respawn
        {
            lock (_lock)
            {
                if (_agents.TryGetValue(agentId, out var botViejo))
                {
                    botViejo.Destruir(); // Implementa este método para limpiar todo
                    _agents.Remove(agentId);
                }
                Create(agentId); // Ahora sí, crea uno nuevo en un hueco limpio
            }
        }

        private IEnumerator<float> EnsureUnityComponents(IAgentController bot)
        {
            yield return Timing.WaitForOneFrame;

            var go = bot.ExiledPlayer.GameObject;

            if (!go.TryGetComponent<CharacterController>(out _))
            {
                go.AddComponent<CharacterController>();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Log.Debug($"[AgentManager] Iniciando purga de {_agents.Count} agentes...");

                foreach (var kvp in _agents)
                {
                    try
                    {
                        if (kvp.Value is ScpAgentBot bot)
                        {
                            bot.Destruir();
                        }
                    }
                    catch (Exception ex)
                    {
                        // 🌟 Si un bot falla, se reporta, pero NO detiene la limpieza de los demás bots
                        Log.Error($"[AgentManager] Error crítico al destruir el agente {kvp.Key} durante el reinicio: {ex.Message}");
                    }
                }

                // Aseguramos el vaciado del diccionario local pase lo que pase
                _agents.Clear();

                // 🌟 RED DE SEGURIDAD ABSOLUTA: Vaciamos el registro estático global de ScpAgentBot
                // Esto destruye cualquier referencia huérfana que el método Destruir() haya omitido por error de ID.
                try
                {
                    ScpAgentBot.AllAgents.Clear();
                    Log.Debug("[AgentManager] Registro estático global ScpAgentBot purgado con éxito.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[AgentManager] No se pudo limpiar ScpAgentBot.AllAgents: {ex.Message}");
                }
            }
        }

        public static void EjecutarRespawn(IAgentController bot, int agentId)
        {
            // 1. Buscamos el bot real en nuestro registro (asumiendo que ScpAgentBot implementa IAgentController)
            if (bot is ScpAgentBot botReal)
            {
                // 2. Le ordenamos al bot que inicie su proceso interno
                botReal.EjecutarRespawn();
            }
        }

        // El servidor de red o el bot llamarán a esto para actualizar el diccionario de la red cuando el ID cambie
        public static void ActualizarRegistroId(int idAntiguo, int idNuevo, IAgentController bot)
        {
            if (idAntiguo != idNuevo)
            {
                _agents.Remove(idAntiguo);
            }
            _agents[idNuevo] = bot;
        }
    
    }
}