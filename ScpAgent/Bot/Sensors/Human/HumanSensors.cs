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
                
        }

        public override void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            _items.VincularEstrategia(fnPrioridad, fnCategoria);
            _inventory.VincularEstrategia(fnCategoria);
        }

        public override AgentObservation GetCurrentState(
            float delta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {   
            if (_player == null || !_player.IsAlive || _player.GameObject == null) 
                return obsVacia;

            // Si la cámara aún no se ha creado en el nuevo cuerpo, abortamos este frame
            if (_player.CameraTransform == null) 
                return obsVacia;
            
            AgentObservation observation = base.GetCurrentState(delta, accionAnterior, reward, done, role, playerTier);


            Vector3 pos         = _player.Position;
            AgentCacheData data = GetData();
            
            
            

            
            //if (_player.Nickname == "IA_Agent_0")
                //Log.Info($"PLAYER {_player.Nickname} ACTION: {accionAnterior} | POSICION: {pos} | AIMTARGET: {observation.AimTarget} | AIMDISTANCE: {observation.AimDistance} | VEL LINEAL: {vLin} | VEL LATERAL: {vLat} | VEL VERTICAL: {vLin} | VEL ANGULAR: {angVelYaw}");
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