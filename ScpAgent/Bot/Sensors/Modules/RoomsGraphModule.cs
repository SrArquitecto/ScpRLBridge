using Exiled.API.Features;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class RoomNode
    {
        public int Id { get; set; }
        public RoomType Type { get; set; }
        public Vector3 Position { get; set; }
        public float Priority { get; set; }
        public int VisitCount { get; set; }
        public float LastTimeVisited { get; set; }
        public HashSet<int> ConnectedRoomIds { get; set; } = new HashSet<int>();

        public RoomNode(int id, RoomType type, Vector3 position, float priority)
        {
            Id = id;
            Type = type;
            Position = position;
            Priority = priority;
            VisitCount = 0;
            LastTimeVisited = 0f;
        }
    }

    public class RoomsGraphModule : ISensorRoomGraphModule
    {
        private const int   GRAPH_SIZE    = 16;
        private const int   MAX_BFS_DEPTH = 2;
        private const float RANGO_MAPA    = 500f;

        private struct BfsEntry
        {
            public RoomNode Node;
            public int Depth;
        }

        private Player _player;
        private readonly Dictionary<int, RoomNode> _nodes = new Dictionary<int, RoomNode>();
        private int _currentRoomId = 0;

        public IReadOnlyDictionary<int, RoomNode> Nodes => _nodes;
        public int CurrentRoomId => _currentRoomId;

        public RoomsGraphModule() { }

        public void VincularPlayer(Player player)
        {
            _player = player;
        }

        public void Reset()
        {
            _nodes.Clear();
            _currentRoomId = 0;
        }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            if (_player == null || _player.Transform == null) return;

            Vector3 playerPos = _player.Position;
            int playerTier    = ModuleUtils.GetBestKeycardTier(_player);

            List<BfsEntry> bfs = BfsLocalSubgraph(MAX_BFS_DEPTH);

            List<RoomNode> selected = SelectTopK(bfs, playerPos, GRAPH_SIZE);

            Dictionary<int, int> idToIndex = new Dictionary<int, int>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
                idToIndex[selected[i].Id] = i;

            FillObservation(obs, selected, playerPos, playerTier);
            FillGraphTopology(obs, selected, idToIndex);
        }

        public bool RegistrarTransicion(Room oldRoom, Room newRoom)
        {
            bool esPrimeraVisita = false;
            if (newRoom == null || newRoom.Type == RoomType.Unknown) return false;
            int newId = GetRoomUniqueId(newRoom);

            if (!_nodes.TryGetValue(newId, out var newNode))
            {
                newNode = new RoomNode(newId, newRoom.Type, newRoom.Position,
                                       ObtenerPrioridadSala(newRoom, ModuleUtils.GetBestKeycardTier(_player)));
                _nodes[newId] = newNode;
                esPrimeraVisita = true;
                Log.Info($"Registrada {newRoom.Name}");
            }

            newNode.VisitCount++;
            newNode.LastTimeVisited = Time.time;
            _currentRoomId = newId;

            if (oldRoom != null && oldRoom.Type != RoomType.Unknown)
            {
                int oldId = GetRoomUniqueId(oldRoom);
                if (_nodes.TryGetValue(oldId, out var oldNode))
                {
                    oldNode.ConnectedRoomIds.Add(newId);
                    newNode.ConnectedRoomIds.Add(oldId);
                }
            }

            return esPrimeraVisita;
        }

        public int GetVisitCount(Room room)
        {
            if (room == null) return 0;
            return _nodes.TryGetValue(GetRoomUniqueId(room), out var node) ? node.VisitCount : 0;
        }

        public int TotalSalasDescubiertas() => _nodes.Count;

        private List<BfsEntry> BfsLocalSubgraph(int maxDepth)
        {
            var result = new List<BfsEntry>();
            if (!_nodes.TryGetValue(_currentRoomId, out var startNode))
                return result;

            var visited = new HashSet<int> { _currentRoomId };
            var queue   = new Queue<(int id, int depth)>();
            queue.Enqueue((_currentRoomId, 0));

            while (queue.Count > 0)
            {
                var (id, depth) = queue.Dequeue();
                if (!_nodes.TryGetValue(id, out var node)) continue;

                result.Add(new BfsEntry { Node = node, Depth = depth });

                if (depth >= maxDepth) continue;

                foreach (var neighborId in node.ConnectedRoomIds)
                {
                    if (!visited.Add(neighborId)) continue;
                    queue.Enqueue((neighborId, depth + 1));
                }
            }
            return result;
        }

        private List<RoomNode> SelectTopK(List<BfsEntry> bfs, Vector3 playerPos, int k)
        {
            return bfs
                .Select(e => new
                {
                    Node  = e.Node,
                    Score = e.Node.Priority / (1f + Vector3.Distance(e.Node.Position, playerPos))
                })
                .OrderByDescending(x => x.Score)
                .Take(k)
                .Select(x => x.Node)
                .ToList();
        }

        private void FillObservation(AgentObservation obs,
                                     List<RoomNode> selected,
                                     Vector3 playerPos,
                                     int playerTier)
        {
            obs.GraphNodes.Clear();

            HashSet<int> salasConEnemigos = ObtenerSalasConEnemigos(obs, playerPos);
            HashSet<int> salasConLoot     = ObtenerSalasConLootValioso(obs, playerPos);
            HashSet<int> salasPuertaBloq  = ObtenerSalasPuertaBloqueada(obs, playerPos);

            for (int i = 0; i < GRAPH_SIZE; i++)
            {
                if (i < selected.Count)
                {
                    var node = selected[i];
                    float dist = Vector3.Distance(node.Position, playerPos);

                    obs.GraphNodes.Add(new GraphNodeData
                    {
                        Id           = node.Id,
                        TypeId       = (int)node.Type,
                        RelX         = (node.Position.x - playerPos.x) / RANGO_MAPA,
                        RelY         = (node.Position.y - playerPos.y) / RANGO_MAPA,
                        RelZ         = (node.Position.z - playerPos.z) / RANGO_MAPA,
                        PosX         = node.Position.x,
                        PosY         = node.Position.y,
                        PosZ         = node.Position.z,
                        Prioridad    = node.Priority / 200f,
                        Distancia    = dist,
                        DistNorm     = Mathf.Clamp01(dist / RANGO_MAPA),
                        VisitCount   = node.VisitCount,
                        Antiguedad   = Time.time - node.LastTimeVisited,
                        EsActual     = (node.Id == _currentRoomId) ? 1f : 0f,
                        TieneEnemigo = salasConEnemigos.Contains(node.Id) ? 1f : 0f,
                        TieneLoot    = salasConLoot.Contains(node.Id) ? 1f : 0f,
                        PuertaBloq   = salasPuertaBloq.Contains(node.Id) ? 1f : 0f
                    });
                }
                else
                {
                    obs.GraphNodes.Add(GraphNodeData.Pad());
                }
            }
        }

        private void FillGraphTopology(AgentObservation obs,
                                      List<RoomNode> selected,
                                      Dictionary<int, int> idToIndex)
        {
            for (int i = 0; i < GRAPH_SIZE; i++)
            {
                for (int j = 0; j < GRAPH_SIZE; j++)
                    obs.GraphAdjacency[i, j] = 0f;
                obs.GraphMask[i] = (i < selected.Count) ? 1f : 0f;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                obs.GraphAdjacency[i, i] = 1f;
                var node = selected[i];
                foreach (var neighborId in node.ConnectedRoomIds)
                {
                    if (idToIndex.TryGetValue(neighborId, out int j))
                        obs.GraphAdjacency[i, j] = 1f;
                }
            }
        }

        private HashSet<int> ObtenerSalasConEnemigos(AgentObservation obs, Vector3 playerPos)
        {
            var set = new HashSet<int>();
            if (obs.NearPlayers == null) return set;

            foreach (var p in obs.NearPlayers)
            {
                if (p.Hostilidad <= 0.5f) continue;
                Vector3 worldPos = playerPos + new Vector3(p.RelX*500f, p.RelY*500f, p.RelZ*500f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) set.Add(salaId);
            }
            return set;
        }

        private HashSet<int> ObtenerSalasConLootValioso(AgentObservation obs, Vector3 playerPos)
        {
            var set = new HashSet<int>();
            if (obs.NearItems == null) return set;

            foreach (var item in obs.NearItems)
            {
                if (item.Prioridad <= 0.3f) continue;
                Vector3 worldPos = playerPos + new Vector3(item.RelX*20f, item.RelY*20f, item.RelZ*20f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) set.Add(salaId);
            }
            return set;
        }

        private HashSet<int> ObtenerSalasPuertaBloqueada(AgentObservation obs, Vector3 playerPos)
        {
            var set = new HashSet<int>();
            if (obs.NearDoors == null) return set;

            foreach (var door in obs.NearDoors)
            {
                if (door.CanOpen) continue;
                if (door.RequiredTier <= 0) continue;
                Vector3 worldPos = playerPos + new Vector3(door.RelX*50f, door.RelY*50f, door.RelZ*50f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) set.Add(salaId);
            }
            return set;
        }

        private int GetRoomIdAtPosition(Vector3 worldPos)
        {
            int closestId    = 0;
            float closestSq  = float.MaxValue;
            foreach (var kvp in _nodes)
            {
                float dx = kvp.Value.Position.x - worldPos.x;
                float dy = kvp.Value.Position.y - worldPos.y;
                float dz = kvp.Value.Position.z - worldPos.z;
                float sq = dx * dx + dy * dy + dz * dz;
                if (sq < closestSq)
                {
                    closestSq  = sq;
                    closestId  = kvp.Key;
                }
            }
            return closestId;
        }

        private int GetRoomUniqueId(Room room)
        {
            return room.GameObject.GetInstanceID();
        }

        private float ObtenerPrioridadSala(Room room, int tierTarjeta)
        {
            if (_player == null || _player.Transform == null) return 0.0f;
            float prioridad = 0.0f;

            switch (room.Type)
            {
                case RoomType.Lcz914:
                    prioridad = tierTarjeta is >= 1 and < 3 ? 80f : 0f;
                    break;

                case RoomType.LczCheckpointB:
                case RoomType.LczCheckpointA:
                    prioridad = tierTarjeta >= 3 ? 100f : 0f;
                    break;

                case RoomType.LczPlants:
                    prioridad = tierTarjeta <= 2 ? 40f : 5f;
                    break;

                case RoomType.LczClassDSpawn:
                    prioridad = 0;
                    break;

                case RoomType.LczArmory:
                    prioridad = tierTarjeta > 3 ? 60f : 0f;
                    break;

                case RoomType.Lcz330:
                    prioridad = tierTarjeta == 2 ? 100f : 0f;
                    break;

                case RoomType.Lcz173:
                case RoomType.LczGlassBox:
                case RoomType.LczCafe:
                    prioridad = tierTarjeta < 3 ? 100f : 0f;
                    break;

                case RoomType.LczToilets:
                    prioridad = tierTarjeta < 1 ? 80f : 0f;
                    break;

                default:
                    prioridad = 5f;
                    break;
            }
            return prioridad;
        }
    }
}
