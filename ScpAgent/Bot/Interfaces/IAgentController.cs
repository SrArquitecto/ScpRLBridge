using ScpAgent.Bot.Data;
using Exiled.API.Features;
using ScpAgent.Components;
using PlayerRoles;

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
        public void SpawnearEnNuevaRonda(RoleTypeId role = RoleTypeId.ClassD);     
        void SetPlayer(Player exiledPlayer);
        void SetSensores(AgentSensors sensores);
        void ResetEstado();
        AgentObservation GetObservation(float deltaTime);
        
        //AgentSensors GetSensores();
        void Destruir();
    }
}