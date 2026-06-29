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
using ScpAgent.Bot.Sensors.Intefaces;

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
        private Action<int, IAgentController, ISensors> _procesarDelegate;
        private float _pendingDelta;

        // ── Multi-agent mode (PettingZoo) ──────────────────────────────────
        // Una sola conexión TCP que multiplexa N agentes vía dicts JSON.
        // Coexiste con el modo single-agent: _isMultiMode decide qué camino usar.
        private bool _isMultiMode = false;
        private int _multiNumAgents = 0;
        private TcpClient _multiClient;
        private StreamWriter _multiWriter;
        private readonly SemaphoreSlim _multiActionSignal = new SemaphoreSlim(0);
        private readonly ConcurrentDictionary<int, bool> _multiActionReceived = new ConcurrentDictionary<int, bool>();
        // Última observación serializada por agente (la usa el bucle maestro para
        // construir el dict combinado en multi-modo).
        private readonly ConcurrentDictionary<int, string> _latestObservations = new ConcurrentDictionary<int, string>();

        public ControlServer()
        {
            this.Initialized = false;
            _procesarDelegate = _ProcesarAgenteEnTick;
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
            Log.Debug("[ControlServer] Deteniendo entrenamiento. Cerrando sockets de agentes (listener activo para reconexión)...");

            // Cerramos TODOS los sockets de agentes para forzar a Python a detectar la desconexión.
            // El listener queda vivo para que Python pueda hacer RECONNECT_ cuando la ronda arranque.
            // IsPythonConnected se mantiene true para que OnWaitingForPlayers siga disparando
            // SpawnearAgentes() entre rondas (si no, el servidor se queda idle 30s).
            foreach (var par in _clientes)
            {
                try
                {
                    par.Value?.GetStream()?.Close();
                    par.Value?.Close();
                    par.Value?.Dispose();
                }
                catch { /* Ignorar errores al cerrar flujos ya rotos */ }
            }
            foreach (var writer in _writers.Values)
            {
                try { writer?.Close(); writer?.Dispose(); } catch { }
            }

            _clientes.Clear();
            _writers.Clear();
            _incoming.Clear();
            _outgoing.Clear();
            // NOTA: NO limpiamos _outgoingSignals aquí para que los hilos EscribirAsync antiguos
            // puedan despertar vía LeerAsync->finally->sem.Release() y terminar limpiamente.
            // NOTA: NO ponemos IsPythonConnected = false (ver LeerAsync finally).

            _isTrainingActive = false;
            Timing.KillCoroutines(_masterLoopHandle);
            Log.Debug("[ControlServer] 🛑 Bucle Maestro Central detenido. Aguardando reconexiones...");
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
            // IMPORTANTE: new UTF8Encoding(false) para NO emitir BOM en la primera escritura.
            // Si usamos Encoding.UTF8, el primer WriteLineAsync envía \uFEFF antes del texto,
            // lo que rompe a clientes Python que hacen `resp == "WAIT"`.
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

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
                // ── HANDSHAKE "MULTI_INIT_<N>" (PettingZoo) ────────────────────────
                // Una sola conexión TCP que multiplexa N agentes vía dicts JSON.
                // Formato de acción: {"agent_0": 5, "agent_1": 12, "agent_2": 0, "agent_3": 7}
                // Formato de respuesta: {"agent_0": "<obs_json>", "agent_1": "...", ...}
                else if (handshake.StartsWith("MULTI_INIT_"))
                {
                    string[] partes = handshake.Split('_');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int multiN))
                    {
                        // Solo un multi-cliente a la vez
                        if (_isMultiMode || _clientes.Count > 0)
                        {
                            try
                            {
                                await writer.WriteLineAsync("DUPLICATE");
                                await writer.FlushAsync();
                            }
                            catch { }
                            cliente.Close();
                            return;
                        }

                        // Responder REGISTERED antes de empezar a leer
                        try
                        {
                            await writer.WriteLineAsync("MULTI_REGISTERED");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ControlServer] Fallo enviando MULTI_REGISTERED: {ex.Message}");
                            cliente.Close();
                            return;
                        }

                        // Activar multi-modo
                        _isMultiMode = true;
                        _multiNumAgents = multiN;
                        _multiClient = cliente;
                        _multiWriter = writer;

                        // Pre-crear las colas de incoming/outgoing para los N agentes
                        for (int i = 0; i < multiN; i++)
                        {
                            _incoming[i]  = new ConcurrentQueue<string>();
                            _outgoing[i]  = new ConcurrentQueue<string>();
                            _outgoingSignals[i] = new SemaphoreSlim(0);
                            _multiActionReceived[i] = false;
                        }

                        // CRÍTICO: en single-mode `INIT_<id>_<rol>` crea el AgentSlot
                        // vía el evento AgentHandshakeReceived. En multi-mode no hay INIT_,
                        // así que tenemos que llamar a InstaciarSlot directamente. Si no,
                        // AgentManager._pool queda null y DelayedSpawnSequence se queda
                        // eternamente en "Esperando al resto de agentes..." porque
                        // GetLength() < NumAgentsExpected para siempre.
                        for (int i = 0; i < multiN; i++)
                        {
                            try
                            {
                                ScpAgent.Managers.AgentManager.Instance.InstanciarSlot(i, "classd");
                                Log.Info($"[ControlServer] Slot {i} creado para multi-modo.");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[ControlServer] Error creando slot {i}: {ex.Message}");
                            }
                        }

                        Log.Info($"[ControlServer] 🤝 Multi-agent mode: {multiN} agentes vía 1 conexión TCP.");

                        // CRÍTICO: en single-mode el spawn se dispara cuando los 4 INIT_ se
                        // completan (vía _DispararEventoCreacion → OnAllAgentReady →
                        // DelayedSpawnSequence → SpawnearAgentes). En multi-mode
                        // el handshake es el equivalente a "los 4 agentes conectados",
                        // así que disparamos el spawn directamente. Si no, el master loop
                        // procesa acciones sobre agentes no spawneados → deadlock (el
                        // Python espera una obs que nunca llega porque no hay agentes).
                        NumAgentsExpected = multiN;
                        IsPythonConnected = true;
                        MEC.Timing.RunCoroutine(_DispararEventoCreacion());

                        // Lanzar el handler multi (lee dicts y routea a _incoming)
                        _ = HandleMultiAgentAsync(cliente, reader, multiN);
                        return;
                    }
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
                // ── RECONNECT_id (exclusivo para reconexión entre rondas) ──────────────────
                else if (handshake.StartsWith("RECONNECT_"))
                {
                    string[] partes = handshake.Split('_');
                    if (partes.Length == 2 && int.TryParse(partes[1], out int agentId))
                    {
                        // Solo aceptamos reconexión si el bucle maestro está activo.
                        // Si no, devolvemos "WAIT" y cerramos — Python reintentará tras unos segundos.
                        if (!_isTrainingActive || _clientes.ContainsKey(agentId))
                        {
                            try
                            {
                                await writer.WriteLineAsync(_isTrainingActive ? "DUPLICATE" : "WAIT");
                                await writer.FlushAsync();
                            }
                            catch { /* cliente desconectado, no importa */ }
                            cliente.Close();
                            return;
                        }

                        try
                        {
                            await writer.WriteLineAsync("REGISTERED");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ControlServer] Fallo enviando REGISTERED a RECONNECT {agentId}: {ex.Message}");
                            cliente.Close();
                            return;
                        }

                        _clientes[agentId]        = cliente;
                        _incoming[agentId]        = new ConcurrentQueue<string>();
                        _outgoing[agentId]        = new ConcurrentQueue<string>();
                        _outgoingSignals[agentId] = new SemaphoreSlim(0);
                        _writers[agentId]         = writer;

                        Log.Debug($"[ControlServer] Agente {agentId} RECONECTADO y listo.");

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

        /// <summary>
        /// Handler del multi-cliente: lee dicts JSON con acciones y los routea
        /// a las colas _incoming[agentId]. Señala al bucle maestro vía _multiActionSignal
        /// para que aplique la barrier (esperar a todos los agentes antes de step).
        ///
        /// Formato: {"agent_0": 5, "agent_1": 12, "agent_2": 0, "agent_3": 7}
        ///   — cada valor es el action_id (int) directamente.
        ///
        /// Si el multi-cliente se desconecta, _isMultiMode vuelve a false y
        /// el bucle maestro usa noop (12) hasta que llegue un nuevo MULTI_INIT_.
        /// </summary>
        private async Task HandleMultiAgentAsync(TcpClient cliente, StreamReader reader, int numAgents)
        {
            try
            {
                while (_isMultiMode && cliente.Connected)
                {
                    string line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        Log.Warn("[ControlServer] Multi-client: EOF, cerrando.");
                        break;
                    }
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // Parse dict: {"agent_0": 5, "agent_1": 12, ...}
                    try
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(line);
                        if (dict == null) continue;

                        foreach (var kv in dict)
                        {
                            if (!kv.Key.StartsWith("agent_")) continue;
                            if (!int.TryParse(kv.Key.Substring(6), out int agentId)) continue;

                            if (_incoming.TryGetValue(agentId, out var queue))
                            {
                                queue.Enqueue($"ACTION:{kv.Value}");
                                _multiActionReceived[agentId] = true;
                            }
                        }
                        // Señalar al bucle maestro que hay acciones listas
                        _multiActionSignal.Release();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[ControlServer] Multi parse error: {ex.Message} (line: {line.Substring(0, Math.Min(80, line.Length))}...)");
                    }
                }
            }
            catch (System.IO.IOException)
            {
                Log.Warn("[ControlServer] Multi-client: conexión cerrada.");
            }
            catch (ObjectDisposedException)
            {
                Log.Warn("[ControlServer] Multi-client: stream disposed.");
            }
            catch (Exception ex)
            {
                Log.Warn($"[ControlServer] Multi-client error: {ex.Message}");
            }
            finally
            {
                // Salir de multi-modo. El bucle maestro usará noop hasta nueva conexión.
                _isMultiMode = false;
                Log.Warn("[ControlServer] Multi-client: modo multi desactivado.");
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

                // NOTA: NO ponemos IsPythonConnected = false aquí.
                // Este finally se dispara tanto si el cliente se fue como si el plugin
                // cerró la conexión por DetenerEntrenamiento. Si lo ponemos en false,
                // OnWaitingForPlayers deja de spawnear bots y el servidor se queda idle
                // 30s entre rondas. Lo correcto: IsPythonConnected solo se pone en false
                // en DetenerServidor() (apagado total del plugin).

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
                //_frameCount++;

                // ── MULTI-MODE: barrier (esperar a todos los agentes antes de step) ──
                if (_isMultiMode)
                {
                    // Reset flags
                    for (int i = 0; i < _multiNumAgents; i++)
                        _multiActionReceived[i] = false;

                    // Esperar la señal con timeout de 0.5s (yield de frames de Unity)
                    float barrierStart = UnityEngine.Time.realtimeSinceStartup;
                    while (_multiActionSignal.CurrentCount == 0 &&
                           UnityEngine.Time.realtimeSinceStartup - barrierStart < 0.5f)
                        yield return Timing.WaitForOneFrame;

                    // Consumir la señal si llegó (CurrentCount pasa a 0)
                    if (_multiActionSignal.CurrentCount > 0)
                        _multiActionSignal.Wait(0);
                }

                    // Rellenar con noop (12) los agentes que no llegaron a tiempo
                    for (int i = 0; i < _multiNumAgents; i++)
                    {
                        if (!_multiActionReceived[i] && _incoming.TryGetValue(i, out var q))
                            q.Enqueue("ACTION:12");
                    }

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
                    //Log.Info($"[Perf] EventosVivis: {ScpAgentEvents.TotalEventosSuscritos + BaseStrategy.TotalEventosSuscritos}");
                    Log.Info($"[Perf] Total Jugadores: {ReferenceHub.AllHubs.Count - 1}");
                    _frameCount = 0;
                }

                // ── Procesar mensajes ─────────────────────────────────────────
                _pendingDelta = deltaTime;
                AgentManager.Instance.ForEachListo(_procesarDelegate);

                // ── MULTI-MODE: enviar dict combinado de observaciones ─────────
                // CRÍTICO: NO enviar dict vacío antes de que los agentes estén
                // spawneados. Si _latestObservations.Count == 0, significa que
                // ningún agente IsReady ha procesado acciones aún (típicamente
                // durante el spawn de 3s+). El Python espera obs y falla si
                // recibe {}.
                if (_isMultiMode && _multiWriter != null && _latestObservations.Count > 0)
                {
                    try
                    {
                        // IMPORTANTE: _latestObservations[i] es un STRING JSON (producido
                        // por JsonUtils.ToJson). Si lo metemos directamente en el dict, el
                        // JSON resultante tiene strings anidados, no dicts. Python espera
                        // un dict por agente (accede a s["PosX"], etc.). Parseamos cada
                        // string a dict antes de serializar el combinado.
                        var obsDict = new Dictionary<string, object>();
                        foreach (var kv in _latestObservations)
                        {
                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(kv.Value);
                            obsDict[$"agent_{kv.Key}"] = parsed;
                        }
                        string combined = JsonConvert.SerializeObject(obsDict);
                        _multiWriter.WriteLineAsync(combined);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[ControlServer] Multi send obs error: {ex.Message}");
                    }
                }

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

        private void _ProcesarAgenteEnTick(int agentId, IAgentController bot, ISensors sensors)
        {
            if (!_incoming.TryGetValue(agentId, out var colaIn))
                return;

            // Descartamos todos los mensajes atrasados excepto el último — evita
            // procesar acciones de frames anteriores cuando Python reconecta rápido.
            string msg = null;
            while (colaIn.TryDequeue(out string m)) msg = m;
            if (msg == null) return;

            try
            {
                _ProcesarMensaje(bot, msg, agentId, _pendingDelta);
            }
            catch (Exception ex)
            {
                Log.Warn($"[ControlServer] Error Agente {agentId} ('{msg}'): {ex.Message}");
            }
        }

        private void _ProcesarMensaje(IAgentController bot, string msg, int agentId, float deltaTime)
        {
            try {
                // No verificar _exiledPlayer aquí - dejar que GetObservation()
                // decida si retornar obsVacia o la observación real.
                // El problema era que _exiledPlayer puede ser un wrapper stale
                // de EXILED que dice IsAlive=false aunque el bot esté vivo.

                if (msg == "RESPAWN")
                {
                    bot.EjecutarRespawn();
                    // Responder con estado vacío mientras el respawn se completa
                    _EnviarObservacionVacia(agentId, bot._role);
                    return;
                }

                if (msg.StartsWith("ACTION:"))
                {
                    if (int.TryParse(msg.Substring(7), out int actionId))
                        bot.ReceiveAction(new AgentAction { ActionId = actionId });

                    ActionProcessor.ProcesarAccion(actionId, bot, deltaTime);
                }
                else if (msg == "GET_STATE")
                {
                    // NOOP — solo devolver estado actual sin mover
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
        /// En multi-modo, captura la obs en _latestObservations y NO encola per-agente
        /// (el bucle maestro enviará un dict combinado).
        /// </summary>
        private void _EnviarObservacion(IAgentController bot, int agentId, float deltaTime)
        {
            AgentObservation obs = bot.GetObservation(deltaTime);
            if (obs != null && _frameCount % 1000 == 0)
                Log.Info($"[EnviarObs] Agente {agentId} Role: {bot._exiledPlayer.Role.Type} Alive: {bot._exiledPlayer.IsAlive}");
            // Usar bot._role en lugar de bot._exiledPlayer.Role.Type para evitar
            // NullReferenceException si _exiledPlayer es null (wrapper stale después de respawn)
            RoleTypeId role = bot._role;
            if (bot._exiledPlayer != null)
                role = bot._exiledPlayer.Role.Type;
            string json = JsonUtils.ToJson(obs, role);
            //if (_frameCount % 500 == 0)
                //Log.Info($"[Perf] JSON size Agente {agentId}: {json.Length} chars");

            //if (json.Contains(",]"))
                //Log.Warn("JSON INVALIDO DETECTADO: " + json);

            // SIEMPRE capturar la observación (la usa el bucle maestro en multi-modo)
            _latestObservations[agentId] = json;

            // En multi-modo, el bucle maestro envía un dict combinado al final del step.
            // No encolamos per-agente porque no hay _writers[agentId].
            if (_isMultiMode) return;

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

            // Capturar también en multi-modo (puede que llegue un obs vacía tras respawn)
            _latestObservations[agentId] = json;
            if (_isMultiMode) return;

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
