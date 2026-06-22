using System;
using ScpAgent.Bot.Data;
using Exiled.API.Features;
using Exiled.API.Enums;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;

namespace ScpAgent.Bot.Sensors.Intefaces
{
    public interface ISensors
    {
        // El bot necesita pedir la observación actual sin importarle de quién sea
        AgentObservation GetCurrentState (float fixedDelta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier);
        void Init();
        void VincularPlayer(Player freshPlayer);
        void ResetEstado();
        void Destruir();
        void MarcarRoomDescubierta(Room room);
        void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria);
        void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria);
        //void _CargarDaño(AgentObservation obs);
        bool TieneEnMemoriaJugadores(int playerId);
    }
}