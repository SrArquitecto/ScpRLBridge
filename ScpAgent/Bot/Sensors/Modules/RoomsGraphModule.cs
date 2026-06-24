using Exiled.API.Features;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System.Collections.Generic;
using System;


/************************************************************************************
                PROXIMAMENTE AÑADIR NAVEGACION POR GRAFO
************************************************************************************/

namespace ScpAgent.Bot.Sensors.Modules
{
    public class RoomNode
    {
        public int Id { get; set; }
        public RoomType Type { get; set;}
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
        private Player _player;
        private readonly Dictionary<int, RoomNode> _nodes = new Dictionary<int, RoomNode>();
        private int _currentRoomId = 0;

        public IReadOnlyDictionary<int, RoomNode> Nodes => _nodes;
        public int CurrentRoomId => _currentRoomId;
        private static readonly IComparer<Habitaciones> _roomComparerInstance = 
            Comparer<Habitaciones>.Create((a, b) => b.Prioridad.CompareTo(a.Prioridad) == 0 
                ? a.Distancia.CompareTo(b.Distancia) 
                : b.Prioridad.CompareTo(a.Prioridad));
        public RoomsGraphModule()
        {
            
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            

        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
           
        }

        public bool RegistrarTransicion(Room oldRoom, Room newRoom, out bool esPrimeraVisita)
        {
            esPrimeraVisita = false;
            if (newRoom == null || newRoom.Type == RoomType.Unknown) return false;

            int newId = GetRoomUniqueId(newRoom);
            
            // 1. Obtener o Crear el nodo de la nueva habitación
            if (!_nodes.TryGetValue(newId, out var newNode))
            {
                newNode = new RoomNode(newId, newRoom.Type, newRoom.Position, ObtenerPrioridadSala(newRoom, ModuleUtils.GetBestKeycardTier(_player)));
                _nodes[newId] = newNode;
                esPrimeraVisita = true; // ¡Ideal para darle un bonus de recompensa en PPO!
            }

            newNode.VisitCount++;
            newNode.LastTimeVisited = Time.time;
            _currentRoomId = newId;

            // 2. Si venimos de una habitación válida, creamos la arista bidireccional (Edge)
            if (oldRoom != null && oldRoom.Type != RoomType.Unknown)
            {
                int oldId = GetRoomUniqueId(oldRoom);
                if (_nodes.TryGetValue(oldId, out var oldNode))
                {
                    oldNode.ConnectedRoomIds.Add(newId);
                    newNode.ConnectedRoomIds.Add(oldId);
                }
            }

            return true;
        }

        public int GetVisitCount(Room room)
        {
            if (room == null) return 0;
            return _nodes.TryGetValue(GetRoomUniqueId(room), out var node) ? node.VisitCount : 0;
        }

        public int TotalSalasDescubiertas() => _nodes.Count;

        /// <summary>
        /// Genera una clave única por ronda usando el ID nativo de Unity.
        /// </summary>
        private int GetRoomUniqueId(Room room)
        {
            return room.GameObject.GetInstanceID();
        }

        private float ObtenerPrioridadSala(Room room, int tierTarjeta)
        {
            //List<Habitaciones> listaPriorizada = new List<Habitaciones>();
            if (_player == null || _player.Transform == null) return 0.0f;
            float prioridad = 0.0f;

            switch (room.Type)
            {
                case RoomType.Lcz914:
                    // Si necesita mejorar la tarjeta, 914 es la prioridad máxima absoluta
                    prioridad = tierTarjeta is >= 1 and < 3 ? 80f : 0f;
                    break;

                case RoomType.LczCheckpointB:
                case RoomType.LczCheckpointA:
                        // Si ya tiene tarjeta buena para salir de LCZ, los checkpoints son vitales
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
                        // Zonas de armas/loot (prioridad media-alta para sobrevivir)
                        
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
                        // Pasillos, curvas y salas estándar tienen prioridad baja (solo sirven para transitar)
                    prioridad = 5f;
                    break;
            }
            return prioridad;
            
        }
        private void _CopiarACacheHabitaciones(AgentObservation obs)
        {
            obs.NearRooms.Clear();
            //obs.NearRooms.AddRange(_nodes);
        }

    }
}