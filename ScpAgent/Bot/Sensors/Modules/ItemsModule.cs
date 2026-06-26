using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class ItemsModule : SensorModuleBase<Pickup, ItemData, ObjectMemoryItem>, ISensorItemsModule
    {
        private Func<ItemType, float> _fnPrioridad;

        public ItemsModule() : base(poolSize: 10, capacity: 25, refreshCache: true) { }

        protected override IEnumerable<Pickup> GetSourceList() => Pickup.List;
        protected override Vector3 GetPosition(Pickup pk) => pk.Position;
        protected override GameObject GetGameObject(Pickup pk) => pk.GameObject;
        protected override float GetMaxRange() => 25f;
        protected override int GetMaxVisible() => 5;
        protected override int GetMaxTotal() => 10;
        protected override float GetNormalizationFactor() => 25f;

        protected override bool IsValidEntity(Pickup pk) => pk != null && pk.IsSpawned && pk.Transform != null;

        public void VincularEstrategia(Func<ItemType, float> fnPrioridad)
        {
            _fnPrioridad = fnPrioridad;
        }

        protected override void RegisterMemory(ObjectMemoryItem mem, Pickup pk)
        {
            mem.Tipo = pk.Type;
            mem.Tier = ModuleUtils.GetKeycardTier(pk.Type);
        }

        protected override void OnPreCargar(SensorContext ctx)
        {
            ctx.LootPositions.Clear();
        }

        protected override IComparer<(Pickup, float)> GetComparer() =>
            Comparer<(Pickup, float)>.Create((a, b) =>
            {
                float prioA = _fnPrioridad?.Invoke(a.Item1.Type) ?? 10f;
                float prioB = _fnPrioridad?.Invoke(b.Item1.Type) ?? 10f;
                int cmp = prioB.CompareTo(prioA);
                return cmp != 0 ? cmp : a.Item2.CompareTo(b.Item2);
            });

        protected override void FillVisible(ItemData data, Pickup pk, float dist, Vector3 playerPos, SensorContext ctx)
        {
            data.Type      = pk.Type.ToString();
            data.Category  = ModuleUtils.CategorizarItem(pk.Type) ?? "Other";
            data.Prioridad = _fnPrioridad?.Invoke(pk.Type) ?? 10f;
            data.Tier      = ModuleUtils.GetKeycardTier(pk.Type);

            if (data.Prioridad > 0.3f)
                ctx.LootPositions.Add(pk.Position);
        }

        protected override void FillRecordado(ItemData data, ObjectMemoryItem mem, float dist, Vector3 playerPos, SensorContext ctx)
        {
            data.Type      = mem.Tipo.ToString();
            data.Tier      = mem.Tier;
            data.Category  = ModuleUtils.CategorizarItem(mem.Tipo) ?? "Other";
            data.Prioridad = _fnPrioridad?.Invoke(mem.Tipo) ?? 10f;

            if (data.Prioridad > 0.3f)
                ctx.LootPositions.Add(mem.UltimaPosicion);
        }

        protected override void CopiarACache(AgentObservation obs)
        {
            obs.NearItems.Clear();
            obs.NearItems.AddRange(_cachedNear);
        }
    }
}
