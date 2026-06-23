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
    public class RoomsModule : ISensorRoomModule
    {
        private Player _player;
        private const float RANGO_MAPA     = 500f;
        private const int UPDATE_FREQUENCY = 20;
        private int _frameCounter = UPDATE_FREQUENCY;
        private readonly RoomData[]     _roomPool    = new RoomData[5];
        public List<Room> _cachedRooms { get; set; } = new List<Room>();
        private readonly HashSet<int> _roomsDescubiertas = new HashSet<int>();
        private List<RoomData> _cachedNearRooms { get; set; } = new List<RoomData>();
        private readonly List<Habitaciones> _roomsPriorizada = new List<Habitaciones>(250);
        private static readonly Comparison<Habitaciones> _roomComparison = 
            (a, b) => b.Prioridad.CompareTo(a.Prioridad) == 0 ? a.Distancia.CompareTo(b.Distancia) : b.Prioridad.CompareTo(a.Prioridad);

        public RoomsModule()
        {
            for (int i = 0; i < _roomPool.Length;    i++) 
                _roomPool[i]    = new RoomData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _cachedNearRooms.Clear();
            _roomsPriorizada.Clear();
            _roomsDescubiertas.Clear();
            _cachedRooms = null;

        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _frameCounter++;
            if (_frameCounter < UPDATE_FREQUENCY)
            {
                _CopiarACacheHabitaciones(obs);
                return;
            }

            _cachedNearRooms.Clear();
            obs.NearRooms.Clear();
            try { _CargarRooms(ModuleUtils.GetBestKeycardTier(_player)); }
            catch (Exception ex) { Log.Error($"[Sensors] NULL en ROOMS: {ex.Message}"); }
            _CopiarACacheHabitaciones(obs);
        }

        public void MarcarRoomDescubierta(Room sala)
        {
            if (sala == null || sala.GameObject == null) return;
            _roomsDescubiertas.Add(sala.GameObject.GetInstanceID());
        }

        private void _CargarRooms(int playerTier)
        {
            if (_cachedRooms == null || _cachedRooms.Count == 0)
                _cachedRooms = new List<Room>(Room.List);

            _roomsPriorizada.Clear();
            ObtenerListaSalasPriorizadas(playerTier);

            int roomsCounter = 0;
            foreach (var h in _roomsPriorizada)
            {
                if (h == null || roomsCounter >= 5) break;
                if (h.PosicionReal == null) continue;

                // Filtro de descubrimiento — solo rooms ya visitadas por el bot
                if (h.RoomInstanceId == 0 || !_roomsDescubiertas.Contains(h.RoomInstanceId)) continue;

                var r = _roomPool[roomsCounter];
                r.Nombre    = h.NombreHabitacion;
                r.Id        = h.IdHabitacion;
                r.PosX      = h.PosicionReal.x;
                r.PosY      = h.PosicionReal.y;
                r.PosZ      = h.PosicionReal.z;
                r.NormX     = h.PosicionNormX;
                r.NormY     = h.PosicionNormY;
                r.NormZ     = h.PosicionNormZ;
                r.UbiX      = h.PosicionUbiX;
                r.UbiY      = h.PosicionUbiY;
                r.UbiZ      = h.PosicionUbiZ;
                r.Prioridad = h.Prioridad;
                r.Dist      = h.Distancia;
                r.DistNorm  = h.DistanciaNormalizada;
                _cachedNearRooms.Add(r);
                roomsCounter++;
            }
        }

        private void ObtenerListaSalasPriorizadas(int tierTarjeta)
        {
            //List<Habitaciones> listaPriorizada = new List<Habitaciones>();
            if (_player == null || _player.Transform == null) return;

            foreach (Room sala in _cachedRooms)
            {
                // Ignoramos salas desconocidas o zonas muertas
                
                if (sala == null || sala.GameObject == null) continue;
                if (sala.Type == RoomType.Unknown) continue;
                float distancia = Vector3.Distance(_player.Position, sala.Position);
                if (distancia > 500f) continue;

                float prioridad = 0;

                // --- LÓGICA DINÁMICA DE PRIORIDADES ---
                switch (sala.Type)
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

                //float distancia = Vector3.Distance(_player.Position, sala.Position);
                Vector3 vectorObjetivo = sala.Position - _player.Transform.position;
                Vector3 dirNormalizada = vectorObjetivo.normalized;
                float distNormalizada = Mathf.Clamp01(vectorObjetivo.magnitude / RANGO_MAPA);
                float salaNormX = Mathf.Clamp(sala.Position.x / RANGO_MAPA, -1f, 1f);
                float salaNormY = Mathf.Clamp(sala.Position.y / RANGO_MAPA, -1f, 1f); // Altura (LCZ vs HCZ)
                float salaNormZ = Mathf.Clamp(sala.Position.z / RANGO_MAPA, -1f, 1f);

                _roomsPriorizada.Add(new Habitaciones
                {
                    RoomInstanceId = sala.GameObject.GetInstanceID(),
                    NombreHabitacion = sala.Type.ToString(),
                    IdHabitacion = (int)sala.Type,
                    PosicionReal = sala.Position,
                    PosicionNormX = dirNormalizada.x,
                    PosicionNormY = dirNormalizada.y,
                    PosicionNormZ = dirNormalizada.z,
                    PosicionUbiX = salaNormX,
                    PosicionUbiY = salaNormY,
                    PosicionUbiZ = salaNormZ,
                    Prioridad = prioridad/200f,
                    Distancia = distancia,
                    DistanciaNormalizada = distNormalizada
                });

            }

            _roomsPriorizada.Sort(_roomComparison);
        }
        private void _CopiarACacheHabitaciones(AgentObservation obs)
        {
            obs.NearRooms.Clear();
            obs.NearRooms.AddRange(_cachedNearRooms);
        }

    }
}