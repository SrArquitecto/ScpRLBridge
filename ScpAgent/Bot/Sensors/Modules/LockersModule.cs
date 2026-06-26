using Exiled.API.Features;
using Exiled.API.Features.Lockers;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using UnityEngine;
using System.Collections.Generic;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class LockersModule : SensorModuleBase<Locker, LockerData, ObjectMemoryLocker>
    {
        public LockersModule() : base(poolSize: 5, capacity: 10) { }

        protected override IEnumerable<Locker> GetSourceList() => Locker.List;
        protected override Vector3 GetPosition(Locker l) => l.Position;
        protected override GameObject GetGameObject(Locker l) => l.GameObject;
        protected override float GetMaxRange() => 25f;
        protected override int GetMaxVisible() => 5;
        protected override int GetMaxTotal() => 5;
        protected override float GetNormalizationFactor() => 25f;

        protected override void RegisterMemory(ObjectMemoryLocker mem, Locker l)
        {
            mem.TipoLocker = l.Type.ToString();
        }

        protected override void FillVisible(LockerData lkd, Locker l, float dist, Vector3 playerPos, SensorContext ctx)
        {
            lkd.Type      = l.Type.ToString();
            lkd.HasIsOpen = false;
        }

        protected override void FillRecordado(LockerData lkd, ObjectMemoryLocker mem, float dist, Vector3 playerPos, SensorContext ctx)
        {
            var lockerRef = mem.ReferenciaObjeto as Locker;
            lkd.Type = lockerRef != null && lockerRef.GameObject != null
                ? lockerRef.Type.ToString()
                : mem.TipoLocker;

            lkd.HasIsOpen = false;
        }

        protected override void CopiarACache(AgentObservation obs)
        {
            obs.NearLockers.Clear();
            obs.NearLockers.AddRange(_cachedNear);
        }
    }
}
