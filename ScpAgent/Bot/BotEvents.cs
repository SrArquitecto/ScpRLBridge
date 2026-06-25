using Exiled.API.Features;
using System;
using UnityEngine;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Enums;

namespace ScpAgent.Bot
{
    public class BotEvents
    {
        private readonly ScpAgentBot _bot;
        private bool _isSubscribed = false;
        public bool firstTime = true;
        public static int TotalEventosSuscritos { get; private set; } = 0;
        public BotEvents(ScpAgentBot bot)
        {
            _bot = bot;
        }

        public void SuscribirEventos()
        {
            if (_isSubscribed) return;
            Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             += OnHurt;
            _isSubscribed = true;
            BotEvents.TotalEventosSuscritos += 2;
        }
        public void DesuscribirEventos()
        {
            if (!_isSubscribed) return;
            Exiled.Events.Handlers.Player.RoomChanged         -= OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             -= OnHurt;
            _isSubscribed = false;
            BotEvents.TotalEventosSuscritos -= 2;
        }

        public void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.NewRoom == null || ev.NewRoom.Type == RoomType.Unknown) return;
            try
            {
                MapUtils.addBoundsToCache(ev.Player, _bot._sensores);
                
                // ── REGISTRO EN EL GRAFO DE NAVEGACIÓN ──────────────────────────────
                if (_bot._sensores?.GetGraph() != null && !firstTime)
                {
                    bool esPrimeraVisita = false;
                    esPrimeraVisita = (bool)_bot._sensores?.RegistrarTransicion(ev.OldRoom, ev.NewRoom);
                        
                    if (esPrimeraVisita)
                    {
                        Log.Debug($"[Grafo] ¡Agente {_bot._agentId} descubrió una nueva sala: {ev.NewRoom.Type}!");
                            // Aquí puedes inyectar una recompensa positiva directa a Python:
                            // _bot._ctx?.AddReward(1.0f); // Recompensa por exploración
                    }
                }
                
                firstTime = false;    
                _bot._strategy?.OnRoomChanged(ev.OldRoom, ev.NewRoom);
            }
            catch (Exception ex)
            {
                Log.Error($"[ScpAgentBot] OnRoomChanged Agente {_bot._agentId}: {ex.Message}");
            }
        }

        private void OnHurt(Exiled.Events.EventArgs.Player.HurtingEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.Amount <= 0f) return;
        
            // ── Tipo de daño ──────────────────────────────────────────────────────
            string tipoDaño = "Unknown";
            if (ev.DamageHandler != null)
            {
                string handlerName = ev.DamageHandler.GetType().Name;
                
                if (handlerName.Contains("Firearm") || handlerName.Contains("Bullet"))
                    tipoDaño = "Firearm";
                else if (handlerName.Contains("Explosion") || handlerName.Contains("Grenade"))
                    tipoDaño = "Explosion";
                else if (handlerName.Contains("Scp") || handlerName.Contains("Scpitem"))
                    tipoDaño = "Scp";
                else if (handlerName.Contains("Fall"))
                    tipoDaño = "Fall";
                else if (handlerName.Contains("Bleeding") || handlerName.Contains("Poison"))
                    tipoDaño = "Status";
                else
                    tipoDaño = "Unknown";
            }
        
            // ── Dirección hacia el atacante ───────────────────────────────────────
            Vector3 dirHaciaAtacante = Vector3.zero;
            bool atacanteEnMemoria   = false;
        
            if (ev.Attacker != null && ev.Attacker != ev.Player)
            {
                Vector3 vecHaciaAtacante = (ev.Attacker.Position - ev.Player.Position);
                vecHaciaAtacante.y = 0f; // solo plano horizontal
                if (vecHaciaAtacante.sqrMagnitude > 0.001f)
                    dirHaciaAtacante = vecHaciaAtacante.normalized;
        
                // Comprobar si el atacante está en la memoria visual del sensor
                // Necesitas acceso al sensor — opciones:
                // A) Pasar la comprobación al sensor directamente
                // B) Usar AgentContext para preguntar
                atacanteEnMemoria = _bot._sensores?.TieneEnMemoriaJugadores(ev.Attacker.Id) ?? false;
            }
        
            // ── Pasar al sensor ───────────────────────────────────────────────────
            _bot._sensores?.RegistrarDaño(ev.Amount, tipoDaño, dirHaciaAtacante, atacanteEnMemoria);
            _bot._strategy?.OnDamageTaken(ev.Amount, tipoDaño);
            // ── Dar recompensa negativa por daño recibido (a la estrategia) ───────
            //_ctx?.AddReward(-ev.Amount * 0.5f); // penalización proporcional al daño
        } 

        private  bool _EsEsteAgente(Player player)
        {
            return _bot._exiledPlayer == player;
        }
    }

}