using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Sensors;
using ScpAgent.Bot.Data;

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

    public static void addBoundsToCache(Player player, ISensors sensores)
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

        sensores?.MarcarRoomDescubierta(player.CurrentRoom);        
    }

    public static void destroyBoundsCache(int idAntiguo, int idNuevo)
    {
        if (idAntiguo != idNuevo && idAntiguo >= 0)
        {
            if (BaseSensors.agentCacheData.TryGetValue(idAntiguo, out var datos))
            {
                BaseSensors.agentCacheData[idNuevo] = datos;
                BaseSensors.agentCacheData.Remove(idAntiguo);
                //Log.Debug($"[ScpAgentBot] Cache migrada ID {idAntiguo} → {idNuevo}.");
            }
        }
    }  


}