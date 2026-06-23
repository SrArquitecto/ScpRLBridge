using ScpAgent.Bot.Sensors.Data;
using Exiled.API.Features;
using UnityEngine;
using System;

namespace ScpAgent.Bot.Sensors.Modules
{
    public interface ISensorModule
    {
        void VincularPlayer(Player player);
        void Reset();
        void Actualizar(AgentObservation obs, SensorContext ctx);
    }

    public interface ISensorRoomModule : ISensorModule
    {
        void MarcarRoomDescubierta(Room sala);
    }

    public interface ISensorPlayerModule : ISensorModule
    {
        bool TieneEnMemoria(int playerId);
    }

    public interface ISensorDamageModule : ISensorModule
    {
        void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria);
    }
    public interface ISensorItemsModule : ISensorModule
    {
        void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria);
    }
    public interface ISensorInventoryModule : ISensorModule
    {
        void VincularEstrategia(Func<ItemType, string> fnCategoria);
    }
    public interface ISensorVelocityModule : ISensorModule
    {
        void SetLastPos(Vector3 pos);
        void SetLastYaw(float yaw);
        void SetLastPitch(float pitch);
    }
   
}