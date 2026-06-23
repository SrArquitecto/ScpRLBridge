using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using System;
using UnityEngine;
using System.Linq;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class BasicPlayerModule : ISensorModule
    {
        private Player _player;
        const float RANGO_MAPA     = 500f;
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

            bool hasKeycard  = false;
            int playerTier  = 0;
            if (faction == PlayerRoles.Team.SCPs)
            {
                hasKeycard = true;
                playerTier = 3;
            }
            else
            {
                hasKeycard = _player.Items.Any(i => ModuleUtils.IsKeycard(i.Type));
                playerTier = ModuleUtils.GetBestKeycardTier(_player); 
            }


            Vector3 relativePos = ctx.Pos - ctx.Center;

            float relX = 0f, relY = 0f, relZ = 0f;

            if (ctx.HalfX > 0) relX = Mathf.Clamp(relativePos.x / ctx.HalfX, -1f, 1f);
            if (ctx.HalfY > 0) relY = Mathf.Clamp(relativePos.y / ctx.HalfY, -1f, 1f);
            if (ctx.HalfZ > 0) relZ = Mathf.Clamp(relativePos.z / ctx.HalfZ, -1f, 1f);

            
            obs.Faction     = faction;
            obs.FactionId   = (float)faction/8f;
            obs.PosX        = ctx.Pos.x;
            obs.PosY        = ctx.Pos.y; 
            obs.PosZ        = ctx.Pos.z;
            obs.RelX        = relX;
            obs.RelY        = relY;
            obs.RelZ        = relZ;
            obs.GPSX        = Mathf.Clamp(ctx.Pos.x / RANGO_MAPA, -1f, 1f);
            obs.GPSY        = Mathf.Clamp(ctx.Pos.y / RANGO_MAPA, -1f, 1f);
            obs.GPSZ        = Mathf.Clamp(ctx.Pos.z / RANGO_MAPA, -1f, 1f);
            obs.Yaw         = ctx.CamRotation.y;
            obs.Pitch       = ctx.CamRotation.x;
            obs.Health      = _player.Health / _player.MaxHealth;
            obs.Zone        = _player.CurrentRoom?.Zone.ToString() ?? "Unknown";
            obs.Room        = _player.CurrentRoom?.Type.ToString() ?? "Unknown";
            obs.HasKeycard  = hasKeycard;
            obs.KeycardTier = playerTier;
            obs.LastAction  = ctx.LastAction;
            obs.Reward      = ctx.Reward;
            obs.Done        = ctx.Done;
            
        }




    }
}