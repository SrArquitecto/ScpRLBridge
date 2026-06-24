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
    public class ItemsModule : ISensorItemsModule
    {
        private Player _player;
 
        private const float TIEMPO_OLVIDO   = 45f;
        private const int   UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;
 
        private readonly ItemData[]  _itemPool      = new ItemData[5];
        private List<Pickup>         _cachedItems;
        private List<ItemData>       _cachedNearItems = new List<ItemData>(5);
 
        // ── FIX PRINCIPAL: lista cacheada en vez de new cada ciclo ────────────
        private readonly List<(Pickup p, float dist)> _itemsConDist
            = new List<(Pickup, float)>(25);
 
        // ── Comparador estático — evita crear lambda/Comparison cada Sort ─────
        private Func<ItemType, float>  _fnPrioridad;
        private Func<ItemType, string> _fnCategoria;
 
        private readonly VisualMemory<ObjectMemoryItem> _memoriaItems
            = new VisualMemory<ObjectMemoryItem>(TIEMPO_OLVIDO);
 
        public ItemsModule()
        {
            for (int i = 0; i < _itemPool.Length; i++)
                _itemPool[i] = new ItemData();
        }
 
        public void VincularPlayer(Player player) => _player = player;
 
        public void Reset()
        {
            _cachedNearItems.Clear();
            _memoriaItems.Clear();
            _cachedItems = null;
            _itemsConDist.Clear();
        }
 
        public void VincularEstrategia(Func<ItemType, float> fnPrioridad,
            Func<ItemType, string> fnCategoria)
        {
            _fnPrioridad = fnPrioridad;
            _fnCategoria = fnCategoria;
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
 
            try { _CargarItems(_player.Position); }
            catch (Exception ex) { Log.Error($"[ItemsModule] {ex.Message}"); }
 
            _CopiarACache(obs);
        }
 
        private void _CargarItems(Vector3 pos)
        {
            if (_cachedItems == null)
                _cachedItems = new List<Pickup>(Pickup.List);
            else
            {
                _cachedItems.Clear();
                _cachedItems.AddRange(Pickup.List);
            }
 
            Vector3 miMirada = _player.CameraTransform != null
                ? _player.CameraTransform.forward : _player.Transform.forward;
            Vector3 misOjos = _player.CameraTransform != null
                ? _player.CameraTransform.position : pos + Vector3.up;
            float ahora = Time.time;
 
            _memoriaItems.MarcarTodosNoVistos();
 
            // ── 1. Filtrar por rango + visibilidad — reutilizar lista cacheada ──
            _itemsConDist.Clear(); // Clear en vez de new List<>
            foreach (var pk in _cachedItems)
            {
                if (pk == null || !pk.IsSpawned || pk.Transform == null) continue;
 
                float dist = Vector3.Distance(pk.Transform.position, pos);
                if (dist >= 25f) continue;
 
                if (!ModuleUtils.EsVisible(_player, misOjos, miMirada,
                    pk.Position, dist, pk.GameObject)) continue;
 
                int itemId = pk.GameObject.GetInstanceID();
                var mem = _memoriaItems.ObtenerORegistrar(itemId, pk.Position, ahora, pk);
                mem.Tipo = pk.Type;
                mem.Tier = ModuleUtils.GetKeycardTier(pk.Type);
 
                _itemsConDist.Add((pk, dist));
            }
 
            // ── Sort con Comparison cacheada para evitar crear objeto cada vez ──
            _itemsConDist.Sort(_CompararItems);
 
            // ── 2. Volcar items VISTOS al pool ────────────────────────────────────
            int itemCount = 0;
            foreach (var (pk, dist) in _itemsConDist)
            {
                if (itemCount >= 5) break;
 
                var id       = _itemPool[itemCount];
                id.Type      = pk.Type.ToString();
                id.Category  = _fnCategoria?.Invoke(pk.Type) ?? "Other";
                id.Prioridad = _fnPrioridad?.Invoke(pk.Type) ?? 10f;
                id.Tier      = ModuleUtils.GetKeycardTier(pk.Type);
                id.Distance  = dist / 25f;
                id.RelX      = (pk.Position.x - pos.x) / 25f;
                id.RelY      = (pk.Position.y - pos.y) / 25f;
                id.RelZ      = (pk.Position.z - pos.z) / 25f;
                id.RealRelX  = pk.Position.x - pos.x;
                id.RealRelY  = pk.Position.y - pos.y;
                id.RealRelZ  = pk.Position.z - pos.z;
                id.EsRecordado = false;
                id.Antiguedad  = 0f;
                _cachedNearItems.Add(id);
                itemCount++;
            }
 
            _memoriaItems.PurgarOlvidados(ahora);
 
            // ── 3. Volcar items RECORDADOS ─────────────────────────────────────────
            foreach (var kv in _memoriaItems.Entradas)
            {
                if (itemCount >= 10) break;
                if (kv.Value.VistoEsteCiclo) continue;
 
                var mem  = kv.Value;
                float dist = Vector3.Distance(mem.UltimaPosicion, pos);
                if (dist >= 30f) continue;
 
                var tipo = mem.Tipo;
                var id   = _itemPool[itemCount];
                id.Type      = tipo.ToString();
                id.Tier      = mem.Tier;
                id.Category  = _fnCategoria?.Invoke(tipo) ?? "Other";
                id.Prioridad = _fnPrioridad?.Invoke(tipo) ?? 10f;
                id.Distance  = dist / 25f;
                id.RelX      = (mem.UltimaPosicion.x - pos.x) / 25f;
                id.RelY      = (mem.UltimaPosicion.y - pos.y) / 25f;
                id.RelZ      = (mem.UltimaPosicion.z - pos.z) / 25f;
                id.RealRelX  = mem.UltimaPosicion.x - pos.x;
                id.RealRelY  = mem.UltimaPosicion.y - pos.y;
                id.RealRelZ  = mem.UltimaPosicion.z - pos.z;
                id.EsRecordado = true;
                id.Antiguedad  = (ahora - mem.UltimoTimestamp) / TIEMPO_OLVIDO;
                _cachedNearItems.Add(id);
                itemCount++;
            }
        }
 
        // ── Comparison estático — NO crea objeto en heap al llamar Sort ──────
        private int _CompararItems((Pickup p, float dist) a, (Pickup p, float dist) b)
        {
            float prioA = _fnPrioridad?.Invoke(a.p.Type) ?? 10f;
            float prioB = _fnPrioridad?.Invoke(b.p.Type) ?? 10f;
            int   cmp   = prioB.CompareTo(prioA); // descendente
            return cmp != 0 ? cmp : a.dist.CompareTo(b.dist);
        }
 
        private void _CopiarACache(AgentObservation obs)
        {
            obs.NearItems.Clear();
            obs.NearItems.AddRange(_cachedNearItems);
        }
    }
}
