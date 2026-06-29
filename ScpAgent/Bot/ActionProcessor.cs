using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Strategies.Interfaces;

namespace ScpAgent.Bot
{
    /// <summary>
    /// Traduce el ActionId numérico de Python a acciones concretas en el juego.
    /// Curriculum 1: acciones humanas (0-12).
    /// Curriculum 2: acciones SCP (13-15) — implementar según el SCP concreto.
    ///
    /// IMPORTANTE: C# no necesita conocer las máscaras — eso lo gestiona Python.
    /// Si Python envía una acción inválida para el rol (bug o transición de rol),
    /// C# simplemente la ignora (NOOP). Así el sistema es robusto a carreras.
    /// </summary>
    public static class ActionProcessor
    {
        // ── IDs de acción — deben coincidir exactamente con Actions en train.py ──
        public const int NOOP             = 0;
        public const int MOVE_FORWARD     = 1;
        public const int MOVE_BACKWARD    = 2;
        public const int MOVE_RIGHT       = 3;
        public const int MOVE_LEFT        = 4;
        public const int SPRINT_FORWARD   = 5;
        public const int CAM_RIGHT        = 6;
        public const int CAM_LEFT         = 7;
        public const int CAM_UP           = 8;
        public const int CAM_DOWN         = 9;
        public const int INTERACT         = 10;
        public const int PICK_ITEM        = 11;
        public const int EQUIP_KEYCARD    = 12;

        // ── Curriculum 2 — SCPs ───────────────────────────────────────────────
        public const int SCP_ABILITY_PRIMARY   = 13;
        public const int SCP_ABILITY_SECONDARY = 14;
        public const int SCP_ABILITY_TERTIARY  = 15;

        /// <summary>
        /// Procesa la acción recibida de Python según el rol del bot.
        /// Devuelve false si la acción es inválida para el rol actual (se ignora).
        /// </summary>
        public static bool ProcesarAccion(int actionId, IAgentController bot, float deltaTime)
        {
            if (bot?._exiledPlayer == null || !bot._exiledPlayer.IsAlive)
                return false;

            RoleTypeId rol = bot._role;

            // ── Acciones comunes a todos los roles ────────────────────────────
            switch (actionId)
            {
                case NOOP:
                    return true;

                case MOVE_FORWARD:
                case MOVE_BACKWARD:
                case MOVE_RIGHT:
                case MOVE_LEFT:
                    bot._strategy?.MoverPersonaje(actionId, deltaTime, bot._exiledPlayer, bot._botGameObject);
                    return true;
                case SPRINT_FORWARD:
                case CAM_UP:
                case CAM_DOWN:
                case CAM_RIGHT:
                case CAM_LEFT:
                    bot._strategy?.MoverCamara(actionId);
                    return true;
                    // El movimiento lo gestiona la estrategia (BotMovement.cs)
                case INTERACT:
                    // Válido para todos — abrir puertas, usar terminales, etc.
                    bot._strategy?.AbrirPuerta(bot._exiledPlayer);
                    return true;
            }

            // ── Acciones exclusivas de humanos (11-12) ────────────────────────
            bool esHumano = _EsRolHumano(rol);

            if (actionId == PICK_ITEM)
            {
                //if (!esHumano) return false;   // SCP no puede coger items
                //bot._strategy?.OnPickItem(bot._exiledPlayer);
                return true;
            }

            if (actionId == EQUIP_KEYCARD)
            {
                if (!esHumano) return false;
                if (bot._strategy is IAgentRoleHumanStrategy humanStrategy)
                {
                    humanStrategy.EquiparTarjeta(bot._exiledPlayer);
                }
                
                return true;
            }

            // ── Acciones SCP — Curriculum 2 ───────────────────────────────────
            // Descomenta y extiende cuando implementes cada SCP.
            if (actionId == SCP_ABILITY_PRIMARY)
            {
                if (esHumano) return false;    // humanos no tienen habilidad primaria SCP
                //return _EjecutarHabilidadPrimaria(bot, deltaTime);
            }

            if (actionId == SCP_ABILITY_SECONDARY)
            {
                if (esHumano) return false;
                //return _EjecutarHabilidadSecundaria(bot, deltaTime);
            }

            if (actionId == SCP_ABILITY_TERTIARY)
            {
                if (esHumano) return false;
                //return _EjecutarHabilidadTerciaria(bot, deltaTime);
            }

            Log.Debug($"[ActionProcessor] Acción desconocida: {actionId} para rol {rol}");
            return false;
        }

