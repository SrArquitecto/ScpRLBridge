using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class VelocityModule : ISensorVelocityModule
    {
        private Player _player;
        private Vector3 _lastPos;
        private float _lastYaw;
        private float _lastPitch;
        public VelocityModule()
        {
            
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _lastPos   = Vector3.zero;
            _lastYaw   = 0f;
            _lastPitch = 0f;
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            // ── Velocidades angulares ──────────────────────────────────────────
            var camara = _player.CameraTransform.rotation.eulerAngles;
            float deltaYaw   = Mathf.DeltaAngle(_lastYaw, camara.y);
            float deltaPitch = Mathf.DeltaAngle(_lastPitch, camara.x);
            float angVelYaw   = deltaYaw   / ctx.Delta;
            float angVelPitch = deltaPitch / ctx.Delta;
            _lastYaw   = camara.y;
            _lastPitch = camara.x;


            // Velocidad lineal, lateral y vertical:
            float velLin, velLat, velVer;
            if (ctx.Delta <= 0f)
            {
                velLin = 0f; velLat = 0f; velVer = 0f;
                return;
            }

            // 1. Calcular velocidad en el espacio del mundo (World Space)
            Vector3 posActual = _player.Position;
            Vector3 delta = posActual - _lastPos;
            Vector3 worldVelocity = delta / ctx.Delta;

            // 2. PASO CLAVE: En lugar de usar _player.Transform (que está roto en el servidor),
            // creamos una rotación limpia usando el Yaw que ya funciona bien.
            Quaternion rotacionReal = Quaternion.Euler(0f, _player.CameraTransform.rotation.eulerAngles.y, 0f);
            
            // Multiplicar por la inversa rota el vector del mundo al espacio local del bot
            Vector3 localVel = Quaternion.Inverse(rotacionReal) * worldVelocity;

            // 3. ASIGNACIÓN MATEMÁTICA REAL Y CORRECTA
            velLin = localVel.z;      // Adelante (+) o Atrás (-) -> ¡Ahora sí tendrá signo!
            velLat = localVel.x;      // Derecha (+) o Izquierda (-)
            velVer = worldVelocity.y; // Altura real del mundo (Y global). Si no sube/baja, será 0.0

            obs.AngVelYaw = angVelYaw;
            obs.AngVelPitch = angVelPitch;
            obs.LinVel = velLin;
            obs.LatVel = velLat;
            obs.VerVel = velVer;
            _lastPos = posActual;
        }

        public void SetLastPos(Vector3 pos)
        {
            _lastPos = pos;
        }
        public void SetLastYaw(float yaw)
        {
            _lastYaw = yaw;
        }
        public void SetLastPitch(float yaw)
        {
            _lastYaw = yaw;
        }

    }
}