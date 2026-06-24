

using Exiled.API.Features;
using ScpAgent.Bot;
using ScpAgent.Bot.Interfaces;
using ScpAgent.Bot.Simulation;
using ScpAgent.Bot.Sensors.Intefaces;
using ScpAgent.Bot.Strategies.Interfaces;
using PlayerRoles;
using System;
using ScpAgent.Bot.Strategies.Human;
using ScpAgent.Bot.Sensors;

namespace ScpAgent.Managers.Data
{
    public class AgentSlot
    {
        public  int AgentId;
        public  IAgentController Bot;
        public RoleTypeId Rol;
        public IAgentRoleStrategyBase Strategy;
        public ISensors Sensors;
        public  FakeConnection FakeConnection;

        // true cuando ExiledPlayer es válido y el bot puede recibir acciones
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
        public AgentSlot(int agentId, IAgentController bot, ISensors sensors, FakeConnection fakeConn)
        {
            AgentId        = agentId;
            Bot            = bot;
            Sensors        = sensors;
            FakeConnection = fakeConn;
            IsReady        = false;
        }

        public void Instanciar(int id, string rol)
        {   
            Log.Info($"INSTANCIANDO AGENTE {id}");
            SeleccionarRol(id, rol);
            string nickname = $"IA_Agent_{id}";
            FakeConnection = null;
            Bot = new ScpAgentBot(nickname, id, Rol);
            Bot.SetStrategy(Strategy);
            IsReady = false;
        }
        /// <summary>
        /// Llamado cuando el bot ha completado el spawn y ExiledPlayer es válido.
        /// Vincula los sensores al nuevo Player wrapper y marca el slot como listo.
        /// </summary>
        public void OnSpawnComplete(Player exiledPlayer)
        {   
            Log.Info("OnSpawnComplete");
            Reset();
            //Bot.SetPlayer(exiledPlayer);
            Bot.ResetearPosicionInicial(exiledPlayer.Position);
            Sensors.VincularPlayer(exiledPlayer);
            (Bot as ScpAgentBot)._sensores = Sensors;
            IsReady = true;
        }

        /// <summary>
        /// Reset entre rondas — limpia el estado pero NO destruye nada.
        /// </summary>
        public void Reset()
        {
            IsReady = false;
            Bot.ResetEstado();
            Sensors.ResetEstado();
        }

        private void SeleccionarRol(int agentId, string role)
        {
            if (role == null)
            {
                Rol = RoleTypeId.ClassD;
                Strategy = new SurvivorStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
            else if (role == "classd")
            {
                Rol = RoleTypeId.ClassD;
                Strategy = new SurvivorStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
            else if (role == "chaos") 
            {
                Rol = RoleTypeId.ChaosRifleman;
                Strategy = new CombatStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
            else if (role == "scientist")
            {
                Rol = RoleTypeId.Scientist;
                Strategy = new SurvivorStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
            else if (role == "ntf") 
            {
                Rol = RoleTypeId.NtfPrivate;
                Strategy = new CombatStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
            else if (role == "guard") 
            {
                Rol = RoleTypeId.FacilityGuard;
                Strategy = new CombatStrategy(Rol);
                Sensors = new HumanSensors(agentId);
            }
        }
        public void ConfigurarRol(IAgentRoleStrategyBase nuevaEstrategia, ISensors nuevosSensores)
        {
            // Desvinculamos la estrategia anterior si existía una
            Strategy?.OnUnbind();

            Strategy = nuevaEstrategia;

            //Sensors = nuevosSensores;

            // Le pasamos la estrategia y los sensores directamente al bot
            //if (Bot is ScpAgentBot scpBot)
            //{
                //scpBot.SetStrategy(nuevaEstrategia);
                //scpBot.SetSensores(nuevosSensores);
            //}
        }
        
    }
}