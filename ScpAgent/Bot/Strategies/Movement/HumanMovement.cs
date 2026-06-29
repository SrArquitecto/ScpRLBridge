using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Mirror;

namespace ScpAgent.Bot.Strategy.Movement
{
    /// <summary>
    /// Encapsula toda la lógica de movimiento físico de un bot humano.
    /// Recibe el GameObject y Player como parámetros — no guarda referencias
    /// que puedan quedar stale entre respawns.
    /// </summary>
    public class HumanMovement : BaseMovement
    {
        // ── Campos propios de BotMovement ────────────────────────────────────

        // ── Velocidades ──────────────────────────────────────────────────────
        private const float VEL_CAMINAR = 3.9f;
        private const float VEL_SPRINT  = 5.4052f;
        private const float VEL_CAMARA  = 15f;
        private const float VEL_CAMARA_PITCH = 7.5f;

        // ───────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN
        // ───────────────────────────────────────────────────────────────────

        public HumanMovement(int agentId) : base(agentId)
        {
            
        }

        /// <summary>
        /// Inicializa CharacterController y MouseLook desde el GameObject.
        /// Llamar tras cada spawn/respawn cuando el cuerpo ya existe.
        /// </summary>


        // ───────────────────────────────────────────────────────────────────
        // EJECUCIÓN POR TICK
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ejecuta la acción física. Recibe Player y GameObject como parámetros
        /// para evitar referencias stale entre respawns.
        /// </summary>



        // ───────────────────────────────────────────────────────────────────
        // MOVIMIENTO Y CÁMARA (privados)
        // ───────────────────────────────────────────────────────────────────

        protected override void _MoverPersonaje(int accion, float deltaTime, Player player, GameObject go)
        {
            if (_cc == null) return;

            float yawRad  = player.CameraTransform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3( Mathf.Sin(yawRad), 0f,  Mathf.Cos(yawRad)).normalized;
            Vector3 right = new Vector3( Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)).normalized;

            Vector3 vel = accion switch
            {
                1 =>  fwd   * VEL_CAMINAR,
                2 => -fwd   * VEL_CAMINAR,
                3 => -right * VEL_CAMINAR,
                4 =>  right * VEL_CAMINAR,
                5 =>  fwd   * VEL_SPRINT,
                _ =>  Vector3.zero
            };

            vel.y = _cc.isGrounded ? -0.5f : -9.81f;
            _cc.Move(vel * deltaTime);

            // Sincronizar posición lógica de EXILED con Unity
            player.Position = go.transform.position;
        }


        public void EquiparTarjeta(Player player)
        {
            _EquiparTarjeta(player);
        }

        private void _EquiparTarjeta(Player player)
        {
            var item = player.Items.FirstOrDefault(
                i => i.Type.ToString().IndexOf("Keycard",
                    StringComparison.OrdinalIgnoreCase) >= 0);

            if (item != null) player.CurrentItem = item;
        }

        // ───────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────

    }
}