using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Sensors;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Strategies.Human;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Managers.Data
{
    public static class RoleFactory
    {
        public static (RoleTypeId rol, IAgentRoleBaseStrategy strategy, ISensors sensors) 
            CreateForRole(string roleName, int agentId)
        {
            RoleTypeId rol;
            IAgentRoleBaseStrategy strategy;
            ISensors sensors = new HumanSensors(agentId);

            if (roleName == null || roleName == "classd")
            {
                rol = RoleTypeId.ClassD;
                strategy = new SurvivorStrategy(rol);
            }
            else if (roleName == "chaos")
            {
                rol = RoleTypeId.ChaosRifleman;
                strategy = new CombatStrategy(rol);
            }
            else if (roleName == "scientist")
            {
                rol = RoleTypeId.Scientist;
                strategy = new SurvivorStrategy(rol);
            }
            else if (roleName == "ntf")
            {
                rol = RoleTypeId.NtfPrivate;
                strategy = new CombatStrategy(rol);
            }
            else if (roleName == "guard")
            {
                rol = RoleTypeId.FacilityGuard;
                strategy = new CombatStrategy(rol);
            }
            else
            {
                rol = RoleTypeId.ClassD;
                strategy = new SurvivorStrategy(rol);
            }

            return (rol, strategy, sensors);
        }
    }
}
