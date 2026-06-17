using UnityEngine;
using Exiled.API.Features;


public static class MapUtils
{ 
    public static Bounds ObtenerBoundsTotal(Room room)
    {
        // 🛡️ CONTROL DE SEGURIDAD 1: Validamos que el objeto Room no sea nulo
        if (room == null)
        {
            Log.Warn("[ObtenerBoundsTotal] La sala recibida es null. Devolviendo Bounds por defecto.");
            return new Bounds(Vector3.zero, Vector3.one * 10f);
        }

        // 🛡️ CONTROL DE SEGURIDAD 2: Validamos que Unity ya haya cargado el GameObject
        if (room.GameObject == null)
        {
            Log.Warn($"[ObtenerBoundsTotal] La sala '{room.Name}' no tiene un GameObject asignado todavía.");
            return new Bounds(room.Position, Vector3.one * 10f);
        }

        // Buscamos TODOS los colliders en todos los hijos
        Collider[] colliders = room.GameObject.GetComponentsInChildren<Collider>();
        
        // 🛡️ CONTROL DE SEGURIDAD 3: Validamos que la lista de colliders no sea nula por si acaso
        if (colliders == null || colliders.Length == 0) 
            return new Bounds(room.Position, Vector3.one * 10f);

        // Creamos un Bounds inicial basado en el primer collider
        Bounds totalBounds = colliders[0].bounds;

        // Expandimos el Bounds para que incluya a todos los demás
        for (int i = 1; i < colliders.Length; i++)
        {
            // 🛡️ CONTROL DE SEGURIDAD 4: Evitamos colliders corruptos o destruidos en el loop
            if (colliders[i] != null)
            {
                totalBounds.Encapsulate(colliders[i].bounds);
            }
        }
        
        return totalBounds;
    }
}