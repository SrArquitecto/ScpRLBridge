using ScpAgent.Bot.Data;
using Exiled.API.Features;


namespace ScpAgent.Bot.Sensors.Intefaces
{
    public interface ISensors
    {
        // El bot necesita pedir la observación actual sin importarle de quién sea
        AgentObservation GetCurrentState (float fixedDelta, int accionAnterior, float reward, bool done);
        void Init();
        void VincularPlayer(Player freshPlayer);
        void ResetEstado();
        void Destruir();
    }
}