using ScpAgent.Bot.Data;
using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Intefaces;
using UnityEngine;
using Exiled.API.Features.Components;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Simulation;

namespace ScpAgent.Bot.Interfaces 
{
    public interface IAgentController 
    {
        // ───────────────────────────────────────────────────────────────────────
        // 1. PROPIEDADES (Sustituyen a los antiguos Get() y Set())
        // ───────────────────────────────────────────────────────────────────────
        int _agentId { get; set; }
        Player _exiledPlayer { get; set; }
        string _nickname { get; set; }
        RoleTypeId _role { get; set; }
        Simulation.FakeConnection _fakeConn { get; set; }
        GameObject _botGameObject { get; set; }
        CharacterController _cc { get; set; }
        ISensors _sensores { get; set;}
        IAgentRoleStrategyBase _strategy { get; set; }
        float PendingReward { get; set; }
        bool EpisodioTerminado { get; set; }

        // ───────────────────────────────────────────────────────────────────────
        // 2. CICLO DE VIDA Y SPAWNER
        // ───────────────────────────────────────────────────────────────────────
        void Init(Simulation.FakeConnection fakeConn);
        void SetStrategy(IAgentRoleStrategyBase strategy);
        void SetDependencias(Simulation.FakeConnection fakeConn, GameObject botGameObject, Player player, CharacterController cc, RoleTypeId role);
        void EjecutarRespawn();
        void SpawnearEnNuevaRonda();
        void FinalizarInicio(Player freshPlayer);  
        void Destruir();

        // ───────────────────────────────────────────────────────────────────────
        // 3. LÓGICA PRINCIPAL DEL AGENTE (Cerebro y Físicas)
        // ───────────────────────────────────────────────────────────────────────
        void ReceiveAction(AgentAction action);
        void ActualizarFisica(float deltaTime);
        AgentObservation GetObservation(float deltaTime);
        
        // ───────────────────────────────────────────────────────────────────────
        // 4. UTILIDADES Y ESTADO
        // ───────────────────────────────────────────────────────────────────────
        void ResetearPosicionInicial(Vector3 posicionSpawn);
        void ResetEstado();
        float ConsumirRecompensa();
    }    
    
}