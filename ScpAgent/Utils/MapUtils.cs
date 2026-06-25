using UnityEngine;
using System.Collections.Generic;
using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Sensors;
using ScpAgent.Bot.Data;

public static class MapUtils
{
    private static readonly List<Collider> _colliderBuffer = new List<Collider>(64);
    private static readonly Dictionary<int, Bounds> _boundsCache = new Dictionary<int, Bounds>();

    public static void ClearBoundsCache() => _boundsCache.Clear();

    public static Bounds ObtenerBoundsTotal(Room room)
    {
        if (room == null)
        {
            Log.Warn("[ObtenerBoundsTotal] La sala recibida es null. Devolviendo Bounds por defecto.");
            return new Bounds(Vector3.zero, Vector3.one * 10f);
        }

        if (room.GameObject == null)
        {
            Log.Warn($"[ObtenerBoundsTotal] La sala '{room.Name}' no tiene un GameObject asignado todavía.");
            return new Bounds(room.Position, Vector3.one * 10f);
        }

        int roomId = room.GameObject.GetInstanceID();
        if (_boundsCache.TryGetValue(roomId, out var cached))
            return cached;

        _colliderBuffer.Clear();
        room.GameObject.GetComponentsInChildren<Collider>(false, _colliderBuffer);

        if (_colliderBuffer.Count == 0)
            return new Bounds(room.Position, Vector3.one * 10f);

        Bounds totalBounds = _colliderBuffer[0].bounds;
        for (int i = 1; i < _colliderBuffer.Count; i++)
        {
            if (_colliderBuffer[i] != null)
                totalBounds.Encapsulate(_colliderBuffer[i].bounds);
        }

        _boundsCache[roomId] = totalBounds;
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
            }
        }
    }


}