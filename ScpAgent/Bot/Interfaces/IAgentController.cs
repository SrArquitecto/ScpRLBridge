using ScpAgent.Bot.Data;
using Exiled.API.Features;
using ScpAgent.Components;

namespace ScpAgent.Bot.Interfaces 
{
    public interface IAgentController 
    {
        int AgentId { get; }

        bool _isRespawning { get; }
        Player ExiledPlayer { get; }
        
        // Métodos clave que invoca el ControlServer
        void ReceiveAction(AgentAction action);
        void ActualizarFisica(float deltaTime);
        void EjecutarRespawn();
        AgentObservation GetObservation(float deltaTime);
        
        AgentSensors GetSensores();
        void Destruir();
    }
}