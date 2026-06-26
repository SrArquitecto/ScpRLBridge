using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using UnityEngine;
using System.Collections.Generic;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class LiftsModule : SensorModuleBase<Lift, LiftData, ObjectMemoryLift>
    {
        public LiftsModule() : base(poolSize: 3, capacity: 10) { }

        protected override IEnumerable<Lift> GetSourceList() => Lift.List;
        protected override Vector3 GetPosition(Lift l) => l.Position;
        protected override GameObject GetGameObject(Lift l) => l.GameObject;
        protected override float GetMaxRange() => 50f;
        protected override int GetMaxVisible() => 3;
        protected override int GetMaxTotal() => 3;
        protected override float GetNormalizationFactor() => 50f;

        protected override void RegisterMemory(ObjectMemoryLift mem, Lift l)
        {
            mem.AscensorCerrado    = l.IsLocked;
            mem.AscensorOperativo  = l.IsOperative;
            mem.EnElAscensor       = l.IsInElevator(_player.Position);
            mem.AscensorMoviendose = l.IsMoving;
            mem.NivelActual        = l.CurrentLevel;
        }

        protected override void FillVisible(LiftData ld, Lift l, float dist, Vector3 playerPos, SensorContext ctx)
        {
            ld.Type         = l.Type.ToString();
            ld.IsLocked     = l.IsLocked;
            ld.IsClosed     = l.IsOperative;
            ld.IsInElevator = l.IsInElevator(_player.Position);
            ld.IsMoving     = l.IsMoving;
            ld.CanUse       = !l.IsMoving;
            ld.CurrentLevel = l.CurrentLevel;
        }

        protected override void FillRecordado(LiftData ld, ObjectMemoryLift mem, float dist, Vector3 playerPos, SensorContext ctx)
        {
            var liftRef = mem.ReferenciaObjeto as Lift;
            ld.Type = liftRef != null && liftRef.GameObject != null
                ? liftRef.Type.ToString()
                : string.Empty;

            ld.IsLocked     = mem.AscensorCerrado;
            ld.IsInElevator = mem.EnElAscensor;
            ld.IsClosed     = mem.AscensorOperativo;
            ld.IsMoving     = mem.AscensorMoviendose;
            ld.CanUse       = mem.PuedeUsarse;
            ld.CurrentLevel = mem.NivelActual;
        }

        protected override void CopiarACache(AgentObservation obs)
        {
            obs.NearLifts.Clear();
            obs.NearLifts.AddRange(_cachedNear);
        }
    }
}
