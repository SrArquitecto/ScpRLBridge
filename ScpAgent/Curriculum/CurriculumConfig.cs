// ═══════════════════════════════════════════════════════════════════════
// 1. CurriculumConfig.cs — configuración centralizada del curriculum
//    Un singleton accesible desde cualquier parte del plugin
// ═══════════════════════════════════════════════════════════════════════
 
using System.Collections.Generic;
using Exiled.API.Features;
 
namespace ScpAgent.Curriculum
{
    public static class CurriculumConfig
    {
        // ── Flags booleanos ──────────────────────────────────────────────
        // Activar/desactivar mecánicas según la fase del curriculum
        public static bool ScpsActivos        { get; set; } = false; // sin SCPs en fase 1
        public static bool PuertasBloqueadas  { get; set; } = false; // puertas sin keycard en fase 1
        public static bool EnemigosActivos    { get; set; } = false; // sin NTF/Chaos en fase 1
        public static bool RespawnInfinito    { get; set; } = true;  // respawn sin penalty en fase 1
        public static bool MapaAleatorio      { get; set; } = true;  // mapa regenerado cada ronda
 
        // ── Parámetros numéricos ─────────────────────────────────────────
        public static float Dificultad        { get; set; } = 0.0f;  // 0.0 (fácil) - 1.0 (difícil)
        public static int   NumEnemigos       { get; set; } = 0;     // bots enemigos activos
        public static int   NumScps           { get; set; } = 0;     // SCPs activos
        public static float ProbKeycard       { get; set; } = 1.0f;  // probabilidad de keycard en spawn
        public static float TiempoMaxEpisodio { get; set; } = 300f;  // segundos max por episodio
        public static int   FaseActual        { get; set; } = 1;     // fase del curriculum actual
        public static float RecompensaScale   { get; set; } = 1.0f;  // escala global de recompensas
 
        // ── Diccionario genérico para parámetros personalizados ──────────
        // Permite añadir cualquier parámetro nuevo sin recompilar
        private static readonly Dictionary<string, float>  _numParams  = new Dictionary<string, float>();
        private static readonly Dictionary<string, bool>   _boolParams = new Dictionary<string, bool>();
        private static readonly Dictionary<string, string> _strParams  = new Dictionary<string, string>();
 
        public static float  GetParam(string key, float  defaultVal = 0f)    => _numParams.TryGetValue(key, out var v)  ? v : defaultVal;
        public static bool   GetFlag(string key,  bool   defaultVal = false)  => _boolParams.TryGetValue(key, out var v) ? v : defaultVal;
        public static string GetStr(string key,   string defaultVal = "")     => _strParams.TryGetValue(key, out var v)  ? v : defaultVal;
 
        /// <summary>
        /// Aplica un par clave-valor recibido por TCP.
        /// Formato: "CONFIG:clave=valor"
        /// </summary>
        public static bool AplicarConfig(string clave, string valor)
        {
            // Intentar parsear como float primero
            if (float.TryParse(valor, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float numVal))
            {
                // Propiedades numéricas conocidas
                switch (clave)
                {
                    case "Dificultad":         Dificultad        = UnityEngine.Mathf.Clamp01(numVal); break;
                    case "NumEnemigos":        NumEnemigos       = (int)numVal; break;
                    case "NumScps":            NumScps           = (int)numVal; break;
                    case "ProbKeycard":        ProbKeycard       = UnityEngine.Mathf.Clamp01(numVal); break;
                    case "TiempoMaxEpisodio":  TiempoMaxEpisodio = numVal; break;
                    case "FaseActual":         FaseActual        = (int)numVal; break;
                    case "RecompensaScale":    RecompensaScale   = numVal; break;
                    default:
                        _numParams[clave] = numVal;
                        break;
                }
                Log.Info($"[Curriculum] {clave} = {numVal}");
                return true;
            }
 
            // Intentar parsear como bool
            if (valor == "true" || valor == "false")
            {
                bool boolVal = valor == "true";
                switch (clave)
                {
                    case "ScpsActivos":       ScpsActivos       = boolVal; break;
                    case "PuertasBloqueadas": PuertasBloqueadas = boolVal; break;
                    case "EnemigosActivos":   EnemigosActivos   = boolVal; break;
                    case "RespawnInfinito":   RespawnInfinito   = boolVal; break;
                    case "MapaAleatorio":     MapaAleatorio     = boolVal; break;
                    default:
                        _boolParams[clave] = boolVal;
                        break;
                }
                Log.Info($"[Curriculum] {clave} = {boolVal}");
                return true;
            }
 
            // Guardar como string genérico
            _strParams[clave] = valor;
            Log.Info($"[Curriculum] {clave} = \"{valor}\"");
            return true;
        }
 
        /// <summary>
        /// Resetea a los valores por defecto (fase 1 — curriculum básico).
        /// </summary>
        public static void Reset()
        {
            ScpsActivos        = false;
            PuertasBloqueadas  = false;
            EnemigosActivos    = false;
            RespawnInfinito    = true;
            MapaAleatorio      = true;
            Dificultad         = 0.0f;
            NumEnemigos        = 0;
            NumScps            = 0;
            ProbKeycard        = 1.0f;
            TiempoMaxEpisodio  = 300f;
            FaseActual         = 1;
            RecompensaScale    = 1.0f;
            _numParams.Clear();
            _boolParams.Clear();
            _strParams.Clear();
            Log.Info("[Curriculum] Configuración reseteada a fase 1.");
        }
 
        public static string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"Fase\":{FaseActual},");
            sb.Append($"\"Dificultad\":{Dificultad.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"ScpsActivos\":{(ScpsActivos ? "true" : "false")},");
            sb.Append($"\"PuertasBloqueadas\":{(PuertasBloqueadas ? "true" : "false")},");
            sb.Append($"\"EnemigosActivos\":{(EnemigosActivos ? "true" : "false")},");
            sb.Append($"\"NumEnemigos\":{NumEnemigos},");
            sb.Append($"\"NumScps\":{NumScps},");
            sb.Append($"\"ProbKeycard\":{ProbKeycard.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"TiempoMax\":{TiempoMaxEpisodio},");
            sb.Append($"\"RecompensaScale\":{RecompensaScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
 
