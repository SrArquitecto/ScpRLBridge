using System;
using ScpAgent.Bot.Data;
using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Intefaces;
using UnityEngine.PlayerLoop;
using Exiled.API.Features.Components;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Interfaces 
{
    public interface IAgentController 
    {
        int AgentId { get; }
        public float PendingReward { get; set; }
        bool _isRespawning { get; }
        Player ExiledPlayer { get; }
        int contadorSuscripciones { get; set;}
        bool EpisodioTerminado { get; set; }
        void Init(ScpAgent.Bot.Simulation.FakeConnection fakeConnection);

        // Métodos clave que invoca el ControlServer
        void ReceiveAction(AgentAction action);
        void ActualizarFisica(float deltaTime);
        void EjecutarRespawn();
        public void SpawnearEnNuevaRonda(RoleTypeId role = RoleTypeId.ClassD);     
        void SetPlayer(Player exiledPlayer);
        void SetSensores(ISensors sensores);
        void SetStrategy(IAgentRoleStrategyBase strategy);
        ISensors GetSensors();
        void ResetearPosicionInicial(UnityEngine.Vector3 posicionspawn);
        void ResetEstado();
        AgentObservation GetObservation(float deltaTime);
        void Destruir();
        void addBoundsToCache(Player player);
        void destroyBoundsCache(int idAntiguo, int idNuevo);
    }    
    
}