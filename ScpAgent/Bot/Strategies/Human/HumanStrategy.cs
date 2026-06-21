using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies;
using ScpAgent.Bot.Interfaces;

namespace ScpAgent.Bot.Strategies.Human
{
    public abstract class HumanStrategy : BaseStrategy
    {
        public HumanStrategy(RoleTypeId role) : base(role)
        {
        }

        public override void addBoundsToCache(Player player)
        {
            base.addBoundsToCache(player);
        }

        public override void destroyBoundsCache(int idAntiguo, int idNuevo)
        {
            base.destroyBoundsCache(idAntiguo, idNuevo);
        }

        public override void OnBind(IAgentController bot)
        {
            base.OnBind(bot);

            // ── SUSCRIPCIÓN DE EVENTOS (Lógica de Recompensas) ──
            Exiled.Events.Handlers.Player.Escaping            += OnEscaping;
            
            Exiled.Events.Handlers.Player.Dying               += OnDying;
            
            Exiled.Events.Handlers.Player.PickingUpItem       += OnPickup;
            
            Exiled.Events.Handlers.Player.InteractingDoor     += OnInteractDoor;
            
            Exiled.Events.Handlers.Player.InteractingElevator += OnInteractElevator;
            
            Exiled.Events.Handlers.Player.InteractingLocker   += OnInteractingLocker;
            
            
        }

        public override void OnUnbind()
        {
            // ── LIMPIEZA OBLIGATORIA (Evita los eventos zombies que bajan los it/s) ──
            Exiled.Events.Handlers.Player.Escaping            -= OnEscaping;
            
            Exiled.Events.Handlers.Player.Dying               -= OnDying;
            
            Exiled.Events.Handlers.Player.PickingUpItem       -= OnPickup;
            
            Exiled.Events.Handlers.Player.InteractingDoor     -= OnInteractDoor;
            
            Exiled.Events.Handlers.Player.InteractingElevator -= OnInteractElevator;
            
            Exiled.Events.Handlers.Player.InteractingLocker   -= OnInteractingLocker;

            _bot = null;
            
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
            _bot.PendingReward += 200f;
            _bot.EpisodioTerminado = true;
            Log.Debug($"[ScpAgentBot] Agente {_bot.AgentId} escapó. +200 — episodio terminado.");
        }

        protected void OnDying(DyingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _bot.PendingReward -= 100f;
            _bot.EpisodioTerminado = true;
            Log.Debug($"[ScpAgentBot] Agente {_bot.AgentId} murió. -100 — episodio terminado.");
        }

        protected void OnPickup(PickingUpItemEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float bonus = GetKeycardBonus(ev.Pickup.Type);
            if (bonus > 0)
            {
                _bot.PendingReward += bonus;
                Log.Debug($"[ScpAgentBot] Agente {_bot.AgentId} recogió {ev.Pickup.Type}. +{bonus}");
            }
        }

        protected void OnInteractDoor(InteractingDoorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            float r = ev.IsAllowed ? 3f : -4f;
            _bot.PendingReward += r;
        }

        protected void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _bot.PendingReward += 8f;
            Log.Debug($"[ScpAgentBot] Agente {_bot.AgentId} abrió locker. +8");
        }

        protected void OnInteractElevator(InteractingElevatorEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            _bot.PendingReward += 15f;
            Log.Debug($"[ScpAgentBot] Agente {_bot.AgentId} usó ascensor. +15");
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