using Exiled.API.Features;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using PlayerRoles;
using Mirror;
using MEC;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Simulation;
using ScpAgent.Bot.Strategies.Human;
using Exiled.API.Features.Doors;
using Exiled.API.Enums;
using ScpAgent.Managers;
using ScpAgent.Managers.Data;

namespace ScpAgent.Bot
{
    public class ScpAgentSpawner
    {   
        private ScpAgentBot _bot;
        private CoroutineHandle _initDelayHandle;
        private CoroutineHandle _respawnHandle;
        public bool _isRespawning = false;
        public ScpAgentSpawner(ScpAgentBot bot)
        {
            _bot = bot;
        }

        public void Init(FakeConnection fakeConn, string nickname, RoleTypeId role)
        {
            // 1. Instanciar en Unity
            GameObject prefab = NetworkManager.singleton.playerPrefab;
            GameObject botGameObject = UnityEngine.Object.Instantiate(prefab);

            // 2. Vincular con Mirror (Red)
            ReferenceHub hub = botGameObject.GetComponent<ReferenceHub>();
            NetworkServer.AddPlayerForConnection(fakeConn, botGameObject);
            hub.nicknameSync.Network_myNickSync = nickname;

            // 3. Obtener el wrapper temporal y asignar el rol
            Player tempPlayer = Player.Get(botGameObject);
            tempPlayer.Role.Set(role);

            CharacterController cc = botGameObject.GetComponent<CharacterController>();
            if (cc == null) cc = botGameObject.AddComponent<CharacterController>();
            // 4. Entregar dependencias iniciales al Orquestador 
            // (El bot aquí crea su AgentContext, pero NO inicializa físicas aún)
            _bot.SetDependencias(fakeConn, botGameObject, tempPlayer, cc, role);

            // 5. CRÍTICO: Esperar a que el servidor genere el cuerpo real
            _initDelayHandle = Timing.CallDelayed(0.5f, () =>
            {
                // A) Refrescar el wrapper de EXILED (¡Vital!)
                Player freshPlayer = Player.Get(botGameObject);
                if (freshPlayer == null)
                {
                    Log.Error($"[BotSpawner] Bot {_bot._agentId} — no se pudo refrescar el wrapper tras el spawn.");
                    return;
                }

                // B) Avisar al Orquestador que el cuerpo ya existe
                // ¡Aquí dentro de este método el bot debe hacer "new BotLocomotion()" 
                // e inicializar el CharacterController y MouseLook!
                _bot.FinalizarInicio(freshPlayer);

                // C) Actualizar Mapas y Managers
                
                AgentManager.Instance?.OnBotSpawnComplete(_bot._agentId, freshPlayer);
                MapUtils.addBoundsToCache(freshPlayer, _bot._sensores);
            });
        }

        public void SpawnearEnNuevaRonda(RoleTypeId role = RoleTypeId.ClassD)
        {
            if (_initDelayHandle.IsValid)
                Timing.KillCoroutines(_initDelayHandle);

            int idAntiguo = _bot._exiledPlayer?.Id ?? -1;
            FakeConnection fakeConn = _bot._fakeConn;
            GameObject botGameObject = _bot._botGameObject;
            Player ExiledPlayer;
            // 1. Destruir el GameObject anterior 
            // si existe
            if (botGameObject != null)
            {
                UnityEngine.Object.Destroy(botGameObject);
                botGameObject = null;
            }

            // 2. Limpiar la conexión anterior de Mirror si sigue registrada
            if (NetworkServer.connections.ContainsKey(fakeConn.connectionId))
                NetworkServer.connections.Remove(fakeConn.connectionId);

            // 3. Clonar el prefab de nuevo
            GameObject prefab = NetworkManager.singleton.playerPrefab;
            botGameObject = UnityEngine.Object.Instantiate(prefab);

            // 4. Vincular con Mirror usando la misma FakeConnection
            ReferenceHub hub = botGameObject.GetComponent<ReferenceHub>();
            NetworkServer.AddPlayerForConnection(fakeConn, botGameObject);
            hub.nicknameSync.Network_myNickSync = $"IA_Agent_{_bot._agentId}";

            // 5. Obtener wrapper inicial y asignar rol
            ExiledPlayer = Player.Get(botGameObject);
            ExiledPlayer.Role.Set(role);

            // 6. Asegurar CharacterController
            CharacterController cc = botGameObject.GetComponent<CharacterController>();
            if (cc == null) cc = botGameObject.AddComponent<CharacterController>();

            _bot.SetDependencias(fakeConn, botGameObject, ExiledPlayer, cc, role);
            // 7. Refrescar wrapper tras Role.Set
            _initDelayHandle = Timing.CallDelayed(0.5f, () =>
            {
                var freshPlayer = Player.Get(botGameObject);
                if (freshPlayer != null)
                {
                    int idNuevo = freshPlayer.Id;
                    MapUtils.destroyBoundsCache(idAntiguo, idNuevo);
                    
                    
                    _bot.FinalizarInicio(freshPlayer);
                    AgentManager.Instance?.OnBotSpawnComplete(_bot._agentId, freshPlayer);
                    MapUtils.addBoundsToCache(freshPlayer, _bot._sensores);
                    
                    Log.Info($"[ScpAgentBot] Bot {_bot._agentId} respawneado en nueva ronda. " +
                            $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive} " +
                            $"Pos=({freshPlayer.Position.x:F2},{freshPlayer.Position.y:F2},{freshPlayer.Position.z:F2}) " +
                            $"Room={freshPlayer.CurrentRoom?.Type.ToString() ?? "?"}");
                }
                else
                {
                    Log.Error($"[ScpAgentBot] Bot {_bot._agentId} — fallo al refrescar wrapper en nueva ronda.");
                }
            });
        }

