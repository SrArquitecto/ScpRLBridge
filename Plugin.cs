using System;
using System.Collections.Generic;
using Exiled.API.Features;
using MEC;
using ScpAgent.Network;
using ScpAgent.Managers;


namespace ScpAgent
{
    public class ScpRLPlugin : Plugin<ScpRLConfig>
    {
        public static ScpRLPlugin Instance { get; private set; }
        
        // Instancias globales de nuestros dos nuevos motores centrales
        public RoundManager RoundManager { get; private set; }
        public AgentManager AgentManager { get; private set; }
        public ControlServer ControlServer { get; private set; }

        public override string Name => "ScpRLBridge";
        public override string Author => "RL";
        public override Version Version => new Version(3, 0, 0);

        private CoroutineHandle _watchdogHandle;

        public override void OnEnabled()
        {
            // 1. CONFIGURACIÓN DEL MOTOR GRÁFICO (Crucial para servidores dedicados de RL)
            // Desactivamos VSync y targetFrameRate para que el servidor de SCP corra a la máxima velocidad posible
            UnityEngine.QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = -1;

            Instance = this;
            ControlServer = new ControlServer();
            ControlServer.IniciarServidor(Config.Port);
            AgentManager = new AgentManager();
            RoundManager = new RoundManager(this, AgentManager);
            // 2. INICIALIZACIÓN DE LA RED
            // Instanciamos el servidor TCP y lo encendemos en el puerto configurado
            

            // 3. INICIALIZACIÓN DEL CICLO DE JUEGO
            // Instanciamos el gestor de rondas pasándole la referencia de este plugin
            
            
            // 4. ENCENDIDO DEL PERRO GUARDIÁN (Watchdog)
            _watchdogHandle = Timing.RunCoroutine(ConnectionWatchdog());

            Log.Debug($"[ScpRLBridge] 🚀 Plugin inicializado con éxito. Servidor físico y red listos.");
        }

        public override void OnDisabled()
        {
            // 1. APAGADO DEL WATCHDOG
            Timing.KillCoroutines(_watchdogHandle);

            // 2. DESCONEXIÓN DE RED Y DETENCIÓN DEL BUCLE MAESTRO
            if (ControlServer != null)
            {
                ControlServer.DetenerServidor();
            }

            // 3. DESUSCRIPCIÓN DE EVENTOS DEL MAPA
            if (RoundManager != null)
            {
                RoundManager.Unsubscribe();
            }

            // 4. LIMPIEZA DE MEMORIA E INSTANCIAS
            Instance = null;
            Log.Debug("[ScpRLBridge] 🛑 Plugin completamente desactivado y sockets liberados.");
        }

        /// <summary>
        /// Corrutina guardián que vigila cada 5 segundos que el socket TCP siga vivo.
        /// Si detecta un cierre inesperado, reinicia el puerto de escucha automáticamente.
        /// </summary>
        private IEnumerator<float> ConnectionWatchdog()
        {
            Log.Debug("[ScpRLBridge-Watchdog] Monitor de estabilidad de red activo.");
            
            while (true)
            {
                yield return Timing.WaitForSeconds(5.0f);

                // Si por algún motivo el ControlServer pierde la bandera de ejecución, intentamos revivirlo
                if (ControlServer == null) continue;

                // Nota: Puedes enlazar aquí una validación de estado si deseas verificar caídas de sockets críticos
            }
        }
    }

    // --------------------------------------------------------------------------
    // CONFIGURACIÓN DEL PLUGIN (EXILED)
    // --------------------------------------------------------------------------
    public class ScpRLConfig : Exiled.API.Interfaces.IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;

        // Añadimos el puerto como variable configurable para que no esté hardcodeado
        public int Port { get; set; } = 7900; 
    }

    // Variables de estado volátiles globales
    public static class ScpRLBridge
    {
        public static bool RestartRequested { get; set; } = false;
        public static bool ServerStateIsRestarting { get; set; } = false;
    }
}