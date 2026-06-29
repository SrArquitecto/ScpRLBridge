using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Strategies.Human
{
    public class SurvivorStrategy : HumanStrategy
    {
        public SurvivorStrategy(RoleTypeId role) : base(role)
        {
            
        }

        public override void OnBind(AgentContext ctx)
        {
            base.OnBind(ctx);
            if (_isSubscribed) return;

            // ── SUSCRIPCIÓN DE EVENTOS (Lógica de Recompensas) ──
            Exiled.Events.Handlers.Player.Escaping            += OnEscaping;

            _isSubscribed = true;
            //BaseStrategy.TotalEventosSuscritos += 6;
            
        }

        public override void OnUnbind()
        {
            base.OnUnbind();
            if (!_isSubscribed) return;
            // ── LIMPIEZA OBLIGATORIA (Evita los eventos zombies que bajan los it/s) ──
            Exiled.Events.Handlers.Player.Escaping            -= OnEscaping;
            
            _isSubscribed = false;
            //BaseStrategy.TotalEventosSuscritos -= 6;
        }

        public override float CalcularPrioridadItem(ItemType tipo)
        {
            switch (tipo)
            {
                // Keycards — máxima prioridad, el objetivo es escapar/progresar de zona
                case ItemType type when type.ToString().Contains("Keycard"):
                    return 95f;
    
                // Medical — supervivencia ante todo
                case ItemType.Medkit:
                case ItemType.Adrenaline:
                case ItemType.Painkillers:
                    return 80f;
    
                // Armas — útiles pero secundarias para un superviviente puro
                case ItemType type when type.ToString().StartsWith("Gun"):
                    return 45f;

                case ItemType.ArmorLight:
                case ItemType.ArmorCombat:
                    return 40f;

                case ItemType type when type.ToString().StartsWith("Ammo"):
                    return 35f;

                case ItemType.Radio:
                case ItemType.Flashlight:
                    return 10f; // utilidad para explorar zonas oscuras
    
                default:
                    return 5f;
            }
        }

        public override void OnDamageTaken(float amount, string type)
        {
            if (_ctx == null) return;
            _ctx?.AddReward(-amount * 1.5f);
        }

        protected void OnEscaping(EscapingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            
            _ctx.AddReward(200f);
            _ctx.EndEpisode();
            
            ScpAgentEvents.PendingRoleChanges.Add(_ctx.AgentId);
            Log.Debug($"[ScpAgentBot] Agente {_ctx.AgentId} escapó. +200 — episodio terminado.");
        }


    }
}