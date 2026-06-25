using System.Text;
using System.Collections.Generic;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Data;
using System;


public static class JsonUtils
{
    private static readonly string JSON_DONE_TRUE = "{\"Done\":true}";
    [ThreadStatic] private static StringBuilder _sb;

    public static string ToJson(AgentObservation obs, RoleTypeId rol)
    {
        if (obs == null) return "{}";

        if (_sb == null) _sb = new StringBuilder(8192);
        _sb.Clear();
        StringBuilder sb = _sb;
        sb.Append("{");

        sb.Append("\"Faccion\":\"").Append(obs.Faction.ToString() ?? "Unknown").Append("\",");
        sb.Append("\"FactionId\":"); AppendFloat(sb, obs.FactionId, 3); sb.Append(",");
        sb.Append("\"Role\":\"").Append(obs.Role.ToString() ?? "Unknown").Append("\",");
        sb.Append("\"RoleId\":"); AppendFloat(sb, obs.RoleId, 3); sb.Append(",");
        sb.Append("\"PosX\":"); AppendFloat(sb, obs.PosX, 3); sb.Append(",");
        sb.Append("\"PosY\":"); AppendFloat(sb, obs.PosY, 3); sb.Append(",");
        sb.Append("\"PosZ\":"); AppendFloat(sb, obs.PosZ, 3); sb.Append(",");
        sb.Append("\"RelX\":"); AppendFloat(sb, obs.PosicionLocalX, 3); sb.Append(",");
        sb.Append("\"RelY\":"); AppendFloat(sb, obs.PosicionLocalY, 3); sb.Append(",");
        sb.Append("\"RelZ\":"); AppendFloat(sb, obs.PosicionLocalZ, 3); sb.Append(",");
        sb.Append("\"GPSX\":"); AppendFloat(sb, obs.GPSX, 3); sb.Append(",");
        sb.Append("\"GPSY\":"); AppendFloat(sb, obs.GPSY, 3); sb.Append(",");
        sb.Append("\"GPSZ\":"); AppendFloat(sb, obs.GPSZ, 3); sb.Append(",");

        sb.Append("\"Yaw\":"); AppendFloat(sb, obs.Yaw, 2); sb.Append(",");
        sb.Append("\"Pitch\":"); AppendFloat(sb, obs.Pitch, 2); sb.Append(",");
        sb.Append("\"VerVel\":"); AppendFloat(sb, obs.VerVel, 2); sb.Append(",");
        sb.Append("\"LatVel\":"); AppendFloat(sb, obs.LatVel, 2); sb.Append(",");
        sb.Append("\"LinVel\":"); AppendFloat(sb, obs.LinVel, 2); sb.Append(",");
        sb.Append("\"AngVelYaw\":"); AppendFloat(sb, obs.AngVelYaw, 2); sb.Append(",");
        sb.Append("\"AngVelPitch\":"); AppendFloat(sb, obs.AngVelPitch, 2); sb.Append(",");

        sb.Append("\"Health\":"); AppendFloat(sb, obs.Health, 1); sb.Append(",");
        sb.Append("\"AmIHurt\":").Append(obs.AmIHurt ? "true" : "false").Append(",");
        sb.Append("\"Zone\":\"").Append(obs.Zone ?? "").Append("\",");
        sb.Append("\"Room\":\"").Append(obs.Room ?? "").Append("\",");
        sb.Append("\"CurrentRoomTypeId\":").Append(obs.CurrentRoomTypeId).Append(",");
        sb.Append("\"HasKeycard\":").Append(obs.HasKeycard ? "true" : "false").Append(",");
        sb.Append("\"KeycardTier\":").Append(obs.KeycardTier).Append(",");
        sb.Append("\"CanInteract\":").Append(obs.CanInteract).Append(",");
        sb.Append("\"LastAction\":").Append(obs.LastAction).Append(",");
        sb.Append("\"TimeLastAction\":"); AppendFloat(sb, obs.TimeLastAction, 3); sb.Append(",");
        sb.Append("\"RoundTimeRemaining\":"); AppendFloat(sb, obs.RoundTimeRemaining, 2); sb.Append(",");
        sb.Append("\"Reward\":"); AppendFloat(sb, obs.Reward, 4); sb.Append(",");

        sb.Append("\"InventorySlots\":").Append(obs.InventorySlots).Append(",");
        sb.Append("\"Ammo9x19\":").Append(obs.Ammo9x19).Append(",");
        sb.Append("\"Ammo12gauge\":").Append(obs.Ammo12gauge).Append(",");
        sb.Append("\"Ammo556x45\":").Append(obs.Ammo556x45).Append(",");
        sb.Append("\"Ammo762x39\":").Append(obs.Ammo762x39).Append(",");
        sb.Append("\"Ammo44cal\":").Append(obs.Ammo44cal).Append(",");
        sb.Append("\"CountKeycards\":"); AppendFloat(sb, obs.CountKeycards, 1); sb.Append(",");
        sb.Append("\"CountFirearms\":"); AppendFloat(sb, obs.CountFirearms, 1); sb.Append(",");
        sb.Append("\"CountMedicals\":"); AppendFloat(sb, obs.CountMedicals, 1); sb.Append(",");
        sb.Append("\"CountArmor\":"); AppendFloat(sb, obs.CountArmor, 1); sb.Append(",");
        sb.Append("\"CountGrenades\":"); AppendFloat(sb, obs.CountGrenades, 1); sb.Append(",");
        sb.Append("\"CountScpItems\":"); AppendFloat(sb, obs.CountScpItems, 1); sb.Append(",");
        sb.Append("\"CountOthers\":"); AppendFloat(sb, obs.CountOthers, 1);

        AppendList(sb, "Inventory", obs.Inventory, (builder, item) => {
            builder.Append("\"Type\":\"").Append(item.Type ?? "").Append("\"");
            builder.Append(",\"Category\":\"").Append(item.Category ?? "").Append("\"");
            builder.Append(",\"Tier\":").Append(item.Tier);
            builder.Append(",\"IsEquipped\":").Append(item.IsEquipped ? "true" : "false");
            builder.Append(",\"Ammo\":").Append(item.Ammo);
        });

        sb.Append(",\"AimTarget\":"); AppendFloat(sb, obs.AimTarget, 2); sb.Append(",");
        sb.Append("\"AimDistance\":"); AppendFloat(sb, obs.AimDistance, 3); sb.Append(",");
        sb.Append("\"AimRoom\":\"").Append(obs.AimRoom ?? "").Append("\",");
        sb.Append("\"AimDoorName\":\"").Append(obs.AimDoorName ?? "").Append("\",");
        sb.Append("\"HitName\":\"").Append(obs.HitName ?? "").Append("\",");
        sb.Append("\"HitX\":"); AppendFloat(sb, obs.HitX, 3); sb.Append(",");
        sb.Append("\"HitY\":"); AppendFloat(sb, obs.HitY, 3); sb.Append(",");
        sb.Append("\"HitZ\":"); AppendFloat(sb, obs.HitZ, 3); sb.Append(",");
        sb.Append("\"ForwardX\":"); AppendFloat(sb, obs.ForwardX, 3); sb.Append(",");
        sb.Append("\"ForwardZ\":"); AppendFloat(sb, obs.ForwardZ, 3);

        sb.Append(",\"DamageReceived\":"); AppendFloat(sb, obs.DamageReceived, 3);
        sb.Append(",\"TimeSinceLastDamage\":"); AppendFloat(sb, obs.TimeSinceLastDamage, 3);
        sb.Append(",\"DamageType\":\"").Append(obs.DamageType ?? "None").Append("\"");
        sb.Append(",\"DamageDirX\":"); AppendFloat(sb, obs.DamageDirX, 3);
        sb.Append(",\"DamageDirZ\":"); AppendFloat(sb, obs.DamageDirZ, 3);
        sb.Append(",\"AttackerInMemory\":").Append(obs.AttackerInMemory ? "true" : "false");

        AppendList(sb, "NearPlayers", obs.NearPlayers, (builder, pd) => {
            builder.Append("\"Role\":\"").Append(pd.Role ?? "").Append("\"");
            builder.Append(",\"Team\":\"").Append(pd.Team ?? "").Append("\"");
            builder.Append(",\"FactionId\":").Append(pd.FactionId);
            builder.Append(",\"Distance\":"); AppendFloat(builder, pd.Distance, 3);
            builder.Append(",\"RelX\":"); AppendFloat(builder, pd.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, pd.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, pd.RelZ, 3);
            builder.Append(",\"Hostilidad\":"); AppendFloat(builder, pd.Hostilidad, 2);
            builder.Append(",\"Health\":"); AppendFloat(builder, pd.Health, 3);
            builder.Append(",\"MiradaHaciaMi\":"); AppendFloat(builder, pd.MiradaHaciaMi, 2);
            builder.Append(",\"EsRecordado\":").Append(pd.EsRecordado ? "true" : "false");
            builder.Append(",\"Antiguedad\":"); AppendFloat(builder, pd.Antiguedad, 2);
        });

        sb.Append(",\"CountEnemies\":"); AppendFloat(sb, obs.CountEnemies, 1);
        sb.Append(",\"CountFriends\":"); AppendFloat(sb, obs.CountFriends, 1);
        sb.Append(",\"CountNeutrals\":"); AppendFloat(sb, obs.CountNeutrals, 1);
        sb.Append(",\"ClosetEnemyDistance\":"); AppendFloat(sb, obs.ClosestEnemyDistance, 3);

        /*

        Actualizado con grafos

        AppendList(sb, "NearRooms", obs.NearRooms, (builder, r) => {
            builder.Append("\"Nombre\":\"").Append(r.Nombre ?? "").Append("\"");
            builder.Append(",\"Id\":").Append(r.Id);
            builder.Append(",\"Dist\":"); AppendFloat(builder, r.Dist, 3);
            builder.Append(",\"DistNorm\":"); AppendFloat(builder, r.DistNorm, 4);
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
            builder.Append(",\"EsRecordado\":").Append(r.EsRecordado ? "true" : "false");
            builder.Append(",\"Antiguedad\":"); AppendFloat(builder, r.Antiguedad, 2);
        });
        
        */

        AppendList(sb, "NearDoors", obs.NearDoors, (builder, d) => {
            builder.Append("\"Name\":\"").Append(d.Name ?? "").Append("\",\"ColliderName\":\"").Append(d.ColliderName ?? "");
            builder.Append("\",\"Distance\":"); AppendFloat(builder, d.Distance, 3);
            builder.Append(",\"RequiredTier\":").Append(d.RequiredTier);
            builder.Append(",\"CanOpen\":").Append(d.CanOpen ? "true" : "false");
            builder.Append(",\"IsOpen\":").Append(d.IsOpen ? "true" : "false");
            builder.Append(",\"RelX\":"); AppendFloat(builder, d.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, d.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, d.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, d.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, d.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, d.RealRelZ, 3);
            builder.Append(",\"EsRecordado\":").Append(d.EsRecordado ? "true" : "false");
            builder.Append(",\"Antiguedad\":"); AppendFloat(builder, d.Antiguedad, 2);
        });

        AppendList(sb, "NearLifts", obs.NearLifts, (builder, l) => {
            builder.Append("\"Type\":\"").Append(l.Type ?? "").Append("\",\"Distance\":"); AppendFloat(builder, l.Distance, 3);
            builder.Append(",\"IsMoving\":").Append(l.IsMoving ? "true" : "false");
            builder.Append(",\"IsLocked\":").Append(l.IsLocked ? "true" : "false");
            builder.Append(",\"IsClosed\":").Append(l.IsClosed ? "true" : "false");
            builder.Append(",\"CanUse\":").Append(l.CanUse ? "true" : "false");
            builder.Append(",\"CurrentLevel\":").Append(l.CurrentLevel);
            builder.Append(",\"IsInElevator\":").Append(l.IsInElevator ? "true" : "false");
            builder.Append(",\"RelX\":"); AppendFloat(builder, l.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, l.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, l.RelZ, 3);
            builder.Append(",\"RealRelX\":"); AppendFloat(builder, l.RealRelX, 3);
            builder.Append(",\"RealRelY\":"); AppendFloat(builder, l.RealRelY, 3);
            builder.Append(",\"RealRelZ\":"); AppendFloat(builder, l.RealRelZ, 3);
            builder.Append(",\"EsRecordado\":").Append(l.EsRecordado ? "true" : "false");
            builder.Append(",\"Antiguedad\":"); AppendFloat(builder, l.Antiguedad, 2);
        });

        sb.Append(",\"WhiskerDist\":[");
        for (int i = 0; i < 8; i++) {
            AppendFloat(sb, obs.WhiskerDist?[i] ?? 1f, 2);
            if (i < 7) sb.Append(",");
        }
        sb.Append("]");

        sb.Append(",\"WhiskerType\":[");
        for (int i = 0; i < 8; i++) {
            AppendFloat(sb, obs.WhiskerType?[i] ?? 0f, 2);
            if (i < 7) sb.Append(",");
        }
        sb.Append("]");

        if (
            rol == RoleTypeId.ChaosConscript || rol == RoleTypeId.ChaosMarauder || rol == RoleTypeId.ChaosRepressor ||
            rol == RoleTypeId.ChaosRifleman || rol == RoleTypeId.NtfCaptain || rol == RoleTypeId.NtfPrivate || rol == RoleTypeId.NtfSergeant ||
            rol == RoleTypeId.NtfSpecialist || rol == RoleTypeId.FacilityGuard || rol == RoleTypeId.ClassD || rol == RoleTypeId.Scientist
        )
        {
            AppendList(sb, "NearItems", obs.NearItems, (builder, item) => {
                builder.Append("\"Type\":\"").Append(item.Type ?? "").Append("\"");
                builder.Append(",\"Category\":\"").Append(item.Category ?? "").Append("\"");
                builder.Append(",\"Prioridad\":"); AppendFloat(builder, item.Prioridad, 2);
                builder.Append(",\"Tier\":").Append(item.Tier);
                builder.Append(",\"Distance\":"); AppendFloat(builder, item.Distance, 3);
                builder.Append(",\"RelX\":"); AppendFloat(builder, item.RelX, 3);
                builder.Append(",\"RelY\":"); AppendFloat(builder, item.RelY, 3);
                builder.Append(",\"RelZ\":"); AppendFloat(builder, item.RelZ, 3);
                builder.Append(",\"RealRelX\":"); AppendFloat(builder, item.RealRelX, 3);
                builder.Append(",\"RealRelY\":"); AppendFloat(builder, item.RealRelY, 3);
                builder.Append(",\"RealRelZ\":"); AppendFloat(builder, item.RealRelZ, 3);
                builder.Append(",\"EsRecordado\":").Append(item.EsRecordado ? "true" : "false");
                builder.Append(",\"Antiguedad\":"); AppendFloat(builder, item.Antiguedad, 2);
            });

            AppendList(sb, "NearLockers", obs.NearLockers, (builder, l) => {
                builder.Append("\"Type\":\"").Append(l.Type ?? "").Append("\"");
                builder.Append(",\"HasIsOpen\":").Append(l.HasIsOpen ? "true" : "false");
                builder.Append(",\"IsOpen\":").Append(l.IsOpen ? "true" : "false");
                builder.Append(",\"Distance\":"); AppendFloat(builder, l.Distance, 3);
                builder.Append(",\"RelX\":"); AppendFloat(builder, l.RelX, 3);
                builder.Append(",\"RelY\":"); AppendFloat(builder, l.RelY, 3);
                builder.Append(",\"RelZ\":"); AppendFloat(builder, l.RelZ, 3);
                builder.Append(",\"RealRelX\":"); AppendFloat(builder, l.RealRelX, 3);
                builder.Append(",\"RealRelY\":"); AppendFloat(builder, l.RealRelY, 3);
                builder.Append(",\"RealRelZ\":"); AppendFloat(builder, l.RealRelZ, 3);
                builder.Append(",\"EsRecordado\":").Append(l.EsRecordado ? "true" : "false");
                builder.Append(",\"Antiguedad\":"); AppendFloat(builder, l.Antiguedad, 2);
            });
        }

        AppendList(sb, "GraphNodes", obs.GraphNodes, (builder, n) => {
            builder.Append("\"Id\":").Append(n.Id);
            builder.Append(",\"TypeId\":").Append(n.TypeId);
            builder.Append(",\"RelX\":"); AppendFloat(builder, n.RelX, 3);
            builder.Append(",\"RelY\":"); AppendFloat(builder, n.RelY, 3);
            builder.Append(",\"RelZ\":"); AppendFloat(builder, n.RelZ, 3);
            builder.Append(",\"PosX\":"); AppendFloat(builder, n.PosX, 3);
            builder.Append(",\"PosY\":"); AppendFloat(builder, n.PosY, 3);
            builder.Append(",\"PosZ\":"); AppendFloat(builder, n.PosZ, 3);
            builder.Append(",\"Prioridad\":"); AppendFloat(builder, n.Prioridad, 2);
            builder.Append(",\"Distancia\":"); AppendFloat(builder, n.Distancia, 3);
            builder.Append(",\"DistNorm\":"); AppendFloat(builder, n.DistNorm, 3);
            builder.Append(",\"VisitCount\":").Append(n.VisitCount);
            builder.Append(",\"Antiguedad\":"); AppendFloat(builder, n.Antiguedad, 2);
            builder.Append(",\"EsActual\":"); AppendFloat(builder, n.EsActual, 1);
            builder.Append(",\"TieneEnemigo\":"); AppendFloat(builder, n.TieneEnemigo, 1);
            builder.Append(",\"TieneLoot\":"); AppendFloat(builder, n.TieneLoot, 1);
            builder.Append(",\"PuertaBloq\":"); AppendFloat(builder, n.PuertaBloq, 1);
        });

        sb.Append(",\"GraphAdjacency\":[");
        bool firstAdj = true;
        for (int i = 0; i < 16; i++) {
            for (int j = 0; j < 16; j++) {
                if (!firstAdj) sb.Append(",");
                AppendFloat(sb, obs.GraphAdjacency[i, j], 1);
                firstAdj = false;
            }
        }
        sb.Append("]");

        sb.Append(",\"GraphMask\":[");
        for (int i = 0; i < 16; i++) {
            AppendFloat(sb, obs.GraphMask[i], 1);
            if (i < 15) sb.Append(",");
        }
        sb.Append("]");

        sb.Append(",\"Done\":").Append(obs.Done ? "true" : "false");
        sb.Append("}");
        return sb.ToString();
    }

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
                sb.Append((char)('0' + digit));
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
