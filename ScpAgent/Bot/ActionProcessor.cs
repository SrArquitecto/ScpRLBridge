using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot
{
    /// <summary>
    /// Traduce las 7 sub-acciones multi-discreto a acciones físicas en el mismo step.
    /// Permite combinaciones simultáneas como [FWD, RIGHT, YAW_R, PITCH_NONE, WEAPON, LCLICK, NONE]
    /// (avanzar + ir a la derecha + girar cámara + equipar arma + disparar todo a la vez).
    /// </summary>
    public static class ActionProcessor
    {
        // ══════════════════════════════════════════════════════════════════════
        // IDs multi-discreto — deben coincidir con Actions en mask_actions.py
        // ══════════════════════════════════════════════════════════════════════

        // Eje 0: Movimiento longitudinal (3)
        public const int LONG_BACK = 0;
        public const int LONG_NONE = 1;
        public const int LONG_FWD  = 2;

        // Eje 1: Movimiento lateral (3)
        public const int LAT_LEFT  = 0;
        public const int LAT_NONE  = 1;
        public const int LAT_RIGHT = 2;

        // Eje 2: Cámara Yaw (9) — 4 velocidades izquierda, quieto, 4 derecha
        public const int YAW_FAST_L  = 0;
        public const int YAW_MED_L   = 1;
        public const int YAW_SLOW_L  = 2;
        public const int YAW_VSLOW_L = 3;
        public const int YAW_NONE    = 4;
        public const int YAW_VSLOW_R = 5;
        public const int YAW_SLOW_R  = 6;
        public const int YAW_MED_R   = 7;
        public const int YAW_FAST_R  = 8;

        // Eje 3: Cámara Pitch (9)
        public const int PITCH_FAST_U = 0;
        public const int PITCH_MED_U  = 1;
        public const int PITCH_SLOW_U = 2;
        public const int PITCH_VSLOW_U= 3;
        public const int PITCH_NONE   = 4;
        public const int PITCH_VSLOW_D= 5;
        public const int PITCH_SLOW_D = 6;
        public const int PITCH_MED_D  = 7;
        public const int PITCH_FAST_D = 8;

        // Eje 4: Gestión de inventario (5) — sin opción de desarmar
        public const int INV_NONE     = 0;
        public const int INV_KEYCARD  = 1;
        public const int INV_WEAPON   = 2;
        public const int INV_MEDICAL  = 3;
        public const int INV_SCP_ITEM = 4;

        // Eje 5: Interacción (4)
        public const int INT_NONE   = 0;
        public const int INT_LCLICK = 1;
        public const int INT_E      = 2;
        public const int INT_PICK   = 3;   // recoger item del suelo

        // Eje 6: Salto (2)
        public const int JUMP_NONE = 0;
        public const int JUMP_DO   = 1;

        // Dimensiones (deben coincidir con tipos.N_ACTIONS_PER_SUB)
        public const int N_LONG    = 3;
        public const int N_LAT     = 3;
        public const int N_YAW     = 9;
        public const int N_PITCH   = 9;
        public const int N_INV     = 5;
        public const int N_INTERACT= 4;
        public const int N_JUMP    = 2;

        /// <summary>
        /// Procesa las 7 sub-acciones multi-discreto en un solo step físico.
        /// </summary>
        public static bool ProcesarAcciones(int longAct, int latAct, int yawAct, int pitchAct,
            int invAct, int intAct, int jumpAct,
            IAgentController bot, float deltaTime)
        {
            if (bot?._exiledPlayer == null || !bot._exiledPlayer.IsAlive)
                return false;

            // ── Eje 0+1: Movimiento (longitudinal + lateral simultáneos) ───
            // Pasamos un "actionId" combinado a MoverPersonaje. La estrategia
            // decodifica la combinación: long + lat → vector de movimiento.
            int moveId = _CombinarMovimiento(longAct, latAct);
            if (moveId > 0)
                bot._strategy?.MoverPersonaje(moveId, deltaTime, bot._exiledPlayer, bot._botGameObject);

            // ── Eje 2+3: Cámara (yaw + pitch simultáneos) ──────────────────
            _AplicarCamara(bot, yawAct, pitchAct);

            // ── Eje 4: Gestión de inventario ───────────────────────────────
            if (invAct != INV_NONE)
                _GestionarInventario(bot, invAct);

            // ── Eje 5: Interacción ────────────────────────────────────────
            switch (intAct)
            {
                case INT_LCLICK:
                    if (bot._strategy is IAgentRoleHumanStrategy human5)
                        human5.UsarItemEquipado(bot._exiledPlayer);
                    break;
                case INT_E:
                    bot._strategy?.AbrirPuerta(bot._exiledPlayer);
                    break;
                case INT_PICK:
                    _PickUpItem(bot);
                    break;
            }

            // ── Eje 6: Salto ──────────────────────────────────────────────
            if (jumpAct == JUMP_DO)
                _Saltar(bot);

            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Combina (long, lat) en un único "actionId" que MoverPersonaje entiende.
        /// Esquema (1-9) — debe coincidir con HumanMovement._MoverPersonaje:
        ///   0 = nada
        ///   1 = forward
        ///   2 = backward
        ///   3 = strafe right
        ///   4 = strafe left
        ///   5 = sprint forward (solo si long=FWD y lat=NONE)
        ///   6 = forward + right
        ///   7 = forward + left
        ///   8 = back + right
        ///   9 = back + left
        /// </summary>
        private static int _CombinarMovimiento(int longAct, int latAct)
        {
            bool fwd   = longAct == LONG_FWD;
            bool back  = longAct == LONG_BACK;
            bool left  = latAct  == LAT_LEFT;
            bool right = latAct  == LAT_RIGHT;

            if (fwd && right)   return 6;  // forward+right
            if (fwd && left)    return 7;  // forward+left
            if (back && right)  return 8;  // back+right
            if (back && left)   return 9;  // back+left
            if (fwd)             return 1;  // forward
            if (back)            return 2;  // backward
            if (right)           return 3;  // strafe right
            if (left)            return 4;  // strafe left
            return 0;  // LONG_NONE + LAT_NONE
        }

        private static void _AplicarCamara(IAgentController bot, int yawAct, int pitchAct)
        {
            // Deltas en GRADOS por STEP. A 50 steps/seg (1/0.02s):
            //   VEL_CAMARA = 15 deg/seg → 0.3 deg/step (referencia original)
            //   VEL_CAMARA_PITCH = 7.5 deg/seg → 0.15 deg/step
            // Los multiplicadores (0.5x, 1x, 2x, 4x) dan 4 velocidades.
            // Antes eran 25-200x más grandes → el bot giraba la cámara tan
            // rápido que colapsaba a una sola acción.
            float dyaw = 0f, dpitch = 0f;
            switch (yawAct)
            {
                case YAW_FAST_L:  dyaw = -15f;  break;   // 60 deg/seg
                case YAW_MED_L:   dyaw = -15f * 0.9f;  break;   // 30 deg/seg
                case YAW_SLOW_L:  dyaw = -15f *  0.7f;  break;   // 15 deg/seg
                case YAW_VSLOW_L: dyaw = -15f * 0.5f; break;   //  7.5 deg/seg
                case YAW_NONE:    dyaw = 0f;    break;
                case YAW_VSLOW_R: dyaw =  15f; break;
                case YAW_SLOW_R:  dyaw =  15f *  0.5f;  break;
                case YAW_MED_R:   dyaw =  15f *  0.7f;  break;
                case YAW_FAST_R:  dyaw =  15f * 0.9f;  break;
            }
            switch (pitchAct)
            {
                case PITCH_FAST_U: dpitch = -7.5f;  break;   // 30 deg/seg
                case PITCH_MED_U:  dpitch = -7.5f * 0.9f;  break;   // 15 deg/seg
                case PITCH_SLOW_U: dpitch = -7.5f * 0.7f; break;   //  7.5 deg/seg
                case PITCH_VSLOW_U:dpitch = -7.5f * 0.5f; break;   //  3.75 deg/seg
                case PITCH_NONE:   dpitch = 0f;     break;
                case PITCH_VSLOW_D:dpitch =  7.5f * 0.5f; break;
                case PITCH_SLOW_D: dpitch =  7.5f * 0.7f; break;
                case PITCH_MED_D:  dpitch =  7.5f * 0.9f;  break;
                case PITCH_FAST_D: dpitch =  7.5f; break;
            }

            // Yaw y pitch simultáneos
            if (dyaw != 0f || dpitch != 0f)
                bot._strategy?.MoverCamara(dyaw, dpitch);
        }

        private static void _GestionarInventario(IAgentController bot, int invAct)
        {
            if (!(bot._strategy is IAgentRoleHumanStrategy human)) return;

            switch (invAct)
            {
                case INV_KEYCARD:
                    human.EquiparTarjeta(bot._exiledPlayer);
                    break;
                case INV_WEAPON:
                    // Intenta arma principal; si no hay, secundaria
                    human.EquiparArmaPrincipal(bot._exiledPlayer);
                    if (bot._exiledPlayer.CurrentItem == null ||
                        !_EsArma(bot._exiledPlayer.CurrentItem))
                        human.EquiparArmaSecundaria(bot._exiledPlayer);
                    break;
                case INV_MEDICAL:
                    human.EquiparMedicamento(bot._exiledPlayer);
                    break;
                case INV_SCP_ITEM:
                    human.EquiparGranada(bot._exiledPlayer);
                    break;
            }
        }

        private static bool _EsArma(Exiled.API.Features.Items.Item item)
            => item is Exiled.API.Features.Items.Firearm;

        private static void _Saltar(IAgentController bot)
        {
            // El salto en bots fake a veces no se procesa porque no hay un
            // cliente que envíe el input de salto. Lo dejamos como no-op
            // por ahora; el control de salto real requiere que el cliente
            // Mirror lo envíe, lo cual no ocurre con FakeConnection/Dummy.
        }

        // Recoger item del suelo. Usa la dummy action "PickUp" si existe
        // en el ReferenceHub (disponible con DummyUtils y cliente Mirror real).
        private static void _PickUpItem(IAgentController bot)
        {
            try
            {
                if (bot?._exiledPlayer?.ReferenceHub == null) return;
                var inv = bot._exiledPlayer.ReferenceHub.inventory;
                if (inv == null) return;

                var actions = new System.Collections.Generic.List<NetworkManagerUtils.Dummies.DummyAction>(4);
                actions.AddRange(NetworkManagerUtils.Dummies.DummyActionCollector.ServerGetActions(bot._exiledPlayer.ReferenceHub));
                inv.PopulateDummyActions(actions.Add, _ => { });

                NetworkManagerUtils.Dummies.DummyAction match = default;
                foreach (var name in new[] { "PickUp", "Pickup" })
                {
                    for (int i = 0; i < actions.Count; i++)
                    {
                        var a = actions[i];
                        if (a.Action == null) continue;
                        if (a.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0
                            && a.Name.IndexOf("Destroy", System.StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            match = a;
                            break;
                        }
                    }
                    if (match.Action != null) break;
                }
                if (match.Action != null) match.Action.Invoke();
            }
            catch (System.Exception ex)
            {
                Log.Debug($"[ActionProcessor] _PickUpItem: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool _EsRolHumano(RoleTypeId rol)
        {
            switch (rol)
            {
                case RoleTypeId.ClassD:
                case RoleTypeId.Scientist:
                case RoleTypeId.FacilityGuard:
                case RoleTypeId.NtfPrivate:
                case RoleTypeId.NtfSergeant:
                case RoleTypeId.NtfSpecialist:
                case RoleTypeId.NtfCaptain:
                case RoleTypeId.ChaosConscript:
                case RoleTypeId.ChaosRifleman:
                case RoleTypeId.ChaosRepressor:
                case RoleTypeId.ChaosMarauder:
                    return true;
                default:
                    return false;
            }
        }
    }
}
