using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exiled.API.Features;
using Newtonsoft.Json;
using ScpAgent.Managers;
using ScpAgent.Bot.Data;
using MEC;
using ScpAgent.Bot.Interfaces;
using UnityEngine;
using ScpAgent.Bot.Sensors;
using ScpAgent.Network.Event;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Strategies;
using ScpAgent.Bot;

namespace ScpAgent.Network
{
    public class ControlServer
    {
        private float _frameTimeAccum = 0f;
        private int _frameTimeCount = 0;
        private TcpListener _listener;
        private int _lastGen2 = 0;
        private bool _isRunning;
        public int NumAgentsExpected = 4;
        private CoroutineHandle _masterLoopHandle;
        private bool _isTrainingActive = false;
        private const float FIXED_DELTA = 0.02f;

        // Diccionario de Sockets y Escritores asíncronos
        private readonly ConcurrentDictionary<int, TcpClient> _clientes = new ConcurrentDictionary<int, TcpClient>();
        private readonly ConcurrentDictionary<int, StreamWriter> _writers = new ConcurrentDictionary<int, StreamWriter>();
        
        // Colas seguras (Thread-Safe) para pasar datos de los hilos de red al hilo de Unity
        private readonly ConcurrentDictionary<int, ConcurrentQueue<string>> _incoming = new ConcurrentDictionary<int, ConcurrentQueue<string>>();

        private readonly ConcurrentDictionary<int, ConcurrentQueue<string>> _outgoing = new ConcurrentDictionary<int, ConcurrentQueue<string>>();
        private CancellationTokenSource _serverCts = new CancellationTokenSource();
        public bool Initialized { get; private set; } = false;
        public bool IsPythonConnected { get; private set; } = false;
        public event Action AllAgentsReady;
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _outgoingSignals = new ConcurrentDictionary<int, SemaphoreSlim>();
        public static event System.EventHandler<AgentHandshakeEventArgs> AgentHandshakeReceived;
        public int _frameCount = 0;
        public ControlServer()
        {
            this.Initialized = false;
        }

        // --------------------------------------------------------------------------
        // FASE 0: INICIALIZACIÓN Y CONEXIONES (SIN LÍMITE DE 4 BOTS)
        // --------------------------------------------------------------------------
        public void IniciarServidor(int puerto, int numAgents = 4)
        {
            _serverCts = new CancellationTokenSource();
            //_numAgentsExpected = numAgents;
            _isRunning = true;
            _listener = new TcpListener(IPAddress.Any, puerto);
            _listener.Start();

            // Listener asíncrono inmortal — nunca bloquea Unity
            Task.Run(AceptarConexionesAsync);

            Log.Debug($"[ControlServer] Escuchando en puerto {puerto}. " +
                     $"Esperando {NumAgentsExpected} agentes.");
        }

        public void IniciarEntrenamiento()
        {
            if (_isTrainingActive) return;
            _isTrainingActive = true;
            _masterLoopHandle = Timing.RunCoroutine(_BucleMaestroCentral());
            Log.Debug("[ControlServer] 🚀 Bucle Maestro Central iniciado.");
        }

        public void DetenerEntrenamiento2()
        {
            Log.Debug("[ControlServer] Deteniendo entrenamiento. Forzando cierre de sockets viejos...");

            // Al limpiar los diccionarios, rompemos el bucle 'while (_clientes.ContainsKey(agentId))'
            // Pero para los hilos que ya están congelados en el 'await', cerramos sus conexiones físicamente:
            foreach (var par in _clientes)
            {
                try
                {
                    // Forzar el cierre del Socket. Esto hace que 'ReadLineAsync()' despierte con una excepción de cierre.
                    par.Value?.GetStream()?.Close();
                    par.Value?.Close();
                    par.Value?.Dispose();
                }
                catch { /* Ignorar errores al cerrar flujos ya rotos */ }
            }

            foreach (var writer in _writers.Values)
            {
                try { writer?.Close(); writer?.Dispose(); } catch {}
            }

            // Vaciamos por completo para asegurar que el próximo episodio empiece de cero
            _clientes.Clear();
            _writers.Clear();
            _incoming.Clear();
            _outgoing.Clear();
            
            //IsPythonConnected = false;
            _isTrainingActive = false;
            Timing.KillCoroutines(_masterLoopHandle);
            Log.Debug("[ControlServer] 🛑 Bucle Maestro Central detenido.");
        }
            