        // ── Dispatcher de habilidades SCP ─────────────────────────────────────

        private static bool _EjecutarHabilidadPrimaria(ScpAgentBot bot, float deltaTime)
        {
            switch (bot._role)
            {
                // ── SCP-049 ──────────────────────────────────────────────────
                // Habilidad primaria: paro cardíaco si hay humano en rango
                case RoleTypeId.Scp049:
                    // TODO Curriculum 2: implementar lógica de paro cardíaco
                    // Ejemplo:
                    // var target = _BuscarHumanoEnRango(bot, 1.75f);
                    // if (target != null) target.Kill(DamageType.CardiacArrest);
                    Log.Debug("[ActionProcessor] SCP-049 habilidad primaria — pendiente C2");
                    return true;

                // ── SCP-096 ──────────────────────────────────────────────────
                // Habilidad primaria: activar rage si alguien lo mira
                case RoleTypeId.Scp096:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-096 habilidad primaria — pendiente C2");
                    return true;

                // ── SCP-173 ──────────────────────────────────────────────────
                // Habilidad primaria: snap (matar si nadie lo mira)
                case RoleTypeId.Scp173:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-173 habilidad primaria — pendiente C2");
                    return true;

                // ── SCP-106 ──────────────────────────────────────────────────
                // Habilidad primaria: atrapar jugador cercano
                case RoleTypeId.Scp106:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-106 habilidad primaria — pendiente C2");
                    return true;

                // ── SCP-939 ──────────────────────────────────────────────────
                // Habilidad primaria: morder
                case RoleTypeId.Scp939:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-939 habilidad primaria — pendiente C2");
                    return true;

                // ── SCP-079 ──────────────────────────────────────────────────
                // Habilidad primaria: interactuar con cámara/puerta activa
                case RoleTypeId.Scp079:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-079 habilidad primaria — pendiente C2");
                    return true;

                default:
                    return false;
            }
        }

        private static bool _EjecutarHabilidadSecundaria(ScpAgentBot bot, float deltaTime)
        {
            switch (bot._role)
            {
                // ── SCP-049 ──────────────────────────────────────────────────
                // Habilidad secundaria: zombificar cadáver cercano (mantener E)
                case RoleTypeId.Scp049:
                    // TODO Curriculum 2: buscar cadáver en rango y animar
                    Log.Debug("[ActionProcessor] SCP-049 habilidad secundaria — pendiente C2");
                    return true;

                // ── SCP-106 ──────────────────────────────────────────────────
                // Habilidad secundaria: crear bolsillo dimensional
                case RoleTypeId.Scp106:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-106 habilidad secundaria — pendiente C2");
                    return true;

                // ── SCP-079 ──────────────────────────────────────────────────
                // Habilidad secundaria: cambiar cámara / activar tesla
                case RoleTypeId.Scp079:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-079 habilidad secundaria — pendiente C2");
                    return true;

                default:
                    return false;
            }
        }

        private static bool _EjecutarHabilidadTerciaria(ScpAgentBot bot, float deltaTime)
        {
            switch (bot._role)
            {
                // ── SCP-079 ──────────────────────────────────────────────────
                // Habilidad terciaria: apagar luces / lockdown
                case RoleTypeId.Scp079:
                    // TODO Curriculum 2
                    Log.Debug("[ActionProcessor] SCP-079 habilidad terciaria — pendiente C2");
                    return true;

                default:
                    return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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