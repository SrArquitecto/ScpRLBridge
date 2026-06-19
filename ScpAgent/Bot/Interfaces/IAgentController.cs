using ScpAgent.Bot.Data;
using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Intefaces;

namespace ScpAgent.Bot.Interfaces 
{
    public interface IAgentController 
    {
        int AgentId { get; }

        bool _isRespawning { get; }
        Player ExiledPlayer { get; }
        int contadorSuscripciones { get; }
        
        // Métodos clave que invoca el ControlServer
        void ReceiveAction(AgentAction action);
        void ActualizarFisica(float deltaTime);
        void EjecutarRespawn();
        public void SpawnearEnNuevaRonda(RoleTypeId role = RoleTypeId.ClassD);     
        void SetPlayer(Player exiledPlayer);
        void SetSensores(ISensors sensores);
        void ResetearPosicionInicial(UnityEngine.Vector3 posicionspawn);
        void ResetEstado();
        AgentObservation GetObservation(float deltaTime);
        
        //AgentSensors GetSensores();
        void Destruir();
    }
}