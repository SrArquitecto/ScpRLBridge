using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class PlayerVisionModule : ISensorModule
    {
        private Player _player;
        public PlayerVisionModule()
        {
            
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            
        }

    }
}