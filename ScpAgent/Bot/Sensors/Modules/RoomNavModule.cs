using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;

namespace ScpAgent.Bot.Sensors.Modules
{
    /// <summary>
    /// Sensor de geometría local de la habitación actual (Opción C: room nav compacto).
    /// Reemplaza los 8 whiskers direccionales (16 floats) por 8 features de alto nivel:
    ///   - 4 paredes en frame del agente (N/S/E/O relativas a yaw)
    ///   - 1 distancia a la puerta más cercana
    ///   - 1 ángulo a la puerta (yaw relativo)
    ///   - 1 área de la habitación (normalizada)
    ///   - 1 ratio de aspecto de la habitación
    ///
    /// Trade-off vs whiskers: pierde clasificación de obstáculo (entity/locker)
    /// y resolución angular de 22.5°, pero gana info global de la habitación
    /// (área, forma) y un ángulo explícito a la puerta.
    /// </summary>
    public class RoomNavModule : ISensorModule
    {
        private Player _player;

        // Rangos de raycast
        private const float WALL_RANGE = 5.0f;   // máx distancia a pared (5m cubre celdas grandes)
        private const float DOOR_RANGE = 10.0f;  // máx distancia a puerta (10m cubre pasillos cortos)
        private const float NORMALIZE_AREA = 50f;  // área de referencia (m²) para normalizar RoomAreaNorm

        // 4 direcciones en frame del agente: frente, atrás, izq, der
        private static readonly Vector3[] WALL_DIRS = {
            new Vector3( 0f, 0f,  1f),   // frente (N en frame agente)
            new Vector3( 0f, 0f, -1f),   // atrás
            new Vector3(-1f, 0f,  0f),   // izquierda
            new Vector3( 1f, 0f,  0f),   // derecha
        };

        // Buffer reutilizable — sin alloc en cada raycast
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[1];

        // Cache de geometría de habitación (se invalida al cambiar de Room)
        private Room _cachedRoom = null;
        private float _cachedAreaNorm = 0f;
        private float _cachedShape    = 1f;

        public void VincularPlayer(Player player) => _player = player;
        public void Reset() { _cachedRoom = null; }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            if (_player == null || _player.Transform == null) return;

            Vector3 pos    = _player.Position;
            float   yawDeg = _player.Transform.rotation.eulerAngles.y;
            Vector3 origen = pos + new Vector3(0f, 0.9f, 0f);
            Quaternion yawRot = Quaternion.Euler(0f, yawDeg, 0f);

            // ── 1. 4 paredes (raycast perpendicular al yaw) ─────────────────
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = yawRot * WALL_DIRS[i];
                int hits = Physics.RaycastNonAlloc(
                    origen, dir, _hitBuffer, WALL_RANGE, ~0, QueryTriggerInteraction.Ignore);

                // hits == 0 → sin pared en rango → distancia "infinita" → 1.0
                float dist = (hits > 0) ? _hitBuffer[0].distance : WALL_RANGE;
                float norm = dist / WALL_RANGE;  // 0=pegado, 1=sin pared en rango

                switch (i)
                {
                    case 0: obs.WallFront  = norm; break;
                    case 1: obs.WallBack   = norm; break;
                    case 2: obs.WallLeft   = norm; break;
                    case 3: obs.WallRight  = norm; break;
                }
            }

            // ── 2. Geometría de habitación (cacheada, se invalida al cambiar) ──
            Room currentRoom = _player.CurrentRoom;
            if (currentRoom != _cachedRoom)
            {
                _cachedRoom = currentRoom;
                _UpdateRoomGeometry(currentRoom);
            }
            obs.RoomAreaNorm = _cachedAreaNorm;
            obs.RoomShape    = _cachedShape;

            // ── 3. Puerta más cercana (distancia + ángulo relativo) ────────
            _ComputeDoorFeatures(pos, yawDeg, obs);
        }

        /// <summary>
        /// Cachea el footprint de la habitación actual.
        /// RoomAreaNorm = (footprint_x * footprint_z) / 50  →  [0, 1+]
        /// RoomShape    = max(x,z) / min(x,z)                 →  [1, ∞)  (1=cuadrada)
        /// </summary>
        private void _UpdateRoomGeometry(Room room)
        {
            if (room == null)
            {
                _cachedAreaNorm = 0f;
                _cachedShape    = 1f;
                return;
            }
            Bounds b = MapUtils.ObtenerBoundsTotal(room);
            float footprintX = b.size.x;
            float footprintZ = b.size.z;
            float area = footprintX * footprintZ;
            _cachedAreaNorm = Mathf.Clamp01(area / NORMALIZE_AREA);
            // Ratio aspecto: 1.0 = cuadrada, >1 = alargada en algún eje
            float minDim = Mathf.Min(footprintX, footprintZ);
            float maxDim = Mathf.Max(footprintX, footprintZ);
            _cachedShape = (minDim > 0.01f) ? (maxDim / minDim) : 1f;
        }

        /// <summary>
        /// Busca la puerta más cercana al agente en DOOR_RANGE.
        /// Calcula distancia normalizada + yaw relativo (frame del agente).
        /// DoorDist = 1.0  sin puerta visible
        /// DoorYawRel = 0  cuando no hay puerta (placeholder neutro)
        /// </summary>
        private void _ComputeDoorFeatures(Vector3 pos, float yawDeg, AgentObservation obs)
        {
            Door nearest = null;
            float minDist = DOOR_RANGE;
            // Búsqueda lineal (cheap, ~15 puertas por mapa). Door.List es
            // IReadOnlyCollection<Door>, no se puede indexar — hay que iterar.
            foreach (var d in Door.List)
            {
                if (d == null || d.Transform == null) continue;
                float dist = Vector3.Distance(d.Transform.position, pos);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = d;
                }
            }

            if (nearest == null)
            {
                obs.DoorDist   = 1.0f;
                obs.DoorYawRel = 0.0f;
                return;
            }

            obs.DoorDist = minDist / DOOR_RANGE;  // 0=en puerta, 1=lejos

            // Yaw relativo: ángulo del agente → puerta, normalizado [-1, 1]
            // 0 = puerta directamente delante, ±1 = puerta directamente detrás
            Vector3 doorPos = nearest.Transform.position;
            float dx = doorPos.x - pos.x;
            float dz = doorPos.z - pos.z;
            // Atan2(dx, dz) sigue la convención de yaw: 0=+Z, 90=+X, 180=-Z, -90=-X
            float targetYaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            float diff = targetYaw - yawDeg;
            // Wrap-around a [-180, 180]
            if (diff > 180f)  diff -= 360f;
            if (diff < -180f) diff += 360f;
            obs.DoorYawRel = Mathf.Clamp(diff / 180f, -1f, 1f);
        }
    }
}
