using UnityEngine;
using System;
using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.API.Features.Doors;

public static class ModuleUtils
{    
    public static RaycastHit[] visibilidadBuffer = new RaycastHit[15];
    public static readonly IComparer<RaycastHit> _raycastComparer =
            Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
    public static bool EsVisible(Player player, Vector3 misOjos, Vector3 miMirada, Vector3 posObjetivo,
        float distancia, GameObject objetivoGO, float fovMinDot = 0.45f)
    {
        if (objetivoGO == null) return false;

        Vector3 dirHaciaObjetivo = (posObjetivo - misOjos).normalized;
        if (Vector3.Dot(miMirada, dirHaciaObjetivo) < fovMinDot) return false;

        if (distancia < 1.5f) return true;

        int hitCount = Physics.RaycastNonAlloc(misOjos, dirHaciaObjetivo, visibilidadBuffer,
            distancia + 0.5f, ~0, QueryTriggerInteraction.Ignore);

        if (hitCount == 0) return false; // nada detectado, sospechoso — no visible por defecto

        System.Array.Sort(visibilidadBuffer, 0, hitCount, _raycastComparer);

        // El primer hit (más cercano) que NO sea el propio jugador es lo que realmente bloquea/confirma
        for (int i = 0; i < hitCount; i++)
        {
            var h = visibilidadBuffer[i];
            if (h.collider.gameObject == player.GameObject) continue; // ignorar el propio cuerpo

            // ¿El primer obstáculo real ES el objetivo (o un hijo de él)?
            return h.collider.gameObject.GetInstanceID() == objetivoGO.GetInstanceID()
                || h.collider.transform.IsChildOf(objetivoGO.transform);
        }

        return false;
    }

        public static int GetKeycardTier(ItemType tipo)
        {
            switch (tipo)
            {
                case ItemType.KeycardJanitor:             return 1;
                case ItemType.KeycardScientist:
                case ItemType.KeycardResearchCoordinator:
                case ItemType.KeycardChaosInsurgency:     return 2;
                case ItemType.KeycardGuard:
                case ItemType.KeycardMTFPrivate:          return 3;
                case ItemType.KeycardZoneManager:
                case ItemType.KeycardMTFOperative:
                case ItemType.KeycardFacilityManager:     return 4;
                case ItemType.KeycardMTFCaptain:
                case ItemType.KeycardO5:                  return 5;
                default:                                   return 0; // no es keycard
            }
        }
        public static int GetBestKeycardTier(ItemType p)
        {
            int t = 0;
            switch (p)
            {
                case ItemType.KeycardJanitor:             t = 1; break;
                case ItemType.KeycardGuard:               t = 4; break;
                case ItemType.KeycardScientist:           t = 2; break;
                case ItemType.KeycardResearchCoordinator: t = 3; break;
                case ItemType.KeycardChaosInsurgency:     t = 5; break;
                case ItemType.KeycardMTFPrivate:          t = 5; break;
                case ItemType.KeycardMTFOperative:        t = 6; break;
                case ItemType.KeycardMTFCaptain:          t = 7; break;
                case ItemType.KeycardZoneManager:         t = 8; break;
                case ItemType.KeycardO5:                  t = 9; break;
                default:
                    t = 1;
                    break;
            }
                
            return t;
        } 
        public static int GetDoorRequiredTier(Door d)
        {
            var perms = (int)d.RequiredPermissions;
            if (perms == 0)        return 0;
            if ((perms & 64)  != 0) return 7;
            if ((perms & 128) != 0) return 7;
            if ((perms & 16)  != 0) return 5;
            if ((perms & 32)  != 0) return 5;
            if ((perms & 4)   != 0) return 3;
            if ((perms & 8)   != 0) return 3;
            if ((perms & 2)   != 0) return 4;
            if ((perms & 256) != 0) return 6;
            return 1;
        } 
        public static bool IsKeycardTypeName(string itemTypeName) => itemTypeName?.IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsKeycard(ItemType t) =>
            t.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0;

        public static int GetBestKeycardTier(Player p)
        {
            int tier = 0;
            foreach (var item in p.Items)
            {
                int t = 0;
                switch (item.Type)
                {
                    case ItemType.KeycardJanitor:             t = 1; break;
                    case ItemType.KeycardGuard:               t = 4; break;
                    case ItemType.KeycardScientist:           t = 2; break;
                    case ItemType.KeycardResearchCoordinator: t = 3; break;
                    case ItemType.KeycardChaosInsurgency:     t = 5; break;
                    case ItemType.KeycardMTFPrivate:          t = 5; break;
                    case ItemType.KeycardMTFOperative:        t = 6; break;
                    case ItemType.KeycardMTFCaptain:          t = 7; break;
                    case ItemType.KeycardZoneManager:         t = 8; break;
                    case ItemType.KeycardO5:                  t = 9; break;
                    default:
                        if (IsKeycardTypeName(item.Type.ToString())) t = 1;
                        else t = 0;
                        break;
                }
                if (t > tier) tier = t;
            }
            return tier;
        }     // implementa tu lógica
   // implementa tu lógica

    public static string CategorizarItem(ItemType tipo)
    {
        string s = tipo.ToString();
        if (s.StartsWith("Gun"))              return "Weapon";
        if (s.StartsWith("Ammo"))             return "Ammo";
        if (s.StartsWith("Armor"))            return "Armor";
        if (s.Contains("Keycard"))            return "Keycard";
        if (s.Contains("SCP"))                return "SCP";
        if (s == "Medkit" || s == "Painkillers" || s == "Adrenaline") return "Medical";
        if (s.StartsWith("Grenade") || s == "SCP018") return "Tactical";
        return "Other";
    }
}