using Exiled.API.Features;
using PlayerRoles;

namespace ScpAgent.Bot.Strategies.Interfaces
{
    public interface IAgentRoleStrategy
    {
        RoleTypeId Role { get; }
        void OnBind(ScpAgentBot bot, Player player);

        // Limpia las suscripciones para evitar fugas de memoria (Memory Leaks)
        void OnUnbind();

        // Ejecuta acciones que sean exclusivas de este rol (ej: usar tarjeta o habilidades SCP)
        void EjecutarAccionEspecial(int actionId, float deltaTime);
    }
}