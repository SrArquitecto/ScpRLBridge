using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;

namespace ScpAgent.Bot.Sensors.Modules
{
    /// <summary>
    /// Sensor de 8 whiskers direccionales (raycast en frame del agente).
    /// Cada whisker emite: [distancia normalizada 0-1, tipo 0/0.25/0.5/0.75/1.0].
    /// Complementa a RoomNavModule: whiskers dan resolución angular fina + clasificación
    /// de obstáculo; room nav da info topológica de la habitación.
    /// </summary>
    public class WhiskersModule : ISensorModule
    {
        private Player _player;

        private const float WHISKER_RANGE = 2.5f;
        private const int   WHISKER_COUNT = 8;

        private static readonly float[] WHISKER_ANGLES
            = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        // Buffer reutilizable — sin alloc en cada raycast
        private readonly RaycastHit[] _whiskerBuffer = new RaycastHit[1];

        // ── Cache de layer masks para clasificación sin GetComponentInParent ──
        private static int? _layerDoor   = null;
        private static int? _layerPlayer = null;

        public void VincularPlayer(Player player) => _player = player;
        public void Reset() { }

        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            if (_player == null || _player.Transform == null) return;

            // Inicializar layer cache la primera vez (solo una vez en toda la sesión)
            _layerDoor   ??= LayerMask.NameToLayer("Door");
            _layerPlayer ??= LayerMask.NameToLayer("Player");

            Vector3 origen  = _player.Position + new Vector3(0f, 0.9f, 0f);
            float   yawBase = _player.Transform.rotation.eulerAngles.y;

            for (int i = 0; i < WHISKER_COUNT; i++)
            {
                float rad = (yawBase + WHISKER_ANGLES[i]) * Mathf.Deg2Rad;

                // Vector3 como struct — no genera GC
                float sinR = Mathf.Sin(rad);
                float cosR = Mathf.Cos(rad);

                int hitCount = Physics.RaycastNonAlloc(
                    origen,
                    new Vector3(sinR, 0f, cosR),
                    _whiskerBuffer,
                    WHISKER_RANGE,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                if (hitCount > 0 &&
                    _whiskerBuffer[0].collider != null &&
                    _whiskerBuffer[0].collider.gameObject != _player.GameObject)

                {
                    obs.WhiskerDist[i] = _whiskerBuffer[0].distance / WHISKER_RANGE;
                    obs.WhiskerType[i] = _ClasificarPorLayer(_whiskerBuffer[0]);
                }
                else
                {
                    obs.WhiskerDist[i] = 1.0f;
                    obs.WhiskerType[i] = 0.0f;
                }
            }
        }

        /// <summary>
        /// Clasifica el obstáculo usando el layer del GameObject — sin GetComponentInParent,
        /// sin GC. Si los layers no coinciden con los esperados, cae a "Wall" (1.0).
        /// </summary>
        private static float _ClasificarPorLayer(RaycastHit hit)
        {
            int layer = hit.collider.gameObject.layer;

            if (_layerDoor.HasValue   && layer == _layerDoor.Value)   return 0.25f; // Puerta
            if (_layerPlayer.HasValue && layer == _layerPlayer.Value)  return 0.5f;  // Entidad

            // Locker no suele tener layer propio — fallback por nombre (solo si falla layer)
            string name = hit.collider.gameObject.name;
            if (name.Contains("Interactable")) return 0.75f;

            return 1.0f; // Pared/geometría estática
        }
    }
}
