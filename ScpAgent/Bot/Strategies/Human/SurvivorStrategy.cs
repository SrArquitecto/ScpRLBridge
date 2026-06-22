using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Strategies.Human
{
    public class SurvivorStrategy : HumanStrategy
    {
        public SurvivorStrategy(RoleTypeId role) : base(role)
        {
            
        }
        public override float CalcularPrioridadItem(ItemType tipo)
        {
            switch (tipo)
            {
                // Keycards — máxima prioridad, el objetivo es escapar/progresar de zona
                case ItemType type when type.ToString().Contains("Keycard"):
                    return 95f;
    
                // Medical — supervivencia ante todo
                case ItemType.Medkit:
                case ItemType.Adrenaline:
                case ItemType.Painkillers:
                    return 80f;
    
                // Armas — útiles pero secundarias para un superviviente puro
                case ItemType type when type.ToString().StartsWith("Gun"):
                    return 45f;
    
                case ItemType type when type.ToString().StartsWith("Ammo"):
                    return 30f;
    
                case ItemType.Radio:
                case ItemType.Flashlight:
                    return 35f; // utilidad para explorar zonas oscuras
    
                case ItemType.ArmorLight:
                case ItemType.ArmorCombat:
                    return 25f;
    
                default:
                    return 5f;
            }
        }

    }
}