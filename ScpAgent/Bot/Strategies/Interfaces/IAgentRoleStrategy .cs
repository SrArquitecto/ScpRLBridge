using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces; // (Este te lo puedes ahorrar si estás dentro)
using Exiled.API.Enums;

namespace ScpAgent.Bot.Strategies.Interfaces
{
    public interface IAgentRoleStrategyBase
    {
        RoleTypeId Role { get; }
        void OnBind(AgentContext ctx);
        void OnUnbind();
        void EjecutarAccionEspecial(int actionId, float deltaTime);
        public void OnRoomChanged(Room anterior, Room nueva);
    }

    public interface IAgentRoleStrategyHuman : IAgentRoleStrategyBase
    {
        float CalcularPrioridadItem(ItemType tipo);
        string CategorizarItem(ItemType tipo);
    }
}