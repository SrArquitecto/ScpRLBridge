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

        public RoundManager(ScpRLPlugin plugin)
        {
            _plugin = plugin;
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
        }

        /// <summary>
        /// Crea e inyecta la instancia del bot en el servidor utilizando su constructor de red interno.
        /// </summary>
        /// 
        private void SpawnearAgentes()
        {
            if (_plugin.AgentManager.NumAgentes == 0 && _firstSpawn == true) {
                _plugin.AgentManager.Inicializar(_plugin.ControlServer.NumAgentsExpected);
                _firstSpawn = false;
                //Log.Debug("Inicializado por primera vez --------------------------------------------------------------------------------");
            }
            else
            {
                if (_plugin.AgentManager.NumAgentes > 0 && _firstSpawn == true)
                {
                    _plugin.AgentManager.Reinicializar();
                    _firstSpawn = false;
                    Log.Debug("Inicializado -------------------------------------------------------------------------------------------------");
                }

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
            
            if(_plugin.AgentManager.NumAgentes > 0)
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

            SpawnearAgentes();
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
            _episode++;
            _roundEnding = false;
            Log.Debug($"[RoundManager] 🏁 === Iniciando Episodio {_episode} ===");

            // 2 ¡ENCENDEMOS EL MOTOR CENTRAL!: Le entregamos los bots activos al servidor TCP
            _plugin.ControlServer.IniciarEntrenamiento();
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
            //if (_agentManager.GetAll().ContainsKey(ev.Player.Id) || ev.Player.Nickname.StartsWith("IA_Agent_"))
            //{
                //Log.Warn($"[RoundManager] El Agente {ev.Player.Nickname} ha muerto. Reiniciando episodio...");
                //ForzarReinicioEpisodio();
            //}
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