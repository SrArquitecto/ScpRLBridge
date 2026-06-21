using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Sensors;
using ScpAgent.Bot.Data;

namespace ScpAgent.Bot.Strategies
{
    public abstract class BaseStrategy : IAgentRoleStrategy
    {
        public RoleTypeId Role { get; }

        protected IAgentController _bot;

        public BaseStrategy(RoleTypeId role) 
        {
            Role = role;
        }

    
        public virtual void OnBind(IAgentController bot)
        {
            _bot = bot;
            Exiled.Events.Handlers.Player.RoomChanged         += OnRoomChanged;
        }

        public virtual void OnUnbind()
        {
            _bot = null;
            Exiled.Events.Handlers.Player.RoomChanged         -= OnRoomChanged;
        }

        public virtual void EjecutarAccionEspecial(int actionId, float deltaTime) {}

        protected bool _EsEsteAgente(Player p)
        {
            if (p == null || _bot.ExiledPlayer == null) return false;
            return p.Id == _bot.ExiledPlayer.Id;
        }
        // ── MANEJADORES DE EVENTOS DE RECOMPENSA ──

        public void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (!_EsEsteAgente(ev.Player)) return;
            if (ev.NewRoom == null || ev.NewRoom.Type == RoomType.Unknown) return;

            try
            {
                addBoundsToCache(ev.Player);
            }
            catch (Exception ex)
            {
                Log.Error($"[ScpAgentBot] OnRoomChanged Agente {_bot.AgentId}: {ex.Message}");
            }
        }

        public virtual void addBoundsToCache(Player player)
        {
            Bounds b = MapUtils.ObtenerBoundsTotal(player.CurrentRoom);
            int pid = player.Id;

            if (!BaseSensors.agentCacheData.ContainsKey(pid))
                BaseSensors.agentCacheData[pid] = new AgentCacheData();

            BaseSensors.agentCacheData[pid].center = b.center;
            BaseSensors.agentCacheData[pid].halfX = b.size.x / 2f;
            BaseSensors.agentCacheData[pid].halfY = b.size.y / 2f;
            BaseSensors.agentCacheData[pid].halfZ = b.size.z / 2f;
            BaseSensors.agentCacheData[pid].IsDataReady   = true;           
        }

        public virtual void destroyBoundsCache(int idAntiguo, int idNuevo)
        {
            if (idAntiguo != idNuevo && idAntiguo >= 0)
            {
                if (BaseSensors.agentCacheData.TryGetValue(idAntiguo, out var datos))
                {
                    BaseSensors.agentCacheData[idNuevo] = datos;
                    BaseSensors.agentCacheData.Remove(idAntiguo);
                    Log.Debug($"[ScpAgentBot] Cache migrada ID {idAntiguo} → {idNuevo}.");
                }
            }
        }        
    }
}