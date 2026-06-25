using Exiled.API.Features;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System.Collections.Generic;
using System;

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
        private const int   BFS_BUFFER    = 64;
        private const float RANGO_MAPA    = 500f;

        private struct BfsEntry
        {
            public RoomNode Node;
            public int Depth;
        }

        private struct ScoredNode
        {
            public RoomNode Node;
            public float Score;
        }

        private static readonly Comparer<ScoredNode> _scoreDescComparer =
            Comparer<ScoredNode>.Create((a, b) => b.Score.CompareTo(a.Score));

        private Player _player;
        private readonly Dictionary<int, RoomNode> _nodes = new Dictionary<int, RoomNode>();
        private int _currentRoomId = 0;

        private readonly BfsEntry[]          _bfsBuffer      = new BfsEntry[BFS_BUFFER];
        private readonly ScoredNode[]        _scoredBuffer   = new ScoredNode[BFS_BUFFER];
        private readonly RoomNode[]          _selectedBuffer = new RoomNode[GRAPH_SIZE];
        private readonly int[]               _bfsIds         = new int[BFS_BUFFER];
        private readonly int[]               _bfsDepths      = new int[BFS_BUFFER];
        private int _bfsHead;
        private int _bfsTail;

        private readonly HashSet<int>          _bfsVisited    = new HashSet<int>(BFS_BUFFER);
        private readonly Dictionary<int, int>  _idToIndex     = new Dictionary<int, int>(GRAPH_SIZE);
        private readonly HashSet<int>          _salasEnemigos = new HashSet<int>();
        private readonly HashSet<int>          _salasLoot     = new HashSet<int>();
        private readonly HashSet<int>          _salasPuerta   = new HashSet<int>();

        public IReadOnlyDictionary<int, RoomNode> Nodes => _nodes;
        public int CurrentRoomId => _currentRoomId;

        public RoomsGraphModule() { }

        public void VincularPlayer(Player player) => _player = player;

        public void Reset()
        {
            _nodes.Clear();
            _currentRoomId = 0;
            _bfsVisited.Clear();
            _idToIndex.Clear();
            _salasEnemigos.Clear();
            _salasLoot.Clear();
            _salasPuerta.Clear();
        }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            if (_player == null || _player.Transform == null) return;

            Vector3 playerPos = _player.Position;
            int playerTier    = ModuleUtils.GetBestKeycardTier(_player);

            int bfsCount      = BfsLocalSubgraph(MAX_BFS_DEPTH);
            int selectedCount = SelectTopK(bfsCount, playerPos, GRAPH_SIZE, _selectedBuffer);

            _idToIndex.Clear();
            for (int i = 0; i < selectedCount; i++)
                _idToIndex[_selectedBuffer[i].Id] = i;

            FillObservation(obs, selectedCount, playerPos, playerTier);
            FillGraphTopology(obs, selectedCount);
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

        private int BfsLocalSubgraph(int maxDepth)
        {
            if (!_nodes.TryGetValue(_currentRoomId, out _))
                return 0;

            _bfsVisited.Clear();
            _bfsVisited.Add(_currentRoomId);

            _bfsHead = 0;
            _bfsTail = 0;
            _bfsIds[_bfsTail]    = _currentRoomId;
            _bfsDepths[_bfsTail] = 0;
            _bfsTail++;

            int count = 0;

            while (_bfsHead < _bfsTail)
            {
                int id    = _bfsIds[_bfsHead];
                int depth = _bfsDepths[_bfsHead];
                _bfsHead++;

                if (!_nodes.TryGetValue(id, out var node)) continue;
                if (count >= BFS_BUFFER) break;

                _bfsBuffer[count].Node  = node;
                _bfsBuffer[count].Depth = depth;
                count++;

                if (depth >= maxDepth) continue;

                var neighbors = node.ConnectedRoomIds;
                foreach (var neighborId in neighbors)
                {
                    if (!_bfsVisited.Add(neighborId)) continue;
                    if (_bfsTail >= BFS_BUFFER) break;
                    _bfsIds[_bfsTail]    = neighborId;
                    _bfsDepths[_bfsTail] = depth + 1;
                    _bfsTail++;
                }
            }
            return count;
        }

        private int SelectTopK(int bfsCount, Vector3 playerPos, int k, RoomNode[] outBuffer)
        {
            for (int i = 0; i < bfsCount; i++)
            {
                var node = _bfsBuffer[i].Node;
                float dist = Vector3.Distance(node.Position, playerPos);
                _scoredBuffer[i].Node  = node;
                _scoredBuffer[i].Score = node.Priority / (1f + dist);
            }

            Array.Sort(_scoredBuffer, 0, bfsCount, _scoreDescComparer);

            int count = Math.Min(k, bfsCount);
            for (int i = 0; i < count; i++)
                outBuffer[i] = _scoredBuffer[i].Node;

            return count;
        }

        private void FillObservation(AgentObservation obs,
                                     int selectedCount,
                                     Vector3 playerPos,
                                     int playerTier)
        {
            obs.GraphNodes.Clear();

            ObtenerSalasConEnemigos(obs, playerPos);
            ObtenerSalasConLootValioso(obs, playerPos);
            ObtenerSalasPuertaBloqueada(obs, playerPos);

            for (int i = 0; i < GRAPH_SIZE; i++)
            {
                if (i < selectedCount)
                {
                    var node = _selectedBuffer[i];
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
                        TieneEnemigo = _salasEnemigos.Contains(node.Id) ? 1f : 0f,
                        TieneLoot    = _salasLoot.Contains(node.Id) ? 1f : 0f,
                        PuertaBloq   = _salasPuerta.Contains(node.Id) ? 1f : 0f
                    });
                }
                else
                {
                    obs.GraphNodes.Add(GraphNodeData.Pad());
                }
            }
        }

        private void FillGraphTopology(AgentObservation obs, int selectedCount)
        {
            for (int i = 0; i < GRAPH_SIZE; i++)
            {
                for (int j = 0; j < GRAPH_SIZE; j++)
                    obs.GraphAdjacency[i, j] = 0f;
                obs.GraphMask[i] = (i < selectedCount) ? 1f : 0f;
            }

            for (int i = 0; i < selectedCount; i++)
            {
                obs.GraphAdjacency[i, i] = 1f;
                var node = _selectedBuffer[i];
                var neighbors = node.ConnectedRoomIds;
                foreach (var neighborId in neighbors)
                {
                    if (_idToIndex.TryGetValue(neighborId, out int j))
                        obs.GraphAdjacency[i, j] = 1f;
                }
            }
        }

        private void ObtenerSalasConEnemigos(AgentObservation obs, Vector3 playerPos)
        {
            _salasEnemigos.Clear();
            if (obs.NearPlayers == null) return;

            var list = obs.NearPlayers;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.Hostilidad <= 0.5f) continue;
                Vector3 worldPos = playerPos + new Vector3(p.RelX * 50f, p.RelY * 50f, p.RelZ * 50f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) _salasEnemigos.Add(salaId);
            }
        }

        private void ObtenerSalasConLootValioso(AgentObservation obs, Vector3 playerPos)
        {
            _salasLoot.Clear();
            if (obs.NearItems == null) return;

            var list = obs.NearItems;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Prioridad <= 0.3f) continue;
                Vector3 worldPos = playerPos + new Vector3(item.RelX * 20f, item.RelY * 20f, item.RelZ * 20f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) _salasLoot.Add(salaId);
            }
        }

        private void ObtenerSalasPuertaBloqueada(AgentObservation obs, Vector3 playerPos)
        {
            _salasPuerta.Clear();
            if (obs.NearDoors == null) return;

            var list = obs.NearDoors;
            for (int i = 0; i < list.Count; i++)
            {
                var door = list[i];
                if (door.CanOpen) continue;
                if (door.RequiredTier <= 0) continue;
                Vector3 worldPos = playerPos + new Vector3(door.RelX * 50f, door.RelY * 50f, door.RelZ * 50f);
                int salaId = GetRoomIdAtPosition(worldPos);
                if (salaId != 0) _salasPuerta.Add(salaId);
            }
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
