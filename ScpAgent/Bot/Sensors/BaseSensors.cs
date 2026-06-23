using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;
using PlayerRoles;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules;


namespace ScpAgent.Bot.Sensors
{

    public abstract class BaseSensors : ISensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
        protected Player _player;
        protected int _agentId;
        public static readonly AgentObservation obsVacia = new AgentObservation { Done = true };
        

        // ── Estado de movimiento ────────────────────────────────────────────────
        protected Vector3 _lastPos;
        protected float   _lastYaw;
        protected float   _lastPitch;


        // ── Cache de datos de sala por agente ──────────────────────────────────
        public static Dictionary<int, AgentCacheData> agentCacheData = new Dictionary<int, AgentCacheData>();
        protected static readonly AgentCacheData _fallbackCacheData = new AgentCacheData 
        { 
            center = Vector3.zero,
            halfX = Vector3.one.x * 10f,
            halfY = Vector3.one.y * 10f,
            halfZ = Vector3.one.z * 10f,
        };

       
        //ESTRATEGIAS:
        protected Func<ItemType, float> _fnPrioridad;
        protected Func<ItemType, string> _fnCategoria;

        //MODULOS DE SENSORES
        protected readonly ISensorPlayerModule  _players        = new PlayerVisionModule();
        protected readonly ISensorDamageModule  _damage         = new DamageModule();
        protected readonly ISensorRoomModule    _rooms          = new RoomsModule();
        protected readonly ISensorModule        _doors          = new DoorsModule();
        protected readonly ISensorModule        _lifts          = new LiftsModule();
        protected readonly ISensorModule        _velocity       = new VelocityModule();
        protected readonly ISensorModule        _whiskers       = new WhiskersModule();
        protected readonly ISensorModule        _basic          = new BasicPlayerModule();
        protected readonly ISensorModule        _aim            = new WhiskersModule();

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public BaseSensors(int agentId)
        {
            _agentId = agentId;
        }

        public virtual void Init()
        {
            
            
        }

        /// <summary>
        /// Actualiza la referencia al jugador tras un respawn sin recrear la instancia.
        /// Preserva las cachés de listas globales (puertas, lifts, keycards).
        /// </summary>
        public void VincularPlayer(Player freshPlayer)
        {
            _player = freshPlayer;

            // Actualizar posición base con la nueva posición de spawn
            if (_player != null)
            {
                _lastPos   = _player.Position;
                _lastYaw   = _player.CameraTransform.rotation.eulerAngles.y;
                _lastPitch = _player.CameraTransform.rotation.eulerAngles.x;
            }

            Log.Debug($"[AgentSensors] Player vinculado: {freshPlayer?.Nickname}");
        }

        public void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria)
        {
            _fnPrioridad = fnPrioridad;
            _fnCategoria = fnCategoria;
        }

        /// <summary>
        /// Invalida las cachés de objetos del mapa (llamar tras Round.Restart).
        /// </summary>

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public virtual AgentObservation GetCurrentState(
            float delta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {

            Vector3 pos         = _player.Position;
            Vector3 camRotation = _player.CameraTransform.rotation.eulerAngles;
            AgentCacheData data = GetData();
            Vector3 relativePos = pos - data.center;

            AgentObservation obs = new AgentObservation();
            


            //Actualizamos ultimas posiciones de la camara y el personaje
            _lastYaw   = camRotation.y;
            _lastPitch = camRotation.x;
            _lastPos = pos;

            return obs;
        }
        
        public void MarcarRoomDescubierta(Room sala)
        {
            _rooms.MarcarRoomDescubierta(sala);
        }

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────

        protected AgentCacheData GetData()
        {
            AgentCacheData data;
            if (_player == null || !agentCacheData.TryGetValue(_player.Id, out data) || !data.IsDataReady)
            {
                data = _fallbackCacheData;
            }
            return data;
        }

        public virtual void ResetEstado()
        {
            _lastPos   = Vector3.zero;
            _lastYaw   = 0f;
            _lastPitch = 0f;

        }
        
        public abstract void Destruir();

        public void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria)
        {
            _damage.RegistrarDaño(cantidad, tipo, dirHaciaAtacante, atacanteEnMemoria);
        }

        public bool TieneEnMemoriaJugadores(int playerId)
            => _players.TieneEnMemoria(playerId);
    }
}