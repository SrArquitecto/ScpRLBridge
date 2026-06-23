using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using UnityEngine;
using System.Collections.Generic;
using System;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class ItemsModule : ISensorModule
    {
        private Player _player;

        private const float RANGO_MAPA     = 500f;
        private const float TIEMPO_OLVIDO = 45f;
        private const int UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;
        private readonly ItemData[] _itemPool = new ItemData[10];
        private List<Pickup> _cachedItems;
        
        private List<ItemData> _cachedNearItems { get; set;} = new List<ItemData>();
        private readonly VisualMemory<ObjectMemoryItem> _memoriaItems = new VisualMemory<ObjectMemoryItem>(TIEMPO_OLVIDO);

        public ItemsModule()
        {
            for (int i = 0; i < _itemPool.Length;  i++) _itemPool[i]  = new ItemData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _cachedNearItems.Clear();
            _memoriaItems.Clear();
            _cachedItems   = null;
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
            
            _cachedNearItems.Clear();
            obs.NearItems.Clear();
            try { _CargarItems(ctx.Pos, ctx.FnPrioridad, ctx.FnCategoria); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en KEYCARDS: {ex.Message}"); }
            _CopiarACache(obs);
        }

        private void _CargarItems(Vector3 pos,Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            if (_cachedItems == null)
                _cachedItems = new List<Pickup>(Pickup.List);
            else
            {
                _cachedItems.Clear();
                _cachedItems.AddRange(Pickup.List);
            }
            
            Vector3 miMirada = _player.CameraTransform != null ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos  = _player.CameraTransform != null ? _player.CameraTransform.position : pos + Vector3.up;
            float   ahora    = Time.time;
        
            _memoriaItems.MarcarTodosNoVistos();

            // ── 1. Filtrar por rango + visibilidad real ───────────────────────────
            var itemsConDist = new List<(Pickup p, float dist)>(25); // considera cachear esto como campo si se llama a menudo
            foreach (var pk in _cachedItems)
            {
                if (pk == null || !pk.IsSpawned || pk.Transform == null) continue;
        
                float dist = Vector3.Distance(pk.Transform.position, pos);
                if (dist >= 25f) continue;
        
                if (!ModuleUtils.EsVisible(_player, misOjos, miMirada, pk.Position, dist, pk.GameObject)) continue;
        
                int itemId = pk.GameObject.GetInstanceID();
                var mem = _memoriaItems.ObtenerORegistrar(itemId, pk.Position, ahora, pk);
                mem.Tipo = pk.Type;
                mem.Tier = ModuleUtils.GetKeycardTier(pk.Type);

                itemsConDist.Add((pk, dist));
            }
        
            // Ordenar por prioridad del rol activo, no solo por distancia
            itemsConDist.Sort((a, b) =>
            {
                float prioA = fnPrioridad?.Invoke(a.p.Type) ?? 10f;
                float prioB = fnPrioridad?.Invoke(b.p.Type) ?? 10f;
                // Prioridad descendente; a igual prioridad, más cercano primero
                int cmp = prioB.CompareTo(prioA);
                return cmp != 0 ? cmp : a.dist.CompareTo(b.dist);
            });
        
            // ── 2. Volcar items VISTOS AHORA al pool ──────────────────────────────
            int itemCount = 0;
            foreach (var (pk, dist) in itemsConDist)
            {
                if (itemCount >= 10) break;
        
                var id = _itemPool[itemCount];
                id.Type      = pk.Type.ToString();
                id.Category  = fnCategoria?.Invoke(pk.Type) ?? "Other";
                id.Prioridad = fnPrioridad?.Invoke(pk.Type) ?? 10f;
                id.Distance  = dist / 25f;
                id.RelX      = (pk.Position.x - pos.x) / 25f;
                id.RelY      = (pk.Position.y - pos.y) / 25f;
                id.RelZ      = (pk.Position.z - pos.z) / 25f;
                id.RealRelX  = pk.Position.x - pos.x;
                id.RealRelY  = pk.Position.y - pos.y;
                id.RealRelZ  = pk.Position.z - pos.z;
                id.EsRecordado = false;
                id.Antiguedad   = 0f;
                _cachedNearItems.Add(id);
                itemCount++;
            }
        
            _memoriaItems.PurgarOlvidados(ahora);
            // ── 3. Volcar items RECORDADOS ─────────────────────────────────────────
            foreach (var kv in _memoriaItems.Entradas)
            {
                if (itemCount >= 10) break;
                if (kv.Value.VistoEsteCiclo) continue;
        
                var mem = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 25f * 1.2f) continue;
        
                var tipoRecordado = mem.Tipo;
        
                var id = _itemPool[itemCount];
                id.Type      = tipoRecordado.ToString();
                id.Tier = ModuleUtils.GetKeycardTier(tipoRecordado);
                id.Category  = fnCategoria?.Invoke(tipoRecordado) ?? "Other";
                id.Prioridad = fnPrioridad?.Invoke(tipoRecordado) ?? 10f;
                id.Distance  = dist / 25f;
                id.RelX      = (mem.UltimaPosicion.x - pos.x) / 25f;
                id.RelY      = (mem.UltimaPosicion.y - pos.y) / 25f;
                id.RelZ      = (mem.UltimaPosicion.z - pos.z) / 25f;
                id.RealRelX  = mem.UltimaPosicion.x - pos.x;
                id.RealRelY  = mem.UltimaPosicion.y - pos.y;
                id.RealRelZ  = mem.UltimaPosicion.z - pos.z;
                id.EsRecordado = true;
                id.Antiguedad   = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO;
                _cachedNearItems.Add(id);
                itemCount++;
            }
        
            _memoriaItems.PurgarOlvidados(ahora);
        }
        private void _CopiarACache(AgentObservation obs)
        {
            obs.NearItems.Clear();
            obs.NearItems.AddRange(_cachedNearItems);
        }
    }
}