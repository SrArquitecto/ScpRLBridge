using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;
using System.Runtime.InteropServices.ComTypes;
using InventorySystem.Items.Thirdperson;

namespace ScpAgent.Bot.Strategies.Human
{
    public abstract class HumanStrategy : BaseStrategy, IAgentRoleStrategyHuman
    {
        
        public HumanStrategy(RoleTypeId role) : base(role)
        {
            
        }

        public override void InicializarMovimiento(GameObject go, CharacterController cc)
        {
                if (go == null)
                {
                    Log.Error("GameObject nulo");
                    return;
                }

                if (cc == null)
                {
                    Log.Error("CharacterController nulo");
                    return;
                }

            _movimiento = new BotMovement(_ctx.AgentId);
            _movimiento.Inicializar(go, cc);
        }

        public override void ActualizarFisica(float deltaTime, Player player, int accion, GameObject go)
        {
            _movimiento?.Ejecutar(accion, deltaTime, player, go);
        }

        public abstract float CalcularPrioridadItem(ItemType tipo);

        public override void OnBind(AgentContext ctx)
        {
            base.OnBind(ctx);
            if (_isSubscribed) return;
            if (_movimiento == null)
                _movimiento = new BotMovement(_ctx.AgentId);

            // ── SUSCRIPCIÓN DE EVENTOS (Lógica de Recompensas) ──
            Exiled.Events.Handlers.Player.Escaping            += OnEscaping;
            
            Exiled.Events.Handlers.Player.Dying               += OnDying;
            
            Exiled.Events.Handlers.Player.PickingUpItem       += OnPickup;
            
            Exiled.Events.Handlers.Player.InteractingDoor     += OnInteractDoor;
            
            Exiled.Events.Handlers.Player.InteractingElevator += OnInteractElevator;
            
            Exiled.Events.Handlers.Player.InteractingLocker   += OnInteractingLocker;
            
            _isSubscribed = true;
            BaseStrategy.TotalEventosSuscritos += 6;
            
        }

        public override void OnUnbind()
        {
            base.OnUnbind();
            if (!_isSubscribed) return;
            // ── LIMPIEZA OBLIGATORIA (Evita los eventos zombies que bajan los it/s) ──
            Exiled.Events.Handlers.Player.Escaping            -= OnEscaping;
            
            Exiled.Events.Handlers.Player.Dying               -= OnDying;
            
            Exiled.Events.Handlers.Player.PickingUpItem       -= OnPickup;
            
            Exiled.Events.Handlers.Player.InteractingDoor     -= OnInteractDoor;
            
            Exiled.Events.Handlers.Player.InteractingElevator -= OnInteractElevator;
            
            Exiled.Events.Handlers.Player.InteractingLocker   -= OnInteractingLocker;
            
            _isSubscribed = false;
            BaseStrategy.TotalEventosSuscritos -= 6;
        }

        public override void EjecutarAccionEspecial(int actionId, float deltaTime)
        {   
            base.EjecutarAccionEspecial(actionId, deltaTime);
            // Acciones específicas del humano (basadas en tu ScpAgentBot.cs anterior)
            switch (actionId)
            {
                case 7:
                    // Aquí invocas el método de equipar tarjeta que estaba en el bot
                    // Podemos exponerlo públicamente en el bot o moverlo aquí si hereda el inventario
                    //_bot.EquiparTarjeta(); 
                    break;
                case 10:
                   //_bot.DropearItemActual();
                    break;
            }
        }

        protected void OnEscaping(EscapingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            
            _ctx.AddReward(200f);
            _ctx.EndEpisode();
            
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} escapó. +200 — episodio terminado.");
        }

        protected void OnDying(DyingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            
            _ctx.AddReward(-100f);
            _ctx.EndEpisode();
           
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} murió. -100 — episodio terminado.");
        }

        protected void OnPickup(PickingUpItemEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float bonus = GetKeycardBonus(ev.Pickup.Type);
            if (bonus > 0)
            {
                _ctx.AddReward(bonus);
                Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} recogió {ev.Pickup.Type}. +{bonus}");
            }
        }

        protected void OnInteractDoor(InteractingDoorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float r = ev.IsAllowed ? 3f : -4f;
            _ctx.AddReward(r);
        }

        protected void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _ctx.AddReward(8f);

            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} abrió locker. +8");
        }

        protected void OnInteractElevator(InteractingElevatorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _ctx.AddReward(15f);
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} usó ascensor. +15");
        }


        // Métodos auxiliares de ítems que se quedan en la estrategia humana
        protected bool _IsKeycard(ItemType t) => t.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        protected float GetKeycardBonus(ItemType type) => type switch
        {
            ItemType.KeycardJanitor             => 5f,
            ItemType.KeycardGuard               => 25f,
            ItemType.KeycardScientist           => 35f,
            ItemType.KeycardZoneManager         => 40f,
            ItemType.KeycardO5                  => 100f, // Prioridad máxima
            _ => _IsKeycard(type) ? 10f : 0f
        };
    }
}