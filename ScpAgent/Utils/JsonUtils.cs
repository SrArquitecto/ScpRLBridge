using ScpAgent.Bot.Data;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public static class JsonUtils
{
    private static readonly string JSON_DONE_TRUE = "{\"Done\":true}";

    public static string ToJson(AgentObservation obs)
    {
        if (obs == null) return "{}";

        // Inicializamos con suficiente capacidad para evitar redimensionamientos del buffer
        StringBuilder sb = new StringBuilder(4096);
        sb.Append("{");
        
        // Físicas y GPS (Precisión 3 decimales)
        sb.Append("\"PosX\":"); AppendFloat(sb, obs.PosX, 3); sb.Append(",");
        sb.Append("\"PosY\":"); AppendFloat(sb, obs.PosY, 3); sb.Append(",");
        sb.Append("\"PosZ\":"); AppendFloat(sb, obs.PosZ, 3); sb.Append(",");
        sb.Append("\"RelX\":"); AppendFloat(sb, obs.RelX, 3); sb.Append(",");
        sb.Append("\"RelY\":"); AppendFloat(sb, obs.RelY, 3); sb.Append(",");
        sb.Append("\"RelZ\":"); AppendFloat(sb, obs.RelZ, 3); sb.Append(",");
        sb.Append("\"GPSX\":"); AppendFloat(sb, obs.GPSX, 3); sb.Append(",");
        sb.Append("\"GPSY\":"); AppendFloat(sb, obs.GPSY, 3); sb.Append(",");
        sb.Append("\"GPSZ\":"); AppendFloat(sb, obs.GPSZ, 3); sb.Append(",");
        
        // Rotación y Velocidad (Precisión 2 decimales)
        sb.Append("\"Yaw\":"); AppendFloat(sb, obs.Yaw, 2); sb.Append(",");
        sb.Append("\"Pitch\":"); AppendFloat(sb, obs.Pitch, 2); sb.Append(",");
        sb.Append("\"VerVel\":"); AppendFloat(sb, obs.VerVel, 2); sb.Append(",");
        sb.Append("\"LatVel\":"); AppendFloat(sb, obs.LatVel, 2); sb.Append(",");
        sb.Append("\"LinVel\":"); AppendFloat(sb, obs.LinVel, 2); sb.Append(",");
        sb.Append("\"AngVelYaw\":"); AppendFloat(sb, obs.AngVelYaw, 2); sb.Append(",");
        sb.Append("\"AngVelPitch\":"); AppendFloat(sb, obs.AngVelPitch, 2); sb.Append(",");
        
        // Estado del Jugador
        sb.Append("\"Health\":"); AppendFloat(sb, obs.Health, 1); sb.Append(",");
        sb.Append("\"Zone\":\"").Append(obs.Zone ?? "").Append("\",");
        sb.Append("\"Room\":\"").Append(obs.Room ?? "").Append("\",");
        sb.Append("\"HasKeycard\":").Append(obs.HasKeycard ? "true" : "false").Append(",");
        sb.Append("\"KeycardTier\":").Append(obs.KeycardTier).Append(",");
        sb.Append("\"LastAction\":").Append(obs.LastAction).Append(",");
        sb.Append("\"Reward\":"); AppendFloat(sb, obs.Reward, 4); sb.Append(",");
        
        // Datos de Aim y Hit
        sb.Append("\"AimTarget\":\"").Append(obs.AimTarget ?? "").Append("\",");
        sb.Append("\"AimDistance\":"); AppendFloat(sb, obs.AimDistance, 3); sb.Append(",");
        sb.Append("\"AimRoom\":\"").Append(obs.AimRoom ?? "").Append("\",");
        sb.Append("\"AimDoorName\":\"").Append(obs.AimDoorName ?? "").Append("\",");
        sb.Append("\"HitName\":\"").Append(obs.HitName ?? "").Append("\",");
        sb.Append("\"HitX\":"); AppendFloat(sb, obs.HitX, 3); sb.Append(",");
        sb.Append("\"HitY\":"); AppendFloat(sb, obs.HitY, 3); sb.Append(",");
        sb.Append("\"HitZ\":"); AppendFloat(sb, obs.HitZ, 3); sb.Append(",");
        sb.Append("\"ForwardX\":"); AppendFloat(sb, obs.ForwardX, 3); sb.Append(",");
        sb.Append("\"ForwardZ\":"); AppendFloat(sb, obs.ForwardZ, 3);

        // Listas con TRUE ZERO GC ALLOCATION utilizando delegados por referencia de buffer
        AppendList(sb, "NearKeycards", obs.NearKeycards, (builder, k) => {
            builder.Append("\"Type\":\"").Append(k.Type ?? "").Append("\",\"Distance\":"); AppendFloat(builder, k.Distance, 3);
            builder.Append(",\"RelX\":"); AppendFloat(builder, k.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, k.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, k.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, k.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, k.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, k.RealRelZ, 3);
        });

        AppendList(sb, "NearDoors", obs.NearDoors, (builder, d) => {
            builder.Append("\"Name\":\"").Append(d.Name ?? "").Append("\",\"ColliderName\":\"").Append(d.ColliderName ?? "");
            builder.Append("\",\"Distance\":"); AppendFloat(builder, d.Distance, 3);
            builder.Append(",\"RequiredTier\":").Append(d.RequiredTier); // Corregido: Sin comillas para Python
            builder.Append(",\"CanOpen\":").Append(d.CanOpen ? "true" : "false");
            builder.Append(",\"IsOpen\":").Append(d.IsOpen ? "true" : "false");
            builder.Append(",\"RelX\":"); AppendFloat(builder, d.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, d.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, d.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, d.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, d.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, d.RealRelZ, 3);
        });

        AppendList(sb, "NearLifts", obs.NearLifts, (builder, l) => {
            builder.Append("\"Type\":\"").Append(l.Type ?? "").Append("\",\"Distance\":"); AppendFloat(builder, l.Distance, 3);
            builder.Append(",\"IsMoving\":").Append(l.IsMoving ? "true" : "false");
            builder.Append(",\"CanUse\":").Append(l.CanUse ? "true" : "false");
            builder.Append(",\"CurrentLevel\":").Append(l.CurrentLevel);
            builder.Append(",\"RelX\":"); AppendFloat(builder, l.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, l.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, l.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, l.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, l.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, l.RealRelZ, 3);
        });

        AppendList(sb, "NearLockers", obs.NearLockers, (builder, l) => {
            builder.Append("\"Type\":\"").Append(l.Type ?? "").Append("\",\"HasIsOpen\":").Append(l.HasIsOpen ? "true" : "false");
            builder.Append(",\"Distance\":"); AppendFloat(builder, l.Distance, 3);
            builder.Append(",\"RelX\":"); AppendFloat(builder, l.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, l.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, l.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, l.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, l.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, l.RealRelZ, 3);
        });

        AppendList(sb, "NearRooms", obs.NearRooms, (builder, r) => {
            builder.Append("\"Nombre\":\"").Append(r.Nombre ?? "").Append("\",\"Id\":").Append("\",\"Dist\":").Append(r.Id); AppendFloat(builder, r.Dist, 3);
            builder.Append(",\"Prioridad\":"); AppendFloat(builder, r.Prioridad, 1);
            builder.Append(",\"PosX\":"); AppendFloat(builder, r.PosX, 3);
            builder.Append(",\"PosY\":"); AppendFloat(builder, r.PosY, 3);
            builder.Append(",\"PosZ\":"); AppendFloat(builder, r.PosZ, 3);
            builder.Append(",\"UbiX\":"); AppendFloat(builder, r.UbiX, 3);
            builder.Append(",\"UbiY\":"); AppendFloat(builder, r.UbiY, 3);
            builder.Append(",\"UbiZ\":"); AppendFloat(builder, r.UbiZ, 3);
            builder.Append(",\"NormX\":"); AppendFloat(builder, r.NormX, 3);
            builder.Append(",\"NormY\":"); AppendFloat(builder, r.NormY, 3);
            builder.Append(",\"NormZ\":"); AppendFloat(builder, r.NormZ, 3);
        });

        sb.Append(",\"Done\":").Append(obs.Done ? "true" : "false");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// Formatea e inyecta un float en el StringBuilder con precisión fija de decimales SIN generar Garbage Collector.
    /// </summary>
    private static void AppendFloat(StringBuilder sb, float val, int decimals)
    {
        if (float.IsNaN(val) || float.IsInfinity(val))
        {
            sb.Append("0");
            return;
        }

        if (val < 0)
        {
            sb.Append("-");
            val = -val;
        }

        long inlineMultiplier = 1;
        if (decimals == 1) inlineMultiplier = 10;
        else if (decimals == 2) inlineMultiplier = 100;
        else if (decimals == 3) inlineMultiplier = 1000;
        else if (decimals == 4) inlineMultiplier = 10000;
        else
        {
            for (int i = 0; i < decimals; i++) inlineMultiplier *= 10;
        }

        // Redondeo aritmético manual rápido y seguro para números positivos (.5f)
        long total = (long)(val * inlineMultiplier + 0.5f);
        long integerPart = total / inlineMultiplier;
        long fractionalPart = total % inlineMultiplier;

        sb.Append(integerPart);

        if (decimals > 0)
        {
            sb.Append(".");
            long check = inlineMultiplier / 10;
            while (check > 0)
            {
                long digit = fractionalPart / check;
                sb.Append((char)('0' + digit)); // Inyección de carácter pura sin boxing
                fractionalPart %= check;
                check /= 10;
            }
        }
    }

    private static void AppendList<T>(StringBuilder sb, string key, List<T> list, System.Action<StringBuilder, T> appendAction)
    {
        if (list == null) return;
        sb.Append(",\"").Append(key).Append("\":[");
        for (int i = 0; i < list.Count; i++) {
            sb.Append("{");
            appendAction(sb, list[i]);
            sb.Append("}");
            if (i < list.Count - 1) sb.Append(",");
        }
        sb.Append("]");
    }
}
