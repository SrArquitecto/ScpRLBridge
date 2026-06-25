using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System.Collections.Generic;
using Exiled.API.Features.Doors;
using Exiled.API.Enums;
using System;

namespace ScpAgent.Bot.Sensors.Modules
{   
    public static class AimTargetCode
    {   
        public const float None    = 0.0f; 
        public const float Wall    = 0.1f;
        public const float Floor   = 0.2f;
        public const float Ceiling = 0.3f;
        public const float Door    = 0.4f;
        public const float Locker  = 0.5f;
        public const float Pickup  = 0.6f;
        public const float Entity  = 0.7f;
    }

    public class AimModule : ISensorModule
    {   
        private Player _player;
        private const int   AIM_CACHE_FRAMES = 5;
        private readonly RaycastHit[] _raycastBuffer = new RaycastHit[10];
        protected static readonly IComparer<RaycastHit> _raycastComparer =
            Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
        
        // ── CACHÉ DE ENUMS Y COLLIDERS ──
        private static readonly Dictionary<RoomType, string> _roomCache = new Dictionary<RoomType, string>();
        private static readonly Dictionary<int, string> _colliderNameCache = new Dictionary<int, string>();

        private int    _aimCacheCounter  = AIM_CACHE_FRAMES;
        private float  _cachedAimTarget  = 0f;
        private float  _cachedAimDist    = 0f;
        private string _cachedAimRoom    = "Unknown";
        private string _cachedAimDoorName = "None";
        private string _cachedHitName    = "None";
        private float  _cachedHitX, _cachedHitY, _cachedHitZ;
        private float  _cachedForwardX,  _cachedForwardZ;

        public AimModule() { }
        public void VincularPlayer(Player player) => _player = player;

        public void Reset()
        {
            _aimCacheCounter   = AIM_CACHE_FRAMES;
            _cachedAimTarget   = 0f;
            _cachedAimDist     = 0f;
            _cachedAimRoom     = "Unknown";
            _cachedAimDoorName = "None";
            _cachedHitName     = "None";
            _cachedHitX        = 0f; _cachedHitY = 0f; _cachedHitZ = 0f;
            _cachedForwardX    = 0f; _cachedForwardZ = 0f;
            _colliderNameCache.Clear();
        }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _ProcesarAimRaycast(obs);
            bool canInteract = (obs.AimTarget == AimTargetCode.Door ||
                                obs.AimTarget == AimTargetCode.Locker ||
                                obs.AimTarget == AimTargetCode.Pickup)
                               && obs.AimDistance <= 2.4f;
            obs.CanInteract = canInteract ? 1 : 0;
        }

        private void _ProcesarAimRaycast(AgentObservation obs)
        {
            _aimCacheCounter++;
            if (_aimCacheCounter < AIM_CACHE_FRAMES)
            {   
                _CopiarCacheAObs(obs);
                return;
            }

            _aimCacheCounter = 0;
            var ray = new Ray(_player.CameraTransform.position, _player.CameraTransform.forward);
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastBuffer, 75f);
            Array.Sort(_raycastBuffer, 0, hitCount, _raycastComparer);

            Vector3 flat = new Vector3(ray.direction.x, 0, ray.direction.z).normalized;
            _cachedForwardX = flat.x;
            _cachedForwardZ = flat.z;

            RaycastHit validHit = default;
            bool hasHit = false;
            for (int i = 0; i < hitCount; i++)
            {
                var h = _raycastBuffer[i];
                if (h.collider.gameObject == _player.GameObject ||
                    h.collider.transform.root == _player.Transform.root) continue;
                validHit = h;
                hasHit   = true;
                break;
            }

            if (hasHit)
            {
                _cachedAimDist = validHit.distance;
                _cachedHitX    = validHit.point.x;
                _cachedHitY    = validHit.point.y;
                _cachedHitZ    = validHit.point.z;

                int colId = validHit.collider.GetInstanceID();
                if (!_colliderNameCache.TryGetValue(colId, out _cachedHitName))
                {
                    _cachedHitName = validHit.collider.name.ToLower();
                    _colliderNameCache[colId] = _cachedHitName;
                }

                var door = validHit.collider.GetComponentInParent<Interactables.Interobjects.DoorUtils.DoorVariant>();
                bool isDoor = door != null || _cachedHitName.Contains("door") || _cachedHitName.Contains("gate");

                if (isDoor)
                {
                    _cachedAimTarget = AimTargetCode.Door;
                    if (door != null)
                    {
                        var exD = Door.Get(door);
                        if (exD != null) _cachedAimDoorName = exD.Name;
                    }
                }
                else if (validHit.collider.GetComponentInParent<MapGeneration.Distributors.Locker>() != null)
                    _cachedAimTarget = AimTargetCode.Locker;
                else if (validHit.collider.GetComponentInParent<InventorySystem.Items.Pickups.ItemPickupBase>() != null)
                    _cachedAimTarget = AimTargetCode.Pickup;
                else if (validHit.collider.GetComponentInParent<ReferenceHub>() != null)
                    _cachedAimTarget = AimTargetCode.Entity;
                else
                {
                    float y = ray.direction.y;
                    if      (y < -0.40f) _cachedAimTarget = AimTargetCode.Floor;
                    else if (y >  0.40f) _cachedAimTarget = AimTargetCode.Ceiling;
                    else                 _cachedAimTarget = AimTargetCode.Wall;
                }

                var hitRoom = Room.Get(validHit.point);
                if (hitRoom != null)
                {
                    if (!_roomCache.TryGetValue(hitRoom.Type, out _cachedAimRoom))
                    {
                        _cachedAimRoom = hitRoom.Type.ToString();
                        _roomCache[hitRoom.Type] = _cachedAimRoom;
                    }
                }
                else _cachedAimRoom = "Unknown";
            }
            
            _CopiarCacheAObs(obs);
        }  

        private void _CopiarCacheAObs(AgentObservation obs)
        {
            obs.AimTarget   = _cachedAimTarget;
            obs.AimDistance = _cachedAimDist;
            obs.AimRoom     = _cachedAimRoom;
            obs.AimDoorName = _cachedAimDoorName;
            obs.HitName     = _cachedHitName;
            obs.HitX        = _cachedHitX; obs.HitY = _cachedHitY; obs.HitZ = _cachedHitZ;
            obs.ForwardX    = _cachedForwardX; obs.ForwardZ = _cachedForwardZ;
        }
    }
}