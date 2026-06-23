using ScpAgent.Bot.Sensors.Data;
using Exiled.API.Features;

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
}