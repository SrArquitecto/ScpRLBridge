using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Interfaces;

namespace ScpAgent.Bot.Strategies.Interfaces
{
    public interface IAgentRoleStrategy
    {
        RoleTypeId Role { get; }
        void OnBind(IAgentController bot);
        void OnUnbind();
        void addBoundsToCache(Player player);
        void destroyBoundsCache(int idAntiguo, int idNuevo);
        void EjecutarAccionEspecial(int actionId, float deltaTime);
    }
}