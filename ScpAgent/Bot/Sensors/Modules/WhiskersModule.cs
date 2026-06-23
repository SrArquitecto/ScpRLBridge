using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class WhiskersModule : ISensorModule
    {
        private Player _player;
        private const float WHISKER_RANGE = 2.5f;   // metros — rango corto, solo obstáculos inmediatos
        private const int   WHISKER_COUNT = 8;
        private static readonly float[] WHISKER_ANGLES = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
        private readonly RaycastHit[] _whiskerBuffer = new RaycastHit[1];

        public WhiskersModule()
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
            if (_player == null || _player.Transform == null) return;
        
            // Origen desde la cintura del bot (no los pies, no la cabeza)
            Vector3 origen = _player.Position + Vector3.up * 0.9f;
        
            // Yaw actual del bot (dirección que mira en el plano horizontal)
            float yawBase = _player.Transform.rotation.eulerAngles.y;
        
            for (int i = 0; i < WHISKER_COUNT; i++)
            {
                float anguloTotal = yawBase + WHISKER_ANGLES[i];
                float rad = anguloTotal * Mathf.Deg2Rad;
        
                // Dirección horizontal pura (sin componente Y)
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        
                int hitCount = Physics.RaycastNonAlloc(origen, dir, _whiskerBuffer, WHISKER_RANGE,
                    ~0, QueryTriggerInteraction.Ignore);
        
                if (hitCount > 0 && _whiskerBuffer[0].collider != null &&
                    _whiskerBuffer[0].collider.gameObject != _player.GameObject)
                {
                    // Normalizado: 0 = obstáculo pegado, 1 = rango libre
                    obs.WhiskerDist[i] = _whiskerBuffer[0].distance / WHISKER_RANGE;
                    obs.WhiskerType[i] = _clasificarObstaculo(_whiskerBuffer[0]);
                }
                else
                {
                    obs.WhiskerDist[i] = 1.0f; // sin obstáculo en el rango
                    obs.WhiskerType[i] = 0.0f;
                }
            }
        }

        private float _clasificarObstaculo(RaycastHit hit)
        {
            var collider = hit.collider.gameObject;
            if (collider.GetComponentInParent<Interactables.Interobjects.DoorUtils.DoorVariant>())
                return 0.25f;
            if (collider.GetComponentInParent<ReferenceHub>())
                return 0.5f;
            if (collider.GetComponentInParent<MapGeneration.Distributors.Locker>())
                return 0.75f
                ;
            return 1.0f; //Pared
        }

    }
}