using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Sensors.Data;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Modules;
using System;

namespace ScpAgent.Bot.Sensors
{   


    public class HumanSensors : BaseSensors
    {
        private readonly ISensorModule           _lockers   = new LockersModule();

        private readonly ISensorInventoryModule  _inventory = new InventoryModule();
        private readonly ISensorItemsModule      _items     = new ItemsModule();
        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public HumanSensors(int agentId) : base(agentId)
        {
        }

        public override void Init()
        {
            base.Init();
            _modules.Add(_lockers);
            _modules.Add(_inventory);
            _modules.Add(_items);
            // El grafo debe ir al final para ver datos frescos de TODOS los módulos
            _modules.Add(_graph);
        }

        public override void VincularEstrategia(Func<ItemType, float> fnPrioridad)
        {
            _items.VincularEstrategia(fnPrioridad);
        }

        public override AgentObservation GetCurrentState(
            float delta, int[] accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {     
            AgentObservation observation = base.GetCurrentState(delta, accionAnterior, reward, done, role, playerTier);
            Vector3 pos         = _player.Position;
            AgentCacheData data = GetData();
        
            return observation;
        }
        public override void ResetEstado()
        {
            base.ResetEstado();
        }
        public override void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }
      
        
    }
}