using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Server;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;
using Exiled.API.Enums;
using ScpAgent.Bot;

namespace ScpAgent.Managers
{
    public class RoundManager
    {
        private readonly ScpRLPlugin _plugin;
        private int _episode = 0;
        private bool _roundEnding = false;
        private bool _isSpawning = false;
        public readonly AgentManager _agentManager;
        public static volatile bool IsC1Enabled = false;
        public bool BotsListos { get; private set; } = false;
        private CoroutineHandle _monitorHandle;
        private bool _firstSpawn = true;

        // Cambiamos StateManager por nuestro diccionario definitivo de agentes de IA
        //public Dictionary<int, Bot.ScpAgentBot> BotsActivos { get; private set; } = new Dictionary<int, Bot.ScpAgentBot>();

        public RoundManager(ScpRLPlugin plugin, AgentManager agentManager)
        {
            _plugin = plugin;
            _agentManager = agentManager;
            // Suscripción estricta a los eventos de EXILED para controlar los episodios
            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
            Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
            Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
            Exiled.Events.Handlers.Player.Died += OnDied;
            Exiled.Events.Handlers.Server.EndingRound += OnEndingRound;
            _plugin.ControlServer.AllAgentsReady += OnAllAgentReady;
            // Monitor de señales TCP cruzadas (como comandos de reinicio forzado desde Python)
            _monitorHandle = Timing.RunCoroutine(GlobalTcpMonitor());
        }

        public void Unsubscribe()
        {
            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
            Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
            Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
            Exiled.Events.Handlers.Player.Died -= OnDied;
            Exiled.Events.Handlers.Server.EndingRound -= OnEndingRound;
            _plugin.ControlServer.AllAgentsReady -= OnAllAgentReady;
            Timing.KillCoroutines(_monitorHandle);
            _agentManager.Clear();
        }

        /// <summary>
        /// Crea e inyecta la instancia del bot en el servidor utilizando su constructor de red interno.
        /// </summary>
        /// 
        private void SpawnearAgentes(IEnumerable<int> agentIds)
        {
            if (_firstSpawn) {
                _agentManager.CreateAll(agentIds);
                _firstSpawn = false;
            }
            else
            {
                    foreach (var id in agentIds)
                {
                    var bot = _agentManager.Get(id);
                    if (bot != null)
                    {
                        bot.EjecutarRespawn(); // Ejecuta internamente la rutina de Respawn limpia
                    }
                }
            }
        }
        private void SpawnearYRegistrarAgente2(int agentId)
        {
            try
            {
                // Invocamos el constructor limpio que refactorizamos para ScpAgent
                string nickname = $"IA_Agent_{agentId}";
                Bot.ScpAgentBot nuevoBot = new Bot.ScpAgentBot(nickname, agentId, RoleTypeId.ClassD);

                // Forzamos la inyección del controlador físico nativo de Unity si falta
                if (nuevoBot.ExiledPlayer.GameObject.GetComponent<CharacterController>() == null)
                {
                    nuevoBot.ExiledPlayer.GameObject.AddComponent<CharacterController>();
                }

                // Guardamos en la lista maestra de agentes vivos
                //BotsActivos[agentId] = nuevoBot;
                Log.Debug($"[RoundManager] Agente {agentId} instanciado e inyectado físicamente en el servidor.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RoundManager] Error crítico al spawnear el Agente {agentId}: {ex.Message}");
            }
        }


        // --------------------------------------------------------------------------
        // GESTIÓN DE EVENTOS Y CICLO DEL SERVIDOR
        // --------------------------------------------------------------------------

        private void OnWaitingForPlayers()
        {   
            if (_isSpawning) return;
            _isSpawning = true;
            _roundEnding = false;
            
            AgentManager.Instance.ResetearTodos(); 
            if (_plugin.ControlServer.IsPythonConnected)
            {
                Timing.RunCoroutine(DelayedSpawnSequence());
            }
            else
            {
                Log.Debug("[RoundManager] Esperando conexión de Python...");
            }
        
        }
        private void OnAllAgentReady()
        {
            _plugin.ControlServer.AllAgentsReady -= OnAllAgentReady;
            Timing.RunCoroutine(DelayedSpawnSequence());
        }

