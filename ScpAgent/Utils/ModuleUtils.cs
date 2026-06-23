using UnityEngine;
using System.Collections.Generic;
using Exiled.API.Features;

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
}