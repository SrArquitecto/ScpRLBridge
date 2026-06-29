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
    public abstract class BaseMovement
    {
        // ── Campos propios de BotMovement ────────────────────────────────────
        protected CharacterController _cc;
        protected int _agentId; // solo para logs

        // ── Reflection cache para FpcMouseLook ──────────────────────────────
        protected FieldInfo _fieldCurH;
        protected FieldInfo _fieldCurV;
        protected FieldInfo _fieldSyncH;
        protected FieldInfo _fieldSyncV;
        protected object    _mouseLookInstance;
        protected bool      _mouseLookListo = false;

        // ── Velocidades ──────────────────────────────────────────────────────
        private const float VEL_CAMINAR = 3.9f;
        private const float VEL_SPRINT  = 5.4052f;
        private const float VEL_CAMARA  = 15f;
        private const float VEL_CAMARA_PITCH = 7.5f;

        // ───────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN
        // ───────────────────────────────────────────────────────────────────

        public BaseMovement(int agentId)
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

        public void MoverPersonaje(int accion, float deltaTime, Player player, GameObject go)
        {
            _MoverPersonaje(accion, deltaTime, player, go);
        }

        // ───────────────────────────────────────────────────────────────────
        // MOVIMIENTO Y CÁMARA (privados)
        // ───────────────────────────────────────────────────────────────────

        protected abstract void _MoverPersonaje(int accion, float deltaTime, Player player, GameObject go);

        public void MoverCamara(int accion)
        {
            switch (accion)
            {
                case 6:  _MoverCamara(-VEL_CAMARA, 0f);        break; // girar izquierda
                case 7:  _MoverCamara( VEL_CAMARA, 0f);        break; // girar derecha
                case 8: _MoverCamara(0f, -VEL_CAMARA_PITCH);        break; // mirar arriba
                case 9: _MoverCamara(0f,  VEL_CAMARA_PITCH);        break; // mirar abajo
            }
        }

        protected void _MoverCamara(float deltaYaw, float deltaPitch = 0f)
        {
            if (!_mouseLookListo || _fieldCurH == null) return;

            float h    = (float)_fieldCurH.GetValue(_mouseLookInstance);
            float v    = (float)_fieldCurV.GetValue(_mouseLookInstance);
            float newH = h + deltaYaw;
            float newV = Mathf.Clamp(v + deltaPitch, -80f, 80f); // ← clamp para no romper la cámara

            _fieldCurH.SetValue(_mouseLookInstance,  newH);
            _fieldCurV.SetValue(_mouseLookInstance,  newV); // ← newV en vez de v
            _fieldSyncH.SetValue(_mouseLookInstance, newH);
            _fieldSyncV.SetValue(_mouseLookInstance, newV); // ← newV en vez de v
        }

        public void AbrirPuerta(Player player)
        {
            _AbrirPuerta(player);
        }
        protected void _AbrirPuerta(Player player)
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

        // ───────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────

        protected void _InicializarMouseLook(GameObject go)
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