        public void DetenerEntrenamiento()
        {
            Log.Debug("[ControlServer] Limpiando colas de mensajes para la nueva ronda (Preservando Sockets)...");
            
            // NO cerramos _clientes ni _writers si Python va a seguir mandando datos.
            // Solo purgamos los mensajes acumulados de la ronda anterior para que no haya lag.
            foreach (var agentId in _incoming.Keys)
            {
                if (_incoming.TryGetValue(agentId, out var qIn)) while (qIn.TryDequeue(out _)) { }
                if (_outgoing.TryGetValue(agentId, out var qOut)) while (qOut.TryDequeue(out _)) { }
            }
            //_clientes.Clear();
            //_writers.Clear();
            //_incoming.Clear();
            //_outgoing.Clear();
            
            //IsPythonConnected = false;
            _isTrainingActive = false;
            Timing.KillCoroutines(_masterLoopHandle);
            Log.Debug("[ControlServer] 🛑 Bucle Maestro Central detenido.");
        }

        public void DetenerServidor()
        {
            _isRunning = false;
            _isTrainingActive = false;
            Initialized = false;

            foreach (var c in _clientes.Values) c?.Close();
            _clientes.Clear();
            _writers.Clear();
            _incoming.Clear();
            _outgoing.Clear();

            _listener?.Stop();
            Log.Debug("[ControlServer] Servidor detenido y sockets cerrados.");
        }

