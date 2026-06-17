using System;
using System.Collections.Generic;
using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot;
using MEC;
using ScpAgent.Bot.Interfaces;

namespace ScpAgent.Managers
{
    public class AgentManager
    {
        private readonly Dictionary<int, IAgentController> _agents2 = new();
        private readonly object _lock = new();

        public static Dictionary<int, IAgentController> _agents { get;  set; } = new Dictionary<int, IAgentController>();

        public Dictionary<int, IAgentController> Agents2 { get; private set; } = new Dictionary<int, IAgentController>();

        public int numAgentes = 0;

        public static AgentManager Instance { get; private set; }

        public AgentManager()
        {
            Instance = this;
        }

        public bool Exists(int agentId)
        {
            lock (_lock)
                return _agents.ContainsKey(agentId);
        }

        public IAgentController Get(int agentId)
        {
            lock (_lock)
                return _agents.TryGetValue(agentId, out var agent) ? agent : null;
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