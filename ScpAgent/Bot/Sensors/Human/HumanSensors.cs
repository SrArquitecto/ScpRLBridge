using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using ScpAgent.Bot.Sensors.Data;
using PlayerRoles;
using ScpAgent.Bot.Sensors.Modules;


namespace ScpAgent.Bot.Sensors
{   


    public class HumanSensors : BaseSensors
    {
        private readonly ISensorModule        _lockers    = new LockersModule();

        private readonly ISensorModule        _inventory = new InventoryModule();
        private readonly ISensorModule        _items = new ItemsModule();
        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public HumanSensors(int agentId) : base(agentId)
        {
        }

        public override void Init()
        {   
            base.Init();
            
            
            
                
        }

        /*
        public void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            _fnPrioridad = fnPrioridad;
            _fnCategoria = fnCategoria;
        }
        */

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
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

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────

        // ───────────────────────────────────────────────────────────────────────
        // ELEMENTOS CERCANOS
        // ───────────────────────────────────────────────────────────────────────


    

        
        


        

        // ───────────────────────────────────────────────────────────────────────
        // AIM RAYCAST
        // ───────────────────────────────────────────────────────────────────────
    
        
        public override void ResetEstado()
        {
            base.ResetEstado();
            // ── Estado de movimiento ─────────────────────────────────────────
 
            // ── Listas de entorno cercano ────────────────────────────────────
            

            // ── Contador de frames ───────────────────────────────────────────
            //_frameCounter = 0;

            Log.Debug($"[AgentSensors] Sensores reseteados para nueva ronda.");
        }

        public override void Destruir()
        {
            _player = null;
            ResetEstado();    
        
        }

        


        
        public string CategorizarItem(ItemType tipo)
        {
            string s = tipo.ToString();
            if (s.StartsWith("Gun"))              return "Weapon";
            if (s.StartsWith("Ammo"))             return "Ammo";
            if (s.StartsWith("Armor"))            return "Armor";
            if (s.Contains("Keycard"))            return "Keycard";
            if (s == "Medkit" || s == "Painkillers" || s == "Adrenaline") return "Medical";
            if (s.StartsWith("Grenade") || s == "SCP018") return "Tactical";
            return "Other";
        }
    // implementa tu lógica

        
        
    }
}