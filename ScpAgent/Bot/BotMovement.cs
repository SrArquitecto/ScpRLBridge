using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Mirror;

namespace ScpAgent.Bot
{
    /// <summary>
    /// Encapsula toda la lógica de movimiento físico de un bot humano.
    /// Recibe el GameObject y Player como parámetros — no guarda referencias
    /// que puedan quedar stale entre respawns.
    /// </summary>
    public class BotMovement
    {
        // ── Campos propios de BotMovement ────────────────────────────────────
        private CharacterController _cc;
        private int _agentId; // solo para logs

        // ── Reflection cache para FpcMouseLook ──────────────────────────────
        private FieldInfo _fieldCurH;
        private FieldInfo _fieldCurV;
        private FieldInfo _fieldSyncH;
        private FieldInfo _fieldSyncV;
        private object    _mouseLookInstance;
        private bool      _mouseLookListo = false;

        // ── Velocidades ──────────────────────────────────────────────────────
        private const float VEL_CAMINAR = 3.9f;
        private const float VEL_SPRINT  = 5.4052f;
        private const float VEL_CAMARA  = 15f;

        // ───────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN
        // ───────────────────────────────────────────────────────────────────

        public BotMovement(int agentId)
        {
            _agentId = agentId;
        }

        /// <summary>
        /// Inicializa CharacterController y MouseLook desde el GameObject.
        /// Llamar tras cada spawn/respawn cuando el cuerpo ya existe.
        /// </summary>
        public void Inicializar(GameObject go, CharacterController cc)
        {
            _cc = cc;
            _InicializarMouseLook(go);
        }

        /// <summary>
        /// Re-inicializar solo MouseLook (el CC se mantiene).
        /// Útil si el módulo de movimiento se recreó tras un cambio de rol.
        /// </summary>
        public void ReinicializarMouseLook(GameObject go)
        {
            _mouseLookListo    = false;
            _mouseLookInstance = null;
            _InicializarMouseLook(go);
        }

        // ───────────────────────────────────────────────────────────────────
        // EJECUCIÓN POR TICK
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ejecuta la acción física. Recibe Player y GameObject como parámetros
        /// para evitar referencias stale entre respawns.
        /// </summary>
        public void Ejecutar(int accion, float deltaTime, Player player, GameObject go)
        {
            if (player == null || !player.IsAlive || go == null) return;

            switch (accion)
            {
                case 0: case 1: case 2: case 3: case 4:
                    _MoverPersonaje(accion, deltaTime, player, go);
                    break;
                case 5: case 6:
                    _Accion(player);
                    break;
                case 7:
                    _EquiparItem(player);
                    break;
                case 8:
                    _MoverCamara(-VEL_CAMARA);
                    break;
                case 9:
                    _MoverCamara(VEL_CAMARA);
                    break;
                // 10, 11, 12 = NOOP
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // MOVIMIENTO Y CÁMARA (privados)
        // ───────────────────────────────────────────────────────────────────

        private void _MoverPersonaje(int accion, float deltaTime, Player player, GameObject go)
        {
            if (_cc == null) return;

            float yawRad  = player.CameraTransform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3( Mathf.Sin(yawRad), 0f,  Mathf.Cos(yawRad)).normalized;
            Vector3 right = new Vector3( Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)).normalized;

            Vector3 vel = accion switch
            {
                0 =>  fwd   * VEL_CAMINAR,
                1 => -fwd   * VEL_CAMINAR,
                2 => -right * VEL_CAMINAR,
                3 =>  right * VEL_CAMINAR,
                4 =>  fwd   * VEL_SPRINT,
                _ =>  Vector3.zero
            };

            vel.y = _cc.isGrounded ? -0.5f : -9.81f;
            _cc.Move(vel * deltaTime);

            // Sincronizar posición lógica de EXILED con Unity
            player.Position = go.transform.position;
        }

        private void _MoverCamara(float deltaYaw)
        {
            if (!_mouseLookListo || _fieldCurH == null) return;

            float h    = (float)_fieldCurH.GetValue(_mouseLookInstance);
            float v    = (float)_fieldCurV.GetValue(_mouseLookInstance);
            float newH = h + deltaYaw;

            _fieldCurH.SetValue(_mouseLookInstance,  newH);
            _fieldCurV.SetValue(_mouseLookInstance,  v);
            _fieldSyncH.SetValue(_mouseLookInstance, newH);
            _fieldSyncV.SetValue(_mouseLookInstance, v);
        }

        private void _Accion(Player player)
        {
            int layerMask = ~(1 << 13);
            if (!Physics.Raycast(
                player.CameraTransform.position,
                player.CameraTransform.forward,
                out RaycastHit hit, 2.4f, layerMask)) return;

            var doorVariant = hit.collider.GetComponentInParent<
                Interactables.Interobjects.DoorUtils.DoorVariant>();

            if (doorVariant != null)
            {
                var exiledDoor = Door.Get(doorVariant);
                if (exiledDoor != null)
                    exiledDoor.IsOpen = !exiledDoor.IsOpen;
                else
                    doorVariant.NetworkTargetState = !doorVariant.TargetState;
            }
        }

        private void _EquiparItem(Player player)
        {
            var item = player.Items.FirstOrDefault(
                i => i.Type.ToString().IndexOf("Keycard",
                    StringComparison.OrdinalIgnoreCase) >= 0);

            if (item != null) player.CurrentItem = item;
        }

        // ───────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────

        private void _InicializarMouseLook(GameObject go)
        {
            if (go == null) return;
            try
            {
                var movModule = go.GetComponentInChildren<PlayerRoles.FirstPersonControl.FirstPersonMovementModule>(true);

                if (movModule == null)
                {
                    Log.Warn($"[BotMovement] Bot {_agentId} — FirstPersonMovementModule no encontrado.");
                    return;
                }

                _mouseLookInstance = movModule.MouseLook;
                var type  = _mouseLookInstance.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                _fieldCurH  = type.GetField("_curHorizontal",  flags);
                _fieldCurV  = type.GetField("_curVertical",    flags);
                _fieldSyncH = type.GetField("_syncHorizontal", flags);
                _fieldSyncV = type.GetField("_syncVertical",   flags);

                _mouseLookListo = _fieldCurH != null && _fieldCurV != null;

                if (!_mouseLookListo)
                    Log.Warn($"[BotMovement] Bot {_agentId} — campos MouseLook no encontrados.");
                else
                    Log.Debug($"[BotMovement] Bot {_agentId} — MouseLook inicializado.");
            }
            catch (Exception ex)
            {
                Log.Error($"[BotMovement] Bot {_agentId} — error MouseLook: {ex.Message}");
            }
        }
    }
}