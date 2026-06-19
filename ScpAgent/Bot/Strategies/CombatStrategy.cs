using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Strategies
{
    public class ClassDStrategy : IAgentRoleStrategy
    {
        public RoleTypeId Role => RoleTypeId.ClassD;

        private ScpAgentBot _bot;
        private Player _player;

        public void OnBind(ScpAgentBot bot, Player player)
        {
            _bot = bot;
            _player = player;

            // ── SUSCRIPCIÓN DE EVENTOS (Lógica de Recompensas) ──
            Exiled.Events.Handlers.Player.Dying += OnDying;
            Exiled.Events.Handlers.Player.Escaping += OnEscaping;
            //Exiled.Events.Handlers.Player.SearchingLocker += OnSearchingLocker;
            // Nota: Añade aquí los demás eventos que tenías en ScpAgentBot (ej: PickUpItem)
        }

        public void OnUnbind()
        {
            // ── LIMPIEZA OBLIGATORIA (Evita los eventos zombies que bajan los it/s) ──
            Exiled.Events.Handlers.Player.Dying -= OnDying;
            Exiled.Events.Handlers.Player.Escaping -= OnEscaping;
            //Exiled.Events.Handlers.Player.SearchingLocker -= OnSearchingLocker;

            _bot = null;
            _player = null;
        }

        public void EjecutarAccionEspecial(int actionId, float deltaTime)
        {
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

        // ── MANEJADORES DE EVENTOS DE RECOMPENSA ──

        private void OnDying(DyingEventArgs ev)
        {
            if (ev.Player == null || _player == null || ev.Player.Id != _player.Id) return;

            // Penalización por morir (Extraído de tu lógica original)
            _bot.PendingReward -= 100f;
            _bot.contadorSuscripciones++; // Si llevas la cuenta para tus logs
        }

        private void OnEscaping(EscapingEventArgs ev)
        {
            if (ev.Player == null || _player == null || ev.Player.Id != _player.Id) return;

            if (ev.NewRole == RoleTypeId.ChaosConscript || ev.NewRole == RoleTypeId.ChaosRifleman)
            {
                _bot.PendingReward += 500f; // ¡Gran recompensa por escapar!
                //_bot.MarcarEpisodioTerminado();
            }
        }


        // Métodos auxiliares de ítems que se quedan en la estrategia humana
        private bool _IsKeycard(ItemType t) => t.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        public float GetKeycardBonus(ItemType type) => type switch
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