        private IEnumerator<float> DelayedSpawnSequence()
        {
            // Esperamos 3 segundos a que Unity asiente las estructuras y pasillos generados
            yield return Timing.WaitForSeconds(3.0f); 

            // Detenemos el motor de red temporalmente por seguridad si venía encendido
            //_plugin.ControlServer.DetenerEntrenamiento();

            /*
            while (!_plugin.ControlServer.Initialized) 
            {
                Log.Debug("[RoundManager] Esperando inicialización del ControlServer (Python)...");
                yield return Timing.WaitForSeconds(1.0f);
            }
            */

            SpawnearAgentes(Enumerable.Range(0, _plugin.ControlServer.NumAgentsExpected));
            Log.Debug("[RoundManager] Red de simulación detectada. Instanciando 4 agentes...");
            
            // Instanciamos los 4 bots del entorno Gymnasium multiplexado
            for (int id = 0; id < 4; id++)
            {
                //SpawnearYRegistrarAgente(id);
                yield return Timing.WaitForSeconds(0.2f); // Pequeña tregua para evitar saturación del motor
            }

            yield return Timing.WaitForSeconds(1.0f);

            Log.Debug("[RoundManager] Todo listo. Forzando el inicio de la simulación física.");
            CharacterClassManager.ForceRoundStart();
            
            _isSpawning = false;
        }

        private void OnRoundStarted()
        {
            //_plugin.ControlServer.IniciarEntrenamiento(_agentManager);
            foreach (var p in _agentManager.GetAll().Values)
            {
                p.GetSensores().InvalidarCachesMapa();
            }

            _episode++;
            _roundEnding = false;
            Log.Debug($"[RoundManager] 🏁 === Iniciando Episodio {_episode} ===");

            // 1. Teletransportamos y preparamos físicamente a cada agente en el mapa
            foreach (var item in _agentManager.GetAll().Values)
            {
                if (item.ExiledPlayer != null)
                {
                    item.ExiledPlayer.Role.Set(RoleTypeId.ClassD, RoleSpawnFlags.All);
                    //AsignarSpawnUnico(item);
                }
            }

            // 2. ¡ENCENDEMOS EL MOTOR CENTRAL!: Le entregamos los bots activos al servidor TCP
            _plugin.ControlServer.IniciarEntrenamiento(_agentManager);
        }

        private void OnEndingRound(EndingRoundEventArgs ev)
        {
            // Bloqueamos el fin de ronda del juego nativo. La ronda solo acaba si Python o nosotros lo decidimos.
            if (!_roundEnding)
            {
                ev.IsAllowed = false;
                
            }
        }

        private void OnDied(DiedEventArgs ev)
        {
            // Si muere cualquier bot de entrenamiento, el episodio actual fracasa inmediatamente
            if (ev.Player == null || _roundEnding) return;

            // Verificamos si el jugador fallecido es parte de nuestra red de IA
            if (_agentManager.GetAll().ContainsKey(ev.Player.Id) || ev.Player.Nickname.StartsWith("IA_Agent_"))
            {
                //Log.Warn($"[RoundManager] El Agente {ev.Player.Nickname} ha muerto. Reiniciando episodio...");
                //ForzarReinicioEpisodio();
            }
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            
            ForzarReinicioEpisodio();
        }

        public void ForzarReinicioEpisodio()
        {
            if (_roundEnding) return;
            _roundEnding = true;
            

            // Apagamos el bucle central de control TCP inmediatamente para evitar lecturas fantasmas
            _plugin.ControlServer.DetenerEntrenamiento();
            System.GC.Collect();
            Log.Debug("[RoundManager] Solicitando restablecimiento completo del mapa a EXILED...");
            Round.Restart(false, true);
        }

        private IEnumerator<float> GlobalTcpMonitor()
        {
            while (true)
            {
                // Si desde Python se inyecta una petición volátil de reinicio por red
                if (ScpRLBridge.RestartRequested) 
                {
                    ScpRLBridge.RestartRequested = false;
                    _firstSpawn = true;
                    System.GC.Collect();
                    Log.Warn("[RoundManager] Petición de reinicio externa recibida.");
                    ForzarReinicioEpisodio();
                }
                yield return Timing.WaitForOneFrame;
            }
        }
    }
}