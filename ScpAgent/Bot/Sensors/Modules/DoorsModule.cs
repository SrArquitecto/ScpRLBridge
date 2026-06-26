using Exiled.API.Features;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using UnityEngine;
using System.Collections.Generic;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class DoorsModule : SensorModuleBase<Door, DoorData, ObjectMemoryDoor>
    {
        private static readonly Dictionary<int, string> _doorColliderCache = new Dictionary<int, string>();
        private static readonly Dictionary<Interactables.Interobjects.DoorUtils.DoorPermissionFlags, string> _permCache = new Dictionary<Interactables.Interobjects.DoorUtils.DoorPermissionFlags, string>();

        public static void ClearGlobalCache() => _doorColliderCache.Clear();

        public DoorsModule() : base(poolSize: 15, capacity: 50) { }

        protected override IEnumerable<Door> GetSourceList() => Door.List;
        protected override Vector3 GetPosition(Door d) => d.Position;
        protected override GameObject GetGameObject(Door d) => d.GameObject;
        protected override float GetMaxRange() => 50f;
        protected override int GetMaxVisible() => 15;
        protected override int GetMaxTotal() => 15;
        protected override float GetNormalizationFactor() => 50f;

        protected override void RegisterMemory(ObjectMemoryDoor mem, Door d)
        {
            mem.PermisoPuerta = ModuleUtils.GetDoorRequiredTier(d);
            mem.PuertaAbierta = d.IsOpen;
        }

        protected override void OnPreCargar(SensorContext ctx)
        {
            ctx.BlockedDoorPositions.Clear();
        }

        protected override void FillVisible(DoorData dd, Door d, float dist, Vector3 playerPos, SensorContext ctx)
        {
            int doorId = d.GameObject.GetInstanceID();
            if (!_doorColliderCache.TryGetValue(doorId, out string colliderName))
            {
                colliderName = "Unknown";
                var colliders = d.GameObject.GetComponentsInChildren<Collider>(true);
                var valid = System.Array.Find(colliders,
                    c => !c.isTrigger && !c.name.Contains("TouchScreenPanel") && !c.name.Contains("Frame"));
                if (valid != null) colliderName = valid.name;
                _doorColliderCache[doorId] = colliderName;
            }

            if (!_permCache.TryGetValue(d.RequiredPermissions, out string permStr))
            {
                permStr = d.RequiredPermissions.ToString();
                _permCache[d.RequiredPermissions] = permStr;
            }

            int reqTier = ModuleUtils.GetDoorRequiredTier(d);
            int playerTier = ModuleUtils.GetBestKeycardTier(_player);

            dd.Type         = permStr;
            dd.Name         = d.Name;
            dd.ColliderName = colliderName;
            dd.RequiredTier = reqTier;
            dd.CanOpen      = playerTier >= reqTier;
            dd.IsOpen       = d.IsOpen;

            if (!dd.CanOpen && dd.RequiredTier > 0)
                ctx.BlockedDoorPositions.Add(d.Position);
        }

        protected override void FillRecordado(DoorData dd, ObjectMemoryDoor mem, float dist, Vector3 playerPos, SensorContext ctx)
        {
            var doorRef = mem.ReferenciaObjeto as Door;
            int playerTier = ModuleUtils.GetBestKeycardTier(_player);

            if (doorRef != null && doorRef.GameObject != null)
            {
                int doorId = doorRef.GameObject.GetInstanceID();
                if (!_doorColliderCache.TryGetValue(doorId, out string colliderName))
                {
                    colliderName = "Unknown";
                    var colliders = doorRef.GameObject.GetComponentsInChildren<Collider>(true);
                    var valid = System.Array.Find(colliders,
                        c => !c.isTrigger && !c.name.Contains("TouchScreenPanel") && !c.name.Contains("Frame"));
                    if (valid != null) colliderName = valid.name;
                    _doorColliderCache[doorId] = colliderName;
                }

                if (!_permCache.TryGetValue(doorRef.RequiredPermissions, out string permStr))
                {
                    permStr = doorRef.RequiredPermissions.ToString();
                    _permCache[doorRef.RequiredPermissions] = permStr;
                }

                int reqTier = ModuleUtils.GetDoorRequiredTier(doorRef);
                dd.Type         = permStr;
                dd.Name         = doorRef.Name;
                dd.ColliderName = colliderName;
                dd.CanOpen      = playerTier >= reqTier;
                dd.RequiredTier = reqTier;
            }
            else
            {
                dd.Type         = "Unknown";
                dd.Name         = "Recordada";
                dd.ColliderName = "Unknown";
                dd.CanOpen      = false;
                dd.RequiredTier = mem.PermisoPuerta;
            }

            dd.IsOpen = mem.PuertaAbierta;

            if (!dd.CanOpen && dd.RequiredTier > 0)
                ctx.BlockedDoorPositions.Add(mem.UltimaPosicion);
        }

        protected override void CopiarACache(AgentObservation obs)
        {
            obs.NearDoors.Clear();
            obs.NearDoors.AddRange(_cachedNear);
            obs.DoorIsOpen = false;
            foreach (var d in _cachedNear)
            {
                if (d.IsOpen) { obs.DoorIsOpen = true; break; }
            }
        }
    }
}
