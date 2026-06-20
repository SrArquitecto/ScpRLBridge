using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Strategies
{
    public class CombatStrategy : HumanStrategy
    {

        public CombatStrategy(RoleTypeId role) : base(role)
        {
            
        }
        public override void addBoundsToCache(Player player)
        {
            base.addBoundsToCache(player);
        }

        public override void destroyBoundsCache(int idAntiguo, int idNuevo)
        {
            base.destroyBoundsCache(idAntiguo, idNuevo);
        }


    }
}