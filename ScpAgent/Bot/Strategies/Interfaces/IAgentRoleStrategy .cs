using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;

namespace ScpAgent.Bot.Strategies.Interfaces
{
    public interface IAgentRoleStrategyBase
    {
        RoleTypeId Role { get; }
        void InicializarMovimiento(GameObject go, CharacterController cc);
        void ActualizarFisica(float deltaTime, Player player, int accion, GameObject go);
        void OnBind(AgentContext ctx);
        void OnUnbind();
        void EjecutarAccionEspecial(int actionId, float deltaTime);
        public void OnRoomChanged(Room anterior, Room nueva);
        void OnDamageTaken(float amount, string type);
    }

    public interface IAgentRoleStrategyHuman : IAgentRoleStrategyBase
    {
        float CalcularPrioridadItem(ItemType tipo);
        
        
    }
}