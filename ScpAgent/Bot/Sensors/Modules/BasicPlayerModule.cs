using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using PlayerRoles;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class BasicPlayerModule : ISensorModule
    {
        private Player _player;
        const float RANGO_MAPA     = 500f;
        // ── CACHÉ DE ENUMS ──
        private static readonly Dictionary<Exiled.API.Enums.ZoneType, string> _zoneCache = new Dictionary<Exiled.API.Enums.ZoneType, string>();
        private static readonly Dictionary<Exiled.API.Enums.RoomType, string> _roomCache = new Dictionary<Exiled.API.Enums.RoomType, string>();
        public BasicPlayerModule()
        {
            
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            var faction = _player.Role.Team;
            var role = _player.Role.Type;
            var pos = _player.Position;
            var camara = _player.CameraTransform.rotation.eulerAngles;
            
            bool hasKeycard  = false;
            int playerTier  = 0;
            if (faction == PlayerRoles.Team.SCPs)
            {
                hasKeycard = true;
                playerTier = 3;
            }
            else
            {
                foreach (var item in _player.Items)
                {
                    if (ModuleUtils.IsKeycard(item.Type))
                    {
                        hasKeycard = true;
                        break; // Cortamos el bucle al encontrarla
                    }
                }
                playerTier = ModuleUtils.GetBestKeycardTier(_player); 
            }
            bool amIHurt = false;
            if (_player.Health <= _player.MaxHealth/2f)
                amIHurt = true;
            string zoneStr = "Unknown";
            if (_player.CurrentRoom != null)
            {
                var zone = _player.CurrentRoom.Zone;
                if (!_zoneCache.TryGetValue(zone, out zoneStr))
                {
                    zoneStr = zone.ToString();
                    _zoneCache[zone] = zoneStr;
                }
            }

            string roomStr = "Unknown";
            if (_player.CurrentRoom != null)
            {
                var rType = _player.CurrentRoom.Type;
                if (!_roomCache.TryGetValue(rType, out roomStr))
                {
                    roomStr = rType.ToString();
                    _roomCache[rType] = roomStr;
                }
            }

            Vector3 relativePos = _player.Position - ctx.Center;

            float relX = 0f, relY = 0f, relZ = 0f;

            if (ctx.HalfX > 0) relX = Mathf.Clamp(relativePos.x / ctx.HalfX, -1f, 1f);
            if (ctx.HalfY > 0) relY = Mathf.Clamp(relativePos.y / ctx.HalfY, -1f, 1f);
            if (ctx.HalfZ > 0) relZ = Mathf.Clamp(relativePos.z / ctx.HalfZ, -1f, 1f);

            
            obs.Faction     = faction;
            obs.FactionId   = (float)faction/8f;
            obs.Role        = role;
            obs.RoleId      = (float)role/31f;
            obs.PosX        = pos.x;
            obs.PosY        = pos.y; 
            obs.PosZ        = pos.z;
            obs.PosicionLocalX        = relX;
            obs.PosicionLocalY        = relY;
            obs.PosicionLocalZ        = relZ;
            obs.GPSX        = Mathf.Clamp(pos.x / RANGO_MAPA, -1f, 1f);
            obs.GPSY        = Mathf.Clamp(pos.y / RANGO_MAPA, -1f, 1f);
            obs.GPSZ        = Mathf.Clamp(pos.z / RANGO_MAPA, -1f, 1f);
            obs.Yaw         = camara.y;
            obs.Pitch       = camara.x;
            obs.Health      = _player.Health / _player.MaxHealth;
            obs.AmIHurt     = amIHurt;
            obs.Zone        = zoneStr;
            obs.Room        = roomStr;
            obs.HasKeycard  = hasKeycard;
            obs.KeycardTier = playerTier;
            obs.LastAction  = ctx.LastAction;
            obs.Reward      = ctx.Reward;
            obs.Done        = ctx.Done;
            
        }




    }
}