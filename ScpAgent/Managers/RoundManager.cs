using System;
using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Server;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;
using Exiled.API.Enums;

namespace ScpAgent.Managers
{
    public class RoundManager
    {
        private readonly ScpRLPlugin _plugin;
        private int _episode = 0;
        private bool _roundEnding = false;
        private bool _isSpawning = false;
        public static volatile bool IsC1Enabled = false;
        public bool BotsListos { get; private set; } = false;
        private CoroutineHandle _monitorHandle;
        private bool _firstInstance = true;
        private CoroutineHandle _respawnTimerHandle;
        private const float RESPAWN_ADVANCE_PER_TICK = 20f;

        public RoundManager(ScpRLPlugin plugin)
        {
            _plugin = plugin;
            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
            Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
            Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
            Exiled.Events.Handlers.Player.Died += OnDied;
            Exiled.Events.Handlers.Server.EndingRound += OnEndingRound;
            _plugin.ControlServer.AllAgentsReady += OnAllAgentReady;
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
            Timing.KillCoroutines(_respawnTimerHandle);
        }

        private void OnWaitingForPlayers()
        {
            if (_plugin.ControlServer.IsPythonConnected && !_firstInstance)
            {
                if (_isSpawning) return;
                _isSpawning = true;
                _roundEnding = false;
                
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
            _firstInstance = false;
            Timing.RunCoroutine(DelayedSpawnSequence());
        }

        private IEnumerator<float> DelayedSpawnSequence()
        {
            yield return Timing.WaitForSeconds(3.0f);

            while (_plugin.AgentManager.GetLength() < _plugin.ControlServer.NumAgentsExpected)
            {
                Log.Info("[RoundManager] Esperando al resto de agentes...");
                yield return Timing.WaitForSeconds(0.1f);
            }

            Log.Info("[RoundManager] Forzando inicio de ronda antes de spawnear agentes...");
            CharacterClassManager.ForceRoundStart();
            yield return Timing.WaitForSeconds(0.5f);

            Log.Info("[RoundManager] Red de simulación detectada. Spawneando agentes...");
            _plugin.AgentManager.SpawnAll();
            Log.Debug("[RoundManager] Agentes spawneados.");

            for (int id = 0; id < 4; id++)
            {
                yield return Timing.WaitForSeconds(0.2f);
            }

            yield return Timing.WaitForSeconds(1.0f);

            Log.Debug("[RoundManager] Todo listo. Ronda y agentes en posición.");
            _isSpawning = false;
        }

                private IEnumerator<float> RespawnTimerLoop()
        {
            while (!_roundEnding)
            {
                try
                {
                    // Avanzamos el timer de ambas facciones. El juego dispara la oleada
                    // cuando el timer supera el intervalo configurado internamente.
                    Exiled.API.Features.Respawn.AdvanceTimer(Faction.FoundationStaff, RESPAWN_ADVANCE_PER_TICK);
                    Exiled.API.Features.Respawn.AdvanceTimer(Faction.FoundationEnemy, RESPAWN_ADVANCE_PER_TICK);
                }
                catch (Exception ex)
                {
                    Log.Debug($"[RoundManager] RespawnTimer: {ex.Message}");
                }
                yield return Timing.WaitForSeconds(1.0f);
            }
        }


        private void OnRoundStarted()
        {
            _episode++;
            _roundEnding = false;
            MapUtils.ClearBoundsCache();
            ScpAgent.Bot.Sensors.Modules.DoorsModule.ClearGlobalCache();
            Log.Debug($"[RoundManager] === Iniciando Episodio {_episode} ===");

            _plugin.ControlServer.IniciarEntrenamiento();
            
            if (_respawnTimerHandle.IsRunning)
                Timing.KillCoroutines(_respawnTimerHandle);
            _respawnTimerHandle = Timing.RunCoroutine(RespawnTimerLoop());

        }

        private void OnEndingRound(EndingRoundEventArgs ev)
        {
            if (!_roundEnding)
            {
                ev.IsAllowed = false;
            }
        }

        private void OnDied(DiedEventArgs ev)
        {
            if (ev.Player == null || _roundEnding) return;
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            ForzarReinicioEpisodio();
        }

        public void ForzarReinicioEpisodio()
        {
            if (_roundEnding) return;
            _roundEnding = true;

            _plugin.ControlServer.DetenerEntrenamiento();
            _plugin.AgentManager.ResetearTodos();
            Log.Debug("[RoundManager] Solicitando restablecimiento completo del mapa a EXILED...");
            Round.Restart(false, true);
        }

        private IEnumerator<float> GlobalTcpMonitor()
        {
            while (true)
            {
                if (ScpRLBridge.RestartRequested)
                {
                    ScpRLBridge.RestartRequested = false;
                    System.GC.Collect();
                    Log.Warn("[RoundManager] Petición de reinicio externa recibida.");
                    ForzarReinicioEpisodio();
                }
                yield return Timing.WaitForOneFrame;
            }
        }
    }
}
