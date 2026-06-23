using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using UnityEngine;
using System.Collections.Generic;
using System;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class LiftsModule : ISensorModule
    {
        private Player _player;

        private const float RANGO_MAPA     = 500f;
        private const float TIEMPO_OLVIDO = 45f;
        private const int UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;
        private readonly LiftData[] _liftPool = new LiftData[3];
        private List<Lift> _cachedLifts;
        
        private List<LiftData> _cachedNearLifts { get; set;} = new List<LiftData>();
        protected readonly List<(Lift d, float dist)> _liftsConDist = new List<(Lift d, float dist)>(50);
        private readonly VisualMemory<ObjectMemoryLift> _memoriaLifts = new VisualMemory<ObjectMemoryLift>(TIEMPO_OLVIDO);

        private static readonly Comparison<(Lift d, float dist)> _liftComparison =
            (a, b) => a.dist.CompareTo(b.dist);
        public LiftsModule()
        {
            for (int i = 0; i < _liftPool.Length;  i++) _liftPool[i]  = new LiftData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _cachedNearLifts.Clear();
            _memoriaLifts.Clear();
            _cachedLifts   = null;
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
            obs.NearLifts.Clear();
            _cachedNearLifts.Clear();
            try { _CargarAscensores(_player.Position); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en KEYCARDS: {ex.Message}"); }
            _CopiarACache(obs);
        }

        private void _CargarAscensores(Vector3 pos)
        {   

            if (_cachedLifts == null) _cachedLifts = new List<Lift>(Lift.List);
            else { _cachedLifts.Clear(); _cachedLifts.AddRange(Lift.List); }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
            
            _memoriaLifts.MarcarTodosNoVistos();

            _liftsConDist.Clear();
            foreach (var l in _cachedLifts)
            {
                if (l == null) continue;

                try
                {
                    if (l.GameObject == null || l.Transform == null) continue;

                    float dist = Vector3.Distance(l.Transform.position, pos);
                    if (dist >= 50f) continue;

                    // Filtro de visibilidad — FOV + raycast
                    if (!ModuleUtils.EsVisible(_player, misOjos, miMirada, l.Position, dist, l.GameObject)) continue;

                    // Visible ahora — registrar/actualizar memoria
                    int liftId = l.GameObject.GetInstanceID();
                    var memLifts = _memoriaLifts.ObtenerORegistrar(liftId, l.Position, ahora, l);
                    memLifts.AscensorCerrado = l.IsLocked; 
                    memLifts.AscensorOperativo = l.IsOperative; 
                    memLifts.AscensorMoviendose = l.IsMoving; 
                    memLifts.NivelActual = l.CurrentLevel; 
                    _liftsConDist.Add((l, dist));
                }
                catch { continue; }
            }
            _liftsConDist.Sort(_liftComparison);

            int liftCount = 0;
            foreach (var (l, dist) in _liftsConDist)
            {
                if (l == null || liftCount >= 3) break;
                if (l.Transform == null) continue;
                float d = Vector3.Distance(l.Transform.position, pos);
                if (d > 50f) continue;
        
                // Reutilizar objeto del pool en vez de new LiftData
                var ld = _liftPool[liftCount];
                ld.Type         = l.Type.ToString();
                ld.Distance     = d / 50f;
                ld.IsLocked     = l.IsLocked;
                ld.IsClosed     = l.IsOperative;
                ld.IsMoving     = l.IsMoving;
                ld.CanUse       = !l.IsMoving;
                ld.CurrentLevel = l.CurrentLevel;
                ld.RelX = (l.Position.x - pos.x) / 50f;
                ld.RelY = (l.Position.y - pos.y) / 50f;
                ld.RelZ = (l.Position.z - pos.z) / 50f;
                ld.RealRelX     = l.Position.x - pos.x;
                ld.RealRelY     = l.Position.y - pos.y;
                ld.RealRelZ     = l.Position.z - pos.z;
                _cachedNearLifts.Add(ld);
                liftCount++;
            }

            _memoriaLifts.PurgarOlvidados(ahora);

            foreach (var kv in _memoriaLifts.Entradas)
            {
                if (liftCount >= 3) break;
                if (kv.Value.VistoEsteCiclo) continue; // ya procesada arriba

                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 50f * 1.2f) continue; // ya muy lejos, no relevante


                var l = _liftPool[liftCount];
                var liftRef = mem.ReferenciaObjeto as Lift;

                if (liftRef != null && liftRef.GameObject != null)
                {
                    l.Type         = liftRef.Type.ToString();;; // no tenemos el wrapper Door a mano, solo posición
                }
                else
                {
                    
                }
                l.IsLocked     = mem.AscensorCerrado;
                l.IsClosed     = mem.AscensorOperativo;
                l.IsMoving     = mem.AscensorMoviendose;
                l.CanUse       = mem.PuedeUsarse;;                       
                l.CurrentLevel = mem.NivelActual;
                l.RelX = (mem.UltimaPosicion.x - pos.x) / 50f;
                l.RelY = (mem.UltimaPosicion.y - pos.y) / 50f;
                l.RelZ = (mem.UltimaPosicion.z - pos.z) / 50f;
                l.RealRelX     = mem.UltimaPosicion.x - pos.x;
                l.RealRelY     = mem.UltimaPosicion.y - pos.y;
                l.RealRelZ     = mem.UltimaPosicion.z - pos.z;
                l.EsRecordado  = true;
                l.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO;
                _cachedNearLifts.Add(l);
                liftCount++;
            }

            
        }
        private void _CopiarACache(AgentObservation obs)
        {
            obs.NearLifts.Clear();
            obs.NearLifts.AddRange(_cachedNearLifts);
        }
    }
}