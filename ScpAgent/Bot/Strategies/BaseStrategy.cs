using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Strategies.Interfaces;
using System.Collections.Generic;
namespace ScpAgent.Bot.Strategies
{
    public abstract class BaseStrategy : IAgentRoleStrategyBase
    {
        protected readonly HashSet<int> _salasVisitadas = new HashSet<int>();
        public RoleTypeId Role { get; }
        protected AgentContext _ctx;

        public BaseStrategy(RoleTypeId role) 
        {
            Role = role;
        }

        public virtual void OnBind(AgentContext ctx)
        {
            _ctx = ctx;
        }

        public virtual void OnUnbind()
        {
            
            _ctx = null;
        }

        public virtual void EjecutarAccionEspecial(int actionId, float deltaTime) {}

        protected bool _EsEsteAgente(Player p) 
            => _ctx?.Player != null && p?.Id == _ctx.Player.Id;
        // ── MANEJADORES DE EVENTOS DE RECOMPENSA ──

        public void OnRoomChanged(Room roomAnterior, Room roomNueva)
        {
            if (_ctx == null) return;

            // Recompensa por explorar una sala nueva (solo la primera vez)
            if (!_salasVisitadas.Contains(roomNueva.GameObject.GetInstanceID()))
            {
                _salasVisitadas.Add(roomNueva.GameObject.GetInstanceID());
                _ctx.AddReward(10f);
            }
        }

        public abstract void OnDamageTaken(float amount, string type);
        
    }
}