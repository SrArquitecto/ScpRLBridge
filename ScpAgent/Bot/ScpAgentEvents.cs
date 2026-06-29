using Exiled.API.Features;
using System;
using UnityEngine;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Enums;
using PlayerRoles;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Strategies.Human;
using ScpAgent.Managers;
using Respawning.Objectives;

namespace ScpAgent.Bot
{
    public class ScpAgentEvents
    {
        private readonly ScpAgentBot _bot;
        private bool _isSubscribed = false;
        public bool firstTime = true;
        //public static int TotalEventosSuscritos { get; private set; } = 0;

        // Flag estático: marca qué agentId está esperando un cambio de rol.
        // Se establece en OnDying y se limpia en OnChangingRole.
        public static System.Collections.Generic.HashSet<int> PendingRoleChanges = new System.Collections.Generic.HashSet<int>();

        public ScpAgentEvents(ScpAgentBot bot)
        {
            _bot = bot;
        }

        public void SuscribirEventos()
        {
            if (_isSubscribed) return;
            Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             += OnHurt;
            Exiled.Events.Handlers.Player.ChangingRole        += OnChangingRole;

            _isSubscribed = true;
            //ScpAgentEvents.TotalEventosSuscritos += 3;
        }
        public void DesuscribirEventos()
        {
            if (!_isSubscribed) return;
            Exiled.Events.Handlers.Player.RoomChanged         -= OnRoomChanged;
            Exiled.Events.Handlers.Player.Hurting             -= OnHurt;
            Exiled.Events.Handlers.Player.ChangingRole        -= OnChangingRole;
            _isSubscribed = false;
            //ScpAgentEvents.TotalEventosSuscritos -= 3;
        }

        public void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.NewRoom == null || ev.NewRoom.Type == RoomType.Unknown) return;
            try
            {
                MapUtils.addBoundsToCache(ev.Player, _bot._sensores);
                
                // ── REGISTRO EN EL GRAFO DE NAVEGACIÓN ──────────────────────────────
                if (_bot._sensores?.GetGraph() != null)
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

        private void OnChangingRole(Exiled.Events.EventArgs.Player.ChangingRoleEventArgs ev)
        {
            // Usar flag estático: solo procesar si este bot está esperando un cambio de rol.
            if (!PendingRoleChanges.Contains(_bot._agentId))
            {
                return;
            }

            RoleTypeId newRole = ev.NewRole;
            RoleTypeId oldRole = _bot._role;

            // Si el cambio es a Spectator (muerte), no procesar: el flag se mantiene
            // para procesar el siguiente cambio al rol asignado por el juego.
            if (newRole == RoleTypeId.Spectator)
            {
                //Log.Debug($"[BotEvents] Agente {_bot._agentId} pasó a Spectator, esperando nuevo rol del juego...");
                return;
            }

            // Limpiar el flag: ya procesamos el cambio completo
            PendingRoleChanges.Remove(_bot._agentId);

            // Si el rol no cambió, no hacer nada
            if (newRole == oldRole) return;

            _bot.EjecutarRespawn(ev.NewRole);
            //Log.Info($"[BotEvents] Agente {_bot._agentId} cambiando rol: {oldRole} → {newRole}");
            
            // Actualizar el rol del bot

            /*
            _bot._role = newRole;

            // Crear nueva estrategia basada en el nuevo rol
            IAgentRoleBaseStrategy newStrategy;
            switch (newRole)
            {
                case RoleTypeId.ChaosRifleman:
                case RoleTypeId.ChaosMarauder:
                case RoleTypeId.ChaosConscript:
                case RoleTypeId.ChaosRepressor:
                case RoleTypeId.FacilityGuard:
                case RoleTypeId.NtfCaptain:
                case RoleTypeId.NtfPrivate:
                case RoleTypeId.NtfSergeant:
                case RoleTypeId.NtfSpecialist:
                    newStrategy = new CombatStrategy(newRole);
                    break;
                case RoleTypeId.ClassD:
                case RoleTypeId.Scientist:
                default:
                    newStrategy = new SurvivorStrategy(newRole);
                    break;
            }

            // Aplicar la nueva estrategia
            _bot.SetStrategy(newStrategy);

            // Actualizar el slot en AgentManager
            var slot = AgentManager.Instance?.GetSlot(_bot._agentId);
            if (slot != null)
            {
                slot.Strategy = newStrategy;
                slot.Rol = newRole;
            }

            // Buscar el Player correcto por nickname en vez de usar ev.Player
            // (ev.Player puede ser cualquier player del juego en este momento)
            string expectedNick = $"IA_Agent_{_bot._agentId}";
            Player myPlayer = null;
            GameObject foundGo = null;

            // 1) Intentar por Player.List (sin filtro IsAlive para incluir spectators)
            foreach (var p in Player.List)
            {
                if (p != null && p.Nickname == expectedNick && p.GameObject != null)
                {
                    myPlayer = p;
                    break;
                }
            }

            // 2) Si no se encontró, buscar por ReferenceHub.AllHubs verificando nickname
            //    y que el GameObject esté activo (no destruido)
            if (myPlayer == null)
            {
                foreach (var hub in ReferenceHub.AllHubs)
                {
                    if (hub != null && hub.gameObject != null &&
                        hub.gameObject.activeInHierarchy &&
                        hub.nicknameSync != null &&
                        hub.nicknameSync.Network_myNickSync == expectedNick)
                    {
                        foundGo = hub.gameObject;
                        break;
                    }
                }
            }

            // 3) Si aún no se encontró, buscar por FindObjectsByType con filtro activo
            if (myPlayer == null && foundGo == null)
            {
                var allHubs = UnityEngine.Object.FindObjectsByType<ReferenceHub>(FindObjectsSortMode.None);
                foreach (var hub in allHubs)
                {
                    if (hub != null && hub.gameObject != null &&
                        hub.gameObject.activeInHierarchy &&
                        hub.nicknameSync != null &&
                        hub.nicknameSync.Network_myNickSync == expectedNick)
                    {
                        foundGo = hub.gameObject;
                        break;
                    }
                }
            }

            if (myPlayer != null && myPlayer.GameObject != null)
            {
                _bot._botGameObject = myPlayer.GameObject;
                _bot._cc = myPlayer.GameObject.GetComponent<CharacterController>();
                _bot._exiledPlayer = myPlayer;
                Log.Info($"[BotEvents] Agente {_bot._agentId} GameObject actualizado a {myPlayer.GameObject.name}");
            }
            else if (foundGo != null)
            {
                _bot._botGameObject = foundGo;
                _bot._cc = foundGo.GetComponent<CharacterController>();
                Log.Info($"[BotEvents] Agente {_bot._agentId} GameObject encontrado por nombre: {foundGo.name}");
            }
            else
            {
                Log.Warn($"[BotEvents] Agente {_bot._agentId} no encontró su Player/GameObject con nickname '{expectedNick}'");
            }

            // Re-inicializar el movimiento con el nuevo GameObject
            if (_bot._botGameObject != null && _bot._cc != null && newStrategy is ScpAgent.Bot.Strategies.Human.HumanStrategy humanStrategy)
            {
                humanStrategy.InicializarMovimiento(_bot._botGameObject, _bot._cc);
                Log.Info($"[BotEvents] Agente {_bot._agentId} BotMovement reinicializado");
            }

            Log.Info($"[BotEvents] Agente {_bot._agentId} ahora usa estrategia: {newStrategy.GetType().Name}");
            */
        }

        private  bool _EsEsteAgente(Player player)
        {
            return _bot._exiledPlayer == player;
        }
    }

}