        // --------------------------------------------------------------------------
        // EL NUEVO LISTENER INMORTAL Y ASÍNCRONO
        // --------------------------------------------------------------------------
        private async Task AceptarConexionesAsync()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient cliente = await _listener.AcceptTcpClientAsync();
                    cliente.NoDelay = true; // Sin Nagle — crítico para baja latencia RL
                    _ = ManejarClienteAsync(cliente);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        Log.Error($"[ControlServer] Error aceptando conexión: {ex.Message}");
                }
            }
        }

        private async Task ManejarClienteAsync(TcpClient cliente)
        {
            // NO uses usings globales aquí arriba para no asfixiar el bucle largo de los agentes
            var stream = cliente.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            try
            {
                string handshake = (await reader.ReadLineAsync())?.Replace("\0", "").Trim();
                if (string.IsNullOrEmpty(handshake)) 
                { 
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close(); 
                    return; 
                }

                string clientIp = cliente.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                Log.Debug($"[ControlServer] Mensaje recibido. Longitud: {handshake.Length}. Contenido: '{handshake}'");

                // ── COMANDOS GLOBALES (Cortos: se destruyen AQUÍ con limpieza manual instantánea) ──
                if (handshake == "RESTART")
                {
                    Log.Warn("[ControlServer] RESTART recibido.");
                    ScpRLBridge.RestartRequested = true;
                    
                    byte[] ack = Encoding.UTF8.GetBytes("OK\n");
                    await stream.WriteAsync(ack, 0, ack.Length);
                    await stream.FlushAsync();
                    
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close();
                    return;
                }
                if (handshake == "C1") 
                { 
                    RoundManager.IsC1Enabled = true; 
                    Log.Debug("[ControlServer] Currículum 1 Activado.");
                    
                    byte[] ack = Encoding.UTF8.GetBytes("OK\n");
                    await stream.WriteAsync(ack, 0, ack.Length);
                    await stream.FlushAsync();
                    
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close(); 
                    return; 
                }
                if (handshake == "C2") 
                { 
                    RoundManager.IsC1Enabled = false; 
                    Log.Debug("[ControlServer] Currículum 2 Activado.");
                    
                    byte[] ack = Encoding.UTF8.GetBytes("OK\n");
                    await stream.WriteAsync(ack, 0, ack.Length);
                    await stream.FlushAsync();
                    
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close(); 
                    return; 
                }
                    // Comando de curriculum: "CONFIG:clave=valor"
                if (handshake.StartsWith("CONFIG:"))
                {
                    string payload = handshake.Substring(7); // "clave=valor"
                    int sep = payload.IndexOf('=');
                    if (sep > 0)
                    {
                        string clave = payload.Substring(0, sep).Trim();
                        string valor = payload.Substring(sep + 1).Trim();
                        ScpAgent.Curriculum.CurriculumConfig.AplicarConfig(clave, valor);
                        await writer.WriteLineAsync("OK");
                    }
                    else
                    {
                        await writer.WriteLineAsync("ERROR:formato_invalido");
                    }
                    cliente.Close();
                    return;
                }
            
                // Consultar configuración actual: "GET_CONFIG"
                if (handshake == "GET_CONFIG")
                {
                    await writer.WriteLineAsync(ScpAgent.Curriculum.CurriculumConfig.ToJson());
                    cliente.Close();
                    return;
                }

                // ── HANDSHAKE "HELLO_" (Corto) ──────────────────────────────────────────────────
                if (handshake.StartsWith("HELLO_") && int.TryParse(handshake.Substring(6), out int numAgents))
                {
                    NumAgentsExpected = numAgents;
                    
                    Log.Debug($"[ControlServer] Handshake recibido. Preparando respuesta...");
                        
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("CONNECTED\n");
                    try {
                        await stream.WriteAsync(buffer, 0, buffer.Length);
                        await stream.FlushAsync();
                        Log.Debug($"[ControlServer] Respuesta HELLO enviada exitosamente.");
                    } catch (Exception ex) {
                        Log.Error($"[ControlServer] Error enviando respuesta HELLO: {ex.Message}");
                    }
                    
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close();
                    return;
                }
                else if (handshake.StartsWith("INIT_"))
                {
                    string[] partes = handshake.Split('_');

                    if (partes.Length == 3 && int.TryParse(partes[1], out int agentId))
                    {
                        string nombreRol = partes[2].Trim();
                        if (_clientes.ContainsKey(agentId)) 
                        {
                            Log.Warn($"[ControlServer] Agente {agentId} ya registrado. Rechazando.");
                            cliente.Close();
                            return;
                        }

                        // 1. ENVIAR RESPUESTA MANDATORIA SÍNCRONA
                        try 
                        {
                            // En lugar de usar cliente.Client.Send, usamos el escritor que ya tenemos arriba de forma limpia
                            await writer.WriteLineAsync("REGISTERED");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ControlServer] Fallo crítico enviando REGISTERED al agente {agentId}: {ex.Message}");
                            cliente.Close();
                            return;
                        }

                        // 2. REGISTRAR ESTRUCTURAS REUTILIZANDO EL WRITER EXISTENTE
                        _clientes[agentId]  = cliente;
                        _incoming[agentId]  = new ConcurrentQueue<string>();
                        _outgoing[agentId]  = new ConcurrentQueue<string>();
                        _outgoingSignals[agentId] = new SemaphoreSlim(0);
                        
                        // 🌟 REUTILIZACIÓN CRÍTICA: Guardamos el escritor original. Ya no fallará con "Stream was not writable"
                        _writers[agentId]   = writer; 

                            // Disparamos de forma segura (si hay alguien suscrito)
                        AgentHandshakeReceived?.Invoke(this, new AgentHandshakeEventArgs(agentId, nombreRol));

                        

                        Log.Debug($"[ControlServer] Agente {agentId} registrado correctamente y listo.");

                        if (AgentManager.Instance.GetLength() >= NumAgentsExpected && !Initialized)
                        {
                            Initialized = true;
                            Log.Debug("[ControlServer] ✅ Todos los agentes conectados de forma segura.");
                            IsPythonConnected = true;
                            MEC.Timing.RunCoroutine(_DispararEventoCreacion());
                        }
                        // 3. ENTRAR EN LOS BUCLES ASÍNCRONOS
                        // 🌟 REUTILIZACIÓN CRÍTICA: Pasamos el 'reader' original para NO perder los datos del buffer
                        await Task.WhenAll(
                            LeerAsync(agentId, reader), 
                            EscribirAsync(agentId)
                        );
                    }
                }
                // ── CONEXIÓN PERSISTENTE DE AGENTE "INIT_" (Larga: delega su ciclo de vida) ─────
                else
                {
                    Log.Warn($"[ControlServer] Handshake desconocido: '{handshake}'");
                    reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ControlServer] Error manejando cliente: {ex.Message}");
                reader.Dispose(); writer.Dispose(); stream.Dispose(); cliente.Close();
            }
        }

        private IEnumerator<float> _DispararEventoCreacion()
        {
            // 1. Le decimos a Unity: "Espera al siguiente frame/tick oficial del servidor"
            yield return MEC.Timing.WaitForOneFrame;
            // 2. Disparamos tu evento inyectando el numero de instancias 
            Log.Debug("[ControlServer] 🚀 Disparando AllAgentsReady.");
            AllAgentsReady?.Invoke();
        }

        private async Task LeerAsync(int agentId, StreamReader reader)
        {
            try
            {
                // Volvemos a tu bucle original, ya que no acepta el token directamente
                while (_isRunning && _clientes.ContainsKey(agentId))
                {
                    string linea = await reader.ReadLineAsync();

                    if (linea == null)
                    {
                        Log.Debug($"[ControlServer] Agente {agentId} desconectado (EOF).");
                        break;
                    }

                    linea = linea.Trim();
                    Log.Debug($"[ControlServer] Agente {agentId} recibió: '{linea}'");
                    if (!string.IsNullOrEmpty(linea))
                    {
                        if (_incoming.TryGetValue(agentId, out var queue))
                        {
                            queue.Enqueue(linea);
                        }
                    }
                }
            }
            catch (System.IO.IOException)
            {
                Log.Debug($"[ControlServer] Conexión del Agente {agentId} cerrada por el servidor (Fin de episodio).");
            }
            catch (ObjectDisposedException)
            {
                Log.Debug($"[ControlServer] Stream del Agente {agentId} destruido limpiamente.");
            }
            catch (Exception ex)
            {
                Log.Warn($"[ControlServer] Lector Agente {agentId}: {ex.Message}");
            }
            finally
            {
                // Tu lógica exacta de limpieza que libera la RAM y las colas
                if (_clientes.TryRemove(agentId, out var c)) { c?.Close(); c?.Dispose(); }
                if (_writers.TryRemove(agentId, out var w)) { w?.Close(); w?.Dispose(); }
                
                if (_incoming.TryRemove(agentId, out var qIn)) { while (qIn.TryDequeue(out _)) { } }
                if (_outgoing.TryRemove(agentId, out var qOut)) { while (qOut.TryDequeue(out _)) { } }
                
                if (_clientes.Count == 0) 
                {
                    IsPythonConnected = false;
                }

                if (_outgoingSignals.TryGetValue(agentId, out var sem))
                    sem.Release();
            }
        }
        /// <summary>
        /// Loop de escritura por agente — drena _outgoing y envía a Python.
        /// Corre en un hilo de red, nunca toca Unity.
                /// </summary>
        private async Task EscribirAsync(int agentId)
        {
            if (!_outgoingSignals.TryGetValue(agentId, out var signal)) return;

            try
            {
                while (_isRunning && _clientes.ContainsKey(agentId))
                {
                    // Espera eficiente hasta que haya algo — sin crear objetos
                    await signal.WaitAsync();

                    if (_outgoing.TryGetValue(agentId, out var cola) &&
                        cola.TryDequeue(out string msg) &&
                        _writers.TryGetValue(agentId, out var w))
                    {
                        await w.WriteLineAsync(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[ControlServer] Escritor Agente {agentId}: {ex.Message}");
            }
            finally
            {
                _outgoingSignals.TryRemove(agentId, out _);
            }
        }




        // --------------------------------------------------------------------------
        // BUCLE MAESTRO DE FÍSICA Y SINCRONIZACIÓN (El Hilo de Unity)
        // --------------------------------------------------------------------------
        private IEnumerator<float> _BucleMaestroCentral()
        {
            while (_isTrainingActive)
            {
                yield return Timing.WaitForOneFrame;

                float frameStart = UnityEngine.Time.realtimeSinceStartup;
                float deltaTime  = UnityEngine.Time.deltaTime;
                _frameCount++;
                // ── Métricas de perf ──────────────────────────────────────────
                if (++_frameCount % 1000 == 0)
                {
                    Log.Info($"[Perf] Mem={System.GC.GetTotalMemory(false)/1024/1024}MB " +
                            $"Gen2={System.GC.CollectionCount(2)} FPS={1f/deltaTime:F1}");
                    float fps = 1f / UnityEngine.Time.deltaTime;
                    float unscaledFps = 1f / UnityEngine.Time.unscaledDeltaTime;
                    Log.Info($"[Perf] FPS={fps:F1} UnscaledFPS={unscaledFps:F1} " +
                            $"FixedDelta={UnityEngine.Time.fixedDeltaTime*1000f:F1}ms");
                
                    Log.Info($"[Perf] Mirror connections: {Mirror.NetworkServer.connections.Count}");
                    Log.Info($"[Perf] EventosVivis: {BotEvents.TotalEventosSuscritos + BaseStrategy.TotalEventosSuscritos}");
                    Log.Info($"[Perf] Total Jugadores: {ReferenceHub.AllHubs.Count - 1}");
                    //_frameCount = 0;
                }

                // ── Procesar mensajes ─────────────────────────────────────────
                AgentManager.Instance.ForEachListo((agentId, bot, sensors) =>
                {
                    if (!_incoming.TryGetValue(agentId, out var colaIn) ||
                        !colaIn.TryDequeue(out string msg))
                        return; // lambda equivale a continue
                    try
                    {

                        _ProcesarMensaje(bot, msg, agentId, deltaTime);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[ControlServer] Error Agente {agentId} ('{msg}'): {ex.Message}");
                    }
                });

                // ── Medir tiempo del bucle ────────────────────────────────────
                _frameTimeAccum += UnityEngine.Time.realtimeSinceStartup - frameStart;
                if (++_frameTimeCount >= 100)
                {
                    Log.Debug($"[Perf] BucleMaestro: {_frameTimeAccum / _frameTimeCount * 1000f:F2}ms");
                    _frameTimeAccum = 0f;
                    _frameTimeCount = 0;
                }
            }
        }


        private void _ProcesarMensaje(IAgentController bot, string msg, int agentId, float deltaTime)
        {
            try {
                try
                {
                    if (bot._exiledPlayer == null || 
                        !bot._exiledPlayer.IsAlive || 
                        bot._exiledPlayer.GameObject == null)
                    {
                        _EnviarObservacionVacia(agentId, bot._exiledPlayer.Role.Type);
                        return;
                    }
                }
                catch
                {
                    _EnviarObservacionVacia(agentId, bot._exiledPlayer.Role.Type);
                    return;
                }
                if (msg == "RESPAWN")
                {
                    bot.EjecutarRespawn();
                    // Responder con estado vacío mientras el respawn se completa
                    _EnviarObservacionVacia(agentId, bot._exiledPlayer.Role.Type);
                    return;
                }

                if (msg.StartsWith("ACTION:"))
                {
                    if (int.TryParse(msg.Substring(7), out int actionId))
                        bot.ReceiveAction(new AgentAction { ActionId = actionId });

                    bot.ActualizarFisica(deltaTime);
                }
                else if (msg == "GET_STATE")
                {
                    // NOOP — solo devolver estado actual sin mover
                    //Log.Info("GET_STATE");
                }
                else
                {
                    Log.Debug($"[ControlServer] Mensaje desconocido de Agente {agentId}: '{msg}'");
                    return;
                }

                _EnviarObservacion(bot, agentId, deltaTime);
            }
            catch (Exception ex)
            {
                Log.Warn($"[ControlServer] Error Agente {agentId} ('{msg}'): {ex.Message}");
                Log.Warn($"[ControlServer] StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Serializa la observación del bot y la encola para envío asíncrono.
        /// </summary>
        private void _EnviarObservacion(IAgentController bot, int agentId, float deltaTime)
        {
            AgentObservation obs = bot.GetObservation(deltaTime);
            string json = JsonUtils.ToJson(obs, bot._exiledPlayer.Role.Type);
            if (_frameCount % 500 == 0)
                Log.Info($"[Perf] JSON size Agente {agentId}: {json.Length} chars");
            
            if (json.Contains(",]"))
                Log.Warn("JSON INVALIDO DETECTADO: " + json);

            if (_outgoing.TryGetValue(agentId, out var colaOut))
            {
                colaOut.Enqueue(json);

                // Señalizar al escritor que hay un mensaje nuevo
                if (_outgoingSignals.TryGetValue(agentId, out var sem))
                    sem.Release();
            }
        }

        private void _EnviarObservacionVacia(int agentId, RoleTypeId role)
        {
            AgentObservation obs = BaseSensors.obsVacia;

            string json = JsonUtils.ToJson(obs, role);

            if (_outgoing.TryGetValue(agentId, out var colaOut))
            {
                colaOut.Enqueue(json);

                // Señalizar al escritor que hay un mensaje nuevo
                if (_outgoingSignals.TryGetValue(agentId, out var sem))
                    sem.Release();
            }
        }

    }
}