using System;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Strategy;

namespace ScpAgent.Bot.Strategies.Human
{
    public abstract class HumanStrategy : BaseStrategy, IAgentRoleHumanStrategy
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

            _movimiento = new BaseMovement(_ctx.AgentId);
            _movimiento.Inicializar(go, cc);
        }

        public abstract float CalcularPrioridadItem(ItemType tipo);

        public override void OnBind(AgentContext ctx)
        {
            base.OnBind(ctx);
            if (_isSubscribed) return;
            if (_movimiento == null)
                _movimiento = new BaseMovement(_ctx.AgentId);

            // ── SUSCRIPCIÓN DE EVENTOS (Lógica de Recompensas) ──
            Exiled.Events.Handlers.Player.PickingUpItem       += OnPickup;
            Exiled.Events.Handlers.Player.InteractingLocker   += OnInteractingLocker;
            
            _isSubscribed = true;
            //BaseStrategy.TotalEventosSuscritos += 6;
            
        }

        public override void OnUnbind()
        {
            base.OnUnbind();
            if (!_isSubscribed) return;
            // ── LIMPIEZA OBLIGATORIA (Evita los eventos zombies que bajan los it/s) ──          
            Exiled.Events.Handlers.Player.PickingUpItem       -= OnPickup;    
            Exiled.Events.Handlers.Player.InteractingLocker   -= OnInteractingLocker;
            
            _isSubscribed = false;
            //BaseStrategy.TotalEventosSuscritos -= 6;
        }


        public void EquiparTarjeta(Player player)
        {
            _movimiento.EquiparTarjeta(player);
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



        protected void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _ctx.AddReward(8f);

            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} abrió locker. +8");
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