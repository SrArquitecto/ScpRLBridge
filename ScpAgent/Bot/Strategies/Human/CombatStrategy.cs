using System;
using Exiled.API.Features;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot.Strategies.Human
{
    public class CombatStrategy : HumanStrategy
    {

        public CombatStrategy(RoleTypeId role) : base(role)
        {
            
        }

            public override float CalcularPrioridadItem(ItemType tipo)
        {
            switch (tipo)
            {
                
                case ItemType.Jailbird:
                case ItemType.MicroHID:
                    return 100;

                case ItemType.GunCOM15:
                case ItemType.GunCOM18:
                case ItemType.GunFSP9:
                case ItemType.GunE11SR:
                case ItemType.GunLogicer:
                case ItemType.GunAK:
                case ItemType.GunCrossvec:
                case ItemType.GunRevolver:
                case ItemType.GunShotgun:
                case ItemType.GunFRMG0:
                    return 90f;

                case ItemType.ArmorHeavy:
                    return 85f;
                // Munición — alta si ya tiene arma
                case ItemType.Ammo9x19:
                case ItemType.Ammo12gauge:
                case ItemType.Ammo556x45:
                case ItemType.Ammo762x39:
                case ItemType.Ammo44cal:
                    return 70f;
    
                // Chaleco/armadura
                case ItemType.ArmorCombat:
                    return 65;
    
                // Granadas — utilidad táctica
                case ItemType.GrenadeHE:
                case ItemType.GrenadeFlash:
                case ItemType.SCP018:
                    return 60f;
    
                // Medical — secundario para un rol de combate, pero relevante
                case ItemType.Medkit:
                case ItemType.Painkillers:
                case ItemType.Adrenaline:
                case ItemType.SCP500:
                    return 55f;

                case ItemType.ArmorLight:
                    return 30;

                // Keycards — bajo, el rol de combate no necesita explorar tanto
                case ItemType type when type.ToString().Contains("Keycard"):
                    return 20f;
    
                // Radio, linterna, etc.
                case ItemType.Radio:
                case ItemType.Flashlight:
                    return 15f;
    
                default:
                    return 5f; // irrelevante pero no cero — el agente puede aprender excepciones
            }
        }


    }
}