using Exiled.API.Features;
using PlayerRoles;
using ScpAgent.Bot.Strategies.Interfaces;
using System.Collections.Generic;
using UnityEngine;
using ScpAgent.Bot.Strategy.Movement;
using Exiled.Events.EventArgs.Player;

namespace ScpAgent.Bot.Strategies
{
    public abstract class BaseStrategy : IAgentRoleBaseStrategy
    {
        protected BaseMovement _movimiento = null;
        protected readonly HashSet<int> _salasVisitadas = new HashSet<int>();
        public RoleTypeId Role { get; }
        protected AgentContext _ctx;
        protected bool _isSubscribed = false;
        public BaseStrategy(RoleTypeId role) 
        {
            Role = role;
        }

        public abstract void InicializarMovimiento(GameObject go, CharacterController cc);

        public void MoverPersonaje(int accion, float deltaTime, Player player, GameObject go)
        {
            _movimiento.MoverPersonaje(accion, deltaTime, player, go);
        }

        public void MoverCamara(int accion)
        {
            _movimiento.MoverCamara(accion);
        }


        public void AbrirPuerta(Player player)
        {
            _movimiento.AbrirPuerta(player);
        }


        public virtual void OnBind(AgentContext ctx)
        {
            _ctx = ctx;
            Exiled.Events.Handlers.Player.Dying               += OnDying;
            Exiled.Events.Handlers.Player.InteractingDoor     += OnInteractDoor;
            Exiled.Events.Handlers.Player.InteractingElevator += OnInteractElevator;
        }

        public virtual void OnUnbind()
        {
            _ctx = null;
            Exiled.Events.Handlers.Player.Dying               -= OnDying;
            Exiled.Events.Handlers.Player.InteractingDoor     -= OnInteractDoor;
            Exiled.Events.Handlers.Player.InteractingElevator -= OnInteractElevator;
        }

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

        protected void OnDying(DyingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;

            _ctx.AddReward(-100f);
            _ctx.EndEpisode();

            // Marcar este bot como pendiente de cambio de rol.
            // El juego asignará un nuevo GameObject y disparará ChangingRole.
            ScpAgentEvents.PendingRoleChanges.Add(_ctx.AgentId);
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} murió. -100 — episodio terminado.");
        }

        protected void OnInteractDoor(InteractingDoorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float r = ev.IsAllowed ? 3f : -4f;
            _ctx.AddReward(r);
        }

        protected void OnInteractElevator(InteractingElevatorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _ctx.AddReward(15f);
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} usó ascensor. +15");
        }
        
    }
}