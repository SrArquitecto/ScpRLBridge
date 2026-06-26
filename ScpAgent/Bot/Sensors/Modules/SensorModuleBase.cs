using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using UnityEngine;
using System;
using System.Collections.Generic;
using ScpAgent.Bot.Sensors.Data.Interfaces;

namespace ScpAgent.Bot.Sensors.Modules
{
    public abstract class SensorModuleBase<TEntity, TData, TMemory> : ISensorModule
        where TEntity : class
        where TData : class, ISpatialData, new()
        where TMemory : ObjectMemory, new()
    {
        protected Player _player;
        protected const float TIEMPO_OLVIDO = 45f;
        protected const int UPDATE_FREQUENCY = 20;
        protected int _frameCounter = UPDATE_FREQUENCY;

        protected readonly TData[] _pool;
        protected List<TEntity> _cachedEntities;
        protected readonly List<TData> _cachedNear;
        protected readonly List<(TEntity, float)> _conDist;
        protected readonly List<(TMemory, float)> _recordadosConDist;
        protected readonly VisualMemory<TMemory> _memoria;
        protected readonly bool _refreshCache;

        protected SensorModuleBase(int poolSize, int capacity = 50, bool refreshCache = false)
        {
            _pool = new TData[poolSize];
            for (int i = 0; i < poolSize; i++) _pool[i] = new TData();
            _cachedNear = new List<TData>(poolSize);
            _conDist = new List<(TEntity, float)>(capacity);
            _recordadosConDist = new List<(TMemory, float)>(capacity);
            _memoria = new VisualMemory<TMemory>(TIEMPO_OLVIDO);
            _refreshCache = refreshCache;
        }

        public void VincularPlayer(Player player) => _player = player;

        public virtual void Reset()
        {
            _memoria.Clear();
            _cachedNear.Clear();
            _conDist.Clear();
            _recordadosConDist.Clear();
            _cachedEntities = null;
        }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                CopiarACache(obs);
                return;
            }
            _frameCounter = 0;
            _cachedNear.Clear();
            OnPreCargar(ctx);
            try { Cargar(_player.Position, ctx); }
            catch (Exception ex) { Log.Error($"[{GetType().Name}] {ex.Message}"); }
            CopiarACache(obs);
        }

        protected abstract IEnumerable<TEntity> GetSourceList();
        protected abstract Vector3 GetPosition(TEntity entity);
        protected abstract GameObject GetGameObject(TEntity entity);
        protected abstract float GetMaxRange();
        protected abstract int GetMaxVisible();
        protected abstract int GetMaxTotal();
        protected abstract float GetNormalizationFactor();
        protected abstract void RegisterMemory(TMemory mem, TEntity entity);
        protected abstract void FillVisible(TData data, TEntity entity, float dist, Vector3 playerPos, SensorContext ctx);
        protected abstract void FillRecordado(TData data, TMemory mem, float dist, Vector3 playerPos, SensorContext ctx);
        protected abstract void CopiarACache(AgentObservation obs);

        protected virtual void OnPreCargar(SensorContext ctx) { }

        protected virtual bool IsValidEntity(TEntity entity) => entity != null;

        protected virtual IComparer<(TEntity, float)> GetComparer() =>
            Comparer<(TEntity, float)>.Create((a, b) => a.Item2.CompareTo(b.Item2));

        protected void FillSpatial(ISpatialData data, Vector3 entityPos, Vector3 playerPos, float dist, bool esRecordado, float antiguedad)
        {
            float norm = GetNormalizationFactor();
            data.Distance  = dist / norm;
            data.RelX      = (entityPos.x - playerPos.x) / norm;
            data.RelY      = (entityPos.y - playerPos.y) / norm;
            data.RelZ      = (entityPos.z - playerPos.z) / norm;
            data.RealRelX  = entityPos.x - playerPos.x;
            data.RealRelY  = entityPos.y - playerPos.y;
            data.RealRelZ  = entityPos.z - playerPos.z;
            data.EsRecordado = esRecordado;
            data.Antiguedad  = antiguedad;
        }

        private void Cargar(Vector3 pos, SensorContext ctx)
        {
            if (_cachedEntities == null)
                _cachedEntities = new List<TEntity>(GetSourceList());
            else if (_refreshCache)
            {
                _cachedEntities.Clear();
                _cachedEntities.AddRange(GetSourceList());
            }

            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float ahora      = Time.time;
            float maxRange   = GetMaxRange();

            _memoria.MarcarTodosNoVistos();
            _conDist.Clear();

            foreach (var e in _cachedEntities)
            {
                if (!IsValidEntity(e)) continue;

                try
                {
                    var go = GetGameObject(e);
                    if (go == null || go.transform == null) continue;

                    float dist = Vector3.Distance(go.transform.position, pos);
                    if (dist >= maxRange) continue;

                    Vector3 entityPos = GetPosition(e);
                    if (!ModuleUtils.EsVisible(_player, misOjos, miMirada, entityPos, dist, go)) continue;

                    int id = go.GetInstanceID();
                    var mem = _memoria.ObtenerORegistrar(id, entityPos, ahora, e);
                    RegisterMemory(mem, e);
                    _conDist.Add((e, dist));
                }
                catch { continue; }
            }

            _conDist.Sort(GetComparer());

            int count = 0;
            int maxVisible = GetMaxVisible();
            foreach (var (entity, dist) in _conDist)
            {
                if (count >= maxVisible) break;
                var data = _pool[count];
                FillSpatial(data, GetPosition(entity), pos, dist, false, 0f);
                FillVisible(data, entity, dist, pos, ctx);
                _cachedNear.Add(data);
                count++;
            }

            _memoria.PurgarOlvidados(ahora);

            _recordadosConDist.Clear();
            foreach (var kv in _memoria.Entradas)
            {
                if (kv.Value.VistoEsteCiclo) continue;
                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= maxRange * 1.2f) continue;
                _recordadosConDist.Add((mem, dist));
            }
            _recordadosConDist.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            int maxTotal = GetMaxTotal();
            foreach (var (mem, dist) in _recordadosConDist)
            {
                if (count >= maxTotal) break;
                var data = _pool[count];
                float antiguedad = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO;
                FillSpatial(data, mem.UltimaPosicion, pos, dist, true, antiguedad);
                FillRecordado(data, mem, dist, pos, ctx);
                _cachedNear.Add(data);
                count++;
            }
        }
    }
}
