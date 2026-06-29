using Exiled.API.Features;
using ScpAgent.Bot;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Simulation;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Strategies.Interfaces;
using PlayerRoles;

namespace ScpAgent.Managers.Data
{
    public class AgentSlot
    {
        public int AgentId;
        public IAgentController Bot;
        public RoleTypeId Rol;
        public IAgentRoleBaseStrategy Strategy;
        public ISensors Sensors;
        public FakeConnection FakeConnection;

        public bool IsReady { get; private set; }

        public AgentSlot(int agentId)
        {
            AgentId = agentId;
            Bot = null;
            Sensors = null;
            Strategy = null;
            Rol = RoleTypeId.CustomRole;
            FakeConnection = null;
        }

        public void Initialize(string roleName)
        {
            Log.Info($"[AgentSlot] Inicializando agente {AgentId} con rol {roleName}");

            var (rol, strategy, sensors) = RoleFactory.CreateForRole(roleName, AgentId);
            
            Rol = rol;
            Strategy = strategy;
            Sensors = sensors;
            
            Bot = new ScpAgentBot($"IA_Agent_{AgentId}", AgentId, Rol);
            Bot.SetStrategy(Strategy);
            Bot._sensores = Sensors;
            
            IsReady = false;
        }

        public void IniciarConexion()
        {
            FakeConnection = new FakeConnection(-1000 - AgentId);
            Bot.Init(FakeConnection);
            Sensors.Init();
        }

        public void OnSpawnComplete(Player exiledPlayer)
        {
            Bot.ResetearPosicionInicial(exiledPlayer.Position);
            Sensors.VincularPlayer(exiledPlayer);
            IsReady = true;
            
            // Registrar sala inicial en el grafo (RoomChanged no se dispara en el primer spawn)
            if (exiledPlayer.CurrentRoom != null && exiledPlayer.CurrentRoom.Type != Exiled.API.Enums.RoomType.Unknown)
            {
                Bot._sensores?.RegistrarTransicion(null, exiledPlayer.CurrentRoom);
                Bot._strategy?.OnRoomChanged(null, exiledPlayer.CurrentRoom);
            }
        }

        public void Reset()
        {
            IsReady = false;
            Bot.ResetEstado();
            Sensors.ResetEstado();
        }

        public void Destruir()
        {
            try
            {
                Bot?.Destruir();
                Sensors?.Destruir();
                FakeConnection?.Disconnect();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[AgentSlot] Error destruyendo agente {AgentId}: {ex.Message}");
            }
        }
    }
}
