using Exiled.API.Features;
using Exiled.API.Features.Lockers;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using UnityEngine;
using System.Collections.Generic;
using System;
using FacilitySoundtrack.ChaseThemes;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class LockersModule : ISensorModule
    {
        private Player _player;

        private const float RANGO_MAPA     = 500f;
        private const float TIEMPO_OLVIDO = 45f;
        private const int UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;

        private List<Locker> _cachedLockers;
        private readonly LockerData[]   _lockerPool  = new LockerData[5];
        private List<LockerData> _cachedNearLockers { get; set; } = new List<LockerData>();
        private readonly List<(Locker Locker, float DistMetros)> _lockersVisiblesConDist = new List<(Locker, float)>(5);
        private readonly VisualMemory<ObjectMemoryLocker> _memoriaLockers  = new VisualMemory<ObjectMemoryLocker>(TIEMPO_OLVIDO);
        private readonly List<(ObjectMemoryLocker Memoria, float DistMetros)> _lockersRecordadosConDist = new List<(ObjectMemoryLocker, float)>(5);

        public LockersModule()
        {
            for (int i = 0; i < _lockerPool.Length;  i++) _lockerPool[i]  = new LockerData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {   
            _memoriaLockers.Clear();
            _cachedNearLockers.Clear();
            _lockersVisiblesConDist.Clear();
            _lockersRecordadosConDist.Clear();
            _cachedLockers   = null;
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACache(obs);
                return;
            }
            _frameCounter = 0;
            obs.NearLockers.Clear();
            _cachedNearLockers.Clear();
            try { _CargarLockers(_player.Position); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en LOCKERS: {ex.Message}"); }
        
            _CopiarACache(obs);
        }

        private void _CargarLockers(Vector3 pos)
        {
            if (_cachedLockers == null) _cachedLockers = new List<Locker>(Locker.List);
            else { _cachedLockers.Clear(); _cachedLockers.AddRange(Locker.List); }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
            
            // 1. Resetear visibilidad en la memoria de lockers
            _memoriaLockers.MarcarTodosNoVistos();

            _lockersVisiblesConDist.Clear();

            // 2. Filtrar y registrar Lockers que el bot está VIENDO ahora mismo
            foreach (var l in _cachedLockers)
            {
                if (l == null || l.Transform == null || l.GameObject == null) continue;

                try
                {
                    float distMetros = Vector3.Distance(l.Position, pos);
                    if (distMetros > 25f) continue; // Rango máximo de radar para lockers

                    // Filtro de oclusión física (FOV + Raycast)
                    if (!ModuleUtils.EsVisible(_player, misOjos, miMirada, l.Position, distMetros, l.GameObject)) continue;

                    // ¡Es visible! Lo registramos o actualizamos en la memoria genérica
                    int lockerId = l.GameObject.GetInstanceID();
                    var mem = _memoriaLockers.ObtenerORegistrar(lockerId, l.Position, ahora, l);
                    //mem.ReferenciaObjeto = l;
                    mem.TipoLocker = l.Type.ToString();

                    _lockersVisiblesConDist.Add((l, distMetros));
                }
                catch { continue; }
            }

            // Ordenar los visibles por cercanía (el más cercano primero)
            _lockersVisiblesConDist.Sort((a, b) => a.DistMetros.CompareTo(b.DistMetros));

            int lockerCount = 0;

            // 3. Volcar los lockers VISIBLES actuales al pool
            foreach (var (l, distMetros) in _lockersVisiblesConDist)
            {
                if (lockerCount >= _lockerPool.Length) break;

                var lkd = _lockerPool[lockerCount];
                lkd.Type      = l.Type.ToString();
                lkd.Distance  = distMetros / 25f; // Normalizado a tu escala de 25m
                lkd.HasIsOpen = false;            // Mantener tu lógica de EXILED por chamber
                lkd.RelX      = (l.Position.x - pos.x) / 25f;
                lkd.RelY      = (l.Position.y - pos.y) / 25f;
                lkd.RelZ      = (l.Position.z - pos.z) / 25f;
                lkd.RealRelX  = l.Position.x - pos.x;
                lkd.RealRelY  = l.Position.y - pos.y;
                lkd.RealRelZ  = l.Position.z - pos.z;
                
                // Control de memoria para Python
                lkd.EsRecordado = false;
                lkd.Antiguedad  = 0f;

                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }

            // 4. Procesar los lockers RECORDADOS (los que están en memoria pero ya no se ven)
            _lockersRecordadosConDist.Clear();
            foreach (var kv in _memoriaLockers.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue; // Ya procesado arriba

                var mem = kv.Value;
                float distMetros = Vector3.Distance(mem.UltimaPosicion, pos);
                if (distMetros > 30f) continue; // Si se aleja demasiado (ej. 30m), deja de ser relevante en su radar inmediato

                _lockersRecordadosConDist.Add((mem, distMetros));
            }

            // Ordenar los recuerdos por cercanía
            _lockersRecordadosConDist.Sort((a, b) => a.DistMetros.CompareTo(b.DistMetros));

            _memoriaLockers.PurgarOlvidados(ahora);
            // Volcar los recuerdos en los huecos sobrantes del pool
            foreach (var (mem, distMetros) in _lockersRecordadosConDist)
            {
                if (lockerCount >= _lockerPool.Length) break;

                var lkd = _lockerPool[lockerCount];
                var lockerRef = mem.ReferenciaObjeto as Locker;

                // Comprobación segura por si el mapa cambia o el objeto sufre modificaciones
                if (lockerRef != null && lockerRef.GameObject != null)
                {
                    lkd.Type = lockerRef.Type.ToString();
                }
                else
                {
                    lkd.Type = mem.TipoLocker; // Fallback al string guardado
                }

                lkd.Distance  = distMetros / 25f;
                lkd.HasIsOpen = false;
                lkd.RelX      = (mem.UltimaPosicion.x - pos.x) / 25f;
                lkd.RelY      = (mem.UltimaPosicion.y - pos.y) / 25f;
                lkd.RelZ      = (mem.UltimaPosicion.z - pos.z) / 25f;
                lkd.RealRelX  = mem.UltimaPosicion.x - pos.x;
                lkd.RealRelY  = mem.UltimaPosicion.y - pos.y;
                lkd.RealRelZ  = mem.UltimaPosicion.z - pos.z;
                
                lkd.EsRecordado = true;
                lkd.Antiguedad  = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO;

                _cachedNearLockers.Add(lkd);
                lockerCount++;
            }

            // 5. Purgar memorias obsoletas de lockers
            
        }

        private void _CopiarACache(AgentObservation obs)
        {
            obs.NearLockers.Clear();
            obs.NearLockers.AddRange(_cachedNearLockers);
        }
    }
}