        public void EjecutarRespawn(RoleTypeId role)
        {
            if (_isRespawning) return; // Evita carreras de corrutinas concurrentes
            // 1. 🔥 CORTE QUIRÚRGICO: Si había un respawn en progreso, lo matamos inmediatamente
                if (_respawnHandle.IsValid)
                {
                    Timing.KillCoroutines(_respawnHandle);
                }

                // 2. Forzamos el flag a false por si la corrutina muerta lo dejó en true
                _isRespawning = false; 

                // 3. Iniciamos la nueva rutina y guardamos su handle de seguimiento
                _respawnHandle = Timing.RunCoroutine(_RutinaRespawn(role));
        }

        private IEnumerator<float> _RutinaRespawn(RoleTypeId role)
        {
            _isRespawning = true;
            try
            {
                //int idAntiguo = _bot.GetPlayer()?.Id ?? -1;
                FakeConnection fakeConn = _bot._fakeConn;
                GameObject botGameObject = _bot._botGameObject;
                Player ExiledPlayer = _bot._exiledPlayer;
                int AgentId = _bot._agentId;

                if (botGameObject == null) yield break;

                int idAntiguo = ExiledPlayer?.Id ?? -1;

                // 2. Cambiar a Espectador momentáneamente
                var current = Player.Get(botGameObject);
                if (current != null)
                    current.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.None);

                yield return Timing.WaitForSeconds(0.2f);

                // 3. Asignar ClassD de nuevo
                var respawning = Player.Get(botGameObject);
                if (respawning != null)
                    respawning.Role.Set(role, RoleSpawnFlags.All);

                // Si el rol cambió, actualizar la estrategia
                if (_bot._role != role)
                {
                    var slot = AgentManager.Instance?.GetSlot(AgentId);
                    if (slot != null)
                    {
                        IAgentRoleBaseStrategy newStrategy;
                        switch (role)
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
                                newStrategy = new CombatStrategy(role);
                                break;
                            default:
                                newStrategy = new SurvivorStrategy(role);
                                break;
                        }
                        _bot.SetStrategy(newStrategy);
                        slot.Strategy = newStrategy;
                        _bot._role = role;
                    }
                }

                yield return Timing.WaitForSeconds(0.1f);

                // 4. Refrescar wrapper de EXILED
                var freshPlayer = Player.Get(botGameObject);
                if (freshPlayer == null)
                {
                    Log.Error($"[ScpAgentBot] Bot {AgentId} — no se pudo refrescar wrapper tras respawn.");
                    yield break;
                }

                // 5. Migrar cache de sala si el ID cambió
                int idNuevo = freshPlayer.Id;
                if (idAntiguo != idNuevo && idAntiguo >= 0)
                MapUtils.destroyBoundsCache(idAntiguo, idNuevo);

                ExiledPlayer = freshPlayer;

                // 6. Asegurar CharacterController y actualizar referencia en el bot
                CharacterController cc = botGameObject.GetComponent<CharacterController>();
                if (cc == null)
                    cc = botGameObject.AddComponent<CharacterController>();
                _bot._cc = cc;

                _bot.FinalizarInicio(ExiledPlayer);
                AgentManager.Instance?.OnBotSpawnComplete(AgentId, freshPlayer);
                MapUtils.addBoundsToCache(ExiledPlayer, _bot._sensores);

                Log.Debug($"[ScpAgentBot] Bot {AgentId} respawneado. " +
                        $"Role={ExiledPlayer.Role.Type} IsAlive={ExiledPlayer.IsAlive}");
            }
            finally
            {
                _isRespawning = false;
            }
        }

        public void ResetEstado()
        {  
            // ── Cancelar corrutinas pendientes si las hay ─────────────────────
            if (_respawnHandle.IsRunning)
                Timing.KillCoroutines(_respawnHandle);

            if (_initDelayHandle.IsRunning)
                Timing.KillCoroutines(_initDelayHandle);

        }

        public void Destruir()
        {
            // 1. Apagar los eventos para que no consuman CPU
            if (_respawnHandle.IsValid)
            {
                Timing.KillCoroutines(_respawnHandle);
            }
            Timing.KillCoroutines(_initDelayHandle);
 
            // 4. Destruir el objeto físico en Unity
            if (_bot._botGameObject!= null)
            {
                UnityEngine.Object.Destroy(_bot._botGameObject);
                _bot._botGameObject = null;
            }
            
            Log.Debug($"[ScpAgentBot] Agente {_bot._agentId} destruido y memoria liberada.");
            
        }


        
    }

}