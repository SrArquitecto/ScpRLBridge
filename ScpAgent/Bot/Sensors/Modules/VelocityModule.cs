using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;


namespace ScpAgent.Bot.Sensors.Modules
{
    public class VelocityModule : ISensorModule
    {
        private Player _player;
        public VelocityModule()
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
            // ── Velocidades angulares ──────────────────────────────────────────
            float deltaYaw   = Mathf.DeltaAngle(ctx.LastYaw,   ctx.CamRotation.y);
            float deltaPitch = Mathf.DeltaAngle(ctx.LastPitch,  ctx.CamRotation.x);
            float angVelYaw   = deltaYaw   / ctx.Delta;
            float angVelPitch = deltaPitch / ctx.Delta;


            // Velocidad lineal, lateral y vertical:
            float velLin, velLat, velVer;
            if (ctx.Delta <= 0f)
            {
                velLin = 0f; velLat = 0f; velVer = 0f;
                return;
            }

            // 1. Calcular velocidad en el espacio del mundo (World Space)
            Vector3 delta = ctx.Pos - ctx.LastPos;
            Vector3 worldVelocity = delta / ctx.Delta;

            // 2. PASO CLAVE: En lugar de usar _player.Transform (que está roto en el servidor),
            // creamos una rotación limpia usando el Yaw que ya funciona bien.
            Quaternion rotacionReal = Quaternion.Euler(0f, ctx.CamRotation.y, 0f);
            
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
        }

    }
}